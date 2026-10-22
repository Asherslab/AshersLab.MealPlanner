// The add-on as it is deployed - the published image, the app's own passkey sign-in, its service worker - through
// .claude/tools/addon-sim. Two headless Chromes on fresh profiles, phone-sized, each with a virtual authenticator (the
// DevTools WebAuthn domain: a platform passkey store with user verification, as a phone's is):
//
// 1. A, a fresh phone: the app opens, its sync chip "Signed out" and the banner saying changes wait on the phone.
// 2. A opens the setup link from the add-on's log, writes a name and makes a passkey: signed in, syncing. The service
//    worker installs and caches the build; the manifest is found.
// 3. The app stopped (the proxy answers 502), then nothing answering at all: A still opens, "Offline". Back: online.
// 4. A invites "Alex" from Settings → Who can sign in. B opens the link, makes a passkey, is signed in and syncing; the
//    same link again is refused. A's list shows both.
// 5. A signs out: "Signed out" and the banner. A signs in again with the passkey: online.
// 6. Refused around the app: a POST from another site 403, a sync call with no session 401.
// 7. A removes Alex: within a minute and a bit B's sync says "Signed out".
//
//   docker build -t mealplanner:local . && sh .claude/tools/addon-sim/run.sh mealplanner:local
//   node .claude/tools/chrome-pwa-check.mjs <scratchDir>
import { spawn, execFileSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-pwa-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 8 minutes'); process.exit(3); }, 480_000).unref();
mkdirSync(scratch, { recursive: true });

const APP = 'http://localhost:18410/';
const SIM = join(import.meta.dirname, 'addon-sim', 'run.sh');
const sleep = ms => new Promise(r => setTimeout(r, ms));
let failures = 0;
const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures++; return ok; };

async function browser(name, port) {
  const chrome = spawn('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', [`--user-data-dir=${join(scratch, `${name}-${Date.now()}`)}`,
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
  await send('WebAuthn.enable');
  await send('WebAuthn.addVirtualAuthenticator', { options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true,
    hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true } });
  await sleep(2500);
  const b = {
    name, send, evaluate, until,
    go: async url => { await send('Page.navigate', { url }); await sleep(600); },
    state: () => evaluate(`document.querySelector('[data-testid=sync-status]')?.dataset.state`),
    text: testid => evaluate(`document.querySelector('[data-testid=${testid}]')?.textContent.trim().replace(/\\s+/g, ' ')`),
    click: testid => evaluate(`(() => { const e = document.querySelector('[data-testid=${testid}]'); (e?.querySelector('button') ?? e)?.click(); return !!e; })()`),
    type: (id, value) => evaluate(`(() => { const i = document.getElementById('${id}'); i.focus(); i.value = ${JSON.stringify(value)};
      i.dispatchEvent(new Event('input', { bubbles: true })); i.dispatchEvent(new Event('change', { bubbles: true })); })()`),
  };
  return b;
}

const a = await browser('owner', 19390);
const b = await browser('alex', 19391);
const setupLink = () => /welcome#[A-Za-z0-9_-]+/.exec(execFileSync('docker', ['logs', 'mealplanner-addon-app'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }))?.[0];

// --- 1. A fresh phone ---------------------------------------------------------------------------------------------------
await a.go(APP);
check(await a.until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'signedout'`, 60_000)
  && await a.until(`!!document.querySelector('[data-testid=signed-out]')`),
  `a fresh phone: the app opens, "${await a.text('sync-status')}" - "${await a.text('signed-out')}"`);

// --- 2. The setup link ----------------------------------------------------------------------------------------------------
const setup = setupLink();
check(!!setup, 'the add-on\'s log has a setup link');
await a.go(APP + setup);
check(await a.until(`!!document.getElementById('welcome-name')`), `the welcome page opens: "${await a.evaluate(`document.querySelector('[data-testid=welcome] h2')?.textContent`)}"`);
await a.type('welcome-name', 'Sam Example');
await sleep(200);
await a.click('welcome-create');
check(await a.until(`!!document.querySelector('[data-testid=welcome-done]')`, 20_000), `A makes a passkey: "${await a.text('welcome-done')}"`);
check(await a.evaluate('location.hash') === '', 'the code is gone from the address bar');
await a.go(APP);
check(await a.until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'online'`, 60_000), `signed in, syncing: ${await a.state()}`);
check(await a.until(`navigator.serviceWorker.getRegistration().then(r => !!r?.active)`, 60_000), 'the service worker installs');
await a.go(APP);
check(await a.until(`!!navigator.serviceWorker.controller && document.querySelector('[data-testid=sync-status]')?.dataset.state === 'online'`, 60_000),
  'after a reload it controls the page, still signed in');
const cached = await a.evaluate(`caches.keys().then(async keys => { const c = await caches.open(keys.find(k => k.startsWith('mealplanner-offline-'))); return (await c.keys()).length; })`);
check(cached > 50, `the build is cached: ${cached} files`);
const title = await a.evaluate(`document.querySelector('.pad-head h2')?.textContent`);

// --- 3. Unreachable ---------------------------------------------------------------------------------------------------------
execFileSync('sh', [SIM, '--app-down'], { stdio: 'ignore' });
await a.go(APP);
check(await a.until(`document.querySelector('.pad-head h2')?.textContent === ${JSON.stringify(title)}`, 30_000)
  && await a.until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'offline'`, 30_000),
  `the app stopped (the proxy answers 502): it still opens - "${title}", ${await a.state()}`);
execFileSync('docker', ['stop', 'mealplanner-addon-npm'], { stdio: 'ignore' });
await a.go(APP);
check(await a.until(`document.querySelector('.pad-head h2')?.textContent === ${JSON.stringify(title)}`, 30_000)
  && await a.until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'offline'`, 30_000),
  `nothing answering at all: it still opens, ${await a.state()}`);
execFileSync('docker', ['start', 'mealplanner-addon-npm'], { stdio: 'ignore' });
execFileSync('sh', [SIM, '--app-up'], { stdio: 'ignore' });
check(await a.until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'online'`, 60_000), `both back: ${await a.state()}, still signed in`);

// --- 4. Inviting Alex --------------------------------------------------------------------------------------------------------
await a.go(APP + 'settings/access');
await a.until(`!!document.getElementById('access-invite-name')`, 30_000);
await a.type('access-invite-name', 'Alex Example');
await sleep(200);
await a.click('access-invite');
await a.until(`!!document.querySelector('[data-testid=access-invite-link] code')`);
const invite = await a.evaluate(`document.querySelector('[data-testid=access-invite-link] code').textContent.trim()`);
check(/\/welcome#[A-Za-z0-9_-]{43}$/.test(invite ?? ''), 'A makes an invitation link');
await b.go(invite);
check(await b.until(`document.querySelector('[data-testid=welcome]')?.textContent.includes('Alex Example')`), 'B opens it: the passkey is for Alex Example');
await b.click('welcome-create');
check(await b.until(`!!document.querySelector('[data-testid=welcome-done]')`, 20_000), `B makes a passkey: "${await b.text('welcome-done')}"`);
await b.go(APP);
check(await b.until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'online'`, 60_000), `B is signed in and syncing: ${await b.state()}`);
await b.go(invite);
check(await b.until(`document.querySelector('[data-testid=welcome-problem]')?.textContent.includes('used already')`), 'the same link again is refused: used already');
await a.go(APP + 'settings/access');
check(await a.until(`document.querySelectorAll('[data-testid=access-member]').length === 2`, 30_000),
  `A's list: ${await a.evaluate(`[...document.querySelectorAll('[data-testid=access-member] b')].map(e => e.textContent).join(', ')`)}`);

// --- 5. Signing out and in again -------------------------------------------------------------------------------------------
await a.click('access-sign-out');
await a.go(APP);
check(await a.until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'signedout'`, 40_000)
  && await a.until(`!!document.querySelector('[data-testid=signed-out]')`), `A signs out: ${await a.state()}, the banner`);
await a.click('signed-out-sign-in');
await a.until(`!!document.querySelector('[data-testid=sign-in-passkey]')`);
await a.click('sign-in-passkey');
check(await a.until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'online' && !document.querySelector('[data-testid=signed-out]')`, 40_000),
  `A signs in with the passkey: ${await a.state()}`);

// --- 6. Around the app ------------------------------------------------------------------------------------------------------
const crossSite = execFileSync('curl', ['-s', '-o', '/dev/null', '-w', '%{http_code}', '-X', 'POST', '-H', 'Origin: https://evil.example.com',
  '-H', 'Content-Type: application/json', '-d', '{}', APP + 'account/sign-in/options']).toString();
check(crossSite === '403', `a POST from another site: ${crossSite}`);
const noSession = execFileSync('curl', ['-s', '-o', '/dev/null', '-w', '%{http_code}', '-X', 'POST', '-H', `Origin: ${APP.slice(0, -1)}`,
  '-H', 'Content-Type: application/grpc-web+proto', '--data-binary', '@/dev/null', APP + 'gRPC/AshersLab.MealPlanner.ServerInfo/Get']).toString();
check(noSession === '401', `a sync call with no session: ${noSession}`);

// --- 7. Removing Alex ---------------------------------------------------------------------------------------------------------
await a.go(APP + 'settings/access');
await a.until(`document.querySelectorAll('[data-testid=access-member]').length === 2`, 30_000);
await a.evaluate(`[...document.querySelectorAll('[data-testid=access-member]')].find(m => m.textContent.includes('Alex Example')).querySelector('[data-testid=access-remove-member]').click()`);
await sleep(600);
await a.evaluate(`[...document.querySelectorAll('.mud-dialog button')].find(x => x.textContent.trim() === 'Remove')?.click()`);
check(await a.until(`document.querySelectorAll('[data-testid=access-member]').length === 1`, 20_000), 'A removes Alex');
const removedAt = Date.now();
check(await b.until(`document.querySelector('[data-testid=sync-status]')?.dataset.state === 'signedout'`, 150_000),
  `B is signed out ${Math.round((Date.now() - removedAt) / 1000)} s later`);

console.log(failures ? `${failures} FAILED` : 'ALL PASSED');
process.exit(failures ? 1 : 0);
