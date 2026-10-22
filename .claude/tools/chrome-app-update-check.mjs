// A new build reaching a window that stays open, through .claude/tools/addon-sim - the published image, its service
// worker. One headless Chrome, phone-sized, signed out (the update path needs no session):
//
// 1. The sim runs <first image>. The app opens and its service worker takes control; no "new version" banner.
// 2. The app container is swapped for <second image> (same data). A plain reload still runs the first build - the new
//    worker waits for every window to close, which is the bug this banner is for.
// 3. The app comes back to the foreground: it looks for a new build, finds it waiting, and says "A new version of the
//    app is ready." Reload runs the second build, and the banner is gone.
//
// The two images must differ in a WASM asset; the second must set `--build-probe: 2` on :root (app.css), which is how
// the page tells which build it is running. One way: build the first, add the property, build the second, remove it.
//
//   sh .claude/tools/addon-sim/run.sh mealplanner:upd1
//   node .claude/tools/chrome-app-update-check.mjs <scratchDir> mealplanner:upd2
import { spawn, execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const [scratch, second] = process.argv.slice(2);
if (!scratch || !second) { console.error('usage: chrome-app-update-check.mjs <scratchDir> <second image>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 5 minutes'); process.exit(3); }, 300_000).unref();
mkdirSync(scratch, { recursive: true });

const APP = 'http://localhost:18410/';
const SIM = join(import.meta.dirname, 'addon-sim', 'run.sh');
const DATA = process.env.DATA_DIR ?? join(process.env.TMPDIR ?? '/tmp', 'mealplanner-addon-sim-data');
const sleep = ms => new Promise(r => setTimeout(r, ms));
let failures = 0;
const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures++; return ok; };

const port = 19392;
const chrome = spawn('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', [`--user-data-dir=${join(scratch, `upd-${Date.now()}`)}`,
    `--remote-debugging-port=${port}`, '--headless=new', '--no-first-run', '--no-default-browser-check', 'about:blank'], { stdio: 'ignore' });
process.on('exit', () => chrome.kill('SIGKILL'));
let target;
for (let i = 0; i < 100 && !target; i++) { try { target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json(); } catch { await sleep(100); } }
const ws = new WebSocket(target.webSocketDebuggerUrl);
let id = 0; const pending = new Map();
ws.onmessage = e => { const m = JSON.parse(e.data); pending.get(m.id)?.(m); pending.delete(m.id); };
await new Promise(r => ws.onopen = r);
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const evaluate = async expression => (await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true })).result?.result?.value;
const until = async (expression, ms = 20_000) => { const end = Date.now() + ms; while (Date.now() < end) { try { if (await evaluate(expression)) return true; } catch { } await sleep(250); } return false; };
await send('Network.enable');
await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
await sleep(2500);

const build = () => evaluate(`getComputedStyle(document.documentElement).getPropertyValue('--build-probe').trim() || '1'`);
const appOpen = () => until(`!!document.querySelector('[data-testid=sync-status]')`, 60_000);
const banner = () => evaluate(`!!document.querySelector('[data-testid=update-ready]')`);

// --- 1. The first build ----------------------------------------------------------------------------------------------------
await send('Page.navigate', { url: APP });
check(await appOpen(), 'the app opens');
check(await until(`!!navigator.serviceWorker.controller`, 60_000), 'its service worker has installed and controls the page');
check(await build() === '1' && !await banner(), 'it runs the first build, with no "new version" banner');

// --- 2. A new build on the server ------------------------------------------------------------------------------------------
execFileSync('sh', [SIM, '--app-down']);
writeFileSync(join(DATA, '.image'), `${second}\n`);
execFileSync('sh', [SIM, '--app-up']);
for (const end = Date.now() + 90_000; Date.now() < end; await sleep(1000))
  if (await fetch(APP).then(r => r.ok, () => false)) break;
await send('Page.reload');
await sleep(1000);
check(await appOpen(), 'the app opens again after the server is replaced');
check(await build() === '1', 'a plain reload still runs the first build: the new worker waits');

// --- 3. Back to the foreground -----------------------------------------------------------------------------------------------
// Coming back is a visibilitychange to visible; the check is throttled to once a minute, so the clock is wound back.
await evaluate(`mealPlannerApp._lastCheck = 0; document.dispatchEvent(new Event('visibilitychange'))`);
check(await until(`document.querySelector('[data-testid=update-ready]')?.textContent.includes('A new version of the app is ready')`, 90_000),
  'back in the foreground: "A new version of the app is ready." with Reload');
await evaluate(`document.querySelector('[data-testid=update-ready-reload]').click()`);
await sleep(3000);
check(await appOpen(), 'Reload opens the app');
check(await until(`getComputedStyle(document.documentElement).getPropertyValue('--build-probe').trim() === '2'`, 30_000),
  'and it runs the second build');
check(!await banner(), 'the banner is gone');

console.log(failures ? `${failures} FAILED` : 'ALL PASSED');
process.exit(failures ? 1 : 0);
