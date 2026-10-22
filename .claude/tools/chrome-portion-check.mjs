// A person's portion through the running app, from two browser profiles: A desktop-sized, B phone-sized.
//
// B (phone) opens People, taps the person named on the command line and sets them to half a serve. The server's row holds
// Portion 0.5; A's People list says "eats half a serve"; A's calendar this week counts serves in halves ("½" in a batch's
// serves line, when anything this week is cooked for everyone). B sets them back when given --restore.
//
//   node .claude/tools/chrome-portion-check.mjs <scratchDir> <person name> [--restore]
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19375/19376).
import { mkdirSync } from 'node:fs';
import { launch, check, finish, settled, server, click, phone, screenshot, sleep, until } from './lib/profiles.mjs';

const [scratch, name, flag] = process.argv.slice(2);
if (!scratch || !name) { console.error('usage: chrome-portion-check.mjs <scratchDir> <person name> [--restore]'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 4 minutes'); process.exit(3); }, 240_000).unref();
mkdirSync(scratch, { recursive: true });

const a = await launch(scratch, 'portA', 19375, 'people');
const b = await launch(scratch, 'portB', 19376, 'people');
await a.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false });
await phone(b);
const id = server(`select lower(Id) from Person where Name = '${name.replaceAll("'", "''")}' and Deleted = 0`);
if (!check(!!id, `"${name}" is in the household`)) finish();
const row = `[data-testid=person-row][data-id="${id}"]`;

async function setPortion(p, value) {
  await until(p, `!!document.querySelector('${row} .person-line')`, 30_000);
  await p.evaluate(`document.querySelector('${row} .person-line').click()`);
  await until(p, `!!document.querySelector('[data-testid=person-portion]')`);
  await p.evaluate(`(() => { const s = document.querySelector('[data-testid=person-portion]'); s.value = '${value}'; s.dispatchEvent(new Event('change', { bubbles: true })); })()`);
  await sleep(150);
  await click(p, '[data-testid=person-save]');
}
async function serverPortion(want) {
  // Stored as decimal text ("1.0", "0.5"): compared as numbers.
  for (const end = Date.now() + 20_000; Date.now() < end; await sleep(250))
    if (Number(server(`select coalesce(Portion, '') from Person where lower(Id) = '${id}'`)) === Number(want)) return true;
  return false;
}

if (flag === '--restore') {
  await setPortion(b, '1');
  check(await serverPortion('1'), `B sets ${name} back to a full serve`);
  finish();
}

await setPortion(b, '0.5');
check(await serverPortion('0.5'), `B puts ${name} on half a serve: the server's row holds 0.5`);
console.log(`  phone screenshot (people): ${await screenshot(b, 'people')}`);

await settled(a, 30_000);
check(await until(a, `document.querySelector('${row} [data-testid=person-portion-note]')?.textContent.trim() === 'eats half a serve'`, 30_000),
  `A's People list: "${name} ... eats half a serve"`);

await a.go('calendar');
await until(a, `!!document.querySelector('[data-testid=cal-grid]')`, 30_000);
await sleep(1500);
const lines = await a.evaluate(`[...document.querySelectorAll('[data-testid=cal-entry-serves]')].map(e => e.textContent.trim())`);
console.log(`  this week's serves lines: ${lines.join(' | ') || '(none)'}`);
check(lines.length === 0 || lines.some(l => /[½¼¾]/.test(l)), 'A counts this week\'s serves in part serves');
finish();
