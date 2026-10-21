// Two browser profiles syncing through the running app, in two separate headless Chromes on throwaway profiles
// (never the user's Chrome). Proves: a create converges, a rename made offline (DevTools' offline switch, through
// the DevTools protocol) syncs on reconnect, and two concurrent offline renames resolve to the later one on both
// profiles and the server. Then copies each profile's SQLite out of OPFS and prints its rows, beside the server's.
//
//   node .claude/tools/chrome-sync-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Each run uses fresh profiles under <scratchDir>
// and names its ingredients with a run id, so runs do not disturb each other. APP_URL overrides the address.
import { spawn, execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { ingredients } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-sync-check.mjs <scratchDir>'); process.exit(2); }
const APP = (process.env.APP_URL ?? 'https://localhost:7283/') + 'ingredients';
const CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
// SERVER_DB: the image under .claude/tools/ingress-sim keeps it in its DATA_DIR instead.
const SERVER_DB = process.env.SERVER_DB ?? join(homedir(), 'Library/Application Support/AshersLab.MealPlanner/mealplanner.db');
const RUN = Date.now().toString(36);
const sleep = ms => new Promise(r => setTimeout(r, ms));
let failures = 0;
const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures++; };

setTimeout(() => { console.error('FAIL gave up after 4 minutes'); process.exit(3); }, 240_000).unref();

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
  let id = 0; const pending = new Map();
  ws.onmessage = e => { const m = JSON.parse(e.data); pending.get(m.id)?.(m); pending.delete(m.id); };
  await new Promise(r => ws.onopen = r);
  const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  const evaluate = async expression =>
    (await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true })).result?.result?.value;
  await send('Network.enable');
  // A brand-new headless profile drops requests for about its first second (see chrome-multiwindow-check.mjs).
  await sleep(3000);
  await send('Page.navigate', { url: APP });
  const b = { name, send, evaluate,
    offline: on => send('Network.emulateNetworkConditions', { offline: on, latency: 0, downloadThroughput: -1, uploadThroughput: -1 }) };
  await until(b, `(document.querySelector('[data-testid=sync-status]')?.dataset.state) === 'online'`, 'online at start', 60_000);
  return b;
}

async function until(b, expression, what, ms = 20_000) {
  const end = Date.now() + ms;
  while (Date.now() < end) { if (await b.evaluate(expression)) return true; await sleep(200); }
  return false;
}

const names = b => b.evaluate(`[...document.querySelectorAll('[data-testid=ingredient-name]')].map(e => e.textContent.trim())`);
const status = b => b.evaluate(`(() => { const s = document.querySelector('[data-testid=sync-status]'); return s && { state: s.dataset.state, pending: s.dataset.pending, text: s.textContent.trim() }; })()`);
const has = (b, name) => until(b, `[...document.querySelectorAll('[data-testid=ingredient-name]')].some(e => e.textContent.trim() === ${JSON.stringify(name)})`, name);

// Adding and renaming go through the ingredient editor, driven by the shared harness.
const add = (b, name) => ingredients(b).add(name);
const rename = (b, from, to) => ingredients(b).rename(from, to);

const server = sql => execFileSync('sqlite3', ['-readonly', SERVER_DB, sql], { encoding: 'utf8' }).trim();
const serverName = id => server(`select Name from Ingredient where Id = '${id.toUpperCase()}'`);
const rowId = (b, name) => b.evaluate(`[...document.querySelectorAll('[data-testid=ingredient-row]')]
  .find(r => r.querySelector('[data-testid=ingredient-name]')?.textContent.trim() === ${JSON.stringify(name)})?.dataset.id`);

// Copies the profile's mealplanner.db and -wal out of OPFS (SAHPool: 4096-byte header naming the file).
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

// 1. A create on A reaches B.
const flour = `flour ${RUN}`;
check(await add(a, flour), 'A shows its new ingredient');
let t0 = Date.now();
// Well under the 30 s fallback round: anything that slow means the change stream is not delivering.
check(await has(b, flour) && Date.now() - t0 < 5000, `B receives it through the change stream (${Date.now() - t0} ms)`);
const flourId = await rowId(a, flour);
check(serverName(flourId) === flour, 'the server holds it');

// 2. A rename made offline on A syncs on reconnect.
await a.offline(true);
const plain = `plain flour ${RUN}`;
check(await rename(a, flour, plain), 'A renames while offline and shows it at once');
check(await until(a, `document.querySelector('[data-testid=sync-status]')?.dataset.state === 'offline'
  && document.querySelector('[data-testid=sync-status]')?.dataset.pending === '1'`, 'offline, 1 waiting'), 'A says offline, 1 waiting');
await sleep(3000);
check(serverName(flourId) === flour, 'the server still has the old name while A is offline');
check((await names(b)).includes(flour), 'B still has the old name');
await a.offline(false);
t0 = Date.now();
check(await has(b, plain), `after reconnect B shows the offline rename (${Date.now() - t0} ms)`);
check(serverName(flourId) === plain, 'the server holds the offline rename');
check(await until(a, `document.querySelector('[data-testid=sync-status]')?.dataset.pending === '0'`, 'A drained'), 'A has nothing waiting');

// 3. Concurrent renames, both offline: the later edit wins everywhere, whoever reconnects first.
await a.offline(true); await b.offline(true);
const fromA = `wholemeal flour ${RUN}`, fromB = `bread flour ${RUN}`;
check(await rename(a, plain, fromA), 'A renames offline');
await sleep(1500);
check(await rename(b, plain, fromB), 'B renames offline, later');
await b.offline(false);           // the later edit reaches the server first...
check(await until(b, `document.querySelector('[data-testid=sync-status]')?.dataset.pending === '0'`, 'B drained'), 'B pushed');
await a.offline(false);           // ...and the earlier one arrives after it, and must lose.
check(await has(a, fromB), 'A ends on the later rename (its own was older)');
check(await has(b, fromB), 'B keeps the later rename');
await sleep(1000);
check(serverName(flourId) === fromB, 'the server ends on the later rename');
check(!(await names(a)).includes(fromA) && !(await names(b)).includes(fromA), 'the earlier rename is shown nowhere');

// 4. The rows themselves, in each profile's SQLite and the server's.
const ops = server(`select count(*) from Op where EntityId = '${flourId.toUpperCase()}'`);
check(ops === '4', `the server logged all 4 ops for it, the losing one included (got ${ops})`);
for (const p of [a, b]) {
  const q = await dump(p);
  const local = q(`select Name from Ingredient where Id = '${flourId.toUpperCase()}'`);
  const outbox = q('select count(*) from Outbox');
  const state = q('select ServerEpoch, PulledThroughSeq from SyncState');
  check(local === fromB && outbox === '0', `${p.name} SQLite: Name='${local}', outbox ${outbox}, cursor ${state}`);
  console.log(`  ${p.name} FieldClock: ${q(`select Field, Hlc from FieldClock where EntityId = '${flourId.toUpperCase()}'`)}`);
}
console.log(`  server Ingredient: ${server(`select Id, Name, Deleted from Ingredient where Id = '${flourId.toUpperCase()}'`)}`);
console.log(`  server FieldClock: ${server(`select Field, Hlc from FieldClock where EntityId = '${flourId.toUpperCase()}'`)}`);
console.log(`  server Op:\n${server(`select Seq, Kind, substr(Hlc, 1, 24), substr(DeviceId, 1, 8), FieldsJson from Op where EntityId = '${flourId.toUpperCase()}' order by Seq`)}`);
console.log(`  server SyncEpoch: ${server('select Epoch from SyncEpoch')}`);

console.log(failures ? `${failures} FAILED` : 'ALL PASSED');
process.exit(failures ? 1 : 0);
