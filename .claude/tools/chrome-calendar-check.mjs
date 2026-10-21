// The calendar through the running app, from two browser profiles: A desktop-sized (the week grid), B phone-sized (a day
// at a time). A plan is filled onto real dates, an entry's alternative chosen, one skipped and replanned from the "to
// replan" list, one added by hand, one removed - and the plan applied again from both profiles, once while both are
// offline, without a duplicate or an undone change. The rows are read back from both profiles' SQLite and the server's.
//
//   node .claude/tools/chrome-calendar-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19356/19357); names carry
// the run id. APP_URL / SERVER_DB as in chrome-sync-check.mjs.
import { RUN, launch, check, finish, settled, server, dump, click, typeInto, phone, screenshot, sleep, until, ingredients } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-calendar-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 10 minutes'); process.exit(3); }, 600_000).unref();

const a = await launch(scratch, 'calA', 19356, 'meals');
const b = await launch(scratch, 'calB', 19357, 'meals');
await a.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
await phone(b);
console.log(`run ${RUN}: both profiles online, A desktop-sized, B phone-sized`);
const up = id => id.toUpperCase();
const LUNCH = '00000000-5eed-7000-8000-000000000403', DINNER = '00000000-5eed-7000-8000-000000000405';

// Dates as the browsers see them: next week's Monday onwards.
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const today = new Date(); today.setHours(12, 0, 0, 0);
const mon = new Date(today); mon.setDate(today.getDate() - ((today.getDay() + 6) % 7) + 7);
const day = n => { const d = new Date(mon); d.setDate(mon.getDate() + n); return iso(d); };
console.log(`  next week begins ${day(0)}`);

// --- Meals: two single-serve, a takeaway at $45 for 3, a takeaway with no price yet --------------------------------
const noodles = `Noodles ${RUN}`, soup = `Soup ${RUN}`, takeaway = `Takeaway ${RUN}`, unpriced = `Mystery ${RUN}`;
async function addMeal(p, name, kind, cents, serves) {
  await p.go('meals');
  if (!await until(p, `!!document.getElementById('meal-new-name')`, 30_000)) return false;
  await p.evaluate(typeInto('meal-new-name', name)); await sleep(100);
  await click(p, '[data-testid=meal-add]');
  await until(p, `!!document.getElementById('meal-edit-name')`, 10_000);
  await p.evaluate(`(() => { const e = document.querySelector('[data-testid=meal-kind-${kind}]'); (e.querySelector('input') ?? e).click(); })()`);
  await sleep(200);
  if (cents !== undefined) await p.evaluate(typeInto('meal-edit-fixed-cost', cents));
  if (serves !== undefined) await p.evaluate(typeInto('meal-edit-serves', serves));
  await sleep(150);
  await click(p, '[data-testid=meal-save]');
  return until(p, `document.querySelector('[data-testid=meal-title]')?.textContent.trim() === ${JSON.stringify(name)}`);
}
check(await addMeal(a, noodles, 'Simple') && await addMeal(a, soup, 'Simple') && await addMeal(a, takeaway, 'FixedCost', '45', '3'),
  'A adds three meals');
// The fourth uses an ingredient with no price, so its cost is incomplete.
const spice = `Spice ${RUN}`;
await a.go('ingredients');
check(await ingredients(a).add(spice), 'A adds an ingredient with no price');
check(await addMeal(a, unpriced, 'Simple'), 'and a meal');
await click(a, '[data-testid=meal-ingredient-add]');
await until(a, `!!document.getElementById('meal-ingredient-search')`);
await a.evaluate(typeInto('meal-ingredient-search', spice)); await sleep(300);
await a.evaluate(`[...document.querySelectorAll('[data-testid=meal-ingredient-choice]')].find(o => o.textContent.includes(${JSON.stringify(spice)}))?.click()`);
await until(a, `!!document.getElementById('meal-ingredient-quantity')`);
await a.evaluate(typeInto('meal-ingredient-quantity', '100')); await sleep(150);
await click(a, '[data-testid=meal-ingredient-save]');
check(await until(a, `!!document.querySelector('[data-testid=meal-ingredient-row]')`), 'that uses it');
const mealId = name => server(`select lower(Id) from Meal where Name = '${name}'`);
const [noodlesId, soupId, takeawayId] = [mealId(noodles), mealId(soup), mealId(takeaway)];

// --- A two-week plan ------------------------------------------------------------------------------------------------
const plan = `Rotation ${RUN}`;
await a.go('plans');
await until(a, `!!document.getElementById('plan-new-name')`, 30_000);
await a.evaluate(typeInto('plan-new-name', plan)); await a.evaluate(typeInto('plan-new-weeks', '2')); await sleep(100);
await click(a, '[data-testid=plan-add]');
await until(a, `!!document.querySelector('[data-testid=plan-grid]')`, 20_000);
const planId = await a.evaluate(`location.pathname.split('/').pop()`);
async function planEntry(week, dayName, slot, meals) {
  await click(a, `[data-testid=plan-week][data-week="${week}"]`); await sleep(300);
  await a.evaluate(`document.querySelector('[data-testid=plan-grid] [data-testid=plan-cell][data-day=${dayName}][data-slot="${slot}"] [data-testid=plan-cell-add]').click()`);
  await until(a, `!!document.getElementById('entry-search')`);
  for (const meal of meals) {
    await a.evaluate(typeInto('entry-search', meal)); await sleep(200);
    await a.evaluate(`[...document.querySelectorAll('[data-testid=entry-meal-option]')].find(o => o.textContent.trim() === ${JSON.stringify(meal)})?.click()`);
    await sleep(200);
  }
  await click(a, '[data-testid=entry-save]');
  return until(a, `[...document.querySelectorAll('[data-testid=plan-grid] [data-testid=plan-cell][data-day=${dayName}][data-slot="${slot}"] [data-testid=plan-entry-meals]')]
    .some(e => e.textContent.trim() === ${JSON.stringify(meals.join(' / '))})`);
}
check(await planEntry(1, 'Monday', DINNER, [noodles, soup]) && await planEntry(1, 'Tuesday', LUNCH, [unpriced])
  && await planEntry(2, 'Monday', DINNER, [takeaway]), 'A plans week 1 Monday dinner (noodles or soup), Tuesday lunch, week 2 Monday dinner');
await settled(a);
const planEntries = `select Id from TemplateEntry where TemplateId = '${up(planId)}'`;

// --- The calendar ---------------------------------------------------------------------------------------------------
const cell = (p, date, slot) => p === a
  ? `document.querySelector('[data-testid=cal-grid] [data-testid=cal-cell][data-date="${date}"][data-slot="${slot}"]')`
  : `document.querySelector('[data-testid=cal-day-view] [data-testid=cal-cell][data-date="${date}"][data-slot="${slot}"]')`;
// Only this run's entries: earlier runs leave theirs on the same dates.
const ours = (p, date, slot) => `[...(${cell(p, date, slot)}?.querySelectorAll('[data-testid=cal-entry]') ?? [])]
  .filter(e => e.querySelector('[data-testid=cal-entry-meal]').textContent.trim().endsWith(${JSON.stringify(RUN)}))`;
const costs = (p, date, slot) => p.evaluate(`${ours(p, date, slot)}.map(e => e.querySelector('[data-testid=cal-entry-cost]').textContent.trim())`);
const shows = (p, date, slot, list) => until(p, `JSON.stringify(${ours(p, date, slot)}.map(e => e.querySelector('[data-testid=cal-entry-meal]').textContent.trim())) === ${JSON.stringify(JSON.stringify(list))}`);
const entryOf = (p, date, slot) => `${ours(p, date, slot)}[0]`;
const openWeek = async (p, monday) => { await p.go(`calendar?week=${monday}`); return until(p, `document.querySelector('[data-testid=cal-week]') && !!document.querySelector('[data-testid=cal-apply]')`, 30_000); };
const pickDay = async (p, date) => { await click(p, `[data-testid=cal-day][data-date="${date}"]`); await sleep(300); };
async function fill(p, week, count) {
  await click(p, '[data-testid=cal-apply]');
  await until(p, `!!document.querySelector('[data-testid=apply-plan]')`);
  const select = (testid, value) => p.evaluate(`(() => { const s = document.querySelector('[data-testid=${testid}]'); s.value = ${JSON.stringify(value)};
    s.dispatchEvent(new Event('change', { bubbles: true })); return s.value === ${JSON.stringify(value)}; })()`);
  await select('apply-plan', planId); await sleep(200);
  await select('apply-week', String(week)); await select('apply-count', String(count)); await sleep(200);
  await click(p, '[data-testid=apply-run]');
  await until(p, `!!document.querySelector('[data-testid=cal-applied]')`, 20_000);
  return p.evaluate(`document.querySelector('[data-testid=cal-applied]').textContent.trim()`);
}
const calendarRows = `select lower(Id) || '|' || coalesce(lower(TemplateEntryId), '-') || '|' || Date || '|' || lower(SlotId) || '|' || coalesce(lower(MealId), '-')
  || '|' || coalesce(People, '-') || '|' || Status || '|' || coalesce(MovedDate, '-') || '|' || coalesce(lower(MovedSlotId), '-') || '|' || Deleted
  from CalendarEntry where TemplateEntryId in (${planEntries}) or MealId in ('${up(noodlesId)}', '${up(soupId)}', '${up(takeawayId)}') order by Id`;

check(await openWeek(a, day(0)) && await until(a, `!!document.querySelector('[data-testid=cal-grid]')`) && !await a.evaluate(`!!document.querySelector('[data-testid=cal-day-view]')`),
  'A opens next week: on a desktop it is the week grid');
const filled = await fill(a, 1, 2);
check(/added 3 entries/.test(filled), `A fills two weeks from the plan, next week as week 1 ("${filled}")`);
check(await shows(a, day(0), DINNER, [noodles]), 'Monday dinner is the usual choice, noodles');
check(JSON.stringify(await costs(a, day(0), DINNER)) === '["no ingredients yet"]', `with what a serve costs: ${JSON.stringify(await costs(a, day(0), DINNER))}`);
check(await shows(a, day(1), LUNCH, [unpriced]) && /^from \$0\.00 a serve \+ 1 unpriced$/.test((await costs(a, day(1), LUNCH))[0]),
  `an unpriced meal shows as incomplete, never $0: ${JSON.stringify(await costs(a, day(1), LUNCH))}`);
console.log(`  desktop screenshot: ${await screenshot(a, 'calendar-week')}`);
check(server(`select StartsOn from PlanTemplate where Id = '${up(planId)}'`) === day(0), `the plan remembers week 1 began ${day(0)}`);

await openWeek(a, day(7));
check(await shows(a, day(7), DINNER, [takeaway]) && (await costs(a, day(7), DINNER))[0] === '$15.00 a serve', 'the week after is week 2: the $45 takeaway for 3 at $15.00 a serve');
check((await a.evaluate(`document.querySelector('[data-testid=cal-plan-weeks]')?.textContent`))?.includes(`${plan}: week 2`), 'and it says which week of the plan it is');

// --- B on a phone: choose the alternative ---------------------------------------------------------------------------
check(await openWeek(b, day(0)) && await until(b, `!!document.querySelector('[data-testid=cal-day-view]')`) && !await b.evaluate(`!!document.querySelector('[data-testid=cal-grid]')`),
  'B opens next week: on a phone it is a day at a time');
await pickDay(b, day(0));
check(await shows(b, day(0), DINNER, [noodles]), 'B shows Monday dinner, noodles');
await b.evaluate(`${entryOf(b, day(0), DINNER)}.click()`);
check(await until(b, `document.querySelectorAll('[data-testid=cal-choice]').length === 2`), 'its sheet offers the plan\'s two choices');
console.log(`  phone screenshot: ${await screenshot(b, 'calendar-sheet')}`);
await click(b, `[data-testid=cal-choice][data-id="${soupId}"]`); await sleep(150);
await click(b, '[data-testid=cal-save]');
check(await shows(b, day(0), DINNER, [soup]), 'B chooses soup');
await openWeek(a, day(0));
check(await shows(a, day(0), DINNER, [soup]), 'A shows the soup B chose');
console.log(`  phone screenshot: ${await screenshot(b, 'calendar-day')}`);

// --- A skips Tuesday's lunch; B replans it from "to replan" ---------------------------------------------------------
await a.evaluate(`${entryOf(a, day(1), LUNCH)}.click()`);
await until(a, `!!document.querySelector('[data-testid=cal-skip]')`);
await click(a, '[data-testid=cal-skip]');
check(await until(a, `${entryOf(a, day(1), LUNCH)}?.dataset.skipped === 'true'`), 'A skips Tuesday lunch: struck through, still on Tuesday');
check(await until(a, `!!document.querySelector('[data-testid=cal-replan-entry]')`), 'and it is listed to replan');
check(await until(b, `!!document.querySelector('[data-testid=cal-replan-entry]')`), 'B lists it to replan too');
await click(b, '[data-testid=cal-replan-entry]');
await until(b, `!!document.querySelector('[data-testid=cal-sheet-skipped]')`);
await b.evaluate(`(() => { const d = document.querySelector('[data-testid=cal-move-date]'); d.value = ${JSON.stringify(day(2))}; d.dispatchEvent(new Event('change', { bubbles: true }));
  const s = document.querySelector('[data-testid=cal-move-slot]'); s.value = ${JSON.stringify(DINNER)}; s.dispatchEvent(new Event('change', { bubbles: true })); })()`);
await sleep(200);
await click(b, '[data-testid=cal-save]');
check(await until(a, `!document.querySelector('[data-testid=cal-replan-entry]')`) && await shows(a, day(2), DINNER, [unpriced]) && await shows(a, day(1), LUNCH, []),
  'B replans it on Wednesday dinner: A shows it there, not on Tuesday, and nothing left to replan');

// --- A adds one by hand and removes one -----------------------------------------------------------------------------
await a.evaluate(`${cell(a, day(3), LUNCH)}.querySelector('[data-testid=cal-cell-add]').click()`);
await until(a, `!!document.getElementById('cal-search')`);
await a.evaluate(typeInto('cal-search', takeaway)); await sleep(200);
await click(a, `[data-testid=cal-meal-option][data-id="${takeawayId}"]`); await sleep(150);
await click(a, '[data-testid=cal-save]');
check(await shows(a, day(3), LUNCH, [takeaway]), 'A adds a takeaway to Thursday lunch by hand');
await openWeek(a, day(7));
await a.evaluate(`${entryOf(a, day(7), DINNER)}.click()`);
await until(a, `!!document.querySelector('[data-testid=cal-remove]')`);
await click(a, '[data-testid=cal-remove]');
check(await shows(a, day(7), DINNER, []), 'A removes the week after\'s takeaway');

// --- Applying again: from both, then from both offline at once ------------------------------------------------------
await settled(a); await settled(b);
const before = server(calendarRows);
await openWeek(a, day(0));
const again = await fill(a, 1, 2);
check(/added 0 entries; 3 already on the calendar/.test(again), `A fills the same two weeks again: "${again}"`);
await openWeek(b, day(0));
const againB = await fill(b, 1, 2);
check(/added 0 entries; 3 already/.test(againB), `and so does B: "${againB}"`);
await settled(a); await settled(b);
check(server(calendarRows) === before, 'neither wrote anything: the soup, the replan, the hand-added entry and the removal all stand');

await openWeek(a, day(14)); await openWeek(b, day(14));
await a.offline(true); await b.offline(true);
const offA = await fill(a, 1, 1), offB = await fill(b, 1, 1);
check(/added 2 entries/.test(offA) && /added 2 entries/.test(offB), `both offline, A and B each fill the week after next ("${offA}" / "${offB}")`);
await a.offline(false); await b.offline(false);
await settled(a, 30_000); await settled(b, 30_000);
await sleep(1000);
const third = server(`select count(*) || '|' || count(distinct Id) from CalendarEntry where Date between '${day(14)}' and '${day(20)}' and TemplateEntryId in (${planEntries})`);
check(third === '2|2', `back online, the server holds each of that week's entries once (${third})`);
await openWeek(a, day(14)); await pickDay(b, day(14));
check(await shows(a, day(14), DINNER, [noodles]) && await shows(b, day(14), DINNER, [noodles]), 'and both show one noodles dinner that Monday');

// --- The rows, in all three -----------------------------------------------------------------------------------------
await settled(a); await settled(b);
const rows = server(calendarRows);
console.log(rows.split('\n').map(r => `    ${r}`).join('\n'));
check(rows.split('\n').length === 6, `six calendar rows: three from the first fill (one now a tombstone), the hand-added one, two from the offline fills (${rows.split('\n').length})`);
for (const p of [a, b]) {
  const q = await dump(p);
  check(q(calendarRows) === rows && q('select count(*) from Outbox') === '0', `${p.name} SQLite holds the same calendar rows as the server, outbox empty`);
}

finish();
