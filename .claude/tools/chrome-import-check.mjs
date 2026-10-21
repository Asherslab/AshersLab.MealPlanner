// The spreadsheet import through the running app, from two browser profiles: A picks the sheet, reviews and imports;
// both profiles' SQLite and the server's then hold the same rows; importing the same sheet again - on B, then on A -
// finds nothing new; B checks a guessed unit and A sees the mark go. Prints counts only, never the sheet's contents:
// the sheet is personal data, and this script's output is pasted into docs.
//
//   node .claude/tools/chrome-import-check.mjs <scratchDir> <sheet.xlsx> [<later-sheet.xlsx>]
//
// With a later sheet - the same sheet downloaded again after the planner filled in more, prices especially - A then
// imports that on top: prices that name no store at Woolworths, every guessed unit a price contradicts changed to
// what the price says, every "which ingredient is it" left at its suggestion. It checks every price is accounted
// for (imported, already here, or left out with a reason), that no row already there changed except those units, that
// B holds the same, and that the later sheet again on B finds nothing new.
//
// The sheet is a path outside the repo (the planner's real one lives in private/) or a synthetic one. Needs the app
// running (AppHost: https) and sqlite3 on the PATH; start from a fresh server database for counts that mean something.
// Ports 19352/19353. Household items are put at Costco so the sheet's household prices import too.
import { launch, check, finish, settled, server, dump, phone, screenshot, sleep, until } from './lib/profiles.mjs';

const [scratch, sheet, later] = process.argv.slice(2);
if (!scratch || !sheet) { console.error('usage: chrome-import-check.mjs <scratchDir> <sheet.xlsx> [<later-sheet.xlsx>]'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 9 minutes'); process.exit(3); }, 540_000).unref();

const a = await launch(scratch, 'importA', 19352, 'import');
const b = await launch(scratch, 'importB', 19353, 'import');
console.log('both profiles online');

async function pick(p, file = sheet) {
  await until(p, `!!document.querySelector('[data-testid=import-file]')`, 30_000);
  const doc = await p.send('DOM.getDocument', {});
  const input = await p.send('DOM.querySelector', { nodeId: doc.result.root.nodeId, selector: '[data-testid=import-file]' });
  await p.send('DOM.setFileInputFiles', { nodeId: input.result.nodeId, files: [file] });
  return until(p, `!!document.querySelector('[data-testid=import-summary], [data-testid=import-error]')`, 30_000);
}

const text = (p, testid) => p.evaluate(`document.querySelector('[data-testid=${testid}]')?.textContent.trim().replace(/\\s+/g, ' ') ?? null`);
const count = (p, testid) => p.evaluate(`document.querySelectorAll('[data-testid=${testid}]').length`);

const TABLES = ['Ingredient', 'Category', 'MealGroup', 'Meal', 'MealIngredient', 'Product', 'PriceObservation'];
const counts = q => TABLES.map(t => `${t} ${q(`select count(*) from ${t} where not Deleted`)}`).join(', ')
  + `, guessed ${q('select count(*) from Ingredient where UnitGuessed and not Deleted')}`
  + `, spreadsheet prices ${q(`select count(*) from PriceObservation where Source = 'Sheet' and not Deleted`)}`;

// --- A reviews and imports ----------------------------------------------------------------------------------------
check(await pick(a), 'A picks the sheet and the review appears');
check(await text(a, 'import-error') === null, 'it is read as the meal-plan sheet');
console.log(`  review: ${await text(a, 'import-summary')}`);
console.log(`  questions: ${await count(a, 'import-ingredient-question')} ingredient, ${await count(a, 'import-serves-question')} serves, `
  + `${await count(a, 'import-group-question')} group, ${await count(a, 'import-category-question')} category, `
  + `${await count(a, 'import-price')} prices; ${await count(a, 'import-new-ingredient')} new ingredients to check`);

/** Picks a store by name in one of the review's store selects; null when the review has no such select. */
const chooseStore = (p, testid, store) => p.evaluate(`(() => { const s = document.querySelector('[data-testid=${testid}] select');
  if (!s) return null; const o = [...s.options].find(o => o.text === '${store}'); s.value = o.value; s.dispatchEvent(new Event('change', { bubbles: true })); return o.text; })()`);

/** Waits for an import's ops to reach the server: the log has grown past `from` and stood still for 3 s. */
async function serverSettled(from) {
  for (let last = from, still = 0; still < 15; await sleep(200)) {
    const now = server('select max(Seq) from Op');
    still = now !== from && now === last ? still + 1 : 0;
    last = now;
  }
}

const household = await chooseStore(a, 'import-household-store', 'Costco');
if (household) {
  await sleep(500);
  console.log(`  household items at ${household}; review now: ${await text(a, 'import-summary')}`);
}
const willImport = await a.evaluate(`[...document.querySelectorAll('[data-testid=import-price-state]')].filter(e => e.textContent.includes('Will be')).length`);
console.log(`  prices that will import: ${willImport} of ${await count(a, 'import-price')}`);

await phone(a); await sleep(500);
console.log(`  phone screenshot: ${await screenshot(a, 'review')}`);
await phone(a, false);

const before = server('select max(Seq) from Op');
const started = Date.now();
await a.evaluate(`document.querySelector('[data-testid=import-run]').click()`);
check(await until(a, `!!document.querySelector('[data-testid=import-done]')`, 60_000), `A imports and says what it added (${Date.now() - started} ms)`);
console.log(`  done: ${await text(a, 'import-done')}`);
// Wait on the server's log, not the chip: the chip can read "0 waiting" before the round that pushes has started.
await serverSettled(before);
await settled(a, 60_000);
console.log(`  server log grew from seq ${before} to ${server('select max(Seq) from Op')}`);

// --- The same rows everywhere -------------------------------------------------------------------------------------
await settled(b, 60_000);
const serverCounts = counts(server);
console.log(`  server: ${serverCounts}`);
for (const p of [a, b]) {
  let q, same = false;
  for (let i = 0; i < 10 && !same; i++) {
    if (i) await sleep(1000);
    q = await dump(p);
    same = counts(q) === serverCounts && q('select count(*) from Outbox') === '0';
  }
  check(same, `${p.name} SQLite holds the same counts as the server, outbox empty`);
}
check(server(`select count(*) from MealIngredient where Quantity is not null`) === '0', 'every imported meal ingredient has no quantity yet');

// --- Again, on the other profile and then this one ----------------------------------------------------------------
check(await pick(b), 'B picks the same sheet');
check(await until(b, `!!document.querySelector('[data-testid=import-nothing]')`), `B is told there is nothing new (${await text(b, 'import-nothing')})`);
check(await b.evaluate(`document.querySelector('[data-testid=import-run]').disabled`), "B's Import button is disabled");
await a.go('import');
check(await pick(a), 'A picks it again after a reload');
check(await until(a, `!!document.querySelector('[data-testid=import-nothing]')`), 'A is told there is nothing new');
check(counts(server) === serverCounts, 'the server holds exactly what it did');

// --- A guessed unit, checked on B, unmarked on A ------------------------------------------------------------------
await a.go('ingredients'); await b.go('ingredients');
await until(b, `document.querySelectorAll('[data-testid=ingredient-unit-guessed]').length > 0`, 30_000);
const marked = await count(a, 'ingredient-unit-guessed');
await phone(b); await sleep(500);
console.log(`  phone screenshot: ${await screenshot(b, 'ingredients')}`);
await phone(b, false);
const id = await b.evaluate(`(() => { const r = document.querySelector('[data-testid=ingredient-unit-guessed]').closest('[data-testid=ingredient-row]'); r.click(); return r.dataset.id; })()`);
check(await until(b, `!!document.querySelector('[data-testid=ingredient-unit-guessed-note]')`), 'B opens a guessed ingredient and the editor says the unit was guessed');
await b.evaluate(`document.querySelector('[data-testid=ingredient-save]').click()`);
check(await until(a, `document.querySelectorAll('[data-testid=ingredient-unit-guessed]').length === ${marked - 1}`, 20_000),
  `saving it unchanged on B takes the mark off on A (${marked} to ${marked - 1})`);
await settled(b);
check(server(`select UnitGuessed from Ingredient where Id = '${id.toUpperCase()}'`) === '0', 'the server row is no longer guessed');

if (later) {
  // --- A later download on top ----------------------------------------------------------------------------------
  // Every field of every row the first import left, to compare after: nothing may change but the units answered.
  const rows = () => server(`select 'I', Id, Name, CategoryId, UsualStoreId, IsStaple, Deleted from Ingredient
    union all select 'U', Id, BaseUnit, UnitGuessed, '', '', '' from Ingredient
    union all select 'M', Id, Name, Kind, ServesPerBatch, GroupId, coalesce(Notes, '') || Deleted from Meal
    union all select 'L', Id, MealId, IngredientId, coalesce(Quantity, ''), coalesce(Note, ''), Deleted from MealIngredient
    union all select 'P', Id, Name, StoreId, cast(PackQuantity as real), coalesce(IngredientId, ''), Deleted from Product
    union all select 'O', Id, ProductId, PriceCents, ObservedOn, Source, Deleted from PriceObservation
    union all select 'C', Id, Name, cast(Position as real), Deleted, '', '' from Category
    union all select 'G', Id, Name, cast(Position as real), Deleted, '', '' from MealGroup`).split('\n');
  const old = new Map(rows().map(r => [r.split('|').slice(0, 2).join('|'), r]));

  await a.go('import');
  check(await pick(a, later), 'A picks the later sheet');
  await chooseStore(a, 'import-price-store-all', 'Woolworths'); await sleep(300);
  await chooseStore(a, 'import-household-store', 'Costco'); await sleep(300);
  const unitKeys = await a.evaluate(`[...document.querySelectorAll('[data-testid=import-unit-question]')].map(e => [e.dataset.key, e.dataset.says])`);
  for (const [key, says] of unitKeys) {
    await a.evaluate(`document.querySelector('[data-testid=import-unit-question][data-key="${key}"] [data-testid=import-unit-${says}]').click()`);
    await sleep(300);
  }
  await sleep(500);
  const states = await a.evaluate(`[...document.querySelectorAll('[data-testid=import-price-state]')].map(e => e.textContent.trim())`);
  const willImport = states.filter(s => s.startsWith('Will')).length, same = states.filter(s => s.startsWith('Already')).length;
  const leftOut = states.filter(s => s.startsWith('Left out'));
  const why = {};
  for (const s of leftOut) { const r = s.replace(/"[^"]*"/g, '"…"'); why[r] = (why[r] ?? 0) + 1; }
  console.log(`  later review: ${await text(a, 'import-summary')}`);
  console.log(`  questions: ${await count(a, 'import-ingredient-question')} ingredient (left at their suggestion), ${unitKeys.length} units (answered as the price says), `
    + `${await count(a, 'import-serves-question')} serves, ${await count(a, 'import-group-question')} group`);
  console.log(`  prices in the sheet's review: ${states.length} - will import ${willImport}, already here ${same}, left out ${leftOut.length}`);
  for (const [r, n] of Object.entries(why)) console.log(`    ${n} x ${r}`);
  const notPrices = await a.evaluate(`[...document.querySelectorAll('[data-testid=import-left-out] p')].filter(p => p.textContent.includes('Not a price')).length`);
  console.log(`  cells that look like a price but are not one, reported: ${notPrices}`);
  check(willImport + same + leftOut.length === states.length, 'every price is imported, already here, or left out with a reason');

  const pricesBefore = Number(server('select count(*) from PriceObservation'));
  const seqBefore = server('select max(Seq) from Op');
  await a.evaluate(`document.querySelector('[data-testid=import-run]').click()`);
  check(await until(a, `!!document.querySelector('[data-testid=import-done]')`, 60_000), 'A imports the later sheet');
  console.log(`  done: ${await text(a, 'import-done')}`);
  await serverSettled(seqBefore);
  await settled(a, 60_000);
  check(Number(server('select count(*) from PriceObservation')) - pricesBefore === willImport, `the server gained exactly the ${willImport} prices the review said`);

  const changed = rows().filter(r => old.has(r.split('|').slice(0, 2).join('|')) && old.get(r.split('|').slice(0, 2).join('|')) !== r);
  const unitChanges = changed.filter(r => r.startsWith('U|'));
  check(changed.length === unitChanges.length && unitChanges.length === unitKeys.length,
    `no row already there changed but the ${unitKeys.length} units answered (${changed.length} changed)`);
  check(unitChanges.every(r => r.endsWith('|0|||')), 'each answered unit is no longer marked guessed');

  const serverNow = counts(server);
  console.log(`  server: ${serverNow}`);
  await b.go('import');
  await settled(b, 60_000);
  for (const p of [a, b]) {
    let same2 = false;
    for (let i = 0; i < 10 && !same2; i++) { if (i) await sleep(1000); const q = await dump(p); same2 = counts(q) === serverNow && q('select count(*) from Outbox') === '0'; }
    check(same2, `${p.name} SQLite holds the same counts as the server after the later import`);
  }

  check(await pick(b, later), 'B picks the later sheet');
  await chooseStore(b, 'import-price-store-all', 'Woolworths'); await sleep(300);
  await chooseStore(b, 'import-household-store', 'Costco'); await sleep(500);
  check(await until(b, `!!document.querySelector('[data-testid=import-nothing]')`), `B, choosing the same stores, is told there is nothing new (${await text(b, 'import-nothing')})`);
}

finish();
