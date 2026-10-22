// Batches and serves through the running app, from two browser profiles: A desktop-sized (the week grid), B phone-sized
// (a day at a time). A stew is cooked on Monday with leftovers lunches after it; its serves are counted down, a skip of the
// cooking flags every lunch, a skipped lunch gives its serves back, the serves made are changed, a lunch is set to eat a
// chosen batch, and both profiles add a lunch while offline. The rows are read back from both profiles' SQLite and the server's.
//
//   node .claude/tools/chrome-serves-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19358/19359); names carry
// the run id, and it uses next week, where earlier runs' entries are told apart by meal name.
import { RUN, launch, check, finish, settled, server, dump, click, typeInto, phone, screenshot, sleep, until } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-serves-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 10 minutes'); process.exit(3); }, 600_000).unref();

const a = await launch(scratch, 'srvA', 19358, 'meals');
const b = await launch(scratch, 'srvB', 19359, 'meals');
await a.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
await phone(b);
console.log(`run ${RUN}: both profiles online, A desktop-sized, B phone-sized`);
const up = id => id.toUpperCase();
// The server's answer once it has one: the sync chip reads "online, 0 waiting" before a write made a moment ago is queued.
async function serverHas(sql, ok = v => v !== '', ms = 20_000) {
  for (const end = Date.now() + ms; Date.now() < end; await sleep(250)) { const v = server(sql); if (ok(v)) return v; }
  return server(sql);
}
const LUNCH = '00000000-5eed-7000-8000-000000000403', DINNER = '00000000-5eed-7000-8000-000000000405';

const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const today = new Date(); today.setHours(12, 0, 0, 0);
const mon = new Date(today); mon.setDate(today.getDate() - ((today.getDay() + 6) % 7) + 7);
const day = n => { const d = new Date(mon); d.setDate(mon.getDate() + n); return iso(d); };
// A week nobody has used, a week either side included: leftovers eat any batch cooked before them, so an earlier run's
// stew in the same week would be eaten first - which is the rule working, and not what this check is about.
while (server(`select count(*) from CalendarEntry where coalesce(MovedDate, Date) between '${day(-14)}' and '${day(13)}'`) !== '0')
  mon.setDate(mon.getDate() + 7);
// Everyone is the household: each entry for everyone eats each person's portion (a full serve unless set). The serves
// made below are whole multiples of it, so the portions must add up to whole serves.
const H = Math.max(1, Number(server(`select coalesce(sum(coalesce(cast(Portion as real), 1)), 0) from Person where Deleted = 0`)));
if (!Number.isInteger(H)) {
  console.error(`the household's portions add up to ${H} serves - this check needs whole serves (set someone's portion so they do)`);
  process.exit(2);
}
console.log(`  next week begins ${day(0)}; the household is ${H}`);

// --- Meals ----------------------------------------------------------------------------------------------------------
const stew = `Stew ${RUN}`, leftovers = `Leftovers ${RUN}`;
async function addMeal(p, name, kind, serves) {
  await p.go('meals');
  if (!await until(p, `!!document.getElementById('meal-new-name')`, 30_000)) return false;
  await p.evaluate(typeInto('meal-new-name', name)); await sleep(100);
  await click(p, '[data-testid=meal-add]');
  await until(p, `!!document.getElementById('meal-edit-name')`, 10_000);
  await p.evaluate(`(() => { const e = document.querySelector('[data-testid=meal-kind-${kind}]'); (e.querySelector('input') ?? e).click(); })()`);
  await sleep(200);
  if (serves !== undefined) await p.evaluate(typeInto('meal-edit-serves', serves));
  await sleep(150);
  await click(p, '[data-testid=meal-save]');
  return until(p, `document.querySelector('[data-testid=meal-title]')?.textContent.trim() === ${JSON.stringify(name)}`);
}
check(await addMeal(a, stew, 'Cooked', '4') && await addMeal(a, leftovers, 'Leftovers'), 'A adds a stew that makes 4 and a leftovers meal');
const stewId = await serverHas(`select lower(Id) from Meal where Name = '${stew}'`);
const leftoversId = await serverHas(`select lower(Id) from Meal where Name = '${leftovers}'`);

// --- The calendar ---------------------------------------------------------------------------------------------------
const cell = (p, date, slot) => p === a
  ? `document.querySelector('[data-testid=cal-grid] [data-testid=cal-cell][data-date="${date}"][data-slot="${slot}"]')`
  : `document.querySelector('[data-testid=cal-day-view] [data-testid=cal-cell][data-date="${date}"][data-slot="${slot}"]')`;
const ours = (p, date, slot) => `[...(${cell(p, date, slot)}?.querySelectorAll('[data-testid=cal-entry]') ?? [])]
  .filter(e => e.querySelector('[data-testid=cal-entry-meal]').textContent.trim().endsWith(${JSON.stringify(RUN)}))`;
const servesOf = (p, date, slot) => `${ours(p, date, slot)}.map(e => [e.querySelector('[data-testid=cal-entry-serves]')?.textContent.trim() ?? '',
  e.querySelector('[data-testid=cal-entry-serves]')?.classList.contains('cal-serves-problem') ?? false])`;
const says = (p, date, slot, list) => until(p, `JSON.stringify(${servesOf(p, date, slot)}) === ${JSON.stringify(JSON.stringify(list))}`, 20_000);
const read = (p, date, slot) => p.evaluate(`JSON.stringify(${servesOf(p, date, slot)})`);
const notes = p => p.evaluate(`[...document.querySelectorAll('[data-testid=cal-serves-note]')].map(n => n.textContent.trim())`);
const openWeek = async (p, monday) => { await p.go(`calendar?week=${monday}`); return until(p, `!!document.querySelector('[data-testid=cal-apply]')`, 30_000); };
const pickDay = async (p, date) => { if (p === b) { await click(p, `[data-testid=cal-day][data-date="${date}"]`); await sleep(300); } };
const entry = (p, date, slot, n = 0) => `${ours(p, date, slot)}[${n}]`;
const weekday = n => new Date(`${day(n)}T12:00:00`).toLocaleDateString('en-AU', { weekday: 'long' });
const dow = n => weekday(n).slice(0, 3);

async function add(p, date, slot, mealId, mealName, made) {
  await pickDay(p, date);
  await p.evaluate(`${cell(p, date, slot)}.querySelector('[data-testid=cal-cell-add]').click()`);
  await until(p, `!!document.getElementById('cal-search')`);
  await p.evaluate(typeInto('cal-search', mealName)); await sleep(250);
  await click(p, `[data-testid=cal-meal-option][data-id="${mealId}"]`); await sleep(250);
  if (made !== undefined) { await until(p, `!!document.getElementById('cal-serves-made')`); await p.evaluate(typeInto('cal-serves-made', String(made))); await sleep(200); }
  await click(p, '[data-testid=cal-save]');
}
async function openEntry(p, date, slot, n = 0) {
  await pickDay(p, date);
  await p.evaluate(`${entry(p, date, slot, n)}.click()`);
  return until(p, `!!document.querySelector('[data-testid=cal-save]')`);
}

check(await openWeek(a, day(0)) && await until(a, `!!document.querySelector('[data-testid=cal-grid]')`), 'A opens next week (the grid)');
await add(a, day(0), DINNER, stewId, stew, 3 * H);
for (const n of [1, 2, 5]) await add(a, day(n), LUNCH, leftoversId, leftovers);
check(await says(a, day(0), DINNER, [[`makes ${3 * H} · eaten by ${dow(2)}`, false]]),
  `A cooks the stew on Monday, ${3 * H} serves, and leftovers on Tuesday, Wednesday and Saturday: the stew "${await read(a, day(0), DINNER)}"`);
check(await says(a, day(1), LUNCH, [[`${stew} from ${dow(0)} · ${H} left after`, false]]) && await says(a, day(2), LUNCH, [[`${stew} from ${dow(0)} · 0 left after`, false]]),
  `Tuesday ${await read(a, day(1), LUNCH)}, Wednesday ${await read(a, day(2), LUNCH)}`);
check(await says(a, day(5), LUNCH, [['nothing to eat', true]]), `Saturday has nothing to eat, in red: ${await read(a, day(5), LUNCH)}`);
check((await notes(a)).includes(`${weekday(5)}'s leftovers has nothing to eat`), `the week's notes say so: ${JSON.stringify(await notes(a))}`);
console.log(`  desktop screenshot: ${await screenshot(a, 'serves-week')}`);
await settled(a);
const stewEntry = await serverHas(`select lower(Id) from CalendarEntry where MealId = '${up(stewId)}' and Date = '${day(0)}'`);
check(await serverHas(`select ServesMade from CalendarEntry where Id = '${up(stewEntry)}'`) === String(3 * H), 'the server holds the serves made on the stew\'s entry');

// --- B on a phone sees the same, then skips the cooking -------------------------------------------------------------
check(await openWeek(b, day(0)) && await until(b, `!!document.querySelector('[data-testid=cal-day-view]')`), 'B opens next week (a day at a time)');
await pickDay(b, day(0));
check(await says(b, day(0), DINNER, [[`makes ${3 * H} · eaten by ${dow(2)}`, false]]), `B shows the same batch: ${await read(b, day(0), DINNER)}`);
await pickDay(b, day(5));
check(await says(b, day(5), LUNCH, [['nothing to eat', true]]), 'and Saturday with nothing to eat');
console.log(`  phone screenshot: ${await screenshot(b, 'serves-day')}`);

await openEntry(b, day(0), DINNER);
await click(b, '[data-testid=cal-skip]');
const notCooked = `${stew} from ${dow(0)} wasn't cooked - nothing to eat`;
check(await says(a, day(1), LUNCH, [[notCooked, true]]) && await says(a, day(2), LUNCH, [[notCooked, true]]) && await says(a, day(5), LUNCH, [[notCooked, true]]),
  `B skips Monday's stew: on A every lunch says "${notCooked}"`);
check((await notes(a)).includes(`${weekday(1)}'s leftovers has nothing to eat: ${stew} on ${weekday(0)} was skipped`), `and A's notes name the skipped batch`);
await openEntry(b, day(0), DINNER);
await click(b, '[data-testid=cal-unskip]');
check(await says(a, day(1), LUNCH, [[`${stew} from ${dow(0)} · ${H} left after`, false]]), 'B un-skips it: A counts the stew again');

// --- A skips Tuesday's lunch: its serves go to Saturday --------------------------------------------------------------
await openEntry(a, day(1), LUNCH);
await click(a, '[data-testid=cal-skip]');
check(await says(a, day(5), LUNCH, [[`${stew} from ${dow(0)} · 0 left after`, false]]) && await says(a, day(2), LUNCH, [[`${stew} from ${dow(0)} · ${H} left after`, false]]),
  `A skips Tuesday's lunch: Wednesday ${await read(a, day(2), LUNCH)}, Saturday ${await read(a, day(5), LUNCH)}`);

// --- More serves made: some will go uneaten ---------------------------------------------------------------------------
await openEntry(a, day(0), DINNER);
await a.evaluate(typeInto('cal-serves-made', String(4 * H))); await sleep(200);
await click(a, '[data-testid=cal-save]');
const uneaten = `${H === 1 ? '1 serve' : `${H} serves`} of ${stew} from ${weekday(0)} left after ${weekday(5)} will go uneaten`;
check(await says(a, day(0), DINNER, [[`makes ${4 * H} · ${H} left after ${dow(5)}`, false]]) && (await until(a, `[...document.querySelectorAll('[data-testid=cal-serves-note]')].some(n => n.textContent.trim() === ${JSON.stringify(uneaten)})`)),
  `A says it made ${4 * H}: "${(await read(a, day(0), DINNER))}" and "${uneaten}"`);
await pickDay(b, day(0));
check(await says(b, day(0), DINNER, [[`makes ${4 * H} · ${H} left after ${dow(5)}`, false]]), 'B shows the new count');

// --- A second batch, and B choosing which one Wednesday eats ---------------------------------------------------------
const soup = `Soup ${RUN}`;
check(await addMeal(a, soup, 'Cooked', String(2 * H)), 'A adds a soup that makes two rounds');
const soupId = await serverHas(`select lower(Id) from Meal where Name = '${soup}'`);
await openWeek(a, day(0));
await add(a, day(0), LUNCH, soupId, soup);
// Oldest first with enough: Wednesday now eats Monday lunch's soup.
check(await says(a, day(2), LUNCH, [[`${soup} from ${dow(0)} · 0 left after`, false]]), `A cooks soup for Monday lunch: Wednesday eats it first, oldest first (${await read(a, day(2), LUNCH)})`);
await openEntry(b, day(2), LUNCH);
check(await until(b, `document.querySelectorAll('[data-testid=cal-eats-from] option').length === 3`), 'B\'s sheet for Wednesday offers whatever is left and both batches');
await b.evaluate(`(() => { const s = document.querySelector('[data-testid=cal-eats-from]'); s.value = ${JSON.stringify(stewEntry)};
  s.dispatchEvent(new Event('change', { bubbles: true })); })()`);
await sleep(200);
await click(b, '[data-testid=cal-save]');
check(await says(a, day(2), LUNCH, [[`${stew} from ${dow(0)} · ${2 * H} left after`, false]]), `B sets Wednesday to eat the stew: A shows ${await read(a, day(2), LUNCH)}`);
await settled(b);
const wedEntry = await serverHas(`select lower(Id) from CalendarEntry where MealId = '${up(leftoversId)}' and Date = '${day(2)}'`);
check(await serverHas(`select coalesce(lower(EatsFrom), '') from CalendarEntry where Id = '${up(wedEntry)}'`) === stewEntry, 'the server holds the batch Wednesday eats');

// --- Both offline, each adds Thursday's lunch -----------------------------------------------------------------------
await openWeek(a, day(0)); await openWeek(b, day(0));
await a.offline(true); await b.offline(true);
await add(a, day(3), LUNCH, leftoversId, leftovers);
await add(b, day(3), LUNCH, leftoversId, leftovers);
await sleep(500);
await a.offline(false); await b.offline(false);
await settled(a, 30_000); await settled(b, 30_000);
await sleep(1000);
check(await serverHas(`select count(*) from CalendarEntry where MealId = '${up(leftoversId)}' and Date = '${day(3)}' and Deleted = 0`, v => v === '2') === '2',
  'both offline, A and B each add Thursday\'s lunch: the server holds both');
await openWeek(a, day(0)); await openWeek(b, day(0)); await pickDay(b, day(3));
const thuA = await read(a, day(3), LUNCH), thuB = await read(b, day(3), LUNCH);
check(await until(a, `${ours(a, day(3), LUNCH)}.length === 2`) && thuA === thuB, `both count both - two draws, no counter to lose one: A ${thuA}, B ${thuB}`);

// --- The rows, in all three -----------------------------------------------------------------------------------------
await settled(a); await settled(b);
const entryRows = `select lower(Id) || '|' || Date || '|' || lower(SlotId) || '|' || lower(MealId) || '|' || Status || '|' || coalesce(lower(EatsFrom), '-')
  || '|' || coalesce(ServesMade, '-') || '|' || Deleted from CalendarEntry where MealId in ('${up(stewId)}', '${up(leftoversId)}', '${up(soupId)}') order by Id`;
const rows = server(entryRows);
console.log(rows.split('\n').map(r => `    ${r}`).join('\n'));
check(rows.split('\n').length === 7, `seven entries: the stew, the soup, five lunches (${rows.split('\n').length})`);
for (const p of [a, b]) {
  const q = await dump(p);
  check(q(entryRows) === rows && q('select count(*) from Outbox') === '0', `${p.name} SQLite holds the same rows as the server, outbox empty`);
}

finish();
