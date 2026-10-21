// Receipt import through the running app, from two browser profiles: A desktop-sized, B phone-sized.
//
// 1. A synthetic invoice dated today (.claude/tools/lib/synthetic-invoice.mjs, written to the scratch directory - every
//    name and number in it invented): A adds two ingredients, plans a meal of one for tomorrow and ticks it bought on the
//    shopping list. B imports the invoice: makes a product of each ingredient and a household one, and the beans' purchase
//    replaces A's tick. The server holds the receipt, its lines, three receipt prices, the stock and three aliases; A's
//    pantry counts the receipt's 840 g once, not the tick's 500 g on top; A's list still shows the line bought.
// 2. B picks the same file again: refused, naming the earlier import.
// 3. B imports a second synthetic order with the same lines: every line matches by its alias, by itself.
// 4. Each real invoice given on the command line is imported by A as it reads (nothing matched), then refused on B. Only
//    counts are printed - never a name or number from a real invoice.
// Then every receipt row is read back from both profiles' SQLite and the server's.
//
//   node .claude/tools/chrome-receipts-check.mjs <scratchDir> [<invoice.pdf> ...]
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19364/19365); names
// carry the run id, so runs can share a server database.
import { join } from 'node:path';
import { mkdirSync } from 'node:fs';
import { RUN, launch, check, finish, settled, server, dump, click, typeInto, phone, screenshot, sleep, until, ingredients } from './lib/profiles.mjs';
import { invoicePdf } from './lib/synthetic-invoice.mjs';

const [scratch, ...realInvoices] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-receipts-check.mjs <scratchDir> [<invoice.pdf> ...]'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 10 minutes'); process.exit(3); }, 600_000).unref();
mkdirSync(scratch, { recursive: true });

const a = await launch(scratch, 'rcptA', 19364, 'ingredients');
const b = await launch(scratch, 'rcptB', 19365, 'receipts');
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
const printed = `${today.getDate()} ${'Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec'.split(' ')[today.getMonth()]} ${today.getFullYear()}`;
const text = (p, testid) => p.evaluate(`document.querySelector('[data-testid=${testid}]')?.textContent.trim().replace(/\\s+/g, ' ')`);

// --- A: two ingredients, a meal of beans tomorrow, the beans ticked bought ------------------------------------------------
const beans = `Beans ${RUN}`, chicken = `Chicken ${RUN}`;
const ing = ingredients(a);
check(await ing.add(beans, { category: 'Pantry', store: 'Woolworths' }) && await ing.add(chicken, { category: 'Meat', store: 'Woolworths' }),
  'A adds beans and chicken (grams, Woolworths)');
const ids = { beans: await serverHas(`select lower(Id) from Ingredient where Name = '${beans}'`), chicken: await serverHas(`select lower(Id) from Ingredient where Name = '${chicken}'`) };

await a.go('meals');
await until(a, `!!document.getElementById('meal-new-name')`, 30_000);
await a.evaluate(typeInto('meal-new-name', `Bean stew ${RUN}`)); await sleep(100);
await click(a, '[data-testid=meal-add]');
await until(a, `!!document.getElementById('meal-edit-name')`);
await a.evaluate(`(() => { const e = document.querySelector('[data-testid=meal-kind-Cooked]'); (e.querySelector('input') ?? e).click(); })()`);
await sleep(150); await a.evaluate(typeInto('meal-edit-serves', '4')); await sleep(150);
await click(a, '[data-testid=meal-save]');
await until(a, `document.querySelector('[data-testid=meal-title]')?.textContent.trim() === 'Bean stew ${RUN}'`);
await click(a, '[data-testid=meal-ingredient-add]');
await until(a, `!!document.querySelector('[data-testid=meal-ingredient-sheet]')`);
await a.evaluate(typeInto('meal-ingredient-search', beans)); await sleep(300);
await a.evaluate(`[...document.querySelectorAll('[data-testid=meal-ingredient-choice]')].find(e => e.textContent.trim() === ${JSON.stringify(beans)}).click()`);
await until(a, `!!document.getElementById('meal-ingredient-quantity')`);
await a.evaluate(typeInto('meal-ingredient-quantity', '500g')); await sleep(150);
await a.evaluate(`document.getElementById('meal-ingredient-form').requestSubmit()`);
await until(a, `!!document.querySelector('[data-testid=meal-ingredient-row]')`);
await settled(a);
const stew = await serverHas(`select lower(Id) from Meal where Name = 'Bean stew ${RUN}'`);

await a.go(`calendar?week=${mondayOf(day(1))}`);
await until(a, `!!document.querySelector('[data-testid=cal-grid]')`, 30_000);
await a.evaluate(`document.querySelector('[data-testid=cal-grid] [data-testid=cal-cell][data-date="${day(1)}"][data-slot="${DINNER}"] [data-testid=cal-cell-add]').click()`);
await until(a, `!!document.getElementById('cal-search')`);
await a.evaluate(typeInto('cal-search', `Bean stew ${RUN}`)); await sleep(250);
await click(a, `[data-testid=cal-meal-option][data-id="${stew}"]`); await sleep(250);
await click(a, '[data-testid=cal-save]');
check(!!await serverHas(`select Id from CalendarEntry where MealId = '${up(stew)}' and Date = '${day(1)}'`), `A plans the bean stew (500 g of beans) for ${day(1)}`);

const listUrl = `shopping?from=${day(1)}&to=${day(1)}`;
const line = id => `document.querySelector('[data-testid=shop-line][data-id="${id}"]')`;
await a.go(listUrl);
await until(a, `!!${line(ids.beans)}`, 30_000);
await a.evaluate(`${line(ids.beans)}.querySelector('[data-testid=shop-line-tap]').click()`);
const tick = await serverHas(`select lower(ShoppingItemId) from StockMovement where IngredientId = '${up(ids.beans)}' and ShoppingItemId is not null and Deleted = 0`);
check(!!tick && server(`select cast(Quantity as real) from StockMovement where ShoppingItemId = '${up(tick)}' and Deleted = 0`) === '500.0',
  'A ticks the beans bought on the list: a 500 g Bought movement naming the tick');

// --- B: the synthetic invoice ---------------------------------------------------------------------------------------------
const order = String(Date.now()).slice(-9);
const synthetic = (n, path) => invoicePdf(path, { order: n, date: printed, subtotal: '$16.60', bags: '$2.00', total: '$18.60', lines: [
  { heading: 'Pantry', n: 4, desc: `Synthetic beans ${RUN} 420g`, ordered: 2, supplied: 2, price: '$1.00', amount: '$2.00' },
  { heading: 'Meat', n: 17, name: `Synthetic chicken ${RUN} small`, range: '459g - 749g', perKg: '$14.50/Kg', supplied: '1 @ 662g', amount: '$9.60' },
  { heading: 'Household Cleaning', n: 15, desc: `* Synthetic cleaner ${RUN} 750ml`, ordered: 1, supplied: 1, price: '$5.00', amount: '$5.00' },
] });
const first = synthetic(order, join(scratch, `${RUN}-synthetic-1.pdf`));
const second = synthetic(String(Number(order) + 1).padStart(9, '0'), join(scratch, `${RUN}-synthetic-2.pdf`));

async function pick(p, file) {
  await p.go('receipts');
  await until(p, `!!document.querySelector('[data-testid=receipt-file]')`, 30_000);
  const doc = await p.send('DOM.getDocument', {});
  const input = await p.send('DOM.querySelector', { nodeId: doc.result.root.nodeId, selector: '[data-testid=receipt-file]' });
  await p.send('DOM.setFileInputFiles', { nodeId: input.result.nodeId, files: [file] });
  return until(p, `!!document.querySelector('[data-testid=receipt-summary], [data-testid=receipt-error]')`, 30_000);
}
const rline = n => `document.querySelector('[data-testid=receipt-line][data-position="${n}"]')`;
const choose = (p, n, value) => p.evaluate(`(() => { const s = ${rline(n)}.querySelector('[data-testid=receipt-line-choice]');
  s.value = ${JSON.stringify(value)}; s.dispatchEvent(new Event('change', { bubbles: true })); return s.value === ${JSON.stringify(value)}; })()`);
const lines = p => p.evaluate(`[...document.querySelectorAll('[data-testid=receipt-line]')].map(l => l.dataset.match + '|' + l.dataset.kind + '|' + l.querySelector('[data-testid=receipt-line-will]').textContent.trim())`);

check(await pick(b, first) && await until(b, `!!document.querySelector('[data-testid=receipt-totals-ok]')`), `B picks the synthetic invoice: read, and it adds up`);
check(await b.evaluate(`document.querySelectorAll('[data-testid=receipt-line]').length`) === 3
  && (await lines(b)).every(l => l.startsWith('None|')), 'three lines, none matched by itself the first time');
await click(b, '[data-testid=receipt-show-file]');
check(await until(b, `document.querySelectorAll('[data-testid=receipt-file-view] canvas').length === 2`), 'B taps "Show the invoice": its two pages drawn on the page');
await click(b, '[data-testid=receipt-show-file]');
await choose(b, 0, `n:${ids.beans}`);
check(await until(b, `document.getElementById('receipt-new-pack-0')?.value === '420g'`), 'beans → "a new product of" the beans: pack size read from the description, 420g');
check(await until(b, `/Replaces the shopping list's tick/.test(${rline(0)}.querySelector('[data-testid=receipt-line-cover]')?.textContent ?? '')`),
  `and it offers to replace A's tick: "${await b.evaluate(`${rline(0)}.querySelector('[data-testid=receipt-line-cover]')?.textContent.trim()`)}"`);
await choose(b, 1, `n:${ids.chicken}`);
check(await until(b, `document.getElementById('receipt-new-pack-1')?.value === '749g'`), 'the weighed chicken → a product of the chicken, its pack the heaviest tray, 749g');
await choose(b, 2, 'h');
await sleep(300);
const will = await lines(b);
console.log(will.map(l => `    ${l}`).join('\n'));
check(will[0].includes('840 g of Beans') && will[1].includes('662 g of Chicken') && will[2].includes('Not food'), 'what each line will do: 840 g of beans, 662 g of chicken, the cleaner not food');
console.log(`  phone screenshot: ${await screenshot(b, 'receipt-review')}`);
await click(b, '[data-testid=receipt-import]');
check(await until(b, `!!document.querySelector('[data-testid=receipt-done]')`), `B imports it: "${await text(b, 'receipt-done')}"`);

const receipt = await serverHas(`select lower(Id) from Receipt where OrderNumber = '${order}'`);
check(!!receipt, 'the server holds the receipt');
const kinds = await serverHas(`select group_concat(Kind, ',') from (select Kind from ReceiptLine where ReceiptId = '${up(receipt)}' order by Position)`, v => v.split(',').length === 4);
check(kinds === 'Food,Food,Household,Fee', `its four lines: ${kinds}`);
check(server(`select group_concat(PriceCents || '/' || coalesce(PerKgCents, ''), ',') from (select * from PriceObservation where Source = 'Receipt' and ObservedOn = '${day(0)}'
  and ProductId in (select ProductId from ReceiptLine where ReceiptId = '${up(receipt)}') order by PriceCents)`) === '100/,500/,960/1450',
  'three receipt prices: the beans $1.00 a tin, the cleaner $5.00, the chicken tray $9.60 at $14.50/kg');
const covering = server(`select cast(Quantity as real) || '|' || lower(CoversItemId) from StockMovement where IngredientId = '${up(ids.beans)}' and CoversItemId is not null`);
check(covering === `840.0|${tick}`, `the beans' purchase: 840 g, replacing A's tick (${covering})`);
check(server(`select cast(Quantity as real) from StockMovement where IngredientId = '${up(ids.chicken)}'`) === '662.0', 'the chicken: 662 g, its weight');
check(server(`select count(*) from ProductAlias where Text like '%${RUN}%'`) === '3', 'three aliases taught');

await a.go('pantry');
const pantryRow = `[...document.querySelectorAll('[data-testid=pantry-row]')].find(r => r.querySelector('[data-testid=pantry-name]')?.textContent.trim() === ${JSON.stringify(beans)})`;
check(await until(a, `${pantryRow}?.querySelector('[data-testid=pantry-level]')?.textContent.trim() === 'about 840 g'`, 30_000),
  `A's pantry counts the purchase once: "${await a.evaluate(`${pantryRow}?.querySelector('[data-testid=pantry-level]')?.textContent.trim()`)}" (not 1.34 kg)`);
await a.go(listUrl);
check(await until(a, `${line(ids.beans)}?.dataset.tick === 'Bought' && ${line(ids.beans)}?.dataset.need === 'Buy'`, 30_000),
  'A\'s list still shows the beans bought - a line it bought, not "in the pantry"');

// --- The same file again: refused ---------------------------------------------------------------------------------------
await pick(b, first);
check(await until(b, `!!document.querySelector('[data-testid=receipt-earlier]')`) && await b.evaluate(`document.querySelector('[data-testid=receipt-import]').disabled`),
  `B picks it again: refused - "${await text(b, 'receipt-earlier')}"`);
await click(b, '[data-testid=receipt-cancel]');

// --- A second order with the same lines: matched by itself -------------------------------------------------------------
await pick(b, second);
check(await until(b, `[...document.querySelectorAll('[data-testid=receipt-line]')].every(l => l.dataset.match === 'Alias')`),
  `B's next order: every line matched by its alias (${(await lines(b)).map(l => l.split('|')[0]).join(', ')})`);
check(await b.evaluate(`!document.querySelector('[data-testid=receipt-line-cover]')`), 'and no tick offered - A\'s was replaced already');
await click(b, '[data-testid=receipt-import]');
check(await until(b, `!!document.querySelector('[data-testid=receipt-done]')`)
  && await serverHas(`select count(*) from PriceObservation where Source = 'Receipt' and ObservedOn = '${day(0)}'`, v => v === '6') === '6',
  'imported: three more receipt prices');

// --- Real invoices -----------------------------------------------------------------------------------------------------
let realOk = 0;
for (const [i, file] of realInvoices.entries()) {
  await pick(a, file);
  const ok = await until(a, `!!document.querySelector('[data-testid=receipt-totals-ok]')`);
  const shown = await a.evaluate(`document.querySelectorAll('[data-testid=receipt-line]').length`);
  const fees = await a.evaluate(`document.querySelectorAll('[data-testid=receipt-totals] span').length - (document.querySelector('[data-testid=receipt-totals]').textContent.includes('refunded') ? 1 : 0)`);
  const before = Number(server(`select count(*) from ReceiptLine`));
  await click(a, '[data-testid=receipt-import]');
  const imported = await until(a, `!!document.querySelector('[data-testid=receipt-done]')`);
  const after = Number(await serverHas(`select count(*) from ReceiptLine`, v => Number(v) >= before + shown + fees));
  if (check(ok && imported && after === before + shown + fees, `real invoice ${i + 1}: adds up, ${shown} lines and ${fees} fee(s) imported`)) realOk++;
  if (i === 0) {
    await settled(b, 30_000);
    await pick(b, file);
    check(await until(b, `!!document.querySelector('[data-testid=receipt-earlier]')`), 'real invoice 1 again, on B: refused');
    await click(b, '[data-testid=receipt-cancel]');
  }
}
if (realInvoices.length > 0) console.log(`  ${realOk} of ${realInvoices.length} real invoices imported`);

// --- The rows, in all three ---------------------------------------------------------------------------------------------
await settled(a, 30_000); await settled(b, 30_000); await sleep(500);
const q = {
  receipts: `select lower(Id) || '|' || OrderNumber || '|' || PurchasedOn || '|' || TotalCents || '|' || ImportedOn from Receipt order by Id`,
  lines: `select lower(Id) || '|' || Position || '|' || coalesce(lower(ProductId), '') || '|' || Kind || '|' || AmountCents from ReceiptLine order by Id`,
  aliases: `select lower(Id) || '|' || lower(ProductId) from ProductAlias order by Id`,
  prices: `select lower(Id) || '|' || PriceCents from PriceObservation where Source = 'Receipt' order by Id`,
  moves: `select lower(Id) || '|' || cast(Quantity as real) || '|' || coalesce(lower(CoversItemId), '') from StockMovement order by Id`,
};
const held = Object.fromEntries(Object.entries(q).map(([k, sql]) => [k, server(sql)]));
console.log(`  server: ${Object.entries(held).map(([k, v]) => `${k} ${v ? v.split('\n').length : 0}`).join(', ')}`);
for (const p of [a, b]) {
  const local = await dump(p);
  const same = Object.entries(q).every(([k, sql]) => local(sql).replaceAll('.0|', '|').replace(/\.0$/gm, '') === held[k].replaceAll('.0|', '|').replace(/\.0$/gm, ''));
  check(same && local('select count(*) from Outbox') === '0', `${p.name} SQLite holds the same receipts, lines, aliases, prices and stock as the server, outbox empty`);
}
await b.go('receipts');
console.log(`  phone screenshot (list): ${await screenshot(b, 'receipts')}`);
finish();
