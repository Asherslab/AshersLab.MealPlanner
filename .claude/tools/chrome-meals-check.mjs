// Meal groups, meals and their ingredients through the running app, from two browser profiles: every edit made on
// profile A is seen on profile B, and the rows are read back from both profiles' SQLite and the server's. Saves
// phone-sized screenshots under <scratchDir>.
//
//   node .claude/tools/chrome-meals-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19347/19348); names
// carry the run id. APP_URL / SERVER_DB as in chrome-sync-check.mjs.
import { RUN, launch, check, finish, settled, server, dump, nameList, ingredients, click, clickDialogButton, typeInto, phone,
  screenshot, sleep, until } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-meals-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 8 minutes'); process.exit(3); }, 480_000).unref();

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
  check(await mb.group(meal) === 'Beef' && await mb.detail(meal) === 'makes 4 serves · no ingredients yet',
    `B lists it under Beef, "makes 4 serves · no ingredients yet" (${await mb.group(meal)}, ${await mb.detail(meal)})`);

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
  await b.go('meals'); await mb.has(meal, 30_000); await sleep(300);
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

// --- A meal's ingredients and its cost ----------------------------------------------------------------------------
{
  const mince = `mince ${RUN}`, onion = `onion ${RUN}`, meal = `Cottage pie ${RUN}`;
  await a.go('ingredients');
  check(await ingredients(a).add(mince, { unit: 'Gram', store: 'Costco' }) && await ingredients(a).add(onion, { unit: 'Each', store: 'Woolworths' }),
    'A adds mince (grams, Costco) and onion (each, Woolworths)');

  /** Adds a product with its first price under an ingredient on the Prices page. */
  async function priced(p, ingredient, pack, price) {
    await p.go('prices');
    const block = `[...document.querySelectorAll('[data-testid=price-ingredient]')]
      .find(r => r.querySelector('[data-testid=price-ingredient-name]')?.textContent.trim() === ${JSON.stringify(ingredient)})`;
    if (!await until(p, `!!${block}`, 30_000)) return false;
    await p.evaluate(`${block}.querySelector('[data-testid=price-ingredient-add]').click()`);
    if (!await until(p, `!!document.getElementById('product-edit-pack')`, 10_000)) return false;
    await p.evaluate(typeInto('product-edit-pack', pack));
    await p.evaluate(typeInto('product-edit-price', price)); await sleep(150);
    await click(p, '[data-testid=product-save]');
    // Until the row shows its price: the write lands after the dialog closes, and navigating away first loses it.
    return until(p, `${block}?.querySelector('[data-testid=price-product-price]')?.textContent.trim() === ${JSON.stringify('$' + price)}`, 10_000);
  }
  check(await priced(a, mince, '8x250g', '24.99'), 'A prices the mince: $24.99 for 8x250g at Costco');

  await a.go('meals');
  const ma = meals(a), mb = meals(b);
  await ma.startAdd(meal);
  await ma.fill({ group: 'Beef', serves: '4' });
  await ma.save();
  check(await until(a, `document.querySelector('[data-testid=meal-cost-note]')?.textContent.includes('No ingredients yet')`),
    'a new batch meal says "No ingredients yet", not $0');

  const lineRow = (p, name) => `[...document.querySelectorAll('[data-testid=meal-ingredient-row]')]
    .find(r => r.querySelector('[data-testid=meal-ingredient-name]')?.textContent.trim() === ${JSON.stringify(name)})`;
  const lineText = (p, name, testid) => p.evaluate(`${lineRow(p, name)}?.querySelector('[data-testid=${testid}]')?.textContent.trim()`);
  const lineIs = (p, name, testid, text) => until(p, `${lineRow(p, name)}?.querySelector('[data-testid=${testid}]')?.textContent.trim() === ${JSON.stringify(text)}`);
  const sheetOpen = p => until(p, `!!document.querySelector('[data-testid=meal-ingredient-sheet]')`, 10_000);
  const sheetClosed = p => until(p, `!document.querySelector('[data-testid=meal-ingredient-sheet]')`, 10_000);
  const costIs = (p, testid, text, ms) => until(p, `document.querySelector('[data-testid=${testid}]')?.textContent.trim() === ${JSON.stringify(text)}`, ms);
  const submitSheet = p => p.evaluate(`document.getElementById('meal-ingredient-form').requestSubmit()`);

  /** Taps "Add ingredient", types the search, taps the match, types the quantity and presses Enter. */
  async function addLine(p, ingredient, quantity, note) {
    await click(p, '[data-testid=meal-ingredient-add]');
    if (!await sheetOpen(p)) return 'no sheet';
    const searchFocused = await until(p, `document.activeElement?.id === 'meal-ingredient-search'`, 3000);
    await p.evaluate(typeInto('meal-ingredient-search', ingredient.split(' ')[0] + ' ' + RUN)); await sleep(200);
    const choice = `[...document.querySelectorAll('[data-testid=meal-ingredient-choice]')].find(e => e.textContent.trim() === ${JSON.stringify(ingredient)})`;
    if (!await until(p, `!!${choice}`, 5000)) return 'no match';
    await p.evaluate(`${choice}.click()`);
    const quantityFocused = await until(p, `document.activeElement?.id === 'meal-ingredient-quantity'`, 3000);
    if (quantity) await p.evaluate(typeInto('meal-ingredient-quantity', quantity));
    if (note) await p.evaluate(typeInto('meal-ingredient-note', note));
    await sleep(150);
    await submitSheet(p);
    return { searchFocused, quantityFocused, closed: await sheetClosed(p) };
  }

  const r1 = await addLine(a, mince, '500g', 'browned');
  check(r1.searchFocused && r1.quantityFocused && r1.closed,
    `A adds 500g of mince: the search box has focus, tapping the match focuses the quantity, Enter saves (${JSON.stringify(r1)})`);
  check(await lineIs(a, mince, 'meal-ingredient-cost', '$6.25'), `its line costs $6.25 (${await lineText(a, mince, 'meal-ingredient-cost')})`);
  check(await lineIs(a, mince, 'meal-ingredient-detail', '500 g · browned · $12.50/kg'), `its detail reads "500 g · browned · $12.50/kg" (${await lineText(a, mince, 'meal-ingredient-detail')})`);
  check(await costIs(a, 'meal-cost-batch', '$6.25') && await costIs(a, 'meal-cost-serve', '$1.56'), 'the batch is $6.25 and a serve $1.56 (625 / 4)');

  const r2 = await addLine(a, onion, '', '');
  check(r2.closed, 'A adds an onion with no quantity');
  check(await lineIs(a, onion, 'meal-ingredient-cost', 'how much?') && await lineIs(a, onion, 'meal-ingredient-detail', 'no quantity yet'),
    'its line says "how much?" and "no quantity yet"');
  check(await costIs(a, 'meal-cost-batch', 'from $6.25 + 1 unpriced') && await costIs(a, 'meal-cost-serve', 'from $1.56 + 1 unpriced'),
    'the batch is "from $6.25 + 1 unpriced", a serve "from $1.56 + 1 unpriced" - never $6.25 alone');

  await b.go('meals');
  check(await until(b, `[...document.querySelectorAll('[data-testid=meal-row]')].some(r => r.querySelector('[data-testid=meal-name]')?.textContent.trim() === ${JSON.stringify(meal)}
    && r.querySelector('[data-testid=meal-detail]')?.textContent.trim() === 'makes 4 serves · from $1.56 a serve + 1 unpriced')`),
    'B lists it as "makes 4 serves · from $1.56 a serve + 1 unpriced"');

  // B: the wrong kind of unit is refused; 2 onions saved; still unpriced.
  await mb.open(meal);
  await until(b, `!!${lineRow(b, onion)}`, 30_000);
  await b.evaluate(`${lineRow(b, onion)}.click()`); await sheetOpen(b);
  await b.evaluate(typeInto('meal-ingredient-quantity', '500ml')); await sleep(150);
  await submitSheet(b); await sleep(400);
  check(await b.evaluate(`!!document.querySelector('[data-testid=meal-ingredient-sheet]') && document.body.textContent.includes('It is counted in each')`),
    'B: "500ml" of onion is refused - it is counted in each');
  await b.evaluate(typeInto('meal-ingredient-quantity', '2')); await sleep(150);
  await submitSheet(b); await sheetClosed(b);
  check(await until(a, `${lineRow(a, onion)}?.querySelector('[data-testid=meal-ingredient-cost]')?.textContent.trim() === 'unpriced'`),
    'B sets 2 onions; A shows the line as "unpriced" (the onion has no price)');

  check(await priced(b, onion, '6', '4.50'), 'B prices onions: $4.50 for 6 at Woolworths');
  check(await costIs(a, 'meal-cost-batch', '$7.75', 20_000) && await costIs(a, 'meal-cost-serve', '$1.94'),
    `A's open page completes: batch $7.75 (625 + 150), a serve $1.94 (775 / 4 = 193.75) (${await a.evaluate(`document.querySelector('[data-testid=meal-cost]')?.textContent.replace(/\\s+/g, ' ')`)})`);

  // The onion is now in a meal and sold as a product: deleting it is refused, naming both.
  await b.go('ingredients');
  await ingredients(b).has(onion, 30_000);
  await ingredients(b).open(onion);
  await click(b, '[data-testid=ingredient-delete]');
  check(await until(b, `document.body.textContent.includes('is used by 1 product: ${onion}, and 1 meal: ${meal}')`, 5000),
    'B cannot delete the onion: it names the product and the meal');
  await clickDialogButton(b, 'OK'); await ingredients(b).cancel();

  await settled(a); await settled(b);
  const mealId = await a.evaluate(`location.pathname.split('/').pop()`);
  const select = `select group_concat(cast(Quantity as real) || '|' || coalesce(Note, '') || '|' || Deleted, ';') from (select * from MealIngredient where MealId = '${up(mealId)}' order by Id)`;
  check(server(select) === '500.0|browned|0;2.0||0', `the server holds both lines: 500 g browned, 2 onions (${server(select)})`);
  for (const p of [a, b]) {
    const q = await dump(p);
    check(q(select) === '500.0|browned|0;2.0||0' && q('select count(*) from Outbox') === '0', `${p.name} SQLite holds the same two lines, outbox empty`);
  }

  await b.go(`meals/${mealId}`);
  await phone(b); await costIs(b, 'meal-cost-batch', '$7.75', 30_000); await sleep(500);
  console.log(`  phone screenshot: ${await screenshot(b, 'meal-with-ingredients')}`);
  await click(b, '[data-testid=meal-ingredient-add]'); await sheetOpen(b);
  await b.evaluate(typeInto('meal-ingredient-search', RUN)); await sleep(500);
  console.log(`  phone screenshot: ${await screenshot(b, 'ingredient-sheet-search')}`);
  await b.evaluate(`document.querySelector('[data-testid=meal-ingredient-choice]').click()`); await sleep(500);
  console.log(`  phone screenshot: ${await screenshot(b, 'ingredient-sheet-quantity')}`);
  await click(b, '[data-testid=meal-ingredient-cancel]'); await sheetClosed(b);
  await b.go('meals'); await mb.has(meal, 30_000); await sleep(300);
  console.log(`  phone screenshot: ${await screenshot(b, 'meals-with-costs')}`);
  await phone(b, false);

  // B removes the onion line; A's page drops it and the cost goes back to the mince alone.
  await mb.open(meal);
  await until(b, `!!${lineRow(b, onion)}`, 30_000);
  await b.evaluate(`${lineRow(b, onion)}.click()`); await sheetOpen(b);
  await click(b, '[data-testid=meal-ingredient-remove]'); await sheetClosed(b);
  check(await until(a, `!${lineRow(a, onion)}`) && await costIs(a, 'meal-cost-batch', '$6.25'), 'B removes the onion line; A drops it and the batch is $6.25 again');

  // Deleting the meal deletes its lines too.
  await mb.edit();
  await click(b, '[data-testid=meal-delete]');
  await until(b, `document.querySelectorAll('.mud-dialog').length > 1`, 5000);
  await clickDialogButton(b, 'Delete');
  await until(b, `location.pathname.endsWith('/meals')`);
  await settled(b); await settled(a);
  check(server(`select group_concat(Deleted, '') from MealIngredient where MealId = '${up(mealId)}'`) === '11'
    && server(`select Deleted from Meal where Id = '${up(mealId)}'`) === '1', 'B deletes the meal: the server holds it and both lines as tombstones');
}

finish();
