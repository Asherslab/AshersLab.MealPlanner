// The spreadsheet import through the running app, from two browser profiles: A picks the sheet, reviews and imports;
// both profiles' SQLite and the server's then hold the same rows; importing the same sheet again - on B, then on A -
// finds nothing new; B checks a guessed unit and A sees the mark go. Prints counts only, never the sheet's contents:
// the sheet is personal data, and this script's output is pasted into docs.
//
//   node .claude/tools/chrome-import-check.mjs <scratchDir> <sheet.xlsx>
//
// The sheet is a path outside the repo (the planner's real one lives in private/) or a synthetic one. Needs the app
// running (AppHost: https) and sqlite3 on the PATH; start from a fresh server database for counts that mean something.
// Ports 19352/19353. Household items are put at Costco so the sheet's household prices import too.
import { launch, check, finish, settled, server, dump, phone, screenshot, sleep, until } from './lib/profiles.mjs';

const [scratch, sheet] = process.argv.slice(2);
if (!scratch || !sheet) { console.error('usage: chrome-import-check.mjs <scratchDir> <sheet.xlsx>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 6 minutes'); process.exit(3); }, 360_000).unref();

const a = await launch(scratch, 'importA', 19352, 'import');
const b = await launch(scratch, 'importB', 19353, 'import');
console.log('both profiles online');

async function pick(p) {
  await until(p, `!!document.querySelector('[data-testid=import-file]')`, 30_000);
  const doc = await p.send('DOM.getDocument', {});
  const input = await p.send('DOM.querySelector', { nodeId: doc.result.root.nodeId, selector: '[data-testid=import-file]' });
  await p.send('DOM.setFileInputFiles', { nodeId: input.result.nodeId, files: [sheet] });
  return until(p, `!!document.querySelector('[data-testid=import-summary], [data-testid=import-error]')`, 30_000);
}

const text = (p, testid) => p.evaluate(`document.querySelector('[data-testid=${testid}]')?.textContent.trim().replace(/\\s+/g, ' ') ?? null`);
const count = (p, testid) => p.evaluate(`document.querySelectorAll('[data-testid=${testid}]').length`);

const TABLES = ['Ingredient', 'Category', 'MealGroup', 'Meal', 'MealIngredient', 'Product', 'PriceObservation'];
const counts = q => TABLES.map(t => `${t} ${q(`select count(*) from ${t} where not Deleted`)}`).join(', ')
  + `, guessed ${q('select count(*) from Ingredient where UnitGuessed and not Deleted')}`
  + `, manual prices ${q(`select count(*) from PriceObservation where Source = 'Manual' and not Deleted`)}`;

// --- A reviews and imports ----------------------------------------------------------------------------------------
check(await pick(a), 'A picks the sheet and the review appears');
check(await text(a, 'import-error') === null, 'it is read as the meal-plan sheet');
console.log(`  review: ${await text(a, 'import-summary')}`);
console.log(`  questions: ${await count(a, 'import-ingredient-question')} ingredient, ${await count(a, 'import-serves-question')} serves, `
  + `${await count(a, 'import-group-question')} group, ${await count(a, 'import-category-question')} category, `
  + `${await count(a, 'import-price')} prices; ${await count(a, 'import-new-ingredient')} new ingredients to check`);

const household = await a.evaluate(`(() => { const s = document.querySelector('[data-testid=import-household-store] select');
  if (!s) return null; const o = [...s.options].find(o => o.text === 'Costco'); s.value = o.value; s.dispatchEvent(new Event('change', { bubbles: true })); return o.text; })()`);
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
// A few hundred ops go in one or two pushes; done when the log has grown and then stood still for 3 s.
for (let last = before, still = 0; still < 15; await sleep(200)) {
  const now = server('select max(Seq) from Op');
  still = now !== before && now === last ? still + 1 : 0;
  last = now;
}
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

finish();
