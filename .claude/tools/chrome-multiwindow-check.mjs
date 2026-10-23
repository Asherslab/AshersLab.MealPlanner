// Several windows of the app on one browser database, in a separate headless Chrome on a throwaway
// profile (never the user's Chrome). Checks that windows share the database and see each other's changes,
// that the database survives the leading window closing or reloading, that windows starting at once
// each count their start exactly once, and that a hidden leader hands over and a frozen one (Safari's
// suspension) leaves the others working or saying what they wait on.
//
//   node .claude/tools/chrome-multiwindow-check.mjs <profileDir>
//
// Every failure is classified, so a flaky run says *what* flaked:
//   boot-download  A request was dropped at the network level and the page never fully started. Retried
//                  once by reloading, unless NO_RETRY=1. See "a fresh profile drops requests" below.
//   boot-unknown   Blazor never rendered, with nothing recorded to say why.
//   shim-stall     Blazor rendered but the window never got a database: a real shared-database.js bug.
//                  Prints the window's shim events and the origin's Web Locks.
//
// A fresh profile drops requests: for the first second or so after a brand-new headless profile starts,
// Chrome aborts in-flight requests with net::ERR_CERT_VERIFIER_CHANGED while its certificate verifier is
// set up. Measured 2026-10-21: first window opened the instant Chrome answered, 6 of 10 runs lost a request;
// after an 8 s wait, 0 of 10. A harness artefact, not the app - so the script waits WARMUP_MS (default
// 3000) and attaches DevTools before loading the app. FAST_FIRST=1 WARMUP_MS=0 reproduces it.
//
// APP_URL overrides the address (default https://localhost:7283/).
import { spawn } from 'node:child_process';

const [profile] = process.argv.slice(2);
const PORT = 19334;
// The windows open on Diagnostics: the device card the checks read lives there.
const APP = (process.env.APP_URL ?? 'https://localhost:7283/') + 'settings/diagnostics';
const NO_RETRY = process.env.NO_RETRY === '1';
const WARMUP_MS = Number(process.env.WARMUP_MS ?? 3000);
const CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const sleep = ms => new Promise(r => setTimeout(r, ms));

// A Chrome left over from an earlier run still owning the port would answer instead of this one - on another
// profile, with other state. Refuse rather than test the wrong browser.
if (await fetch(`http://127.0.0.1:${PORT}/json/version`).then(() => true, () => false)) {
  console.error(`port ${PORT} is already taken - kill the leftover headless Chrome first`);
  process.exit(2);
}

const chrome = spawn(CHROME, [
  `--user-data-dir=${profile}`, `--remote-debugging-port=${PORT}`, '--headless=new',
  '--ignore-certificate-errors', '--no-first-run', '--no-default-browser-check', 'about:blank',
], { stdio: 'ignore' });
process.on('exit', () => chrome.kill('SIGKILL'));
setTimeout(() => { console.error('FAIL gave up after 4 minutes'); process.exit(3); }, 240_000).unref();

async function json(path, method = 'GET') {
  for (let i = 0; i < 100; i++) {
    try { return await (await fetch(`http://127.0.0.1:${PORT}${path}`, { method })).json(); } catch { await sleep(100); }
  }
  throw new Error('no devtools endpoint');
}

function cdp(url) {
  const ws = new WebSocket(url);
  let id = 0; const pending = new Map(); const errors = []; const failedLoads = []; const urls = new Map(); const handlers = {};
  ws.onmessage = e => {
    const m = JSON.parse(e.data);
    if (pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); return; }
    if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails.exception?.description ?? m.params.exceptionDetails.text);
    else if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errors.push(m.params.args.map(a => a.value ?? a.description).join(' '));
    else if (m.method === 'Network.requestWillBeSent') urls.set(m.params.requestId, m.params.request.url);
    else if (m.method === 'Network.loadingFailed') failedLoads.push(`${urls.get(m.params.requestId)} (${m.params.errorText})`);
    handlers[m.method]?.(m.params);
  };
  const opened = new Promise(r => ws.onopen = r);
  // A call to a page that never answers resolves empty after 5 s instead of hanging the run.
  return { opened, errors, failedLoads, on: (method, fn) => handlers[method] = fn, send: (method, params = {}) => new Promise(r => {
    const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params }));
    setTimeout(() => { if (pending.delete(i)) r({ timedOut: true }); }, 5000);
  }) };
}

const read = `(() => {
  const t = s => document.querySelector('[data-testid=' + s + ']')?.textContent.trim();
  return { id: t('device-id'), count: t('device-open-count'), role: t('device-role'), state: t('local-db-state'),
           shim: globalThis.mealPlannerDatabase?.role?.() ?? null, waiting: !!document.querySelector('[data-testid=db-waiting]') };
})()`;
const evaluate = async (w, expression) =>
  (await w.page.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true })).result?.result?.value;

// Headless Chrome puts every window but the newest in the background, and a background page is hidden - so a
// leader would hand over to each new window, and which window leads would depend on the order they opened. Every
// window says it is in view unless a check hides it on purpose (`setVisible`), which dispatches visibilitychange
// as the browser does.
const VISIBLE = `(() => {
  let hidden = false;
  Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => hidden ? 'hidden' : 'visible' });
  globalThis.__setVisible = visible => { hidden = !visible; document.dispatchEvent(new Event('visibilitychange')); return true; };
})()`;

// Opens a window on the app. `before` runs after DevTools is attached and before the app is requested, so
// it can set up request interception.
async function open(before, direct = false) {
  // `direct` requests the app the moment the window exists, as the first versions of this script did -
  // kept to reproduce ERR_CERT_VERIFIER_CHANGED on a fresh profile (FAST_FIRST=1).
  if (direct) {
    const target = await json(`/json/new?${APP}`, 'PUT');
    const page = cdp(target.webSocketDebuggerUrl);
    await page.opened;
    await page.send('Runtime.enable');
    await page.send('Network.enable');
    return { target, page };
  }
  const target = await json('/json/new?about:blank', 'PUT');
  const page = cdp(target.webSocketDebuggerUrl);
  await page.opened;
  await page.send('Runtime.enable');
  await page.send('Network.enable');
  await page.send('Page.enable');
  await page.send('Page.addScriptToEvaluateOnNewDocument', { source: VISIBLE });
  const w = { target, page };
  if (before) await before(w);
  await page.send('Page.navigate', { url: APP });
  return w;
}

const bootFailed = w => w.page.errors.some(e => /Failed to start platform|mono_download_assets/.test(e))
  || w.page.failedLoads.some(f => /ERR_CERT_VERIFIER_CHANGED/.test(f));
const notes = [];

async function card(w, until = c => c.count || c.state, label = 'card') {
  let c, reloaded = false;
  for (let i = 0; i < 300; i++) {
    c = await evaluate(w, read);
    if (c && until(c)) return c;
    if (bootFailed(w) && !reloaded && !NO_RETRY) {
      notes.push(`boot-download (retried): ${w.page.failedLoads.join(' | ')}`);
      w.page.failedLoads.length = 0;
      reloaded = true;
      w.page.errors.length = 0;
      await w.page.send('Page.reload');
    }
    await sleep(100);
  }
  // Classify the failure.
  if (bootFailed(w)) {
    throw new Error(`CLASS boot-download (${label}): a download was dropped\n  failed loads: ${w.page.failedLoads.join(' | ')}`);
  }
  // Blazor rendered the server card but the database never came: that, and only that, is the shim's fault.
  const rendered = await evaluate(w, '!!document.querySelector("[data-testid=server-version], [data-testid=device-sheet]")');
  const events = await evaluate(w, 'JSON.stringify(globalThis.mealPlannerDatabase?.events ?? null)');
  const locks = await evaluate(w, 'navigator.locks.query().then(q => JSON.stringify(q))');
  const body = await evaluate(w, 'location.href + " | " + (document.body?.innerText ?? "").slice(0, 160)');
  throw new Error(`CLASS ${rendered ? 'shim-stall' : 'boot-unknown'} (${label}): ${JSON.stringify(c)}\n  page: ${body}\n`
    + `  events: ${events}\n  locks: ${locks}\n`
    + `  failed loads: ${w.page.failedLoads.join(' | ')}\n`
    + `  errors: ${w.page.errors.map(e => e.slice(0, 300)).join(' | ')}`);
}

const close = w => fetch(`http://127.0.0.1:${PORT}/json/close/${w.target.id}`).then(r => r.text());   // plain text
const count = c => parseInt(c.count, 10);
let failures = 0;
const check = (label, ok, detail) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${label} ${JSON.stringify(detail)}`); if (!ok) failures++; };
// A statement sent through the window's shim as SqliteWasmBlazor's bridge sends one, on its already-open connection:
// what a page does after a hand-over, without the reload that would open the database afresh.
const query = async w => JSON.parse(await evaluate(w, `new Promise(resolve => {
  const c = mealPlannerDatabase.client, bridge = c.onmessage, id = 900000 + Math.floor(Math.random() * 1000);
  const done = r => { c.onmessage = bridge; resolve(JSON.stringify(r)); };
  c.onmessage = e => e.data?.id === id                                  // rows come back as MessagePack: binary
    ? done({ ok: e.data.binary === true || e.data.data?.success === true, error: e.data.data?.error ?? e.data.error })
    : bridge?.(e);
  c.postMessage({ id, data: { type: 'execute', database: 'mealplanner.db', sql: 'SELECT COUNT(*) FROM "Device"', parameters: {} } });
  setTimeout(() => done({ ok: false, error: 'no answer in 4 s' }), 4000);
})`) ?? '{"ok":false,"error":"page did not answer"}');

async function leaderOf(windows) {
  for (let i = 0; i < 100; i++) {
    for (const w of windows) if ((await evaluate(w, read))?.shim === 'leader') return w;
    await sleep(100);
  }
  return null;
}

try {
  await json('/json/version');
  if (WARMUP_MS > 0) await sleep(WARMUP_MS);

  const a = await open(undefined, process.env.FAST_FIRST === '1');
  const ca = await card(a, undefined, 'first window');
  check('first window leads', ca.shim === 'leader', ca);

  const b = await open();
  const cb = await card(b, undefined, 'second window');
  check('second window follows, same device, counted', cb.shim === 'follower' && cb.id === ca.id && count(cb) === count(ca) + 1, cb);
  const ca2 = await card(a, c => count(c) === count(cb), 'first window refresh');
  check('first window shows the second window\'s start', count(ca2) === count(cb), ca2);

  // Hand the database over five times: close whoever leads, open a replacement.
  let windows = [a, b], last = count(cb);
  for (let round = 1; round <= 5; round++) {
    const leader = await leaderOf(windows);
    await close(leader);
    windows = windows.filter(w => w !== leader);
    const next = await leaderOf(windows);
    check(`failover ${round}: a remaining window takes over`, next !== null, null);
    const qs = await Promise.all(windows.map(query));
    check(`failover ${round}: every remaining window still reads without reloading`, qs.every(q => q.ok), qs);
    const fresh = await open();
    const cf = await card(fresh, undefined, `failover ${round} new window`);
    check(`failover ${round}: a new window is counted`, cf.shim === 'follower' && count(cf) === last + 1, cf);
    last = count(cf);
    windows.push(fresh);
  }

  // Reload the leading window while a follower is open: it must come back, as follower or leader.
  for (let round = 1; round <= 3; round++) {
    const leader = await leaderOf(windows);
    leader.page.errors.length = 0;
    await leader.page.send('Page.reload');
    await sleep(300);
    const cr = await card(leader, c => (c.count && count(c) === last + 1) || c.state, `leader reload ${round}`);
    check(`leader reload ${round}: the reloaded window gets the database back`, count(cr) === last + 1 && cr.shim !== null, cr);
    last = count(cr);
  }

  // Down to one window, then reload it: nobody else can hold the database while it comes back.
  for (const w of windows.slice(1)) await close(w);
  windows = windows.slice(0, 1);
  for (let round = 1; round <= 3; round++) {
    const only = windows[0];
    await sleep(500);
    only.page.errors.length = 0;
    await only.page.send('Page.reload');
    await sleep(300);
    const cr = await card(only, c => (c.count && count(c) === last + 1) || c.state, `sole window reload ${round}`);
    check(`sole window reload ${round}: comes back leading`, count(cr) === last + 1 && cr.shim === 'leader', cr);
    last = count(cr);
  }

  const many = await Promise.all([open(), open(), open()]);
  const cards = await Promise.all(many.map((w, i) => card(w, undefined, `simultaneous window ${i + 1}`)));
  const after = await card(windows[0], x => count(x) >= last + 3, 'after simultaneous starts');
  check('three windows starting at once each count once, one device row', count(after) === last + 3 && cards.every(x => x.id === ca.id), { after, started: cards.map(x => x.count) });
  last += 3;

  // Safari suspends a tab that is not in view, and a suspended leader keeps the lock and the files but answers
  // nobody (seen 2026-10-23). Hiding goes through VISIBLE's override; Safari's suspension is
  // Page.setWebLifecycleState frozen.
  const hide = w => evaluate(w, '__setVisible(false)');
  const show = w => evaluate(w, '__setVisible(true)');
  const freeze = (w, frozen) => w.page.send('Page.setWebLifecycleState', { state: frozen ? 'frozen' : 'active' });
  for (const w of [...windows.slice(1), ...many]) await close(w);
  windows = [windows[0]];

  // Hidden just as another window opens - a background tab is hidden - the hand-over must not fail that start.
  for (let round = 1; round <= 3; round++) {
    const leader = await leaderOf(windows);
    const opening = await open();
    await sleep(round * 400);
    await hide(leader);
    const co = await card(opening, c => (c.count && c.shim === 'leader') || c.state, `window opening during hand-over ${round}`)
      .catch(async e => { throw new Error(`${e.message}\n  hidden leader's events: ${await evaluate(leader, 'JSON.stringify(mealPlannerDatabase.events)')}`); });
    check(`hand-over during a start ${round}: the new window opens, counted, and leads`,
      count(co) === last + 1 && co.shim === 'leader' && !co.state, co);
    last = count(co);
    await show(leader);
    await close(leader);
    windows = [opening];
  }
  const second = await open();
  const cs = await card(second, undefined, 'second window for the hidden pair');
  last = count(cs);
  const pair = [windows[0], second];
  windows = pair;

  // Hidden: it hands over to the window in view, which then keeps working with the hidden one frozen.
  const hidden = await leaderOf(pair);
  const inView = pair.find(w => w !== hidden);
  await sleep(3000);   // settled: both windows' connections opened on the hidden one's worker, long before it goes
  await hide(hidden);
  const took = await leaderOf([inView]);
  check('hidden leader hands over to the window in view', took === inView, null);
  // Both windows opened the database on the old worker and never send `open` again (2026-10-23: "Database
  // mealplanner.db not open" on Receipts after switching away from the leading tab). The new worker must serve them.
  for (const [w, label] of [[inView, 'the new leader'], [hidden, 'the old leader']]) {
    const q = await query(w);
    check(`after a hand-over, ${label} still reads without reloading`, q.ok, q);
  }
  const leaderEvents = JSON.parse(await evaluate(inView, 'JSON.stringify(mealPlannerDatabase.events)'));
  check('the new leader opened the database itself for windows that had it open already',
    leaderEvents.some(e => /opening mealplanner\.db for a window/.test(e)), leaderEvents.slice(-6));
  await freeze(hidden, true);
  inView.page.errors.length = 0;
  await inView.page.send('Page.reload');
  await sleep(300);
  const cv = await card(inView, c => (c.count && count(c) === last + 1) || c.state, 'reload beside a frozen hidden window');
  check('with the hidden window frozen, the one in view reloads and gets the database', count(cv) === last + 1 && cv.shim === 'leader', cv);
  last = count(cv);
  await freeze(hidden, false);
  await show(hidden);
  const ch = await card(hidden, c => c.shim === 'follower' && count(c) === last, 'hidden window shown again');
  check('the hidden window, shown again, follows', ch.shim === 'follower', ch);

  // Suspended before it could hand over (or an old build): the waiting window says so, and carries on once it wakes.
  const asleep = inView, waiter = hidden;
  await freeze(asleep, true);
  waiter.page.errors.length = 0;
  await waiter.page.send('Page.reload');
  await sleep(300);
  const cw = await card(waiter, c => c.waiting || c.count || c.state, 'window waiting on a frozen leader');
  check('a window waiting on a frozen leader says so after about 10 s', cw.waiting && !cw.count, cw);
  await freeze(asleep, false);
  const cr = await card(waiter, c => (c.count && count(c) === last + 1 && !c.waiting) || c.state, 'waiting window once the leader wakes');
  check('once the leader wakes, the waiting window opens and the notice goes', count(cr) === last + 1 && !cr.waiting, cr);
  last = count(cr);

  // Fault injection, with no other window open so this one must lead: drop its first request for the
  // cross-window script and its first request for the worker script. Both are retried; the database must
  // still open, shared, and count the start once.
  for (const w of [...windows, ...many]) await close(w);
  await sleep(1000);
  const dropped = { script: 0, worker: 0 };
  const faulty = await open(async w => {
    w.page.on('Fetch.requestPaused', p => {
      const kind = p.request.url.includes('shared-database.js') ? 'script' : p.request.url.includes('sqlite-wasm-worker.js') ? 'worker' : null;
      if (kind && dropped[kind] === 0) {
        dropped[kind]++;
        w.page.send('Fetch.failRequest', { requestId: p.requestId, errorReason: 'ConnectionReset' });
      } else {
        w.page.send('Fetch.continueRequest', { requestId: p.requestId });
      }
    });
    await w.page.send('Fetch.enable', { patterns: [{ urlPattern: '*shared-database.js*' }, { urlPattern: '*sqlite-wasm-worker.js*' }] });
  });
  const cfault = await card(faulty, undefined, 'fault-injected window');
  const faultEvents = JSON.parse(await evaluate(faulty, 'JSON.stringify(globalThis.mealPlannerDatabase?.events ?? [])'));
  check('cross-window script dropped once: retried, database shared', dropped.script === 1 && cfault.shim === 'leader' && count(cfault) === last + 1, { dropped, cfault });
  if (dropped.worker === 1) {
    check('worker script dropped once: the leader retries and gets ready',
      faultEvents.some(e => /worker failed \(attempt 1\)/.test(e)) && faultEvents.some(e => /worker ready \(attempt 2\)/.test(e)), faultEvents);
  } else {
    console.log('NOTE the worker script request was not intercepted - its retry is untested in this run');
  }
} catch (error) {
  console.log(`FAIL ${error.message}`);
  failures++;
}

for (const n of notes) console.log(`NOTE ${n}`);
console.log(failures === 0 ? 'ALL PASS' : `${failures} FAILED`);
process.exit(failures === 0 ? 0 : 1);
