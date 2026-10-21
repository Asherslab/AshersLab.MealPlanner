// People, slots and plan templates through the running app, from two browser profiles: profile A on a desktop-sized
// window (the week grid), profile B phone-sized (a day at a time). Every edit on one is seen on the other, and the rows
// are read back from both profiles' SQLite and the server's. Saves screenshots under <scratchDir>.
//
//   node .claude/tools/chrome-plans-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19354/19355); names
// carry the run id, and no person is given a real name. APP_URL / SERVER_DB as in chrome-sync-check.mjs.
import { RUN, launch, check, finish, settled, server, dump, nameList, click, clickDialogButton, typeInto, phone,
  screenshot, sleep, until } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-plans-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 8 minutes'); process.exit(3); }, 480_000).unref();

const a = await launch(scratch, 'planA', 19354, 'people');
const b = await launch(scratch, 'planB', 19355, 'people');
await a.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
await phone(b);
console.log(`run ${RUN}: both profiles online, A desktop-sized, B phone-sized`);
const up = id => id.toUpperCase();
const DINNER = '00000000-5eed-7000-8000-000000000405';

// --- People -------------------------------------------------------------------------------------------------------
const person = `Person ${RUN}`;
let personId;
{
  const row = name => `[...document.querySelectorAll('[data-testid=person-row]')].find(r => r.querySelector('[data-testid=person-name]')?.textContent.trim() === ${JSON.stringify(name)})`;
  check(await until(a, `!!document.getElementById('person-new-name')`, 30_000), 'A opens People');
  await a.evaluate(typeInto('person-new-name', person)); await sleep(100);
  await click(a, '[data-testid=person-add]');
  check(await until(b, `!!${row(person)}`), 'B shows the person A added');
  personId = await b.evaluate(`${row(person)}.dataset.id`);
  // The first free colour: on a fresh database the palette's first, otherwise one nobody else has (while any is free).
  const first = server(`select Colour from Person where Id = '${up(personId)}'`);
  const others = server(`select count(*) from Person where not Deleted and Id <> '${up(personId)}'`);
  const taken = server(`select count(*) from Person where not Deleted and Id <> '${up(personId)}' and Colour = '${first}'`);
  check(others === '0' ? first === '#1e88e5' : Number(others) >= 8 || taken === '0',
    `a new person gets the first free colour (${first}, ${others} others)`);

  await a.evaluate(`${row(person)}.querySelector('[role=button]').click()`);
  await until(a, `!!document.querySelector('[data-testid=person-colour]')`);
  await click(a, '[data-testid=person-colour][data-colour="#8e24aa"]'); await sleep(100);
  await click(a, '[data-testid=person-save]');
  check(await until(b, `${row(person)}?.querySelector('.person-dot')?.style.background.includes('142, 36, 170')`),
    'A changes the colour; B shows it');
  console.log(`  phone screenshot: ${await screenshot(b, 'people')}`);
}

// --- Slots --------------------------------------------------------------------------------------------------------
{
  await a.go('slots'); await b.go('slots');
  const sa = nameList(a, 'slot'), sb = nameList(b, 'slot');
  await sb.has('Dinner', 30_000);
  const seeded = await sb.names();
  check(JSON.stringify(seeded.slice(0, 5)) === JSON.stringify(['Breakfast', 'Snack', 'Lunch', 'Snack', 'Dinner']),
    `a new profile has the five seeded slots, two snacks (${seeded.join(', ')})`);
  const supper = `Supper ${RUN}`;
  check(await sa.add(supper) && await sb.has(supper), 'A adds a slot at the end; B shows it');
  check(await sa.add('Snack') && await until(b, `[...document.querySelectorAll('[data-testid=slot-name]')].filter(e => e.textContent.trim() === 'Snack').length === 3`),
    'a third "Snack" is allowed - slots may share a name');
  const third = (await sa.names()).lastIndexOf('Snack');
  await a.evaluate(`document.querySelectorAll('[data-testid=slot-row]')[${third}].querySelector('[data-testid=slot-delete]').click()`);
  await until(a, `document.querySelectorAll('.mud-dialog').length > 0`, 5000); await clickDialogButton(a, 'Delete');
  await until(a, `!document.querySelector('.mud-dialog')`, 5000);
  check(await sa.remove(supper) === 'deleted' || await sa.gone(supper), 'A deletes both again');
  check(await sb.gone(supper) && await until(b, `[...document.querySelectorAll('[data-testid=slot-name]')].filter(e => e.textContent.trim() === 'Snack').length === 2`),
    'B drops them');
}

// --- Two meals to plan --------------------------------------------------------------------------------------------
const noodles = `Noodles ${RUN}`, soup = `Soup ${RUN}`;
async function addMeal(p, name) {
  await p.go('meals');
  if (!await until(p, `!!document.getElementById('meal-new-name')`, 30_000)) return false;
  await p.evaluate(typeInto('meal-new-name', name)); await sleep(100);
  await click(p, '[data-testid=meal-add]');
  await until(p, `!!document.getElementById('meal-edit-name')`, 10_000);
  await p.evaluate(`(() => { const e = document.querySelector('[data-testid=meal-kind-Simple]'); (e.querySelector('input') ?? e).click(); })()`);
  await sleep(200);
  await click(p, '[data-testid=meal-save]');
  return until(p, `document.querySelector('[data-testid=meal-title]')?.textContent.trim() === ${JSON.stringify(name)}`);
}
check(await addMeal(a, noodles) && await addMeal(a, soup), 'A adds two single-serve meals');
const noodlesId = await a.evaluate(`location.pathname.split('/').pop()`).then(() => server(`select Id from Meal where Name = '${noodles}'`));

// --- A plan -------------------------------------------------------------------------------------------------------
const plan = `Rotation ${RUN}`;
let planId, entryId;
{
  await a.go('plans');
  check(await until(a, `!!document.getElementById('plan-new-name')`, 30_000), 'A opens Plans');
  await a.evaluate(typeInto('plan-new-name', plan));
  await a.evaluate(typeInto('plan-new-weeks', '2')); await sleep(100);
  await click(a, '[data-testid=plan-add]');
  check(await until(a, `document.querySelector('[data-testid=plan-title]')?.textContent.trim() === ${JSON.stringify(plan)}`),
    'Add takes A to the new plan');
  planId = await a.evaluate(`location.pathname.split('/').pop()`);
  check(await until(a, `!!document.querySelector('[data-testid=plan-grid]')`) && !await a.evaluate(`!!document.querySelector('[data-testid=plan-day-view]')`),
    'on a desktop it is the week grid, not the day view');

  // Week 2, Tuesday's dinner: + opens the sheet with the search focused; two taps make two choices.
  await click(a, '[data-testid=plan-week][data-week="2"]'); await sleep(300);
  await a.evaluate(`document.querySelector('[data-testid=plan-grid] [data-testid=plan-cell][data-day=Tuesday][data-slot="${DINNER}"] [data-testid=plan-cell-add]').click()`);
  check(await until(a, `!!document.getElementById('entry-search')`), 'the + on Tuesday\'s dinner opens the entry sheet');
  check(await until(a, `document.activeElement?.id === 'entry-search'`, 3000), 'with the search box focused');
  check(await a.evaluate(`document.querySelector('[data-testid=entry-where]').textContent.trim()`) === 'Week 2 · Tuesday · Dinner', 'it says where');
  for (const meal of [noodles, soup]) {
    await a.evaluate(typeInto('entry-search', meal)); await sleep(200);
    await a.evaluate(`[...document.querySelectorAll('[data-testid=entry-meal-option]')].find(o => o.textContent.trim() === ${JSON.stringify(meal)})?.click()`);
    await sleep(200);
  }
  await a.evaluate(`document.querySelector('[data-testid=entry-person][data-id="${personId}"]').click()`); await sleep(150);
  await click(a, '[data-testid=entry-save]');
  const cell = `document.querySelector('[data-testid=plan-grid] [data-testid=plan-cell][data-day=Tuesday][data-slot="${DINNER}"]')`;
  check(await until(a, `${cell}?.querySelector('[data-testid=plan-entry-meals]')?.textContent.trim() === ${JSON.stringify(`${noodles} / ${soup}`)}`),
    `the cell shows "${noodles} / ${soup}"`);
  check(await a.evaluate(`!!${cell}.querySelector('[data-testid=plan-entry-people] .person-dot')`), 'with the person\'s dot');
  entryId = await a.evaluate(`${cell}.querySelector('[data-testid=plan-entry]').dataset.id`);
  console.log(`  desktop screenshot: ${await screenshot(a, 'plan-grid')}`);

  // B, on a phone: Plans, the plan, week 2, Tuesday.
  await b.go('plans');
  check(await until(b, `[...document.querySelectorAll('[data-testid=plan-name]')].some(e => e.textContent.trim() === ${JSON.stringify(plan)})`, 30_000),
    'B lists the plan');
  // The plan can arrive a pull before its entry; the list follows when the entry does.
  const detail = `[...document.querySelectorAll('[data-testid=plan-row]')].find(r => r.dataset.id === ${JSON.stringify(planId)})?.querySelector('[data-testid=plan-detail]').textContent.trim()`;
  check(await until(b, `${detail} === '2 weeks · 1 entry'`), `as "2 weeks · 1 entry" (${JSON.stringify(await b.evaluate(detail))})`);
  await b.go(`plans/${planId}`);
  check(await until(b, `!!document.querySelector('[data-testid=plan-day-view]')`, 30_000) && !await b.evaluate(`!!document.querySelector('[data-testid=plan-grid]')`),
    'on a phone it is a day at a time, not the grid');
  await click(b, '[data-testid=plan-week][data-week="2"]'); await sleep(200);
  await click(b, '[data-testid=plan-day][data-day=Tuesday]'); await sleep(300);
  const bEntry = `document.querySelector('[data-testid=plan-entry][data-id="${entryId}"]')`;
  check(await until(b, `${bEntry}?.querySelector('[data-testid=plan-entry-meals]')?.textContent.trim() === ${JSON.stringify(`${noodles} / ${soup}`)}`)
    && await b.evaluate(`${bEntry}.querySelector('.person-chip')?.textContent.trim()`) === person,
    'B shows the entry under Dinner, with the person named');
  console.log(`  phone screenshot: ${await screenshot(b, 'plan-day')}`);

  // B makes soup the usual one; A's grid follows.
  await b.evaluate(`${bEntry}.click()`);
  await until(b, `!!document.querySelector('[data-testid=entry-choice-first]')`);
  console.log(`  phone screenshot: ${await screenshot(b, 'entry-sheet')}`);
  await click(b, '[data-testid=entry-choice-first]'); await sleep(150);
  await click(b, '[data-testid=entry-save]');
  check(await until(a, `${cell}?.querySelector('[data-testid=plan-entry-meals]')?.textContent.trim() === ${JSON.stringify(`${soup} / ${noodles}`)}`),
    'B makes soup the usual one; A shows it first');

  // A meal a plan uses cannot be deleted.
  await a.go(`meals/${noodlesId.toLowerCase()}`);
  await until(a, `!!document.querySelector('[data-testid=meal-edit]')`, 30_000);
  await click(a, '[data-testid=meal-edit]'); await until(a, `!!document.getElementById('meal-edit-name')`);
  await click(a, '[data-testid=meal-delete]');
  check(await until(a, `document.querySelector('[data-testid=meal-in-use]')?.textContent.includes(${JSON.stringify(plan)})`, 5000),
    'A cannot delete a meal the plan uses: it names the plan');
  await click(a, '[data-testid=meal-cancel]');
  // Nor the person it names.
  await a.go('people');
  const prow = `[...document.querySelectorAll('[data-testid=person-row]')].find(r => r.dataset.id === ${JSON.stringify(personId)})`;
  await until(a, `!!${prow}`, 30_000);
  await a.evaluate(`${prow}.querySelector('[role=button]').click()`); await sleep(200);
  await click(a, '[data-testid=person-delete]');
  check(await until(a, `document.querySelector('[data-testid=person-in-use]')?.textContent.includes(${JSON.stringify(plan)})`, 5000),
    'nor the person it names');
  await click(a, '[data-testid=person-cancel]');

  await settled(a); await settled(b);
  const entrySql = `select Week || '|' || Day || '|' || SlotId || '|' || People || '|' || Deleted from TemplateEntry where Id = '${up(entryId)}'`;
  const expected = `2|Tuesday|${DINNER.toUpperCase()}|${personId}|0`;
  const choicesSql = `select group_concat(m.Name, ' / ') from (select c.Id, c.MealId from TemplateChoice c where c.EntryId = '${up(entryId)}' and not c.Deleted order by c.Id) c join Meal m on m.Id = c.MealId`;
  check(server(entrySql) === expected, `the server holds the entry: week 2, Tuesday, Dinner, the person (${server(entrySql)})`);
  check(server(choicesSql) === `${soup} / ${noodles}`, `and its live choices in order (${server(choicesSql)})`);
  for (const p of [a, b]) {
    const q = await dump(p);
    check(q(entrySql) === expected && q(choicesSql) === `${soup} / ${noodles}` && q('select count(*) from Outbox') === '0'
      && q(`select WeekCount || '|' || Name from PlanTemplate where Id = '${up(planId)}'`) === `2|${plan}`,
      `${p.name} SQLite holds the same plan, entry and choices, outbox empty`);
  }

  // B removes the entry; A deletes the plan.
  await b.evaluate(`${bEntry}.click()`); await until(b, `!!document.querySelector('[data-testid=entry-remove]')`);
  await click(b, '[data-testid=entry-remove]');
  await a.go(`plans/${planId}`); await until(a, `!!document.querySelector('[data-testid=plan-grid]')`, 30_000);
  await click(a, '[data-testid=plan-week][data-week="2"]');
  check(await until(a, `!${cell}?.querySelector('[data-testid=plan-entry]')`), 'B removes the entry; A\'s grid drops it');
  await click(a, '[data-testid=plan-edit]'); await until(a, `!!document.getElementById('plan-edit-name')`);
  await click(a, '[data-testid=plan-delete]');
  await until(a, `document.querySelectorAll('.mud-dialog').length > 1`, 5000); await clickDialogButton(a, 'Delete');
  check(await until(a, `location.pathname.endsWith('/plans')`) && await until(b, `!!document.querySelector('[data-testid=plan-gone]')`),
    'A deletes the plan; B, on its page, is told it is gone');
  await settled(a); await settled(b);
  check(server(`select Deleted from PlanTemplate where Id = '${up(planId)}'`) === '1'
    && server(`select Deleted from TemplateEntry where Id = '${up(entryId)}'`) === '1'
    && server(`select count(*) from TemplateChoice where EntryId = '${up(entryId)}' and not Deleted`) === '0',
    'the server keeps the plan, its entry and its choices as tombstones');
}

finish();
