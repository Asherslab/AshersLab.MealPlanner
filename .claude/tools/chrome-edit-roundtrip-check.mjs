// The edit boxes round-trip what is stored: a meal's ingredient line and a product's pack size of 1234.5 g are saved,
// reopened and saved untouched - no op may be sent and the quantity must not move to 1235 g. Then a run of digits too
// big for a decimal is pasted into the quantity box: refused, and the page keeps working.
//
//   node .claude/tools/chrome-edit-roundtrip-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https), sqlite3 on the PATH, and a product of an ingredient in grams - run
// chrome-prices-check.mjs once first. Adds a meal of its own, named with the run id. One fresh profile (port 19351).
// APP_URL / SERVER_DB as in chrome-sync-check.mjs.
import { RUN, launch, check, finish, settled, server, dump, click, typeInto, sleep, until } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-edit-roundtrip-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 4 minutes'); process.exit(3); }, 240_000).unref();

const ops = id => Number(server(`select count(*) from Op where upper(EntityId) = upper('${id}');`));
// Decimals are text in the server's SQLite ("500.0"); compare them as numbers.
const stored = (table, column, id) => server(`select cast(${column} as real) from ${table} where upper(Id) = upper('${id}');`);
/** Polls the server's SQLite: the chip says "online, 0 waiting" before a write made a moment ago is even queued. */
async function serverHas(read, want, ms = 15_000) {
  const end = Date.now() + ms;
  while (Date.now() < end) { if (read() === want) return true; await sleep(200); }
  return false;
}

// The newest live product of an ingredient in grams.
const [productId, ingredient] = server(`select p.Id, i.Name from Product p join Ingredient i on i.Id = p.IngredientId
  where not p.Deleted and not i.Deleted and i.BaseUnit = 'Gram' order by p.rowid desc limit 1;`).split('|');
if (!productId) { console.error('no product in grams - run chrome-prices-check.mjs first'); process.exit(2); }

const a = await launch(scratch, 'roundtrip', 19351, 'meals');
await settled(a, 60_000);

// --- A meal's ingredient line -------------------------------------------------------------------------------------
{
  const sheetOpen = () => until(a, `!!document.querySelector('[data-testid=meal-ingredient-sheet]')`, 10_000);
  const sheetClosed = () => until(a, `!document.querySelector('[data-testid=meal-ingredient-sheet]')`, 10_000);
  const box = () => a.evaluate(`document.getElementById('meal-ingredient-quantity')?.value`);
  const submit = () => a.evaluate(`document.getElementById('meal-ingredient-form').requestSubmit()`);

  const meal = `Roundtrip ${RUN}`;
  await until(a, `!!document.getElementById('meal-new-name')`, 30_000);
  await a.evaluate(typeInto('meal-new-name', meal)); await sleep(100);
  await click(a, '[data-testid=meal-add]');
  await until(a, `!!document.getElementById('meal-edit-name')`, 10_000);
  await a.evaluate(typeInto('meal-edit-serves', '4')); await sleep(100);
  await click(a, '[data-testid=meal-save]');
  check(await until(a, `document.querySelector('[data-testid=meal-title]')?.textContent.trim() === ${JSON.stringify(meal)}`),
    `a meal of its own: "${meal}"`);
  const mealId = await a.evaluate(`location.pathname.split('/').pop()`);

  await until(a, `!!document.querySelector('[data-testid=meal-ingredient-add]')`, 10_000);
  await click(a, '[data-testid=meal-ingredient-add]'); await sheetOpen();
  await until(a, `document.activeElement?.id === 'meal-ingredient-search'`, 3000);
  await a.evaluate(typeInto('meal-ingredient-search', ingredient)); await sleep(300);
  const choice = `[...document.querySelectorAll('[data-testid=meal-ingredient-choice]')].find(e => e.textContent.trim() === ${JSON.stringify(ingredient)})`;
  await until(a, `!!${choice}`, 5000); await a.evaluate(`${choice}.click()`);
  await until(a, `document.activeElement?.id === 'meal-ingredient-quantity'`, 3000);
  await a.evaluate(typeInto('meal-ingredient-quantity', '1234.5')); await sleep(200);
  await submit(); await sheetClosed();

  const line = () => server(`select Id from MealIngredient where upper(MealId) = upper('${mealId}') and not Deleted;`);
  await serverHas(() => line() !== '', true);
  const lineId = line();
  check(await serverHas(() => stored('MealIngredient', 'Quantity', lineId), '1234.5'),
    `A adds 1234.5 g of ${ingredient}; the server holds 1234.5 (${stored('MealIngredient', 'Quantity', lineId)})`);
  const row = `document.querySelector('[data-testid=meal-ingredient-row][data-id="${lineId.toLowerCase()}"]')`;
  check(await until(a, `${row}?.querySelector('[data-testid=meal-ingredient-detail]')?.textContent.startsWith('1.235 kg')`),
    `the line reads 1.235 kg (${await a.evaluate(`${row}?.querySelector('[data-testid=meal-ingredient-detail]')?.textContent`)})`);
  await settled(a);
  const before = ops(lineId);

  await a.evaluate(`${row}.click()`); await sheetOpen(); await sleep(300);
  check(await box() === '1.2345kg', `reopened, the box holds every digit: "1.2345kg" (got "${await box()}")`);
  await submit(); await sheetClosed(); await sleep(3000); await settled(a);
  check(ops(lineId) === before, `saved untouched, no op was sent (${before} -> ${ops(lineId)} ops for the line)`);
  check(stored('MealIngredient', 'Quantity', lineId) === '1234.5', `the server still holds 1234.5 (${stored('MealIngredient', 'Quantity', lineId)})`);

  // Too big for a decimal: the helper text re-reads the box on every keystroke, during render.
  await a.evaluate(`${row}.click()`); await sheetOpen(); await sleep(300);
  await a.evaluate(typeInto('meal-ingredient-quantity', '99999999999999999999999999999999kg')); await sleep(400);
  await submit(); await sleep(400);
  const refused = await a.evaluate(`!!document.querySelector('[data-testid=meal-ingredient-sheet]')?.textContent.includes('That number is too big.')`);
  const crashed = await a.evaluate(`(() => { const e = document.getElementById('blazor-error-ui'); return !!e && getComputedStyle(e).display !== 'none'; })()`);
  check(refused && !crashed, `a pasted run of digits is refused with "That number is too big." and the page does not crash (refused ${refused}, crashed ${crashed})`);
  await click(a, '[data-testid=meal-ingredient-cancel]');
  check(await sheetClosed(), 'Cancel still closes the sheet');
  await sleep(1000);
  check(ops(lineId) === before && stored('MealIngredient', 'Quantity', lineId) === '1234.5', 'nothing was saved');
}

// --- A product's pack size ----------------------------------------------------------------------------------------
{
  await a.go('prices'); await settled(a);
  const row = `document.querySelector('[data-testid=price-product-row][data-id="${productId.toLowerCase()}"]')`;
  const editorOpen = () => until(a, `!!document.getElementById('product-edit-pack')`, 10_000);
  const editorClosed = () => until(a, `!document.getElementById('product-edit-pack')`, 10_000);
  const openEditor = async () => {
    await a.evaluate(`${row}.click()`);
    await until(a, `!!document.querySelector('[data-testid=price-sheet-edit-product]')`, 10_000);
    await click(a, '[data-testid=price-sheet-edit-product]'); await editorOpen(); await sleep(300);
  };

  check(await until(a, `!!${row}`, 30_000), `the product of ${ingredient} is on the prices page`);
  await openEditor();
  await a.evaluate(typeInto('product-edit-pack', '1234.5g')); await sleep(200);
  await click(a, '[data-testid=product-save]'); await editorClosed();
  check(await serverHas(() => stored('Product', 'PackQuantity', productId), '1234.5'),
    `a 1234.5 g pack; the server holds it (${stored('Product', 'PackQuantity', productId)})`);
  await settled(a); await sleep(500);
  const before = ops(productId);

  await openEditor();
  const pack = await a.evaluate(`document.getElementById('product-edit-pack').value`);
  check(pack === '1.2345kg', `reopened, the pack box holds every digit: "1.2345kg" (got "${pack}")`);
  await click(a, '[data-testid=product-save]'); await editorClosed(); await sleep(3000); await settled(a);
  check(ops(productId) === before, `saved untouched, no op was sent (${before} -> ${ops(productId)} ops for the product)`);
  check(stored('Product', 'PackQuantity', productId) === '1234.5', `the server still holds 1234.5 (${stored('Product', 'PackQuantity', productId)})`);
}

// --- The browser's own SQLite -------------------------------------------------------------------------------------
{
  const q = await dump(a);
  const pack = q(`select cast(PackQuantity as real) from Product where upper(Id) = upper('${productId}');`);
  const lines = q(`select cast(l.Quantity as real) from MealIngredient l join Meal m on m.Id = l.MealId
    where m.Name = 'Roundtrip ${RUN}' and not l.Deleted;`);
  const outbox = q('select count(*) from Outbox;');
  check(pack === '1234.5' && lines === '1234.5' && outbox === '0',
    `the browser's SQLite holds the same: pack ${pack}, line ${lines}, outbox ${outbox}`);
}

finish();
