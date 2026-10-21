// The shopping list through the running app, from two browser profiles: A desktop-sized, B phone-sized. A sets up a week -
// stew twice (500 g of mince each, sold in 2 kg packs), a salad (300 g of mayo), 600 g of mince bought and 500 g of mayo
// counted, soy sauce running low. B opens the list on the phone: one pack of mince (1.6 kg spare), the soy sauce, the mayo
// "in the pantry", Spend and Eaten. B goes offline, ticks the mince and soy sauce bought and adds bin bags; A sees nothing
// until B is back online, then everything. A unticks the mince (its pack leaves the pantry) and says "have it" instead.
// Then both tick it bought offline (one pack in the pantry), B removes the bags from their menu, and the menu's list and
// the calendar's button are checked to be the same list. The rows are read back from both profiles' SQLite and the server's.
//
//   node .claude/tools/chrome-shopping-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https), sqlite3 on the PATH, and a FRESH server database (stop the app, delete
// mealplanner.db*, start it): Spend and Eaten are checked to the cent, and another run's meals or staples on the same days
// would add to them. Fresh profiles each run (ports 19362/19363). The list is tomorrow to a week from tomorrow.
import { RUN, launch, check, finish, settled, server, dump, click, typeInto, phone, screenshot, sleep, until, ingredients } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-shopping-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 10 minutes'); process.exit(3); }, 600_000).unref();

check(server(`select count(*) from CalendarEntry`) === '0', 'the server database is fresh (no calendar entries)');
const a = await launch(scratch, 'shopA', 19362, 'ingredients');
const b = await launch(scratch, 'shopB', 19363, 'shopping');
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
const pick = (p, testid, text) => p.evaluate(`(() => { const s = document.querySelector('[data-testid=${testid}]');
  const o = [...s.options].find(o => o.textContent.trim() === ${JSON.stringify(text)});
  if (!o) return false; s.value = o.value; s.dispatchEvent(new Event('change', { bubbles: true })); return true; })()`);

// --- The catalogue -----------------------------------------------------------------------------------------------------
const mince = `Mince ${RUN}`, mayo = `Mayo ${RUN}`, soy = `Soy ${RUN}`;
const ing = ingredients(a);
check(await ing.add(mince, { category: 'Meat', store: 'Costco' }) && await ing.add(mayo, { category: 'Fridge', store: 'Woolworths' })
  && await ing.add(soy, { unit: 'Millilitre', staple: true, category: 'Pantry', store: 'Woolworths' }),
  'A adds mince (Costco, Meat), mayo (Woolworths, Fridge) and soy sauce (a staple, Woolworths, Pantry)');
const idOf = name => server(`select lower(Id) from Ingredient where Name = '${name}'`);

async function product(ingredient, name, store, pack, price) {
  await a.go('prices');
  const block = `[...document.querySelectorAll('[data-testid=price-ingredient]')].find(r => r.querySelector('[data-testid=price-ingredient-name]')?.textContent.trim() === ${JSON.stringify(ingredient)})`;
  await until(a, `!!${block}`, 30_000);
  await a.evaluate(`${block}.querySelector('[data-testid=price-ingredient-add]').click()`);
  await until(a, `!!document.getElementById('product-edit-name')`);
  await pick(a, 'product-store', store);
  await a.evaluate(typeInto('product-edit-name', name));
  await a.evaluate(typeInto('product-edit-pack', pack));
  await a.evaluate(typeInto('product-edit-price', price)); await sleep(150);
  await click(a, '[data-testid=product-save]');
  return await until(a, `!document.getElementById('product-edit-name')`) && await serverHas(`select PackQuantity from Product where Name = '${name}'`) !== '';
}
check(await product(mince, `Mince 2kg ${RUN}`, 'Costco', '2kg', '24.99') && await product(soy, `Soy 250ml ${RUN}`, 'Woolworths', '250ml', '4.00'),
  'A adds a 2 kg pack of mince at $24.99 and a 250 ml soy sauce at $4.00 (the mayo stays unpriced)');

async function meal(name, serves, ingredient, quantity) {
  await a.go('meals');
  await until(a, `!!document.getElementById('meal-new-name')`, 30_000);
  await a.evaluate(typeInto('meal-new-name', name)); await sleep(100);
  await click(a, '[data-testid=meal-add]');
  await until(a, `!!document.getElementById('meal-edit-name')`);
  await a.evaluate(`(() => { const e = document.querySelector('[data-testid=meal-kind-Cooked]'); (e.querySelector('input') ?? e).click(); })()`);
  await sleep(150); await a.evaluate(typeInto('meal-edit-serves', String(serves))); await sleep(150);
  await click(a, '[data-testid=meal-save]');
  await until(a, `document.querySelector('[data-testid=meal-title]')?.textContent.trim() === ${JSON.stringify(name)}`);
  await click(a, '[data-testid=meal-ingredient-add]');
  await until(a, `!!document.querySelector('[data-testid=meal-ingredient-sheet]')`);
  await a.evaluate(typeInto('meal-ingredient-search', ingredient)); await sleep(300);
  await a.evaluate(`[...document.querySelectorAll('[data-testid=meal-ingredient-choice]')].find(e => e.textContent.trim() === ${JSON.stringify(ingredient)}).click()`);
  await until(a, `!!document.getElementById('meal-ingredient-quantity')`);
  await a.evaluate(typeInto('meal-ingredient-quantity', quantity)); await sleep(150);
  await a.evaluate(`document.getElementById('meal-ingredient-form').requestSubmit()`);
  await until(a, `!!document.querySelector('[data-testid=meal-ingredient-row]')`);
  await settled(a);
  return serverHas(`select lower(Id) from Meal where Name = '${name}'`);
}
const stew = await meal(`Stew ${RUN}`, 4, mince, '500g'), salad = await meal(`Salad ${RUN}`, 1, mayo, '300g');
check(!!stew && !!salad, 'A adds a stew (4 serves, 500 g of mince) and a salad (300 g of mayo)');

async function plan(date, mealId) {
  await a.go(`calendar?week=${mondayOf(date)}`);
  await until(a, `!!document.querySelector('[data-testid=cal-grid]')`, 30_000);
  await a.evaluate(`document.querySelector('[data-testid=cal-grid] [data-testid=cal-cell][data-date="${date}"][data-slot="${DINNER}"] [data-testid=cal-cell-add]').click()`);
  await until(a, `!!document.getElementById('cal-search')`);
  await a.evaluate(typeInto('cal-search', mealId === stew ? `Stew ${RUN}` : `Salad ${RUN}`)); await sleep(250);
  await click(a, `[data-testid=cal-meal-option][data-id="${mealId}"]`); await sleep(250);
  await click(a, '[data-testid=cal-save]');
  return serverHas(`select lower(Id) from CalendarEntry where MealId = '${up(mealId)}' and Date = '${date}' and Deleted = 0`);
}
check(!!await plan(day(1), stew) && !!await plan(day(2), salad) && !!await plan(day(3), stew),
  `A plans stew ${day(1)} and ${day(3)}, salad ${day(2)}`);

// --- The pantry: 600 g of mince bought, 500 g of mayo counted, soy sauce running low -------------------------------------
await a.go('pantry');
await until(a, `!!document.getElementById('pantry-search')`, 30_000);
async function amount(name, text, button) {
  await a.evaluate(typeInto('pantry-search', name)); await sleep(300);
  await a.evaluate(`[...document.querySelectorAll('[data-testid=pantry-untracked],[data-testid=pantry-row]')].find(r => r.textContent.includes(${JSON.stringify(name)})).click()`);
  await until(a, `!!document.querySelector('[data-testid=pantry-sheet]')`);
  await a.evaluate(typeInto('pantry-amount', text)); await sleep(150);
  await click(a, `[data-testid=${button}]`);
  return until(a, `!document.querySelector('[data-testid=pantry-sheet]')`);
}
check(await amount(mince, '600g', 'pantry-amount-bought') && await amount(mayo, '500g', 'pantry-amount-count'), 'A: bought 600 g of mince, 500 g of mayo left');
await a.evaluate(typeInto('pantry-search', '')); await sleep(300);
const staple = `[...document.querySelectorAll('[data-testid=pantry-staple]')].find(r => r.querySelector('[data-testid=pantry-staple-name]')?.textContent.trim() === ${JSON.stringify(soy)})`;
await until(a, `!!${staple}`);
await a.evaluate(`${staple}.querySelector('[data-testid=pantry-staple-state][data-state=RunningLow]').click()`);
check(await serverHas(`select StapleStock from Ingredient where Name = '${soy}'`, v => v === 'RunningLow') === 'RunningLow', 'A: soy sauce running low');
await settled(a);

// --- The list, on the phone --------------------------------------------------------------------------------------------
const listUrl = `shopping?from=${day(1)}&to=${day(7)}`;
const ids = { mince: idOf(mince), mayo: idOf(mayo), soy: idOf(soy) };
const line = id => `document.querySelector('[data-testid=shop-line][data-id="${id}"]')`;
const lineText = (p, id) => p.evaluate(`(() => { const r = ${line(id)}; return r ? [r.dataset.tick, r.querySelector('[data-testid=shop-line-detail]').textContent.trim(),
  r.querySelector('[data-testid=shop-line-cost]').textContent.trim()].join(' | ') : null; })()`);
const lineIs = (p, id, text, ms = 20_000) => until(p, `(() => { const r = ${line(id)}; return !!r && [r.dataset.tick, r.querySelector('[data-testid=shop-line-detail]').textContent.trim(),
  r.querySelector('[data-testid=shop-line-cost]').textContent.trim()].join(' | ') === ${JSON.stringify(text)}; })()`, ms);
const text = (p, testid) => p.evaluate(`document.querySelector('[data-testid=${testid}]')?.textContent.trim()`);
const textIs = (p, testid, want, ms = 20_000) => until(p, `document.querySelector('[data-testid=${testid}]')?.textContent.trim() === ${JSON.stringify(want)}`, ms);
const storeOf = (p, id) => p.evaluate(`(() => { let e = ${line(id)}?.closest('.mud-paper'); while (e && e.dataset?.testid !== 'shop-store') e = e.previousElementSibling; return e?.textContent.trim(); })()`);

await b.go(listUrl);
await until(b, `!!document.querySelector('[data-testid=shop-range]')`, 30_000);
const minceLine = 'None | 1 pack (1.6 kg spare) · need 1 kg, have about 600 g | $24.99';
check(await lineIs(b, ids.mince, minceLine), `B's list: mince "${await lineText(b, ids.mince)}"`);
check(await storeOf(b, ids.mince) === 'Costco' && await storeOf(b, ids.soy) === 'Woolworths', 'under Costco and Woolworths, its usual stores');
check(await lineIs(b, ids.soy, 'None | 1 pack · running low | $4.00'), `soy sauce: "${await lineText(b, ids.soy)}"`);
check(await until(b, `document.querySelector('[data-testid=shop-pantry-line][data-id="${ids.mayo}"] [data-testid=shop-pantry-detail]')?.textContent.trim() === 'need 300 g, have about 500 g'`),
  'the mayo is "in the pantry": need 300 g, have about 500 g');
// Spend: the mince pack and the soy sauce. Eaten: two stews at $1.56 a serve ($6.25 a batch of 4) and the unpriced salad.
check(await textIs(b, 'shop-spend', '$28.99') && await textIs(b, 'shop-eaten', 'from $3.12 + 1 unpriced'),
  `Spend "${await text(b, 'shop-spend')}", Eaten "${await text(b, 'shop-eaten')}"`);
check(await textIs(b, 'shop-progress', '2 to get · 0 done'), `"${await text(b, 'shop-progress')}"`);
console.log(`  phone screenshot: ${await screenshot(b, 'shopping')}`);

// --- Offline in the shop: tick the mince and soy sauce, add bin bags ----------------------------------------------------
await a.go(listUrl);
await lineIs(a, ids.mince, minceLine, 30_000);
await b.offline(true);
await b.evaluate(`${line(ids.mince)}.querySelector('[data-testid=shop-line-tap]').click()`);
check(await lineIs(b, ids.mince, `Bought | ${minceLine.split(' | ').slice(1).join(' | ')}`), 'offline, B taps the mince: ticked, and still a line to buy - not "in the pantry"');
await b.evaluate(`${line(ids.soy)}.querySelector('[data-testid=shop-line-tap]').click()`);
check(await lineIs(b, ids.soy, 'Bought | 1 pack · have | $4.00'), `B ticks the soy sauce: "${await lineText(b, ids.soy)}" (back to Have, still listed)`);
await b.evaluate(typeInto('shop-extra-new-name', `Bin bags ${RUN}`));
await pick(b, 'shop-extra-new-store', 'Woolworths');
await b.evaluate(typeInto('shop-extra-new-price', '12.99')); await sleep(150);
await b.evaluate(`document.querySelector('[data-testid=shop-extra-form]').requestSubmit()`);
check(await until(b, `[...document.querySelectorAll('[data-testid=shop-extra-name]')].some(e => e.textContent.trim() === 'Bin bags ${RUN}')`),
  'B adds bin bags at Woolworths, $12.99');
check(await textIs(b, 'shop-spend', '$41.98') && await textIs(b, 'shop-progress', '1 to get · 2 done'),
  `B: Spend "${await text(b, 'shop-spend')}", "${await text(b, 'shop-progress')}"`);
const pending = await b.evaluate(`document.querySelector('[data-testid=sync-status]')?.dataset.pending`);
check(Number(pending) > 0 && server(`select count(*) from ShoppingListItem`) === '0', `nothing has reached the server (${pending} waiting on B)`);
await sleep(1500);
check(await lineIs(a, ids.mince, minceLine, 1000), 'A still shows the mince unticked');

await b.offline(false);
await settled(b, 30_000);
check(await lineIs(a, ids.mince, `Bought | ${minceLine.split(' | ').slice(1).join(' | ')}`, 30_000)
  && await lineIs(a, ids.soy, 'Bought | 1 pack · have | $4.00')
  && await until(a, `[...document.querySelectorAll('[data-testid=shop-extra-name]')].some(e => e.textContent.trim() === 'Bin bags ${RUN}')`)
  && await textIs(a, 'shop-spend', '$41.98'),
  `B back online: the desktop shows both ticks, the bin bags and Spend "${await text(a, 'shop-spend')}"`);
console.log(`  desktop screenshot: ${await screenshot(a, 'shopping')}`);
console.log(`  phone screenshot (ticked): ${await screenshot(b, 'shopping-ticked')}`);
const tickId = await serverHas(`select lower(ShoppingItemId) from StockMovement where IngredientId = '${up(ids.mince)}' and Reason = 'Bought' and ShoppingItemId is not null`);
check(server(`select cast(Quantity as real) from StockMovement where ShoppingItemId = '${up(tickId)}'`) === '2000.0',
  'the server holds a Bought movement of the whole 2 kg pack, naming the tick');

// The pantry, on the desktop: 600 g + the 2 kg pack.
await a.go('pantry');
const pantryRow = `[...document.querySelectorAll('[data-testid=pantry-row]')].find(r => r.querySelector('[data-testid=pantry-name]')?.textContent.trim() === ${JSON.stringify(mince)})`;
check(await until(a, `${pantryRow}?.querySelector('[data-testid=pantry-level]')?.textContent.trim() === 'about 2.6 kg'`, 30_000),
  'A\'s pantry: about 2.6 kg of mince');

// --- A unticks the mince, then says they have it -----------------------------------------------------------------------
await a.go(listUrl);
await lineIs(a, ids.mince, `Bought | ${minceLine.split(' | ').slice(1).join(' | ')}`, 30_000);
await a.evaluate(`${line(ids.mince)}.querySelector('[data-testid=shop-line-tap]').click()`);
check(await lineIs(b, ids.mince, minceLine, 30_000) && await serverHas(`select Deleted from StockMovement where ShoppingItemId = '${up(tickId)}' and Reason = 'Bought'`, v => v === '1') === '1',
  'A unticks it: B shows it unticked, and the pack is deleted from the pantry on the server');
await a.evaluate(`${line(ids.mince)}.querySelector('[data-testid=shop-have]').click()`);
check(await lineIs(b, ids.mince, 'HaveIt | have it | ', 30_000) && await textIs(b, 'shop-spend', '$16.99'),
  `A taps "Have": B "${await lineText(b, ids.mince)}", Spend "${await text(b, 'shop-spend')}"`);
check(await serverHas(`select cast(Quantity as real) from StockMovement where ShoppingItemId = '${up(tickId)}' and Reason = 'Adjusted' and Deleted = 0`) === '400.0',
  'and the pantry is told the 400 g it lacked (Adjusted, naming the tick)');

// --- Both in the shop offline, both tick the mince bought: one pack in the pantry, not two -------------------------------
await settled(a); await settled(b);
await a.offline(true); await b.offline(true);
await a.evaluate(`${line(ids.mince)}.querySelector('[data-testid=shop-line-tap]').click()`);
await sleep(300);
await b.evaluate(`${line(ids.mince)}.querySelector('[data-testid=shop-line-tap]').click()`);
check(await until(a, `${line(ids.mince)}?.dataset.tick === 'Bought'`) && await until(b, `${line(ids.mince)}?.dataset.tick === 'Bought'`),
  'both offline, A and B each tick the mince bought');
await a.offline(false); await b.offline(false);
await settled(a, 30_000); await settled(b, 30_000);
check(await serverHas(`select count(*) from StockMovement where ShoppingItemId = '${up(tickId)}' and Reason = 'Bought' and Deleted = 0`, v => v === '2') === '2',
  'back online: the server holds two live Bought movements naming the one tick');
await a.go('pantry');
check(await until(a, `${pantryRow}?.querySelector('[data-testid=pantry-level]')?.textContent.trim() === 'about 2.6 kg'`, 30_000),
  `A's pantry counts one pack: "${await a.evaluate(`${pantryRow}?.querySelector('[data-testid=pantry-level]')?.textContent.trim()`)}" (600 g + 2 kg, not 4.6 kg)`);

// --- Removing an extra is behind its menu, not at the thumb's edge -------------------------------------------------------
const bags = `[...document.querySelectorAll('[data-testid=shop-extra]')].find(r => r.querySelector('[data-testid=shop-extra-name]')?.textContent.trim() === 'Bin bags ${RUN}')`;
await b.go(listUrl);
await until(b, `!!${bags}`, 30_000);
check(await b.evaluate(`!${bags}.querySelector('[data-testid=shop-extra-delete]')`), 'no remove button on the bin bags row itself');
await b.evaluate(`${bags}.querySelector('[data-testid=shop-extra-more] button').click()`);
check(await until(b, `!!document.querySelector('[data-testid=shop-extra-delete]')`), 'its ⋮ menu offers "Remove"');
await b.evaluate(`document.querySelector('[data-testid=shop-extra-delete]').click()`);
check(await serverHas(`select Deleted from ShoppingListItem where Name = 'Bin bags ${RUN}'`, v => v === '1') === '1' && await until(b, `!${bags}`, 30_000),
'removed from the menu: gone from B, deleted on the server');

// --- The nav and the calendar open the same list ------------------------------------------------------------------------
await b.go('shopping');
await until(b, `!!document.querySelector('[data-testid=shop-range]')`, 30_000);
const navRange = await text(b, 'shop-range');
await b.go(`calendar?week=${mondayOf(day(0))}`);
await until(b, `!!document.querySelector('[data-testid=cal-shopping]')`, 30_000);
const calHref = await b.evaluate(`document.querySelector('[data-testid=cal-shopping]').getAttribute('href')`);
await b.go(calHref);
await until(b, `!!document.querySelector('[data-testid=shop-range]')`, 30_000);
check(navRange === await text(b, 'shop-range') && calHref.includes(mondayOf(day(0))),
  `"Shopping" in the menu and the calendar's button open the same list: "${navRange}" (${calHref})`);

// --- The calendar's week shows both costs --------------------------------------------------------------------------------
await a.go(`calendar?week=${mondayOf(day(1))}`);
check(await until(a, `/^(from )?\\$\\d+\\.\\d\\d/.test(document.querySelector('[data-testid=cal-week-spend]')?.textContent.trim() ?? '')`, 30_000)
  && /\$/.test(await text(a, 'cal-week-eaten')), `the calendar's week: Spend "${await text(a, 'cal-week-spend')}", Eaten "${await text(a, 'cal-week-eaten')}"`);

// --- The rows, in all three -----------------------------------------------------------------------------------------
await settled(a); await settled(b); await sleep(500);
const items = `select lower(Id) || '|' || lower(ListId) || '|' || coalesce(lower(IngredientId), '') || '|' || coalesce(Name, '') || '|' || coalesce(PriceCents, '') || '|' || Tick || '|' || Deleted
  from ShoppingListItem order by Id`;
const moves = `select lower(Id) || '|' || cast(Quantity as real) || '|' || Reason || '|' || coalesce(lower(ShoppingItemId), '') || '|' || Deleted from StockMovement order by Id`;
const lists = `select lower(Id) || '|' || StartsOn || '|' || EndsOn from ShoppingList`;
const held = { items: server(items), moves: server(moves), lists: server(lists) };
console.log([held.lists, held.items, held.moves].join('\n').split('\n').map(r => `    ${r}`).join('\n'));
check(held.lists.split('\n').length === 1 && held.lists.endsWith(`${day(1)}|${day(7)}`), 'one list, for the days asked');
check(held.items.split('\n').length === 3, `three items: the mince and soy sauce ticks, the bin bags (${held.items.split('\n').length})`);
for (const p of [a, b]) {
  const q = await dump(p);
  check(q(items) === held.items && q(moves).replaceAll('.0|', '|') === held.moves.replaceAll('.0|', '|') && q(lists) === held.lists
    && q('select count(*) from Outbox') === '0', `${p.name} SQLite holds the same rows as the server, outbox empty`);
}

finish();
