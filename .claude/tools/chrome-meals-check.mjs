// Meal groups, meals and their ingredients through the running app, from two browser profiles: every edit made on
// profile A is seen on profile B, and the rows are read back from both profiles' SQLite and the server's. Saves
// phone-sized screenshots under <scratchDir>.
//
//   node .claude/tools/chrome-meals-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19347/19348); names
// carry the run id. APP_URL / SERVER_DB as in chrome-sync-check.mjs.
import { RUN, launch, check, finish, settled, server, dump, nameList, click, clickDialogButton, typeInto, phone, screenshot,
  sleep, until } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-meals-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 5 minutes'); process.exit(3); }, 300_000).unref();

const a = await launch(scratch, 'mealA', 19347, 'meal-groups');
const b = await launch(scratch, 'mealB', 19348, 'meal-groups');
console.log(`run ${RUN}: both profiles online`);
const up = id => id.toUpperCase();

// --- Meal groups --------------------------------------------------------------------------------------------------
{
  const ga = nameList(a, 'meal-group'), gb = nameList(b, 'meal-group');
  await gb.has('Snack', 30_000);
  const seeded = await gb.names();
  check(['Beef', 'Chicken & egg', 'Fish', 'Other', 'Individual', 'Side', 'Snack'].every((n, i) => seeded[i] === n),
    `a new profile has the seven seeded groups in the spreadsheet's order (${seeded.slice(0, 7).join(', ')})`);

  const group = `Pasta ${RUN}`;
  check(await ga.add(group), 'A adds a group');
  check(await gb.has(group), 'B shows it');
  check((await gb.names()).at(-1) === group, 'it is last on B');
  await ga.move(group, 'up');
  check(await (async () => { for (let i = 0; i < 50; i++) { const n = await gb.names(); if (n.at(-2) === group) return true; await sleep(200); } return false; })(),
    'A moves it up one; B shows it second to last');
  check(await ga.rename(group, `${group} night`), 'A renames it');
  check(await gb.has(`${group} night`), 'B shows the new name');
  const id = await gb.id(`${group} night`);

  await settled(a); await settled(b);
  const expected = `${group} night|0`;
  check(server(`select Name || '|' || Deleted from MealGroup where Id = '${up(id)}'`) === expected, 'the server holds the renamed group');
  for (const p of [a, b]) {
    const q = await dump(p);
    check(q(`select Name || '|' || Deleted from MealGroup where Id = '${up(id)}'`) === expected
      && q(`select count(*) from MealGroup where not Deleted`) === server(`select count(*) from MealGroup where not Deleted`)
      && q('select count(*) from Outbox') === '0', `${p.name} SQLite holds it and as many groups as the server, outbox empty`);
  }

  await phone(b); await sleep(500);
  console.log(`  phone screenshot: ${await screenshot(b, 'meal-groups')}`);
  await phone(b, false);

  check(await gb.remove(`${group} night`) === 'deleted', 'B deletes it after the confirmation');
  check(await ga.gone(`${group} night`), 'A drops it');
}

/** The meals list, a meal's page and the meal editor. */
function meals(p) {
  const row = name => `[...document.querySelectorAll('[data-testid=meal-row]')]
    .find(r => r.querySelector('[data-testid=meal-name]')?.textContent.trim() === ${JSON.stringify(name)})`;
  const editorOpen = () => until(p, `!!document.getElementById('meal-edit-name')`, 10_000);
  const editorClosed = () => until(p, `!document.getElementById('meal-edit-name')`, 10_000);
  const text = testid => p.evaluate(`document.querySelector('[data-testid=${testid}]')?.textContent.trim()`);
  async function fill(o) {
    if (o.name !== undefined) await p.evaluate(typeInto('meal-edit-name', o.name));
    if (o.group !== undefined) await p.evaluate(`(() => { const s = document.querySelector('[data-testid=meal-group]');
      const o = [...s.options].find(o => o.textContent.trim() === ${JSON.stringify(o.group)});
      s.value = o?.value ?? ''; s.dispatchEvent(new Event('change', { bubbles: true })); })()`);
    if (o.kind !== undefined) { await p.evaluate(`(() => { const e = document.querySelector('[data-testid=meal-kind-${o.kind}]');
      (e.querySelector('input') ?? e).click(); })()`); await sleep(200); }
    if (o.serves !== undefined) await p.evaluate(typeInto('meal-edit-serves', o.serves));
    if (o.cost !== undefined) await p.evaluate(typeInto('meal-edit-fixed-cost', o.cost));
    if (o.notes !== undefined) await p.evaluate(`(() => { const i = document.getElementById('meal-edit-notes');
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set.call(i, ${JSON.stringify(o.notes)});
      i.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    await sleep(150);
  }
  return {
    has: (name, ms) => until(p, `!!${row(name)}`, ms),
    gone: (name, ms) => until(p, `!${row(name)}`, ms),
    id: name => p.evaluate(`${row(name)}?.dataset.id`),
    detail: name => p.evaluate(`${row(name)}?.querySelector('[data-testid=meal-detail]')?.textContent.trim()`),
    group: name => p.evaluate(`${row(name)}?.closest('.mud-paper')?.previousElementSibling?.textContent.trim()`),
    /** Types the name in the box, taps Add and waits for the editor. */
    async startAdd(name) {
      if (!await until(p, `!!document.getElementById('meal-new-name')`, 30_000)) return false;
      await p.evaluate(typeInto('meal-new-name', name)); await sleep(100);
      await click(p, '[data-testid=meal-add]');
      return editorOpen();
    },
    open: async name => { await until(p, `!!${row(name)}`, 30_000); await p.evaluate(`${row(name)}.click()`);
      return until(p, `document.querySelector('[data-testid=meal-title]')?.textContent.trim() === ${JSON.stringify(name)}`); },
    edit: async () => { await until(p, `!!document.querySelector('[data-testid=meal-edit]')`, 30_000); await click(p, '[data-testid=meal-edit]'); return editorOpen(); },
    fill, editorOpen, editorClosed,
    save: async () => { await click(p, '[data-testid=meal-save]'); return editorClosed(); },
    title: () => text('meal-title'),
    summary: () => text('meal-summary'),
    summaryIs: (want, ms) => until(p, `document.querySelector('[data-testid=meal-summary]')?.textContent.trim() === ${JSON.stringify(want)}`, ms),
  };
}

// --- Meals --------------------------------------------------------------------------------------------------------
{
  const meal = `Bolognese ${RUN}`;
  await a.go('meals'); await b.go('meals');
  const ma = meals(a), mb = meals(b);

  check(await ma.startAdd(meal), 'A types a meal name and taps Add: the editor opens');
  await ma.fill({ group: 'Beef' });
  await click(a, '[data-testid=meal-save]'); await sleep(400);
  check(await a.evaluate(`!!document.getElementById('meal-edit-name') && document.body.textContent.includes('How many serves does a batch make?')`),
    'saving a batch with no serves is refused, asking how many');
  await ma.fill({ serves: '4', notes: 'Freezes well.\nDouble the sauce.' });
  check(await ma.save(), 'with 4 serves it saves');
  check(await ma.summaryIs('Beef · makes 4 serves'), `A is taken to the meal's page: "${await ma.summary()}"`);
  check(await a.evaluate(`document.querySelector('[data-testid=meal-notes]')?.textContent === 'Freezes well.\\nDouble the sauce.'`), 'the notes keep their line break');
  const id = a.evaluate(`location.pathname.split('/').pop()`);

  check(await mb.has(meal), 'B lists the meal');
  check(await mb.group(meal) === 'Beef' && await mb.detail(meal) === 'makes 4 serves', `B lists it under Beef, "makes 4 serves" (${await mb.group(meal)}, ${await mb.detail(meal)})`);

  // B makes it a takeaway: a fixed cost needs its amount.
  check(await mb.open(meal), 'B taps it and sees its page');
  await mb.edit();
  await mb.fill({ kind: 'FixedCost', cost: '' });
  await click(b, '[data-testid=meal-save]'); await sleep(400);
  check(await b.evaluate(`!!document.getElementById('meal-edit-name') && document.body.textContent.includes('Enter the price.')`),
    'a fixed-cost meal with no amount is refused');
  await mb.fill({ cost: '45', serves: '3' });
  check(await mb.save(), 'B saves it as $45 feeding 3');
  check(await ma.summaryIs('Beef · $45.00 · feeds 3'), `A's page shows "Beef · $45.00 · feeds 3" (${await ma.summary()})`);

  // The group it is in cannot be deleted.
  await a.go('meal-groups');
  check(await nameList(a, 'meal-group').remove('Beef') === 'in-use'
    && await a.evaluate(`/is used by \\d+ meals?: /.test(document.querySelector('[data-testid=meal-group-in-use]').textContent)`),
    'A cannot delete Beef: a meal is in it (counted and named)');

  await settled(a); await settled(b);
  const mealId = await id;
  const expected = `${meal}|${up('00000000-5eed-7000-8000-000000000301')}|FixedCost|3|4500|Freezes well.\nDouble the sauce.|0`;
  const select = `select Name || '|' || GroupId || '|' || Kind || '|' || ServesPerBatch || '|' || FixedCostCents || '|' || Notes || '|' || Deleted from Meal where Id = '${up(mealId)}'`;
  check(server(select) === expected, 'the server holds the meal: Beef, fixed cost, 3, 4500 c, the notes');
  for (const p of [a, b]) {
    const q = await dump(p);
    check(q(select) === expected && q('select count(*) from Outbox') === '0', `${p.name} SQLite holds the same meal, outbox empty`);
  }
  console.log(`  server Op for it:\n${server(`select Seq, Kind, substr(DeviceId, 1, 8), FieldsJson from Op where EntityId = '${up(mealId)}' order by Seq`)}`);

  await phone(b); await sleep(500);
  console.log(`  phone screenshot: ${await screenshot(b, 'meal')}`);
  await mb.edit(); await sleep(400);
  console.log(`  phone screenshot: ${await screenshot(b, 'meal-editor')}`);
  await click(b, '[data-testid=meal-cancel]'); await mb.editorClosed();
  await b.go('meals'); await sleep(800);
  console.log(`  phone screenshot: ${await screenshot(b, 'meals')}`);
  await phone(b, false);

  // Deleted on B: A, on its page, is told it is gone.
  await a.go(`meals/${mealId}`);
  await ma.summaryIs('Beef · $45.00 · feeds 3', 30_000);
  await mb.open(meal); await mb.edit();
  await click(b, '[data-testid=meal-delete]');
  await until(b, `document.querySelectorAll('.mud-dialog').length > 1`, 5000);
  await clickDialogButton(b, 'Delete');
  check(await until(b, `location.pathname.endsWith('/meals') && ${JSON.stringify(meal)} && ![...document.querySelectorAll('[data-testid=meal-name]')].some(e => e.textContent.trim() === ${JSON.stringify(meal)})`),
    'B deletes it after the confirmation and is back on the list without it');
  check(await until(a, `!!document.querySelector('[data-testid=meal-gone]')`), 'A, on its page, is told the meal is not here');
  await settled(a);
  check(server(`select Deleted from Meal where Id = '${up(mealId)}'`) === '1', 'the server keeps the tombstone');
}

finish();
