// The server restored from an older backup while two browser profiles stay open - what Home Assistant does when the
// Pi's /data is restored. Two headless Chromes on throwaway profiles (never the user's Chrome), as
// chrome-sync-check.mjs, and the running app (AppHost: https).
//
//   node .claude/tools/chrome-restore-check.mjs <scratchDir>       # run_in_background
//
// 1. A adds two ingredients; the script takes a backup of the server's SQLite (sqlite3 .backup, consistent while
//    the app runs). 2. A adds a third and renames the first; B adds one; both converge. 3. A goes offline, and the
//    script prints "RESTORE NOW" and waits for <scratchDir>/<run>-restored to exist. Meanwhile, by hand: stop the
//    app, copy the backup it named over the server's mealplanner.db (delete -wal and -shm), start the app, touch
//    the file. 4. B, online throughout, finds the server's log shorter than its cursor, pushes back what it holds,
//    and adds another ingredient; then A comes back with a cursor whose seq now names a different op. Both must end
//    with every ingredient, the server too, outboxes empty, cursors at the server's head.
import { spawn, execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-restore-check.mjs <scratchDir>'); process.exit(2); }
const APP = (process.env.APP_URL ?? 'https://localhost:7283/') + 'ingredients';
const CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const SERVER_DB = process.env.SERVER_DB ?? join(homedir(), 'Library/Application Support/AshersLab.MealPlanner/mealplanner.db');
const RUN = Date.now().toString(36);
const sleep = ms => new Promise(r => setTimeout(r, ms));
let failures = 0;
const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures++; };

setTimeout(() => { console.error('FAIL gave up after 15 minutes'); process.exit(3); }, 900_000).unref();

async function launch(name, port) {
  if (await fetch(`http://127.0.0.1:${port}/json/version`).then(() => true, () => false)) {
    console.error(`port ${port} is taken - kill the leftover headless Chrome first`); process.exit(2);
  }
  const profile = join(scratch, `${RUN}-${name}`);
  const chrome = spawn(CHROME, [`--user-data-dir=${profile}`, `--remote-debugging-port=${port}`, '--headless=new',
    '--ignore-certificate-errors', '--no-first-run', '--no-default-browser-check', 'about:blank'], { stdio: 'ignore' });
  process.on('exit', () => chrome.kill('SIGKILL'));
  let target;
  for (let i = 0; i < 100 && !target; i++) {
    try { target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json(); } catch { await sleep(100); }
  }
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  let id = 0; const pending = new Map(); const console_ = [];
  ws.onmessage = e => {
    const m = JSON.parse(e.data);
    if (m.method === 'Runtime.consoleAPICalled') console_.push(m.params.args.map(a => a.value ?? '').join(' '));
    pending.get(m.id)?.(m); pending.delete(m.id);
  };
  await new Promise(r => ws.onopen = r);
  const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  const evaluate = async expression =>
    (await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true })).result?.result?.value;
  await send('Network.enable');
  await send('Runtime.enable');
  await sleep(3000);
  await send('Page.navigate', { url: APP });
  const b = { name, send, evaluate, console: console_,
    offline: on => send('Network.emulateNetworkConditions', { offline: on, latency: 0, downloadThroughput: -1, uploadThroughput: -1 }) };
  await until(b, `(document.querySelector('[data-testid=sync-status]')?.dataset.state) === 'online'`, 'online at start', 60_000);
  return b;
}

async function until(b, expression, ms = 20_000) {
  const end = Date.now() + ms;
  while (Date.now() < end) { if (await b.evaluate(expression).catch(() => false)) return true; await sleep(200); }
  return false;
}

const names = async b => (await b.evaluate(`[...document.querySelectorAll('[data-testid=ingredient-name]')].map(e => e.textContent.trim())`)) ?? [];
const has = (b, name, ms) => until(b, `[...document.querySelectorAll('[data-testid=ingredient-name]')].some(e => e.textContent.trim() === ${JSON.stringify(name)})`, ms);
const chip = b => b.evaluate(`(() => { const s = document.querySelector('[data-testid=sync-status]'); return s && { state: s.dataset.state, pending: s.dataset.pending }; })()`);
const settled = (b, ms = 60_000) => until(b, `(() => { const s = document.querySelector('[data-testid=sync-status]');
  return s?.dataset.state === 'online' && s?.dataset.pending === '0'; })()`, ms);

const typeInto = (id, text) => `(() => { const i = document.getElementById(${JSON.stringify(id)});
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(i, ${JSON.stringify(text)});
  i.dispatchEvent(new Event('input', { bubbles: true })); return true; })()`;

async function add(b, name) {
  await b.evaluate(typeInto('ingredient-new-name', name));
  await sleep(100);
  await b.evaluate(`document.querySelector('[data-testid=ingredient-add]').click()`);
  return has(b, name);
}

async function rename(b, from, to) {
  await b.evaluate(`[...document.querySelectorAll('[data-testid=ingredient-row]')]
    .find(r => r.querySelector('[data-testid=ingredient-name]')?.textContent.trim() === ${JSON.stringify(from)})
    .querySelector('[data-testid=ingredient-rename]').click()`);
  await until(b, `!!document.getElementById('ingredient-rename-input')`);
  await b.evaluate(typeInto('ingredient-rename-input', to));
  await sleep(100);
  await b.evaluate(`document.querySelector('[data-testid=ingredient-rename-save]').click()`);
  return has(b, to);
}

const server = sql => execFileSync('sqlite3', ['-readonly', SERVER_DB, sql], { encoding: 'utf8' }).trim();
const serverNames = () => server(`select Name from Ingredient where Deleted = 0 and Name like '%${RUN}' order by Name`).split('\n').filter(Boolean);

async function dump(b) {
  const files = await b.evaluate(`(async () => {
    const dir = await (await navigator.storage.getDirectory()).getDirectoryHandle('databases').then(d => d.getDirectoryHandle('.opaque'));
    const out = {};
    for await (const [, h] of dir.entries()) {
      const f = await h.getFile();
      const path = new TextDecoder().decode(new Uint8Array(await f.slice(0, 64).arrayBuffer())).replace(/\\0.*$/s, '');
      if (path === '/databases/mealplanner.db' || path === '/databases/mealplanner.db-wal') {
        const bytes = new Uint8Array(await f.slice(4096).arrayBuffer());
        let s = ''; for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
        out[path.split('/').pop()] = btoa(s);
      }
    }
    return out;
  })()`);
  const dir = join(scratch, `${RUN}-${b.name}-read`);
  mkdirSync(dir, { recursive: true });
  for (const [file, b64] of Object.entries(files)) writeFileSync(join(dir, file.replace('mealplanner', 'm')), Buffer.from(b64, 'base64'));
  return sql => execFileSync('sqlite3', [join(dir, 'm.db'), sql], { encoding: 'utf8' }).trim();
}

const a = await launch('profileA', 19341);
const b = await launch('profileB', 19342);
console.log(`run ${RUN}: both profiles online`);

const flour = `flour ${RUN}`, salt = `salt ${RUN}`, eggs = `eggs ${RUN}`, plain = `plain flour ${RUN}`,
      sugar = `sugar ${RUN}`, milk = `milk ${RUN}`;

// 1. Before the backup.
check(await add(a, flour) && await add(a, salt), 'A adds two ingredients');
check(await has(b, salt), 'B has them');
const backup = join(scratch, `${RUN}-backup.db`);
execFileSync('sqlite3', [SERVER_DB, `.backup '${backup}'`]);
// Not -readonly: the copy is in WAL mode with no -shm yet, which a read-only open cannot create.
const backupHead = execFileSync('sqlite3', [backup, 'select max(Seq) from Op'], { encoding: 'utf8' }).trim();
console.log(`  backup at server seq ${backupHead}: ${backup}`);

// 2. After it: the edits the restore will lose from the server.
check(await add(a, eggs), 'A adds a third');
check(await rename(a, flour, plain), 'A renames the first');
check(await add(b, sugar), 'B adds one');
check(await has(a, sugar) && await has(b, plain) && await has(b, eggs), 'both converge');
check(await settled(a) && await settled(b), 'both drained');
const headBefore = server('select max(Seq) from Op');
const chainBefore = server(`select Chain from Op where Seq = ${headBefore}`);
console.log(`  server head before the restore: ${headBefore}`);

// 3. The restore.
await a.offline(true);
// An idle page notices only at its next round - up to the 30 s fallback.
check(await until(a, `document.querySelector('[data-testid=sync-status]')?.dataset.state === 'offline'`, 40_000), 'A is offline');
const signal = join(scratch, `${RUN}-restored`);
console.log(`RESTORE NOW: stop the app, copy ${backup} over ${SERVER_DB} (delete -wal/-shm), start it, then: touch ${signal}`);
while (!existsSync(signal)) await sleep(1000);
// B may already have pushed back by now, so not "head is the backup's": the log up to the backup's head is the backup's.
const backupChain = execFileSync('sqlite3', [backup, `select Chain from Op where Seq = ${backupHead}`], { encoding: 'utf8' }).trim();
check(server(`select Chain from Op where Seq = ${backupHead}`) === backupChain, `the server's log is the backup's through seq ${backupHead}`);

// 4. B was online throughout: it reconnects to the restored server and gives back what it lost.
const expected = [eggs, plain, salt, sugar].sort();
check(await settled(b, 120_000), 'B reconnects and drains');
await sleep(2000);
check(JSON.stringify(serverNames()) === JSON.stringify(expected), `B pushed the lost edits back: server has ${serverNames().join(', ')}`);
check(await add(b, milk), 'B adds one after the restore');
check(await settled(b), 'B drained again');

const reused = server(`select Chain from Op where Seq = ${headBefore}`);
check(reused !== '' && reused !== chainBefore, `seq ${headBefore}, A's cursor, now names a different op (chain ${reused.slice(0, 8)}, was ${chainBefore.slice(0, 8)})`);
await a.offline(false);
check(await has(a, milk, 60_000), 'A, back online with a cursor the restore invalidated, receives what B added');
check(await settled(a), 'A drained');
await sleep(2000);
const all = [...expected, milk].sort();
check(JSON.stringify(serverNames()) === JSON.stringify(all), `server: ${serverNames().join(', ')}`);
for (const p of [a, b]) {
  const shown = (await names(p)).filter(n => n.endsWith(RUN)).sort();
  check(JSON.stringify(shown) === JSON.stringify(all), `${p.name} shows ${shown.join(', ')}`);
}
// B gives back what it holds; A, recovering after it, finds the server already has it all.
check(b.console.some(l => l.includes('pushed them back')), 'B logged the push-back');

const head = server('select max(Seq) from Op');
for (const p of [a, b]) {
  const q = await dump(p);
  const rows = q(`select Name from Ingredient where Deleted = 0 and Name like '%${RUN}' order by Name`).split('\n');
  const outbox = q('select count(*) from Outbox');
  const cursor = q('select PulledThroughSeq from SyncState');
  check(JSON.stringify(rows) === JSON.stringify(all) && outbox === '0' && cursor === head,
    `${p.name} SQLite: ${rows.length} rows, outbox ${outbox}, cursor ${cursor} (server head ${head})`);
}
console.log(`  server Op after the restore:\n${server(`select Seq, Kind, substr(DeviceId, 1, 8), substr(Chain, 1, 8), FieldsJson from Op where Seq > ${backupHead} order by Seq`)}`);

console.log(failures ? `${failures} FAILED` : 'ALL PASSED');
process.exit(failures ? 1 : 0);
