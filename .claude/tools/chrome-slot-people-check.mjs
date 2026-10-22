// Who usually eats in a slot, through the running app, from two browser profiles: A desktop-sized, B phone-sized.
//
// B (phone) opens Slots and taps the named person's chip under Breakfast. The server's Slot row holds their id; A's
// Slots page shows the chip chosen and A's browser SQLite holds the same People; on A's calendar, + in a Breakfast cell
// opens the sheet with only that person chosen, and + in a Dinner cell with Everyone (cancelled - nothing is written).
// B taps Everyone when given --restore.
//
//   node .claude/tools/chrome-slot-people-check.mjs <scratchDir> <person name> [--restore]
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19377/19378).
import { mkdirSync } from 'node:fs';
import { launch, check, finish, settled, server, click, phone, screenshot, sleep, until, dump } from './lib/profiles.mjs';

const [scratch, name, flag] = process.argv.slice(2);
if (!scratch || !name) { console.error('usage: chrome-slot-people-check.mjs <scratchDir> <person name> [--restore]'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 4 minutes'); process.exit(3); }, 240_000).unref();
mkdirSync(scratch, { recursive: true });

const BREAKFAST = '00000000-5eed-7000-8000-000000000401';
const a = await launch(scratch, 'slotA', 19377, 'slots');
const b = await launch(scratch, 'slotB', 19378, 'slots');
await a.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false });
await phone(b);
const id = server(`select lower(Id) from Person where Name = '${name.replaceAll("'", "''")}' and Deleted = 0`);
if (!check(!!id, `"${name}" is in the household`)) finish();
const row = `[data-testid=slot-row][data-id="${BREAKFAST}"]`;
const chosen = selector => `document.querySelector('${selector}')?.classList.contains('mud-chip-color-primary') === true`;

async function serverPeople(want) {
  for (const end = Date.now() + 20_000; Date.now() < end; await sleep(250))
    if (server(`select coalesce(lower(People), '') from Slot where lower(Id) = '${BREAKFAST}'`) === want) return true;
  return false;
}

await until(b, `!!document.querySelector('${row} [data-testid=slot-everyone]')`, 30_000);
if (flag === '--restore') {
  await click(b, `${row} [data-testid=slot-everyone]`);
  check(await serverPeople(''), 'B sets Breakfast back to everyone: the server\'s People is null');
  finish();
}

await click(b, `${row} [data-testid=slot-person][data-id="${id}"]`);
check(await serverPeople(id), `B makes Breakfast usually for ${name}: the server's Slot row holds their id`);
console.log(`  phone screenshot (slots): ${await screenshot(b, 'slots')}`);

await settled(a, 30_000);
check(await until(a, `${chosen(`${row} [data-testid=slot-person][data-id="${id}"]`)} && !${chosen(`${row} [data-testid=slot-everyone]`)}`, 30_000),
  `A's Slots page: Breakfast shows ${name} chosen, not Everyone`);
const local = await dump(a);
check(local(`select lower(People) from Slot where lower(Id) = '${BREAKFAST}'`) === id, `A's browser SQLite holds the same People`);

await a.go('calendar');
await until(a, `!!document.querySelector('[data-testid=cal-grid]')`, 30_000);
async function sheetFor(slotName) {
  await a.evaluate(`[...document.querySelectorAll('[data-testid=cal-cell-add]')].find(e => e.getAttribute('aria-label')?.endsWith(' ${slotName}'))?.click()`);
  await until(a, `!!document.querySelector('[data-testid=cal-everyone]')`);
  const result = {
    everyone: await a.evaluate(chosen('[data-testid=cal-everyone]')),
    person: await a.evaluate(chosen(`[data-testid=cal-person][data-id="${id}"]`)),
  };
  await click(a, '[data-testid=cal-cancel]');
  await until(a, `!document.querySelector('[data-testid=cal-everyone]')`);
  return result;
}
const breakfast = await sheetFor('Breakfast');
check(breakfast.person && !breakfast.everyone, `A's new Breakfast entry starts for ${name} only`);
const dinner = await sheetFor('Dinner');
check(dinner.everyone && !dinner.person, 'A\'s new Dinner entry starts for everyone');
finish();
