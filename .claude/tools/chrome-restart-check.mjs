// One browser session against a throwaway Chrome profile: launch Chrome, open the app, read the "This
// device" card, then end the browser - gracefully (Browser.close, like Cmd+Q) or by SIGKILL (a crash).
// Run it twice on the same profile directory to prove the browser database survives a browser restart.
//
// A separate headless Chrome, never the user's: it has its own profile directory (put it in a scratch
// directory, not the repo) and its own DevTools port, 19333.
//
//   node .claude/tools/chrome-restart-check.mjs <profileDir> <close|kill>
import { spawn } from 'node:child_process';

const [profile, ending = 'close'] = process.argv.slice(2);
const PORT = 19333;
const CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';

const chrome = spawn(CHROME, [
  `--user-data-dir=${profile}`, `--remote-debugging-port=${PORT}`, '--headless=new',
  '--ignore-certificate-errors', '--no-first-run', '--no-default-browser-check', 'about:blank',
], { stdio: 'ignore' });

const sleep = ms => new Promise(r => setTimeout(r, ms));
async function json(path, method = 'GET') {
  for (let i = 0; i < 100; i++) {
    try { return await (await fetch(`http://127.0.0.1:${PORT}${path}`, { method })).json(); } catch { await sleep(100); }
  }
  throw new Error('no devtools endpoint');
}

function cdp(url) {
  const ws = new WebSocket(url);
  let id = 0; const pending = new Map();
  ws.onmessage = e => { const m = JSON.parse(e.data); if (pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
  const opened = new Promise(r => ws.onopen = r);
  return {
    opened,
    send: (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); }),
  };
}

await json('/json/version');
// A brand-new profile drops requests for about its first second (ERR_CERT_VERIFIER_CHANGED - see
// chrome-multiwindow-check.mjs): without this wait the first run on a fresh profile reads no card. Then Diagnostics,
// where the device card is.
await sleep(3000);
const target = await json('/json/new?about:blank', 'PUT');
const page = cdp(target.webSocketDebuggerUrl);
await page.opened;
await page.send('Page.navigate', { url: 'https://localhost:7283/settings/diagnostics' });

const read = `(() => {
  const t = s => document.querySelector('[data-testid=' + s + ']')?.textContent.trim();
  return t('device-open-count') ? { id: t('device-id'), first: t('device-first-opened'), count: t('device-open-count'), storage: t('device-storage') }
       : t('local-db-state') ? { state: t('local-db-state') } : null;
})()`;
let card = null;
for (let i = 0; i < 300 && !card; i++) {
  card = (await page.send('Runtime.evaluate', { expression: read, returnByValue: true })).result?.result?.value ?? null;
  if (!card) await sleep(100);
}
console.log(JSON.stringify({ pid: chrome.pid, card }));

if (ending === 'kill') {
  chrome.kill('SIGKILL');
} else {
  const browser = cdp((await json('/json/version')).webSocketDebuggerUrl);
  await browser.opened;
  browser.send('Browser.close');
}
await new Promise(r => chrome.on('exit', r));
console.log(`chrome ended by ${ending}`);
process.exit(0);
