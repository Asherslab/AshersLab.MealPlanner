// A browser whose copy of the data cannot be opened, reset from the page with a second window open. Headless Chrome on
// a throwaway profile (never the user's Chrome), and the running app (AppHost: https).
//
//   node .claude/tools/chrome-device-reset-check.mjs <scratchDir>       # run_in_background; port 19351
//
// 1. One ingredient synced to the server; a second added offline, so the outbox holds it.
// 2. The failure seen 2026-10-22, planted: the newest migration's row is deleted from __EFMigrationsHistory, so on the
//    next start EF applies it again and its CREATE TABLE meets the table already there - SCHEMA_INCOMPATIBLE.
// 3. Both windows show the error and the reset offer. The dialog counts the 1 unsent change; Go back and Cancel do
//    nothing. Confirmed: both windows reload into a new device that opens, the synced ingredient comes back from the
//    server, the unsent one is gone, and the browser's SQLite and the server's agree. At 390 px throughout.
//
// UNREADABLE=1 also drops the Outbox table, so the dialog has to say the count cannot be known.
import { join } from 'node:path';
import { APP_URL, RUN, check, click, dump, finish, ingredients, launch, phone, screenshot, server, settled, sleep, until } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-device-reset-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 6 minutes'); process.exit(3); }, 360_000).unref();
const PORT = 19351;
const UNREADABLE = process.env.UNREADABLE === '1';
const NEWEST_MIGRATION = '20261021192236_AddReceipts';

/** A second window in the same profile - a window, not a tab, so its timers are not throttled. */
async function secondWindow(port, name, path) {
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
  await send('Page.navigate', { url: APP_URL + path });
  return w;
}

/** Runs one statement on the leader's worker directly, past EF - only to plant the failure. */
const plant = (b, sql) => b.evaluate(`new Promise(resolve => {
  const c = mealPlannerDatabase.client;
  if (!c?.leader) return resolve('not the leader');
  c.leader.post({ tab: null, kind: 'write', message: { data: { type: 'execute', database: 'mealplanner.db', sql: ${JSON.stringify(sql)}, parameters: {} } } });
  setTimeout(() => resolve('sent'), 1000);
})`);

const text = (b, testid) => b.evaluate(`document.querySelector('[data-testid=${testid}]')?.textContent.trim()`);
const failed = (b, ms = 60_000) => until(b, `/SCHEMA_INCOMPATIBLE/.test(document.querySelector('[data-testid=local-db-state]')?.textContent ?? '')
  && !!document.querySelector('[data-testid=local-db-reset]')`, ms);
// Diagnostics' device card, or the ingredients page's box - each appears only once the database has opened.
const opened = (b, ms = 60_000) => until(b, `!!document.querySelector('[data-testid=device-id]') || !!document.getElementById('ingredient-new-name')`, ms);
const dialog = b => b.evaluate(`!!document.querySelector('[data-testid=device-reset]')`);
const noScroll = b => b.evaluate(`document.documentElement.scrollWidth <= innerWidth`);

const kept = `Reset kept ${RUN}`, lost = `Reset lost ${RUN}`;

const a = await launch(scratch, 'a', PORT, 'ingredients');
await phone(a);
check(await ingredients(a).add(kept), 'added an ingredient online');
check(await settled(a), 'it reached the server');
const oldDevice = (await dump(a))('select Id from Device;').toLowerCase();

// Sync blocked rather than offline: the reload below has to download the app, and the edit must stay unsent.
await a.send('Network.setBlockedURLs', { urls: ['*/gRPC/AshersLab.MealPlanner.Sync/*'] });
check(await ingredients(a).add(lost), 'added a second ingredient with sync blocked');
check(await until(a, `document.querySelector('[data-testid=sync-status]')?.dataset.pending === '1'`, 10_000), 'the outbox holds 1');

console.log(`  plant: ${await plant(a, `DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '${NEWEST_MIGRATION}'`)}`);
if (UNREADABLE) console.log(`  plant: ${await plant(a, 'DROP TABLE "Outbox"')}`);
await a.send('Page.navigate', { url: APP_URL + 'settings/diagnostics' });
check(await failed(a), 'A, reloaded: SCHEMA_INCOMPATIBLE, with the reset offer');
await a.send('Network.setBlockedURLs', { urls: [] });

const w = await secondWindow(PORT, 'a2', 'ingredients');
await w.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
check(await failed(w), 'a second window shows it too');
check(await noScroll(a), 'no sideways scroll at 390 px');
console.log(`  ${await screenshot(a, 'failed')}`);
check(await text(a, 'device-not-open') !== undefined && (await a.evaluate(`document.querySelectorAll('[data-testid=local-db-state]').length`)) === 1,
  'Diagnostics shows the error once; the device card points at it');

// Cancel, and Go back, change nothing.
await click(a, '[data-testid=local-db-reset]');
check(await until(a, `!!document.querySelector('[data-testid=device-reset]')`, 15_000), 'the dialog opens');
const unsent = await text(a, 'device-reset-unsent');
check((UNREADABLE ? /there's no telling/ : /^1 change made on this device may not have reached the server/).test(unsent ?? ''),
  `it ${UNREADABLE ? 'says the count cannot be known' : 'counts the unsent change'}: "${unsent}"`);
console.log(`  ${await screenshot(a, 'dialog')}`);
await click(a, '[data-testid=device-reset-cancel]');
check(await until(a, `!document.querySelector('[data-testid=device-reset]')`, 5000) && await failed(a, 1000), 'Cancel closes it; still failed');

await click(a, '[data-testid=local-db-reset]');
await until(a, `!!document.querySelector('[data-testid=device-reset-next]')`, 15_000);
await click(a, '[data-testid=device-reset-next]');
check(await until(a, `!!document.querySelector('[data-testid=device-reset-warning]')`, 5000), 'Reset asks again, warning');
console.log(`  ${await screenshot(a, 'confirm')}`);
await click(a, '[data-testid=device-reset-back]');
check(await until(a, `!!document.querySelector('[data-testid=device-reset-unsent]')`, 5000), 'Go back returns to the first step');
await click(a, '[data-testid=device-reset-next]');
await until(a, `!!document.querySelector('[data-testid=device-reset-confirm]')`, 5000);
await click(a, '[data-testid=device-reset-confirm]');

check(await opened(a, 90_000), 'A reloads into a copy that opens');
if (!check(await opened(w, 90_000), 'the second window reloads and opens too')) {
  console.log(`  second window: ${await w.evaluate(`JSON.stringify({ url: location.href, state: document.querySelector('[data-testid=local-db-state]')?.textContent,
    body: document.body.innerText.slice(0, 300), role: globalThis.mealPlannerDatabase?.role(), events: globalThis.mealPlannerDatabase?.events })`)}`);
  console.log(`  ${await screenshot(w, 'after')}`);
}
console.log(`  ${await screenshot(a, 'after')}`);
const roles = [await a.evaluate('mealPlannerDatabase.role()'), await w.evaluate('mealPlannerDatabase.role()')].sort();
check(JSON.stringify(roles) === '["follower","leader"]', `one leader, one follower: ${roles}`);
const newDevice = (await text(a, 'device-id'))?.toLowerCase();
check(newDevice && newDevice !== oldDevice, `a new device id (${oldDevice} -> ${newDevice})`);
check(await text(a, 'device-open-count') === '1 time' || await text(a, 'device-open-count') === '2 times', `counted afresh: ${await text(a, 'device-open-count')}`);

await a.send('Page.navigate', { url: APP_URL + 'ingredients' });
check(await settled(a, 60_000), 'A syncs as the new device');
check(await ingredients(a).has(kept, 20_000), 'the synced ingredient came back from the server');
check(await ingredients(a).gone(lost, 1000), 'the unsent one is gone');
check(await ingredients(w).has(kept, 20_000), 'the second window shows it too');

const local = await dump(a);
check(local('select count(*) from Outbox;') === '0', 'the outbox is empty');
check(local(`select MigrationId from __EFMigrationsHistory where MigrationId = '${NEWEST_MIGRATION}';`) === NEWEST_MIGRATION, 'every migration recorded');
const localNames = local(`select Name from Ingredient where Deleted = 0 and Name like '%${RUN}' order by Name;`);
const serverNames = server(`select Name from Ingredient where Deleted = 0 and Name like '%${RUN}' order by Name`);
check(localNames === kept && serverNames === kept, `browser and server agree: [${localNames}] / [${serverNames}]`);
check(server(`select count(*) from Op where DeviceId = '${newDevice.toUpperCase()}' or DeviceId = '${newDevice}'`) === '0',
  'the new device has sent nothing');
console.log(`  shim events: ${JSON.stringify((await a.evaluate('mealPlannerDatabase.events')).slice(-4))}`);
finish();
