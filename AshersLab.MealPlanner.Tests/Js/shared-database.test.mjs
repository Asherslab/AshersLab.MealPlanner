// The pure half of wwwroot/js/shared-database.js: how statements are classified, and how the transaction
// gate orders requests from several windows onto one SQLite connection, and how a reset deletes the files. Run by SharedDatabaseScriptTests,
// or directly: node --test AshersLab.MealPlanner.Tests/Js/shared-database.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const script = fileURLToPath(new URL('../../AshersLab.MealPlanner.WASM/wwwroot/js/shared-database.js', import.meta.url));
const sandbox = {};
vm.runInNewContext(readFileSync(script, 'utf8'), sandbox);   // no window: only the pure half is installed
const { classify, TransactionGate, removeEntries } = sandbox.MealPlannerSharedDatabase;

test('classify: transaction boundaries as EF Core and SqliteWasmBlazor send them', () => {
    assert.equal(classify('BEGIN'), 'begin');
    assert.equal(classify('BEGIN IMMEDIATE'), 'begin');
    assert.equal(classify('  begin deferred transaction'), 'begin');
    assert.equal(classify('COMMIT'), 'commit');
    assert.equal(classify('END TRANSACTION'), 'commit');
    assert.equal(classify('ROLLBACK'), 'rollback');
    assert.equal(classify('ROLLBACK TRANSACTION'), 'rollback');
});

test('classify: ROLLBACK TO a savepoint leaves the transaction open', () => {
    assert.equal(classify('ROLLBACK TO SAVEPOINT "__EFSavePoint"'), 'write');
    assert.equal(classify('ROLLBACK TRANSACTION TO "__EFSavePoint"'), 'write');
    assert.equal(classify('SAVEPOINT "__EFSavePoint"'), 'read');
});

test('classify: writes and reads, after leading whitespace and comments', () => {
    assert.equal(classify('UPDATE "Device" SET "OpenCount" = @p0 WHERE "Id" = @p1 RETURNING 1;'), 'write');
    assert.equal(classify('\n  INSERT INTO "Device" ("Id") VALUES (@p0);'), 'write');
    assert.equal(classify('-- note\nDELETE FROM "Device"'), 'write');
    assert.equal(classify('/* x */ CREATE TABLE "T" ("Id" TEXT)'), 'write');
    assert.equal(classify('SELECT "d"."Id" FROM "Device" AS "d" LIMIT 2'), 'read');
    assert.equal(classify('PRAGMA journal_mode'), 'read');
    assert.equal(classify(undefined), 'read');
});

function gate() {
    const sent = [];
    return { sent, gate: new TransactionGate(r => sent.push(r)) };
}
const r = (tab, kind, n) => ({ tab, kind, n });

test('gate: with no transaction open, every window goes straight through', () => {
    const { sent, gate: g } = gate();
    g.submit(r('A', 'read', 1));
    g.submit(r('B', 'write', 2));
    assert.deepEqual(sent.map(x => x.n), [1, 2]);
});

test('gate: another window waits for an open transaction, then runs in arrival order', () => {
    const { sent, gate: g } = gate();
    const begin = r('A', 'begin', 1);
    g.submit(begin);
    g.submit(r('B', 'read', 2));          // B must not see A's uncommitted rows
    g.submit(r('B', 'write', 3));         // nor write inside A's transaction
    g.submit(r('A', 'write', 4));         // A carries on
    const commit = r('A', 'commit', 5);
    g.submit(commit);
    assert.deepEqual(sent.map(x => x.n), [1, 4, 5]);

    g.completed(begin, true);
    assert.deepEqual(sent.map(x => x.n), [1, 4, 5]);
    assert.equal(g.completed(commit, true), true, 'a commit is a change other windows hear about');
    assert.deepEqual(sent.map(x => x.n), [1, 4, 5, 2, 3]);
    assert.equal(g.owner, null);
});

test('gate: the window is owner from the moment BEGIN is sent, before it answers', () => {
    const { sent, gate: g } = gate();
    g.submit(r('A', 'begin', 1));
    g.submit(r('B', 'write', 2));
    assert.deepEqual(sent.map(x => x.n), [1]);
});

test('gate: a waiting BEGIN takes the connection and holds back the windows behind it', () => {
    const { sent, gate: g } = gate();
    const aBegin = r('A', 'begin', 1); g.submit(aBegin);
    const bBegin = r('B', 'begin', 2); g.submit(bBegin);
    g.submit(r('C', 'read', 3));
    const aCommit = r('A', 'commit', 4); g.submit(aCommit);

    g.completed(aCommit, true);
    assert.deepEqual(sent.map(x => x.n), [1, 4, 2]);
    assert.equal(g.owner, 'B');

    const bRollback = r('B', 'rollback', 5); g.submit(bRollback);
    g.completed(bRollback, true);
    assert.deepEqual(sent.map(x => x.n), [1, 4, 2, 5, 3]);
    assert.equal(g.owner, null);
});

test('gate: a failed COMMIT keeps the transaction until the ROLLBACK that follows', () => {
    const { sent, gate: g } = gate();
    g.submit(r('A', 'begin', 1));
    const commit = r('A', 'commit', 2); g.submit(commit);
    g.submit(r('B', 'read', 3));
    assert.equal(g.completed(commit, false), false);
    assert.equal(g.owner, 'A');
    const rollback = r('A', 'rollback', 4); g.submit(rollback);
    g.completed(rollback, true);
    assert.deepEqual(sent.map(x => x.n), [1, 2, 4, 3]);
});

test('gate: a failed BEGIN does not keep the connection', () => {
    const { sent, gate: g } = gate();
    const begin = r('A', 'begin', 1); g.submit(begin);
    g.submit(r('B', 'read', 2));
    g.completed(begin, false);
    assert.equal(g.owner, null);
    assert.deepEqual(sent.map(x => x.n), [1, 2]);
});

test('gate: a write outside a transaction is a change; one inside is not until the commit', () => {
    const { gate: g } = gate();
    assert.equal(g.completed(r('A', 'write', 1), true), true);
    assert.equal(g.completed(r('A', 'write', 2), false), false);
    g.submit(r('A', 'begin', 3));
    assert.equal(g.completed(r('A', 'write', 4), true), false);
    assert.equal(g.completed(r('A', 'read', 5), true), false);
});

test('gate: a window that closes mid-transaction frees the connection and says a rollback is owed', () => {
    const { sent, gate: g } = gate();
    g.submit(r('A', 'begin', 1));
    g.submit(r('B', 'read', 2));
    g.submit(r('C', 'write', 3));
    assert.equal(g.tabGone('A'), true);
    assert.equal(g.owner, null);
    assert.deepEqual(sent.map(x => x.n), [1, 2, 3]);
});

test('gate: a window that closes while waiting loses its queued requests and owes nothing', () => {
    const { sent, gate: g } = gate();
    g.submit(r('A', 'begin', 1));
    g.submit(r('B', 'write', 2));
    assert.equal(g.tabGone('B'), false);
    const commit = r('A', 'commit', 3); g.submit(commit);
    g.completed(commit, true);
    assert.deepEqual(sent.map(x => x.n), [1, 3]);
});

// A stand-in OPFS directory: each name fails with the listed error names, in order, before it is removed.
function directory(failures) {
    const removed = [], calls = [];
    return {
        removed, calls,
        async removeEntry(name, options) {
            calls.push({ name, recursive: options?.recursive });
            const next = failures[name]?.shift();
            if (next) throw Object.assign(new Error(next), { name: next });
            removed.push(name);
        },
    };
}
const noWait = waits => ms => { waits.push(ms); return Promise.resolve(); };

test('reset: removes every entry, recursively', async () => {
    const dir = directory({});
    await removeEntries(dir, ['databases', 'export-staging'], noWait([]));
    assert.deepEqual(dir.removed, ['databases', 'export-staging']);
    assert.ok(dir.calls.every(c => c.recursive === true));
});

test('reset: an entry that is not there is already gone', async () => {
    const dir = directory({ 'export-staging': ['NotFoundError'] });
    const waits = [];
    await removeEntries(dir, ['databases', 'export-staging'], noWait(waits));
    assert.deepEqual(dir.removed, ['databases']);
    assert.deepEqual(waits, []);
});

test('reset: waits, backing off, while a terminated worker still holds the files', async () => {
    const dir = directory({ databases: ['NoModificationAllowedError', 'NoModificationAllowedError', 'InvalidStateError'] });
    const waits = [];
    await removeEntries(dir, ['databases'], noWait(waits));
    assert.deepEqual(dir.removed, ['databases']);
    assert.deepEqual(waits, [250, 500, 1000]);
});

test('reset: gives up after its attempts, with the last error, waits capped at 2 s', async () => {
    const dir = directory({ databases: Array(8).fill('NoModificationAllowedError') });
    const waits = [];
    await assert.rejects(removeEntries(dir, ['databases'], noWait(waits)), { name: 'NoModificationAllowedError' });
    assert.deepEqual(dir.removed, []);
    assert.deepEqual(waits, [250, 500, 1000, 2000, 2000, 2000, 2000]);
});
