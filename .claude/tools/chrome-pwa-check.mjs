// The installed app (the service worker) behind the add-on's sign-in, through .claude/tools/addon-sim - a published
// image, as only a published build has the caching worker. One headless Chrome, phone-sized, on a fresh profile:
//
// 1. Not signed in: the app's address goes to the login page (another origin). Sign in: back to the app, which loads and
//    syncs; the service worker installs and controls the page; the manifest is found.
// 2. The app container stopped - the proxy answers 502: a reload still opens the app, from the cache, its data there,
//    the sync chip "Offline". Then the proxy stopped too - nothing answers at all: the same.
// 3. Both back: online again.
// 4. Signed out (the session cookie deleted): the next round says "Signed out", not "Offline". Tapping the chip reloads,
//    and the page load reaches the login page - the cached app does not hide it. Signing in brings it back.
// 5. Straight at the app, not through the proxy, with a forged user header: 403.
//
//   sh .claude/tools/addon-sim/run.sh mealplanner:pwa
//   node .claude/tools/chrome-pwa-check.mjs <scratchDir>
import { spawn, execFileSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-pwa-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 6 minutes'); process.exit(3); }, 360_000).unref();
mkdirSync(scratch, { recursive: true });

const APP = 'http://localhost:18410/', LOGIN = 'http://localhost:18411/', DIRECT = 'http://localhost:18412/';
const SIM = join(import.meta.dirname, 'addon-sim', 'run.sh');
const sleep = ms => new Promise(r => setTimeout(r, ms));
let failures = 0;
const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures++; return ok; };
const sim = arg => execFileSync('sh', [SIM, arg], { stdio: 'ignore' });

const port = 19390;
const chrome = spawn('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', [`--user-data-dir=${join(scratch, `pwa-${Date.now()}`)}`,
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
const state = () => evaluate(`document.querySelector('[data-testid=sync-status]')?.dataset.state`);
const go = async url => { await send('Page.navigate', { url }); await sleep(500); };
await send('Network.enable');
await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
await sleep(2500);

// --- 1. Signing in ------------------------------------------------------------------------------------------------------
await go(APP);
check(await until(`location.href.startsWith(${JSON.stringify(LOGIN)}) && !!document.getElementById('signin')`),
  `not signed in, the app's address goes to the login page: ${await evaluate('location.href')}`);
await evaluate(`document.getElementById('signin').click()`);
check(await until(`location.href === ${JSON.stringify(APP)} && document.querySelector('[data-testid=sync-status]')?.dataset.state === 'online'`, 90_000),
  'signed in: back to the app, which loads and syncs');
check(await until(`navigator.serviceWorker.getRegistration().then(r => !!r?.active)`, 60_000), 'the service worker installs');
await go(APP);
check(await until(`!!navigator.serviceWorker.controller && document.querySelector('[data-testid=sync-status]')?.dataset.state === 'online'`, 60_000),
  'after a reload it controls the page');
const manifest = await evaluate(`fetch(document.querySelector('link[rel=manifest]').href).then(r => r.json()).then(m => m.display + ' ' + m.icons.length)`);
check(manifest === 'standalone 4', `the manifest: ${manifest}`);
const cached = await evaluate(`caches.keys().then(async keys => { const c = await caches.open(keys.find(k => k.startsWith('mealplanner-offline-'))); return (await c.keys()).length; })`);
check(cached > 50, `the build is cached: ${cached} files`);
const title = await evaluate(`document.querySelector('.pad-head h2')?.textContent`);

// --- 2. The server unreachable ------------------------------------------------------------------------------------------
sim('--app-down');
await go(APP);
check(await until(`document.querySelector('.pad-head h2')?.textContent === ${JSON.stringify(title)}`, 30_000)
  && await until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'offline'`, 30_000),
  `the app stopped (the proxy answers 502): it still opens - "${await evaluate(`document.querySelector('.pad-head h2')?.textContent`)}", chip ${await state()}`);
execFileSync('docker', ['stop', 'mealplanner-addon-npm'], { stdio: 'ignore' });
await go(APP);
check(await until(`document.querySelector('.pad-head h2')?.textContent === ${JSON.stringify(title)}`, 30_000)
  && await until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'offline'`, 30_000),
  `nothing answers at all: it still opens, chip ${await state()}`);

// --- 3. Back -------------------------------------------------------------------------------------------------------------
execFileSync('docker', ['start', 'mealplanner-addon-npm'], { stdio: 'ignore' });
sim('--app-up');
check(await until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'online'`, 60_000), `both back: ${await state()}`);

// --- 4. Signed out -------------------------------------------------------------------------------------------------------
await send('Network.deleteCookies', { name: 'session', domain: 'localhost' });
await evaluate(`document.querySelector('[data-testid=sync-status]').click()`);
check(await until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'signedout'`, 40_000),
  `the session gone: "${await evaluate(`document.querySelector('[data-testid=sync-status]')?.textContent.trim()`)}", not offline`);
await evaluate(`document.querySelector('[data-testid=sync-status]').click()`);
check(await until(`location.href.startsWith(${JSON.stringify(LOGIN)})`, 20_000), `tapping it reloads into the login page: ${await evaluate('location.href')}`);
await evaluate(`document.getElementById('signin').click()`);
check(await until(`location.href === ${JSON.stringify(APP)} && document.querySelector('[data-testid=sync-status]')?.dataset.state === 'online'`, 60_000),
  'signed in again: online');

// --- 5. Around the proxy -------------------------------------------------------------------------------------------------
const direct = await fetch(DIRECT, { headers: { 'X-Forwarded-User': 'intruder' } }).then(r => r.status, () => 'no answer');
check(direct === 403, `straight at the app with a forged user header: ${direct}`);
const forged = execFileSync('curl', ['-s', '-o', '/dev/null', '-w', '%{http_code} %{redirect_url}', '-H', 'X-Forwarded-User: intruder', APP]).toString();
check(forged.startsWith('302 ' + LOGIN), `through the proxy with a forged header and no session: ${forged}`);

console.log(failures ? `${failures} FAILED` : 'ALL PASSED');
process.exit(failures ? 1 : 0);
