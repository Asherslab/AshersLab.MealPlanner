// The pantry through the running app, from two browser profiles: A desktop-sized, B phone-sized. A buys a pack of mince
// the plan cooks twice this week; both see it drawn down and when it runs out; B counts half a pack; A says a staple is
// running low; B skips one of the meals and the stock comes back; both, offline, put the mince right at once (A "out",
// B bought 2 kg) and end the same; B undoes its purchase. The rows are read back from both profiles' SQLite and the server's.
//
//   node .claude/tools/chrome-pantry-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19360/19361); names carry
// the run id. Stock is "now", so the meals go on tomorrow and the day after next.
import { RUN, launch, check, finish, settled, server, dump, click, typeInto, phone, screenshot, sleep, until, ingredients } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-pantry-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 10 minutes'); process.exit(3); }, 600_000).unref();

const a = await launch(scratch, 'panA', 19360, 'ingredients');
const b = await launch(scratch, 'panB', 19361, 'pantry');
await a.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
await phone(b);
console.log(`run ${RUN}: both profiles online, A desktop-sized, B phone-sized`);
const up = id => id.toUpperCase();
async function serverHas(sql, ok = v => v !== '', ms = 20_000) {
  for (const end = Date.now() + ms; Date.now() < end; await sleep(250)) { const v = server(sql); if (ok(v)) return v; }
  return server(sql);
}
const DINNER = '00000000-5eed-7000-8000-000000000405';
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const today = new Date(); today.setHours(12, 0, 0, 0);
const day = n => { const d = new Date(today); d.setDate(today.getDate() + n); return iso(d); };
const mondayOf = date => { const d = new Date(`${date}T12:00:00`); d.setDate(d.getDate() - ((d.getDay() + 6) % 7)); return iso(d); };
const short = n => new Date(`${day(n)}T12:00:00`).toLocaleDateString('en-AU', { weekday: 'short', day: 'numeric', month: 'short' })
  .replace(',', '').replace(/^(\w+) (\d+) (\w+)$/, '$1 $2 $3');

// --- The catalogue: mince with a 1 kg pack, a staple, and a meal that uses 500 g ------------------------------------------
const mince = `Mince ${RUN}`, soy = `Soy ${RUN}`, bolognese = `Bolognese ${RUN}`;
const ing = ingredients(a);
check(await ing.add(mince) && await ing.add(soy, { unit: 'Millilitre', staple: true }), 'A adds mince (grams) and soy sauce (a staple)');
await a.go('prices');
const pick = (p, testid, text) => p.evaluate(`(() => { const s = document.querySelector('[data-testid=${testid}]');
  const o = [...s.options].find(o => o.textContent.trim() === ${JSON.stringify(text)});
  if (!o) return false; s.value = o.value; s.dispatchEvent(new Event('change', { bubbles: true })); return true; })()`);
const block = `[...document.querySelectorAll('[data-testid=price-ingredient]')].find(r => r.querySelector('[data-testid=price-ingredient-name]')?.textContent.trim() === ${JSON.stringify(mince)})`;
await until(a, `!!${block}`, 30_000);
await a.evaluate(`${block}.querySelector('[data-testid=price-ingredient-add]').click()`);
await until(a, `!!document.getElementById('product-edit-name')`);
await pick(a, 'product-store', 'Costco');
await a.evaluate(typeInto('product-edit-name', `Mince pack ${RUN}`));
await a.evaluate(typeInto('product-edit-pack', '1kg'));
await a.evaluate(typeInto('product-edit-price', '12.00')); await sleep(150);
await click(a, '[data-testid=product-save]');
check(await until(a, `!document.getElementById('product-edit-name')`) && await serverHas(`select PackQuantity from Product where Name = 'Mince pack ${RUN}'`) !== '',
  'A adds a 1 kg pack of it');

await a.go('meals');
await until(a, `!!document.getElementById('meal-new-name')`, 30_000);
await a.evaluate(typeInto('meal-new-name', bolognese)); await sleep(100);
await click(a, '[data-testid=meal-add]');
await until(a, `!!document.getElementById('meal-edit-name')`);
await a.evaluate(`(() => { const e = document.querySelector('[data-testid=meal-kind-Cooked]'); (e.querySelector('input') ?? e).click(); })()`);
await sleep(150); await a.evaluate(typeInto('meal-edit-serves', '4')); await sleep(150);
await click(a, '[data-testid=meal-save]');
await until(a, `document.querySelector('[data-testid=meal-title]')?.textContent.trim() === ${JSON.stringify(bolognese)}`);
await click(a, '[data-testid=meal-ingredient-add]');
await until(a, `!!document.querySelector('[data-testid=meal-ingredient-sheet]')`);
await a.evaluate(typeInto('meal-ingredient-search', mince)); await sleep(300);
await a.evaluate(`[...document.querySelectorAll('[data-testid=meal-ingredient-choice]')].find(e => e.textContent.trim() === ${JSON.stringify(mince)}).click()`);
await until(a, `!!document.getElementById('meal-ingredient-quantity')`);
await a.evaluate(typeInto('meal-ingredient-quantity', '500g')); await sleep(150);
await a.evaluate(`document.getElementById('meal-ingredient-form').requestSubmit()`);
check(await until(a, `!!document.querySelector('[data-testid=meal-ingredient-row]')`), 'A adds a bolognese that uses 500 g of it');
const mealId = await serverHas(`select lower(Id) from Meal where Name = '${bolognese}'`);
await settled(a);

// --- The calendar: bolognese tomorrow and the day after next ----------------------------------------------------------
async function plan(date) {
  await a.go(`calendar?week=${mondayOf(date)}`);
  await until(a, `!!document.querySelector('[data-testid=cal-grid]')`, 30_000);
  await a.evaluate(`document.querySelector('[data-testid=cal-grid] [data-testid=cal-cell][data-date="${date}"][data-slot="${DINNER}"] [data-testid=cal-cell-add]').click()`);
  await until(a, `!!document.getElementById('cal-search')`);
  await a.evaluate(typeInto('cal-search', bolognese)); await sleep(250);
  await click(a, `[data-testid=cal-meal-option][data-id="${mealId}"]`); await sleep(250);
  await click(a, '[data-testid=cal-save]');
  return serverHas(`select lower(Id) from CalendarEntry where MealId = '${up(mealId)}' and Date = '${date}' and Deleted = 0`);
}
const first = await plan(day(1)), second = await plan(day(3));
check(!!first && !!second, `A plans the bolognese for ${day(1)} and ${day(3)}`);

// --- The pantry ------------------------------------------------------------------------------------------------------
const row = name => `[...document.querySelectorAll('[data-testid=pantry-row]')].find(r => r.querySelector('[data-testid=pantry-name]')?.textContent.trim() === ${JSON.stringify(name)})`;
const shows = (p, name) => p.evaluate(`(() => { const r = ${row(name)}; return r ? r.querySelector('[data-testid=pantry-level]').textContent.trim() + ' | ' +
  r.querySelector('[data-testid=pantry-detail]').textContent.trim() : null; })()`);
const showsAs = (p, name, text, ms = 20_000) => until(p, `(() => { const r = ${row(name)}; return !!r && (r.querySelector('[data-testid=pantry-level]').textContent.trim() + ' | ' +
  r.querySelector('[data-testid=pantry-detail]').textContent.trim()) === ${JSON.stringify(text)}; })()`, ms);
const sheetOpen = p => until(p, `!!document.querySelector('[data-testid=pantry-sheet]')`, 10_000);
const sheetClosed = p => until(p, `!document.querySelector('[data-testid=pantry-sheet]')`, 10_000);
const openPantry = async p => { await p.go('pantry'); return until(p, `!!document.getElementById('pantry-search')`, 30_000); };
async function tap(p, name, button) {
  await p.evaluate(`${row(name)}.click()`);
  if (!await sheetOpen(p)) return false;
  await click(p, `[data-testid=${button}]`);
  return sheetClosed(p);
}

await openPantry(a);
await a.evaluate(typeInto('pantry-search', mince)); await sleep(300);
const untracked = `[...document.querySelectorAll('[data-testid=pantry-untracked]')].find(r => r.textContent.trim() === ${JSON.stringify(mince)})`;
check(await until(a, `!!${untracked}`), 'A finds the mince under "Not counted yet"');
await a.evaluate(`${untracked}.click()`);
await sheetOpen(a);
check(await a.evaluate(`document.querySelector('[data-testid=pantry-sheet-now]').textContent.trim()`) === 'Not counted yet'
  && await a.evaluate(`document.querySelectorAll('[data-testid=pantry-sheet-use]').length`) === 2, 'its sheet says not counted yet, and lists the two bolognese coming up');
await click(a, '[data-testid=pantry-bought-1]');
await sheetClosed(a);
await a.evaluate(typeInto('pantry-search', '')); await sleep(200);
check(await showsAs(a, mince, `about 1 kg | runs out ${short(3)}`), `A taps "+1 pack (1 kg)": "${await shows(a, mince)}"`);
console.log(`  desktop screenshot: ${await screenshot(a, 'pantry')}`);

await openPantry(b);
check(await showsAs(b, mince, `about 1 kg | runs out ${short(3)}`), `B (phone) shows the same: "${await shows(b, mince)}"`);
const staple = `[...document.querySelectorAll('[data-testid=pantry-staple]')].find(r => r.querySelector('[data-testid=pantry-staple-name]')?.textContent.trim() === ${JSON.stringify(soy)})`;
const stapleState = p => p.evaluate(`${staple}?.querySelector('[data-testid=pantry-staple-state][data-chosen=true]')?.dataset.state`);
check(await until(b, `!!${staple}`) && await stapleState(b) === 'Have', 'B lists the soy sauce under Staples, as Have');

check(await tap(b, mince, 'pantry-half'), 'B taps the mince, then "½ pack": two taps');
check(await showsAs(b, mince, `about 500 g | runs out ${short(1)}`) && await showsAs(a, mince, `about 500 g | runs out ${short(1)}`),
  `a count sets the level: B "${await shows(b, mince)}", A "${await shows(a, mince)}"`);
console.log(`  phone screenshot: ${await screenshot(b, 'pantry')}`);

await a.evaluate(`${staple}.querySelector('[data-testid=pantry-staple-state][data-state=RunningLow]').click()`);
check(await until(b, `${staple}?.querySelector('[data-testid=pantry-staple-state][data-chosen=true]')?.dataset.state === 'RunningLow'`),
  'A taps "Running low" on the soy sauce: B shows it');
const soyId = await serverHas(`select lower(Id) from Ingredient where Name = '${soy}'`);
check(await serverHas(`select StapleStock from Ingredient where Id = '${up(soyId)}'`, v => v === 'RunningLow') === 'RunningLow', 'and the server holds it');

// --- B skips tomorrow's bolognese: the stock comes back, with no stock op ------------------------------------------------
const movementsBefore = server(`select count(*) from StockMovement where IngredientId = (select Id from Ingredient where Name = '${mince}')`);
await b.go(`calendar?week=${mondayOf(day(1))}`);
await until(b, `!!document.querySelector('[data-testid=cal-day-view]')`, 30_000);
await click(b, `[data-testid=cal-day][data-date="${day(1)}"]`); await sleep(300);
await b.evaluate(`document.querySelector('[data-testid=cal-day-view] [data-testid=cal-entry][data-id="${first}"]').click()`);
await until(b, `!!document.querySelector('[data-testid=cal-skip]')`);
await click(b, '[data-testid=cal-skip]');
await settled(b);
await openPantry(a);
check(await showsAs(a, mince, `about 500 g | runs out ${short(3)}`), `B skips tomorrow's bolognese: A "${await shows(a, mince)}"`);
check(server(`select count(*) from StockMovement where IngredientId = (select Id from Ingredient where Name = '${mince}')`) === movementsBefore,
  'and no stock movement was written for it');

// --- Both offline: A "out", B bought 2 kg ---------------------------------------------------------------------------------
await openPantry(b);
await a.offline(true); await b.offline(true);
check(await tap(a, mince, 'pantry-out'), 'offline, A taps "Out"');
await sleep(300);
await b.evaluate(`${row(mince)}.click()`); await sheetOpen(b);
await b.evaluate(typeInto('pantry-amount', '2kg')); await sleep(150);
await click(b, '[data-testid=pantry-amount-bought]');
check(await sheetClosed(b), 'offline, B types 2kg and taps "Bought this"');
check(await showsAs(a, mince, 'out | ') && await showsAs(b, mince, `about 2.5 kg | 2 kg left after ${short(3)}`),
  `each shows its own: A "${await shows(a, mince)}", B "${await shows(b, mince)}"`);
await a.offline(false); await b.offline(false);
await settled(a, 30_000); await settled(b, 30_000);
const both = `about 2 kg | 1.5 kg left after ${short(3)}`;
check(await showsAs(a, mince, both, 30_000) && await showsAs(b, mince, both, 30_000),
  `back online, both end the same - out, then 2 kg bought: A "${await shows(a, mince)}", B "${await shows(b, mince)}"`);

// --- B undoes its purchase ---------------------------------------------------------------------------------------------
await b.evaluate(`${row(mince)}.click()`); await sheetOpen(b);
check(await until(b, `document.querySelector('[data-testid=pantry-history-row]')?.textContent.trim().endsWith('bought 2 kg')`), 'B\'s history lists its purchase first');
await click(b, '[data-testid=pantry-history-delete]');
await sheetClosed(b);
check(await showsAs(a, mince, 'out | '), `B deletes it: A "${await shows(a, mince)}"`);

// --- The rows, in all three -----------------------------------------------------------------------------------------
await settled(a); await settled(b); await sleep(500);
// Quantity is decimal as text: "1000.0" on the server, "1000" in a browser - compared as numbers.
const rows = `select lower(Id) || '|' || cast(Quantity as real) || '|' || OccurredOn || '|' || Reason || '|' || Deleted from StockMovement
  where IngredientId = (select Id from Ingredient where Name = '${mince}') order by Id`;
const held = server(rows);
console.log(held.split('\n').map(r => `    ${r}`).join('\n'));
check(held.split('\n').length === 4, `four movements: bought, counted, counted out, bought (deleted) (${held.split('\n').length})`);
const stapleRow = `select StapleStock from Ingredient where Name = '${soy}'`;
for (const p of [a, b]) {
  const q = await dump(p);
  check(q(rows) === held && q(stapleRow) === 'RunningLow' && q('select count(*) from Outbox') === '0', `${p.name} SQLite holds the same rows as the server, outbox empty`);
}

finish();
