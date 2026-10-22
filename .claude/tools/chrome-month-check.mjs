// The calendar's month at a glance through the running app, from two browser profiles: A desktop-sized, B phone-sized.
//
// 1. A makes a meal. B (phone) opens Plan and switches to Month: the address says the month, the grid is whole weeks
//    Monday to Sunday, today is marked, a pip per slot on every day.
// 2. B taps the first day to come with nothing planned: the week view opens on that day. B plans the meal in its first
//    slot. Back on the month, that day has one planned pip and is "partial" (or "full" with one slot), and the band's count
//    of days with nothing yet is one less.
// 3. A (desktop) on the same month sees the meal's name in that day after sync; A skips it from the week view; B's month
//    shows that slot skipped and the day empty again.
// 4. Fill from a plan, from this month: the dialog offers the weeks left in the month. The month before and after, and
//    "This month" back.
//
//   node .claude/tools/chrome-month-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19371/19372); names carry
// the run id, so runs can share a server database. Needs at least one slot of the day.
import { mkdirSync } from 'node:fs';
import { RUN, launch, check, finish, settled, server, click, typeInto, phone, screenshot, sleep, until } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-month-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 5 minutes'); process.exit(3); }, 300_000).unref();
mkdirSync(scratch, { recursive: true });

const a = await launch(scratch, 'monA', 19371, 'meals');
const b = await launch(scratch, 'monB', 19372, 'calendar');
await a.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false });
await phone(b);
console.log(`run ${RUN}: both profiles online, A desktop-sized, B phone-sized`);
async function serverHas(sql, ok = v => v !== '', ms = 20_000) {
  for (const end = Date.now() + ms; Date.now() < end; await sleep(250)) { const v = server(sql); if (ok(v)) return v; }
  return server(sql);
}
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const today = new Date(); today.setHours(12, 0, 0, 0);
const month = iso(today).slice(0, 7);
const text = (p, testid) => p.evaluate(`document.querySelector('[data-testid=${testid}]')?.textContent.trim().replace(/\\s+/g, ' ')`);
const dayCell = date => `document.querySelector('[data-testid=cal-month-day][data-date="${date}"]')`;
const pips = (p, date) => p.evaluate(`[...${dayCell(date)}.querySelectorAll('[data-testid=cal-month-pip]')].map(e => e.dataset.state).join(',')`);
const nothingYet = s => Number(/(\d+) with nothing yet/.exec(s ?? '')?.[1] ?? 0);

// --- A: a meal ------------------------------------------------------------------------------------------------------------
const meal = `Month stew ${RUN}`;
await until(a, `!!document.getElementById('meal-new-name')`, 30_000);
await a.evaluate(typeInto('meal-new-name', meal)); await sleep(100);
await click(a, '[data-testid=meal-add]');
await until(a, `!!document.getElementById('meal-edit-name')`);
await a.evaluate(`(() => { const e = document.querySelector('[data-testid=meal-kind-Cooked]'); (e.querySelector('input') ?? e).click(); })()`);
await sleep(150); await a.evaluate(typeInto('meal-edit-serves', '4')); await sleep(150);
await click(a, '[data-testid=meal-save]');
const mealId = await serverHas(`select lower(Id) from Meal where Name = '${meal}'`);
check(!!mealId, `A makes "${meal}"`);

// --- B: the month ---------------------------------------------------------------------------------------------------------
await until(b, `!!document.querySelector('[data-testid=cal-view-month]')`, 30_000);
await click(b, '[data-testid=cal-view-month]');
check(await until(b, `document.querySelectorAll('[data-testid=cal-month-day]').length >= 28`) && await b.evaluate(`location.search`) === `?month=${month}`,
  `B switches Plan to Month: ${await b.evaluate('location.pathname + location.search')}, "${await text(b, 'cal-month-title')}"`);
const dates = await b.evaluate(`[...document.querySelectorAll('[data-testid=cal-month-day]')].map(e => e.dataset.date)`);
const first = new Date(`${dates[0]}T12:00:00`), last = new Date(`${dates.at(-1)}T12:00:00`);
check(first.getDay() === 1 && last.getDay() === 0 && dates.length % 7 === 0 && dates.includes(`${month}-01`)
  && dates.every((d, i) => i === 0 || (new Date(`${d}T12:00:00`) - new Date(`${dates[i - 1]}T12:00:00`)) / 864e5 === 1),
  `whole weeks, Monday ${dates[0]} to Sunday ${dates.at(-1)} (${dates.length / 7} weeks), every day once`);
check(await b.evaluate(`${dayCell(iso(today))}.getAttribute('aria-current')`) === 'date', 'today is marked');
const slotCount = Number(server(`select count(*) from Slot where Deleted = 0`));
check(await b.evaluate(`[...document.querySelectorAll('[data-testid=cal-month-day]')].every(d => d.querySelectorAll('[data-testid=cal-month-pip]').length === ${slotCount})`),
  `a pip per slot on every day (${slotCount} slots)`);
console.log(`  phone screenshot (month): ${await screenshot(b, 'month')}`);

// The first day to come, this month or next, with nothing planned.
let target = await b.evaluate(`[...document.querySelectorAll('[data-testid=cal-month-day][data-cover=Empty]')].map(e => e.dataset.date)
  .find(d => d > '${iso(today)}' && d.startsWith('${month}'))`);
if (!target) {
  await click(b, '[data-testid=cal-next]');
  await until(b, `!location.search.endsWith('${month}')`);
  await until(b, `!!document.querySelector('[data-testid=cal-month-day][data-cover=Empty]')`);
  target = await b.evaluate(`[...document.querySelectorAll('[data-testid=cal-month-day][data-cover=Empty]')].map(e => e.dataset.date)
    .find(d => d.slice(0, 7) === location.search.slice(-7))`);
}
const targetMonth = target.slice(0, 7);
const before = await text(b, 'cal-month-summary');
console.log(`  the day to plan: ${target}; the band says "${before}"`);

await b.evaluate(`${dayCell(target)}.click()`);
const monday = (() => { const d = new Date(`${target}T12:00:00`); d.setDate(d.getDate() - ((d.getDay() + 6) % 7)); return iso(d); })();
check(await until(b, `!!document.querySelector('[data-testid=cal-day-view]') && location.search === '?week=${monday}&day=${target}'`)
  && await until(b, `document.querySelector('[data-testid=cal-day] .mud-button-filled, [data-testid=cal-day].mud-button-filled')?.dataset.date === '${target}'
       || [...document.querySelectorAll('[data-testid=cal-day]')].find(e => e.getAttribute('aria-selected') === 'true')?.dataset.date === '${target}'`),
  `tapping it opens the week view on that day: "${await text(b, 'cal-day-title')}"`);

const slot = await b.evaluate(`document.querySelector('[data-testid=cal-day-view] [data-testid=cal-cell]').dataset.slot`);
await b.evaluate(`document.querySelector('[data-testid=cal-day-view] [data-testid=cal-cell][data-slot="${slot}"] [data-testid=cal-cell-add]').click()`);
await until(b, `!!document.getElementById('cal-search')`);
await b.evaluate(typeInto('cal-search', meal)); await sleep(300);
await click(b, `[data-testid=cal-meal-option][data-id="${mealId}"]`); await sleep(250);
await click(b, '[data-testid=cal-save]');
const entry = await serverHas(`select lower(Id) from CalendarEntry where MealId = '${mealId.toUpperCase()}' and Date = '${target}'`);
check(!!entry, `B plans "${meal}" on ${target} in its first slot`);

await click(b, '[data-testid=cal-view-month]');
await until(b, `!!${dayCell(target)}`);
const planned = slotCount === 1 ? 'Full' : 'Partial';
check(await until(b, `${dayCell(target)}?.dataset.cover === '${planned}'`) && (await pips(b, target)).split(',')[0] === 'Planned',
  `back on the month: ${target} is ${planned}, its first pip planned (${await pips(b, target)})`);
const after = await text(b, 'cal-month-summary');
check(targetMonth !== month || nothingYet(after) === nothingYet(before) - 1, `the band: "${after}"`);

// --- A: the meal on the desktop month, then skipped -------------------------------------------------------------------------
await settled(a, 30_000);
await a.go(`calendar?month=${targetMonth}`);
await until(a, `!!document.querySelector('[data-testid=cal-month]')`, 30_000);
check(await until(a, `${dayCell(target)}?.querySelector('.mon-meals')?.textContent.includes(${JSON.stringify(meal)})`, 30_000),
  `A's desktop month lists "${meal}" on ${target}`);
console.log(`  desktop screenshot (month): ${await screenshot(a, 'month')}`);
await a.evaluate(`${dayCell(target)}.click()`);
await until(a, `!!document.querySelector('[data-testid=cal-grid] [data-testid=cal-entry][data-id="${entry}"]')`);
await click(a, `[data-testid=cal-grid] [data-testid=cal-entry][data-id="${entry}"]`);
await until(a, `!!document.querySelector('[data-testid=cal-skip]')`);
await click(a, '[data-testid=cal-skip]');
check(await serverHas(`select Status from CalendarEntry where Id = '${entry.toUpperCase()}'`, v => v === 'Skipped') === 'Skipped', 'A skips it');

await settled(b, 30_000);
check(await until(b, `${dayCell(target)}?.dataset.cover === 'Empty'`, 30_000) && (await pips(b, target)).split(',')[0] === 'Skipped',
  `B's month: ${target} empty again, its first pip skipped (${await pips(b, target)})`);

// Removed again, so no skipped entry is left "to replan" for the other checks sharing this database.
await settled(a, 30_000);
await a.go(`calendar?month=${targetMonth}`);
await until(a, `document.querySelectorAll('[data-testid=cal-month-day]').length >= 28`, 30_000);
const listed = await until(a, `!!document.querySelector('[data-testid=cal-replan-entry][data-id="${entry}"]')`, 30_000);
await click(a, `[data-testid=cal-replan-entry][data-id="${entry}"]`);
const sheet = await until(a, `!!document.querySelector('[data-testid=cal-remove]')`);
await click(a, '[data-testid=cal-remove]');
check(listed && sheet && await serverHas(`select Deleted from CalendarEntry where Id = '${entry.toUpperCase()}'`, v => v === '1') === '1',
  `A removes it from "To replan", under the month (listed ${listed}, sheet ${sheet})`);

// --- Fill from a plan, and moving between months ---------------------------------------------------------------------------
await b.go(`calendar?month=${month}`);
await until(b, `!!document.querySelector('[data-testid=cal-month]')`, 30_000);
await click(b, '[data-testid=cal-apply]');
const lastDay = new Date(today.getFullYear(), today.getMonth() + 1, 0, 12);
const mondayOf = d => { const m = new Date(d); m.setDate(m.getDate() - ((m.getDay() + 6) % 7)); return m; };
const weeksLeft = Math.round((mondayOf(lastDay) - mondayOf(today)) / (7 * 864e5)) + 1;
if (await until(b, `!!document.querySelector('[data-testid=apply-count]')`, 5_000)) {
  const offered = Number(await b.evaluate(`document.querySelector('[data-testid=apply-count]').value`));
  check(offered >= weeksLeft && offered <= weeksLeft + 1, `Fill from a plan offers the month's ${weeksLeft} weeks left from this week (${offered})`);
} else {
  check(await b.evaluate(`!!document.querySelector('[data-testid=apply-no-plans]')`), 'Fill from a plan: no plans yet, said so');
}
await click(b, '[data-testid=apply-cancel]');
await click(b, '[data-testid=cal-next]');
check(await until(b, `!!document.querySelector('[data-testid=cal-this-month]') && location.search !== '?month=${month}'`),
  `the month after: "${await text(b, 'cal-month-title')}", with "This month"`);
await click(b, '[data-testid=cal-this-month]');
check(await until(b, `location.search === '?month=${month}' && !document.querySelector('[data-testid=cal-this-month]')`), '"This month" comes back');
await click(b, '[data-testid=cal-view-week]');
check(await until(b, `!!document.querySelector('[data-testid=cal-day-view]')`) && await b.evaluate(`!!document.querySelector('[data-testid=cal-week]')`),
  `Week goes back to the week view: "${await text(b, 'cal-week')}"`);
finish();
