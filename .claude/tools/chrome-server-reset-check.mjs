// The server reinstalled without a backup - its database wiped - under two browser profiles that hold data, one of
// them with two windows open. Headless Chromes on throwaway profiles (never the user's Chrome), and the running app
// (AppHost: https).
//
//   node .claude/tools/chrome-server-reset-check.mjs <scratchDir>       # run_in_background; ports 19349/19350
//
// It prints "WIPE NOW" twice, each with a signal file. Each time, by hand: stop the app, delete mealplanner.db and its
// -wal/-shm, start the AppHost run configuration, wait for _framework/blazor.webassembly.js to answer 200, and touch
// the signal file.
//
// 1. A adds two ingredients, B one; B, offline, renames one of A's (queued). Wipe.
// 2. Every window - A's two and B's - asks; the chip says paused; nothing reached the new server. A's second window
//    sends; A's first window's question closes by itself. B, back online, sends too; everything converges, B's later
//    rename winning, and each profile's SQLite matches the server's.
// 3. B adds one more while offline - only B has it. Wipe again. A sends; B chooses clear, is warned, goes back,
//    chooses clear again and confirms: B's extra is gone everywhere, B shows exactly what A sent.
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { APP_URL, RUN, check, click, dump, finish, ingredients, launch, phone, screenshot, server, settled, sleep, until } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-server-reset-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 15 minutes'); process.exit(3); }, 900_000).unref();

/**
 * A second window in the same profile, driven like the first. A window, not a tab: Chrome throttles a hidden
 * background tab's timers, and the first tab then runs its sync rounds minutes apart - seen on the first run of this
 * check. The owner keeps two windows side by side, both visible.
 */
async function secondWindow(first, port, name) {
  const browser = new WebSocket((await (await fetch(`http://127.0.0.1:${port}/json/version`)).json()).webSocketDebuggerUrl);
  await new Promise(r => browser.onopen = r);
  const created = await new Promise(r => {
    browser.onmessage = e => r(JSON.parse(e.data));
    browser.send(JSON.stringify({ id: 1, method: 'Target.createTarget', params: { url: 'about:blank', newWindow: true } }));
  });
  browser.close();
  const target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.id === created.result.targetId);
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  let id = 0; const pending = new Map();
  ws.onmessage = e => { const m = JSON.parse(e.data); pending.get(m.id)?.(m); pending.delete(m.id); };
  await new Promise(r => ws.onopen = r);
  const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  const w = { name, scratch, send,
    evaluate: async expression => (await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true })).result?.result?.value };
  await send('Page.navigate', { url: APP_URL + 'ingredients' });
  console.log(`  ${name}: ${await w.evaluate('document.visibilityState')}; first window: ${await first.evaluate('document.visibilityState')}`);
  await until(w,`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'online'`, 60_000);
  return w;
}

const state = b => b.evaluate(`document.querySelector('[data-testid=sync-status]')?.dataset.state`);
const asking = (b, ms = 60_000) => until(b, `!!document.querySelector('[data-testid=server-replaced]')`, ms);
const notAsking = (b, ms = 20_000) => until(b, `!document.querySelector('[data-testid=server-replaced]')`, ms);
const text = (b, testid) => b.evaluate(`document.querySelector('[data-testid=${testid}]')?.textContent.trim()`);
const shown = async b => ((await ingredients(b).names()) ?? []).filter(n => n.endsWith(RUN)).sort();
const serverNames = () => server(`select Name from Ingredient where Deleted = 0 and Name like '%${RUN}' order by Name`).split('\n').filter(Boolean);
const same = (x, y) => JSON.stringify(x) === JSON.stringify(y);

async function wipe(n) {
  const signal = join(scratch, `${RUN}-wiped-${n}`);
  console.log(`WIPE NOW: stop the app, delete the server's mealplanner.db, -wal and -shm, start it, then: touch ${signal}`);
  while (!existsSync(signal)) await sleep(1000);
}

async function sqliteMatchesServer(b, expected) {
  const q = await dump(b);
  const rows = q(`select Name from Ingredient where Deleted = 0 and Name like '%${RUN}' order by Name`).split('\n').filter(Boolean);
  const outbox = q('select count(*) from Outbox');
  const st = q(`select ServerEpoch || '|' || PulledThroughSeq || '|' || coalesce(ReplacedByEpoch, '') from SyncState`);
  const [epoch, seq, replaced] = st.split('|');
  const serverEpoch = server('select Epoch from SyncEpoch');
  const head = server('select max(Seq) from Op');
  check(same(rows, expected) && outbox === '0' && epoch.toLowerCase() === serverEpoch.toLowerCase() && seq === head && replaced === '',
    `${b.name} SQLite: ${rows.join(', ')}; outbox ${outbox}; cursor ${seq} of server head ${head}; same database: ${epoch.toLowerCase() === serverEpoch.toLowerCase()}; no question open: ${replaced === ''}`);
}

const a = await launch(scratch, 'profileA', 19349, 'ingredients');
const a2 = await secondWindow(a, 19349, 'profileA-window2');
const b = await launch(scratch, 'profileB', 19350, 'ingredients');
console.log(`run ${RUN}: A (two windows) and B online`);

const flour = `flour ${RUN}`, salt = `salt ${RUN}`, eggs = `eggs ${RUN}`, wholemeal = `wholemeal flour ${RUN}`, pepper = `pepper ${RUN}`;

// 1. Data in both profiles, one edit still queued in B.
check(await ingredients(a).add(flour) && await ingredients(a).add(salt), 'A adds two ingredients');
check(await ingredients(b).add(eggs), 'B adds one');
check(await ingredients(a).has(eggs) && await ingredients(b).has(salt) && await ingredients(a2).has(eggs), 'all three windows converge');
check(await settled(a) && await settled(b), 'both drained');
await b.offline(true);
check(await until(b, `document.querySelector('[data-testid=sync-status]')?.dataset.state === 'offline'`, 40_000), 'B is offline');
check(await ingredients(b).rename(flour, wholemeal), 'B renames flour while offline (queued)');

await wipe(1);
const epoch1 = server('select Epoch from SyncEpoch');

// 2. Every window asks; nothing syncs.
check(await asking(a) && await asking(a2), "both of A's windows ask");
check(await state(a) === 'waitingforchoice' && await state(a2) === 'waitingforchoice', `A's chip: ${await text(a, 'sync-status')}`);
console.log(`  A's question: ${await text(a, 'server-replaced-count')}`);
await b.offline(false);
check(await asking(b), 'B, back online, asks too');
await phone(b);
console.log(`  phone-sized screenshot: ${await screenshot(b, 'server-replaced')}`);
await phone(b, false);
await sleep(3000);
check(serverNames().length === 0, `nothing reached the new server while asking (${serverNames().length} rows)`);

await click(a2, '[data-testid=server-replaced-send]');
check(await notAsking(a2) && await notAsking(a), "A's second window sends; A's first window's question closes by itself");
check(await settled(a, 30_000) && await settled(a2, 30_000), 'A drains');
check(same(serverNames(), [eggs, flour, salt].sort()), `server after A sent: ${serverNames().join(', ')}`);
check(await asking(b, 5000), 'B still asks - its question is its own');

await click(b, '[data-testid=server-replaced-send]');
check(await notAsking(b) && await settled(b, 30_000), 'B sends and drains');
const after = [eggs, salt, wholemeal].sort();
check(await ingredients(a).has(wholemeal, 30_000) && await ingredients(a2).has(wholemeal, 30_000), "B's later rename reaches A's windows");
await sleep(2000);
check(same(serverNames(), after), `server: ${serverNames().join(', ')}`);
for (const p of [a, a2, b]) check(same(await shown(p), after), `${p.name} shows ${(await shown(p)).join(', ')}`);
await sqliteMatchesServer(a, after);
await sqliteMatchesServer(b, after);
check(server('select Epoch from SyncEpoch') === epoch1, 'the server database is the wiped one throughout');

// 3. Clear. Only B has pepper.
await b.offline(true);
check(await until(b, `document.querySelector('[data-testid=sync-status]')?.dataset.state === 'offline'`, 40_000), 'B is offline');
check(await ingredients(b).add(pepper), 'B adds pepper while offline - only B has it');

await wipe(2);
check(await asking(a) && await asking(a2), 'A asks again');
await click(a, '[data-testid=server-replaced-send]');
check(await notAsking(a2) && await settled(a, 30_000), 'A sends');
await b.offline(false);
check(await asking(b), 'B asks');
await click(b, '[data-testid=server-replaced-clear]');
check(await until(b, `!!document.querySelector('[data-testid=server-replaced-clear-warning]')`, 5000),
  `clear asks again: "${await text(b, 'server-replaced-clear-warning')}"`);
await phone(b);
console.log(`  phone-sized screenshot: ${await screenshot(b, 'server-replaced-clear')}`);
await phone(b, false);
await click(b, '[data-testid=server-replaced-back]');
check(await until(b, `!!document.querySelector('[data-testid=server-replaced-send]')`, 5000) && await ingredients(b).has(pepper, 1000),
  'going back changes nothing');
await click(b, '[data-testid=server-replaced-clear]');
await until(b, `!!document.querySelector('[data-testid=server-replaced-clear-confirm]')`, 5000);
await click(b, '[data-testid=server-replaced-clear-confirm]');
check(await notAsking(b) && await settled(b, 30_000), 'B clears and drains');
check(await ingredients(b).gone(pepper, 10_000), 'pepper is gone from B');
await sleep(2000);
check(same(serverNames(), after), `server never got pepper: ${serverNames().join(', ')}`);
await sqliteMatchesServer(a, after);
await sqliteMatchesServer(b, after);

finish();
