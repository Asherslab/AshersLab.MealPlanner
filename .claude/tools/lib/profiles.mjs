// The harness the two-profile checks share: separate headless Chromes on throwaway profiles (never the user's
// Chrome), driven over the DevTools protocol; the page's own testids; and the rows read back from each profile's
// SQLite (copied out of OPFS) and the server's.
import { spawn, execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';

export const APP_URL = process.env.APP_URL ?? 'https://localhost:7283/';
const CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
// SERVER_DB: the image under .claude/tools/ingress-sim keeps it in its DATA_DIR instead.
export const SERVER_DB = process.env.SERVER_DB ?? join(homedir(), 'Library/Application Support/AshersLab.MealPlanner/mealplanner.db');
export const RUN = Date.now().toString(36);
export const sleep = ms => new Promise(r => setTimeout(r, ms));

let failures = 0;
export const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures++; return ok; };
export const finish = () => { console.log(failures ? `${failures} FAILED` : 'ALL PASSED'); process.exit(failures ? 1 : 0); };

/** Starts a headless Chrome on a fresh profile under scratch, opens `path` and waits for the sync chip to say online. */
export async function launch(scratch, name, port, path) {
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
  const b = {
    name, scratch, send, evaluate,
    offline: on => send('Network.emulateNetworkConditions', { offline: on, latency: 0, downloadThroughput: -1, uploadThroughput: -1 }),
    go: async p => { await send('Page.navigate', { url: APP_URL + p }); await until(b, `document.readyState === 'complete' && !!document.querySelector('[data-testid=sync-status]')`, 30_000); },
  };
  await b.go(path);
  await until(b, `(document.querySelector('[data-testid=sync-status]')?.dataset.state) === 'online'`, 60_000);
  return b;
}

export async function until(b, expression, ms = 20_000) {
  const end = Date.now() + ms;
  while (Date.now() < end) { if (await b.evaluate(expression)) return true; await sleep(200); }
  return false;
}

/** True once the chip says online with nothing waiting. */
export const settled = (b, ms = 20_000) => until(b, `(() => { const s = document.querySelector('[data-testid=sync-status]');
  return s?.dataset.state === 'online' && s.dataset.pending === '0'; })()`, ms);

// MudTextField with Immediate binds on 'input'; the native setter is needed so Blazor sees the change.
export const typeInto = (id, text) => `(() => { const i = document.getElementById(${JSON.stringify(id)});
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(i, ${JSON.stringify(text)});
  i.dispatchEvent(new Event('input', { bubbles: true })); return true; })()`;

export const click = (b, selector) => b.evaluate(`(() => { const e = document.querySelector(${JSON.stringify(selector)}); e?.click(); return !!e; })()`);

/** Clicks a MudBlazor message box's button by its text ("Delete", "Cancel"). */
export const clickDialogButton = (b, text) => b.evaluate(`(() => {
  const btn = [...document.querySelectorAll('.mud-dialog button')].find(x => x.textContent.trim().toLowerCase() === ${JSON.stringify(text.toLowerCase())});
  btn?.click(); return !!btn; })()`);

/** A phone-sized viewport (iPhone 14-ish), and a PNG of the page saved under scratch. */
export async function phone(b, on = true) {
  if (on) await b.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
  else await b.send('Emulation.clearDeviceMetricsOverride');
}
export async function screenshot(b, label) {
  const shot = await b.send('Page.captureScreenshot', { format: 'png' });
  const file = join(b.scratch, `${RUN}-${b.name}-${label}.png`);
  writeFileSync(file, Buffer.from(shot.result.data, 'base64'));
  return file;
}

export const server = sql => execFileSync('sqlite3', ['-readonly', SERVER_DB, sql], { encoding: 'utf8' }).trim();

/** Copies the profile's mealplanner.db and -wal out of OPFS (SAHPool: 4096-byte header naming the file); returns a query function. */
export async function dump(b) {
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
  const dir = join(b.scratch, `${RUN}-${b.name}-read-${Date.now()}`);
  mkdirSync(dir, { recursive: true });
  for (const [file, b64] of Object.entries(files)) writeFileSync(join(dir, file.replace('mealplanner', 'm')), Buffer.from(b64, 'base64'));
  return sql => execFileSync('sqlite3', [join(dir, 'm.db'), sql], { encoding: 'utf8' }).trim();
}

/** A NameList (stores, categories) on the current page, by its testid prefix. */
export function nameList(b, prefix) {
  const names = () => b.evaluate(`[...document.querySelectorAll('[data-testid=${prefix}-name]')].map(e => e.textContent.trim())`);
  const has = (name, ms) => until(b, `[...document.querySelectorAll('[data-testid=${prefix}-name]')].some(e => e.textContent.trim() === ${JSON.stringify(name)})`, ms);
  const gone = (name, ms) => until(b, `![...document.querySelectorAll('[data-testid=${prefix}-name]')].some(e => e.textContent.trim() === ${JSON.stringify(name)})`, ms);
  const rowAction = (name, action) => b.evaluate(`(() => { const r = [...document.querySelectorAll('[data-testid=${prefix}-row]')]
    .find(r => r.querySelector('[data-testid=${prefix}-name]')?.textContent.trim() === ${JSON.stringify(name)});
    const btn = r?.querySelector('[data-testid=${prefix}-${action}]'); btn?.click(); return !!btn; })()`);
  return {
    names, has, gone,
    id: name => b.evaluate(`[...document.querySelectorAll('[data-testid=${prefix}-row]')]
      .find(r => r.querySelector('[data-testid=${prefix}-name]')?.textContent.trim() === ${JSON.stringify(name)})?.dataset.id`),
    async add(name) {
      await b.evaluate(typeInto(`${prefix}-new-name`, name)); await sleep(100);
      await click(b, `[data-testid=${prefix}-add]`);
      return has(name);
    },
    async rename(from, to) {
      await rowAction(from, 'rename');
      await until(b, `!!document.getElementById('${prefix}-rename-input')`);
      await b.evaluate(typeInto(`${prefix}-rename-input`, to)); await sleep(100);
      await click(b, `[data-testid=${prefix}-rename-save]`);
      return has(to);
    },
    /** Clicks delete; confirms the dialog if one opens. Returns 'deleted', or 'in-use' when the page refused. */
    async remove(name) {
      await rowAction(name, 'delete');
      await until(b, `!!document.querySelector('.mud-dialog, [data-testid=${prefix}-in-use]')`, 5000);
      if (!(await b.evaluate(`!!document.querySelector('.mud-dialog')`))) return 'in-use';
      await clickDialogButton(b, 'Delete');
      return (await gone(name)) ? 'deleted' : 'still there';
    },
    move: (name, dir) => rowAction(name, dir),
  };
}
