// Light or dark kept with the sign-in (docs/modules/infrastructure/accounts.md#light-or-dark), through the add-on as it is
// deployed: .claude/tools/addon-sim, the app's own passkey sign-in. One headless Chrome, phone-sized, with a virtual
// authenticator (as chrome-pwa-check.mjs):
//
// 1. A fresh phone signs up from the setup link. Light, and Settings says the colours are kept with the sign-in.
// 2. Dark on Settings → Colours: the page is dark at once; the server's member row says "dark".
// 3. Another phone of theirs - this one with its own copy wiped: it opens light, then turns dark from the account.
// 4. Signed out, Light: shown at once, "kept on this phone for now"; the account still says dark.
// 5. Signed in again: the waiting choice reaches the account - "light" - and nothing is waiting.
// 6. Reloaded: light from the first paint, before Blazor starts.
// Then: a phone in the system's dark mode is not dark unless chosen.
//
// The app's /data must be a Docker volume, not the simulator's bind mount: on a macOS host SQLite's WAL shared memory is
// not shared across a bind mount, and a write can vanish - here the first member update after signing up, every run, with
// EF reporting it written (2026-10-23). The Pi's /data is a Linux filesystem, as a volume is. So, after run.sh:
//
//   docker build -t mealplanner:local . && DATA_DIR=<scratch>/addon-data sh .claude/tools/addon-sim/run.sh mealplanner:local
//   docker rm -f mealplanner-addon-app && docker volume create mealplanner-appearance-vol
//   docker run --rm -v mealplanner-appearance-vol:/data -v <scratch>/addon-data:/src alpine cp /src/options.json /data/
//   docker run -d --name mealplanner-addon-app --network mealplanner-addon-sim --ip 172.30.33.10 -e Auth__Mode=Accounts \
//       -v mealplanner-appearance-vol:/data mealplanner:local
//   node .claude/tools/chrome-appearance-check.mjs <scratchDir>
//   # after: DATA_DIR=<scratch>/addon-data sh .claude/tools/addon-sim/run.sh --down; docker volume rm mealplanner-appearance-vol
import { spawn, execFileSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-appearance-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 6 minutes'); process.exit(3); }, 360_000).unref();
mkdirSync(scratch, { recursive: true });

const APP = 'http://localhost:18410/';
const sleep = ms => new Promise(r => setTimeout(r, ms));
let failures = 0;
const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures++; return ok; };
// The server's database as the container sees it: the file and its WAL copied out (sqlite3 rebuilds the index from the
// WAL), never read through the bind mount, whose WAL shared memory a macOS host and the Linux container do not share.
const copy = join(scratch, 'server-copy');
const server = sql => {
  mkdirSync(copy, { recursive: true });
  for (const f of ['mealplanner.db', 'mealplanner.db-wal'])
    execFileSync('sh', ['-c', `docker exec mealplanner-addon-app sh -c 'cat /data/${f} 2>/dev/null' > '${join(copy, f)}'`]);
  execFileSync('rm', ['-f', join(copy, 'mealplanner.db-shm')]);
  return execFileSync('sqlite3', [join(copy, 'mealplanner.db'), sql], { encoding: 'utf8' }).trim();
};

const port = 19395;
const chrome = spawn('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', [`--user-data-dir=${join(scratch, `appearance-${Date.now()}`)}`,
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
await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
await send('WebAuthn.enable');
await send('WebAuthn.addVirtualAuthenticator', { options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true,
  hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true } });
await sleep(2500);
const go = async url => { await send('Page.navigate', { url }); await sleep(600); };
const theme = () => evaluate('document.documentElement.dataset.theme');
const click = testid => evaluate(`(() => { const e = document.querySelector('[data-testid=${testid}]'); (e?.querySelector('button') ?? e)?.click(); return !!e; })()`);
const text = testid => evaluate(`document.querySelector('[data-testid=${testid}]')?.textContent.trim().replace(/\\s+/g, ' ')`);
const state = () => evaluate(`document.querySelector('[data-testid=sync-status]')?.dataset.state`);
const online = () => until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'online'`, 60_000);
const accountSays = () => server(`select coalesce(Appearance, 'null') from AspNetUsers`);

// --- 1. Signing up ---------------------------------------------------------------------------------------------------------
const setup = /welcome#[A-Za-z0-9_-]+/.exec(execFileSync('docker', ['logs', 'mealplanner-addon-app'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }))?.[0];
check(!!setup, 'the add-on\'s log has a setup link');
await go(APP + setup);
await until(`!!document.getElementById('welcome-name')`);
await evaluate(`(() => { const i = document.getElementById('welcome-name'); i.focus(); i.value = 'Sam Example';
  i.dispatchEvent(new Event('input', { bubbles: true })); i.dispatchEvent(new Event('change', { bubbles: true })); })()`);
await sleep(200);
await click('welcome-create');
check(await until(`!!document.querySelector('[data-testid=welcome-done]')`), 'a fresh phone makes a passkey');
await go(APP + 'settings');
check(await online(), `signed in: ${await state()}`);
check(await until(`!!document.querySelector('[data-testid=appearance-kept]')`) && await theme() === 'light',
  `light to start, and "${await text('appearance-kept')}"; the account says ${accountSays()}`);

// --- 2. Dark --------------------------------------------------------------------------------------------------------------------
await click('appearance-dark');
check(await until(`document.documentElement.dataset.theme === 'dark'`, 3000), 'Dark: the page is dark at once');
check(await until(`document.querySelector('[data-testid=appearance-dark]')?.getAttribute('aria-checked') === 'true'`), 'and Dark is ticked');
let says = ''; for (let i = 0; i < 40 && (says = accountSays()) !== 'dark'; i++) await sleep(250);
check(says === 'dark', `the account says ${says}`);
check(await until(`document.querySelector('[data-testid=appearance-kept]')?.textContent.includes('Kept with your sign-in')`), `"${await text('appearance-kept')}"`);

// --- 3. Another phone of theirs ---------------------------------------------------------------------------------------------
await evaluate(`localStorage.removeItem('mealplanner.appearance'); localStorage.removeItem('mealplanner.appearance.unsent'); true`);
await send('Page.navigate', { url: APP });
await sleep(150);
const first = await theme();
check(await until(`document.documentElement.dataset.theme === 'dark'`, 30_000), `this phone's own copy gone: it opens ${first}, then turns dark from the account`);

// --- 4. Signed out, Light -------------------------------------------------------------------------------------------------------
await go(APP + 'settings/access');
await until(`!!document.querySelector('[data-testid=access-sign-out]')`, 30_000);
await click('access-sign-out');
await go(APP + 'settings');
check(await until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'signedout'`, 40_000), `signed out: ${await state()}`);
await until(`!!document.querySelector('[data-testid=appearance-light]')`);
await click('appearance-light');
check(await until(`document.documentElement.dataset.theme === 'light'`, 3000), 'Light, signed out: shown at once');
check(await until(`document.querySelector('[data-testid=appearance-kept]')?.textContent.includes('for now')`), `"${await text('appearance-kept')}"`);
check(accountSays() === 'dark' && await evaluate(`localStorage.getItem('mealplanner.appearance.unsent')`) === '1',
  `waiting on this phone: the account still says ${accountSays()}`);

// --- 5. Signed in again -----------------------------------------------------------------------------------------------------------
await click('signed-out-sign-in');
check(await online(), `signed in again with the passkey: ${await state()}`);
says = ''; for (let i = 0; i < 40 && (says = accountSays()) !== 'light'; i++) await sleep(250);
check(says === 'light', `the waiting choice reached the account: ${says}`);
check(await until(`localStorage.getItem('mealplanner.appearance.unsent') === null`) && await theme() === 'light', 'nothing waiting, still light');

// --- 6. Reloaded ----------------------------------------------------------------------------------------------------------------
await send('Page.navigate', { url: APP });
await sleep(150);
check(await theme() === 'light', `reloaded: ${await theme()} from the first paint`);

// --- The phone's own dark mode is not followed ---------------------------------------------------------------------------------
await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'dark' }] });
await go(APP);
await online();
await sleep(1000);
check(await theme() === 'light' && await evaluate(`getComputedStyle(document.body).backgroundColor`) === 'rgb(221, 233, 223)',
  'the phone in dark mode, the planner on Light: light');

console.log(failures ? `${failures} FAILED` : 'all passed');
process.exit(failures ? 1 : 0);
