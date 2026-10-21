// Costco receipt import (OCR in the browser) through the running app, from two profiles: A desktop-sized, B phone-sized.
//
// 1. A synthetic receipt dated today (.claude/tools/lib/synthetic-costco.mjs: an image-only PDF, one bitmap across two
//    pages, every name and number invented; item numbers new each run): A adds mince and apples (Costco), plans a meal of
//    mince tomorrow and ticks it bought. B picks the receipt: OCR on the phone, read whole - item numbers, the discount off
//    the loaves, the GST flag, the total and items sold - each line beside its strip of the picture. B makes products of the
//    mince (replacing A's tick) and apples, leaves the bread unmatched and makes the nappies household. The server holds the
//    receipt, its lines with their item numbers, products carrying them, prices, stock and no aliases; A's pantry counts the
//    mince once.
// 2. B picks the same file: refused.
// 3. A picks a second synthetic receipt whose apples amount and item number are "misread" (8.39 printed for 8.99, a code
//    no product has): flagged, not importable, the apples matching nothing; A corrects both on the line, it adds up, and
//    the line is matched afresh by its item number rather than keeping the misread's "not matched".
// 4. Each real Costco receipt given on the command line is read by A - it must add up and match its items sold with no
//    correction - and imported. Only counts and timings are printed, never what a receipt says.
// Then every receipt row is read back from both profiles' SQLite and the server's.
//
//   node .claude/tools/chrome-costco-check.mjs <scratchDir> [<receipt.pdf> ...]
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19366/19367). B runs
// with the CPU slowed 4x (DevTools throttling) to stand in for a phone.
import { join } from 'node:path';
import { mkdirSync } from 'node:fs';
import { RUN, launch, check, finish, settled, server, dump, click, typeInto, phone, screenshot, sleep, until, ingredients } from './lib/profiles.mjs';
import { costcoPdf } from './lib/synthetic-costco.mjs';

const [scratch, ...realReceipts] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-costco-check.mjs <scratchDir> [<receipt.pdf> ...]'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 10 minutes'); process.exit(3); }, 600_000).unref();
mkdirSync(scratch, { recursive: true });

const COSTCO = '00000000-5eed-7000-8000-000000000102';
const a = await launch(scratch, 'costA', 19366, 'ingredients');
const b = await launch(scratch, 'costB', 19367, 'receipts');
await a.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
await phone(b);
await b.send('Emulation.setCPUThrottlingRate', { rate: 4 });
console.log(`run ${RUN}: both profiles online, A desktop-sized, B phone-sized with its CPU slowed 4x`);
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
const text = (p, testid) => p.evaluate(`document.querySelector('[data-testid=${testid}]')?.textContent.trim().replace(/\\s+/g, ' ')`);

// --- A: mince and apples, a meal of mince tomorrow, the mince ticked bought ------------------------------------------------
const mince = `Mince ${RUN}`, apples = `Apples ${RUN}`;
const ing = ingredients(a);
check(await ing.add(mince, { category: 'Meat', store: 'Costco' }) && await ing.add(apples, { category: 'Fruit', store: 'Costco' }),
  'A adds mince and apples (grams, Costco)');
const ids = { mince: await serverHas(`select lower(Id) from Ingredient where Name = '${mince}'`), apples: await serverHas(`select lower(Id) from Ingredient where Name = '${apples}'`) };

await a.go('meals');
await until(a, `!!document.getElementById('meal-new-name')`, 30_000);
await a.evaluate(typeInto('meal-new-name', `Bolognese ${RUN}`)); await sleep(100);
await click(a, '[data-testid=meal-add]');
await until(a, `!!document.getElementById('meal-edit-name')`);
await a.evaluate(`(() => { const e = document.querySelector('[data-testid=meal-kind-Cooked]'); (e.querySelector('input') ?? e).click(); })()`);
await sleep(150); await a.evaluate(typeInto('meal-edit-serves', '4')); await sleep(150);
await click(a, '[data-testid=meal-save]');
await until(a, `document.querySelector('[data-testid=meal-title]')?.textContent.trim() === 'Bolognese ${RUN}'`);
await click(a, '[data-testid=meal-ingredient-add]');
await until(a, `!!document.querySelector('[data-testid=meal-ingredient-sheet]')`);
await a.evaluate(typeInto('meal-ingredient-search', mince)); await sleep(300);
await a.evaluate(`[...document.querySelectorAll('[data-testid=meal-ingredient-choice]')].find(e => e.textContent.trim() === ${JSON.stringify(mince)}).click()`);
await until(a, `!!document.getElementById('meal-ingredient-quantity')`);
await a.evaluate(typeInto('meal-ingredient-quantity', '1000g')); await sleep(150);
await a.evaluate(`document.getElementById('meal-ingredient-form').requestSubmit()`);
await until(a, `!!document.querySelector('[data-testid=meal-ingredient-row]')`);
await settled(a);
const bolognese = await serverHas(`select lower(Id) from Meal where Name = 'Bolognese ${RUN}'`);

await a.go(`calendar?week=${mondayOf(day(1))}`);
await until(a, `!!document.querySelector('[data-testid=cal-grid]')`, 30_000);
await a.evaluate(`document.querySelector('[data-testid=cal-grid] [data-testid=cal-cell][data-date="${day(1)}"][data-slot="${DINNER}"] [data-testid=cal-cell-add]').click()`);
await until(a, `!!document.getElementById('cal-search')`);
await a.evaluate(typeInto('cal-search', `Bolognese ${RUN}`)); await sleep(250);
await click(a, `[data-testid=cal-meal-option][data-id="${bolognese}"]`); await sleep(250);
await click(a, '[data-testid=cal-save]');
check(!!await serverHas(`select Id from CalendarEntry where MealId = '${up(bolognese)}' and Date = '${day(1)}'`), `A plans the bolognese (1 kg of mince) for ${day(1)}`);

const line = id => `document.querySelector('[data-testid=shop-line][data-id="${id}"]')`;
await a.go(`shopping?from=${day(1)}&to=${day(1)}`);
await until(a, `!!${line(ids.mince)}`, 30_000);
await a.evaluate(`${line(ids.mince)}.querySelector('[data-testid=shop-line-tap]').click()`);
const tick = await serverHas(`select lower(ShoppingItemId) from StockMovement where IngredientId = '${up(ids.mince)}' and ShoppingItemId is not null and Deleted = 0`);
check(!!tick, 'A ticks the mince bought on the list: a Bought movement naming the tick');

// --- B: the synthetic receipt, read by OCR on the phone ---------------------------------------------------------------------
const base = 100000 + (Date.now() % 800000);
const code = n => String(base + n * 10);
const txn = `100 ${String(Date.now() % 1000).padStart(3, '0')} ${String(Math.floor(Date.now() / 1000) % 10000).padStart(4, '0')} 003`;
const now = new Date();
const stampOf = (d, t) => `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()} 0${t}:15:00 PM ${txn}`;
const items = misread => [
  { name: 'GRASSFED BEEF MINCE', detail: '6X500G MP6 P200 SL300', code: code(1), qty: 1, unit: '21.49', amount: '21.49' },
  // Misread: its amount, and its item number - a code no product has, until the planner corrects it.
  { name: 'PINK LADY APPLES 2KG', detail: 'SL12', code: misread ? String(base + 27) : code(2), qty: 1, unit: '8.99', amount: misread ? '8.39' : '8.99' },
  { name: 'SOURDOUGH LOAF 800G', detail: 'SL4', code: code(3), qty: 1, unit: '6.99', amount: '6.99' },
  { name: 'SOURDOUGH LOAF 800G', detail: 'SL4', code: code(3), qty: 1, unit: '6.99', amount: '6.99' },
  { name: 'TPD -BUY 2 SAVE', code: code(4), qty: 1, unit: '3.00', amount: '3.00', discount: true },
  { name: 'NAPPIES SIZE 3 120CT', detail: 'P40', code: code(5), qty: 1, unit: '49.99', amount: '49.99', gst: true },
];
const first = await costcoPdf(b, join(scratch, `${RUN}-costco-1.pdf`), { items: items(false), total: '91.45', sold: 5, stamp: stampOf(now, 1) });
const second = await costcoPdf(b, join(scratch, `${RUN}-costco-2.pdf`), { items: items(true), total: '91.45', sold: 5, stamp: stampOf(now, 2) });

async function pick(p, file) {
  await p.go('receipts');
  await until(p, `!!document.querySelector('[data-testid=receipt-file]')`, 30_000);
  const doc = await p.send('DOM.getDocument', {});
  const input = await p.send('DOM.querySelector', { nodeId: doc.result.root.nodeId, selector: '[data-testid=receipt-file]' });
  const t0 = Date.now();
  await p.send('DOM.setFileInputFiles', { nodeId: input.result.nodeId, files: [file] });
  const progress = await until(p, `!!document.querySelector('[data-testid=receipt-ocr-progress], [data-testid=receipt-summary], [data-testid=receipt-error]')`, 30_000)
    && await p.evaluate(`!!document.querySelector('[data-testid=receipt-ocr-progress]')`);
  const ok = await until(p, `!!document.querySelector('[data-testid=receipt-summary], [data-testid=receipt-error]')`, 180_000);
  return { ok, ms: Date.now() - t0, progress };
}
const rline = n => `document.querySelector('[data-testid=receipt-line][data-position="${n}"]')`;
const choose = (p, n, value) => p.evaluate(`(() => { const s = ${rline(n)}.querySelector('[data-testid=receipt-line-choice]');
  s.value = ${JSON.stringify(value)}; s.dispatchEvent(new Event('change', { bubbles: true })); return s.value === ${JSON.stringify(value)}; })()`);
const lines = p => p.evaluate(`[...document.querySelectorAll('[data-testid=receipt-line]')].map(l => ({ match: l.dataset.match, code: l.dataset.code,
  check: l.hasAttribute('data-check') && l.dataset.check !== 'False', amount: l.querySelector('[data-testid=receipt-line-amount]').textContent.trim().replace(/\\s+/g, ' '),
  will: l.querySelector('[data-testid=receipt-line-will]').textContent.trim() }))`);

const read1 = await pick(b, first);
check(read1.ok && await until(b, `!!document.querySelector('[data-testid=receipt-totals-ok]')`),
  `B picks the synthetic receipt: read by OCR on the phone in ${read1.ms} ms${read1.progress ? ', its progress shown' : ''}, and it adds up - "${await text(b, 'receipt-totals-ok')}"`);
check(await b.evaluate(`document.querySelector('[data-testid=receipt-summary]').dataset.store`) === COSTCO, 'the store is Costco, by its seeded id');
let got = await lines(b);
check(got.map(l => l.code).join(',') === [code(1), code(2), code(3), code(5)].join(','), `four lines, every item number read exactly (${got.length} lines)`);
check(got[2]?.amount.endsWith('2 × $6.99 = $10.98 · $3.00 off') && got[3]?.amount.endsWith('· GST') && got.every(l => l.match === 'None' && !l.check),
  `the loaves scanned twice are one line less the discount ("${got[2]?.amount}"), the nappies carry GST, nothing matched yet, nothing to check`);
check(await until(b, `document.querySelectorAll('[data-testid=receipt-line-picture] canvas[data-drawn]').length === 6`),
  'each line shows the strip of the receipt it was read from (6 strips: the loaves have both scans and the discount)');
console.log(`  phone screenshot: ${await screenshot(b, 'costco-review')}`);

await choose(b, 0, `n:${ids.mince}`);
check(await until(b, `/Replaces the shopping list's tick/.test(${rline(0)}.querySelector('[data-testid=receipt-line-cover]')?.textContent ?? '')`),
  'mince → a new product of the mince, and it offers to replace A\'s tick');
await choose(b, 1, `n:${ids.apples}`);
await choose(b, 3, 'h');
await sleep(400);
got = await lines(b);
console.log(got.map(l => `    ${l.match} | ${l.will}`).join('\n'));
check(got[0].will.includes(`3 kg of ${mince}`) && got[1].will.includes(`2 kg of ${apples}`) && got[2].will.startsWith('Not matched') && got[3].will.includes('Not food'),
  'what each line will do: 3 kg of mince (6X500G), 2 kg of apples, the bread unmatched, the nappies not food');
await click(b, '[data-testid=receipt-import]');
check(await until(b, `!!document.querySelector('[data-testid=receipt-done]')`), `B imports it: "${await text(b, 'receipt-done')}"`);

const receipt = await serverHas(`select lower(Id) from Receipt where OrderNumber like '%${txn}'`);
check(!!receipt && server(`select StoreId from Receipt where Id = '${up(receipt)}'`) === up(COSTCO), 'the server holds the receipt, under Costco, its number the date, time and transaction');
const held = await serverHas(`select group_concat(StoreCode || '/' || Kind || '/' || AmountCents || '/' || DiscountCents, ',') from (select * from ReceiptLine where ReceiptId = '${up(receipt)}' order by Position)`, v => v.split(',').length === 4);
check(held === `${code(1)}/Food/2149/0,${code(2)}/Food/899/0,${code(3)}/Food/1098/300,${code(5)}/Household/4999/0`, `its four lines, with their item numbers: ${held}`);
check(server(`select group_concat(StoreCode, ',') from (select StoreCode from Product where StoreCode in ('${code(1)}', '${code(2)}', '${code(5)}') order by StoreCode)`) === [code(1), code(2), code(5)].join(','),
  'the three new products carry their item numbers');
check(server(`select group_concat(PriceCents, ',') from (select PriceCents from PriceObservation where Source = 'Receipt' and ProductId in (select ProductId from ReceiptLine where ReceiptId = '${up(receipt)}') order by PriceCents)`) === '899,2149,4999',
  'three receipt prices: apples $8.99, mince $21.49, nappies $49.99');
check(server(`select cast(Quantity as real) || '|' || coalesce(lower(CoversItemId), '') from StockMovement where IngredientId = '${up(ids.mince)}' and ShoppingItemId is null`) === `3000.0|${tick}`,
  'the mince: 3 kg into the pantry, replacing A\'s tick');
check(server(`select count(*) from ProductAlias where ProductId in (select ProductId from ReceiptLine where ReceiptId = '${up(receipt)}')`) === '0',
  'no aliases: the item numbers match next time');

await a.go('pantry');
const pantryRow = `[...document.querySelectorAll('[data-testid=pantry-row]')].find(r => r.querySelector('[data-testid=pantry-name]')?.textContent.trim() === ${JSON.stringify(mince)})`;
check(await until(a, `${pantryRow}?.querySelector('[data-testid=pantry-level]')?.textContent.trim() === 'about 3 kg'`, 30_000),
  `A's pantry counts the mince once: "${await a.evaluate(`${pantryRow}?.querySelector('[data-testid=pantry-level]')?.textContent.trim()`)}"`);

// --- The same receipt again: refused ------------------------------------------------------------------------------------
await pick(b, first);
check(await until(b, `!!document.querySelector('[data-testid=receipt-earlier]')`) && await b.evaluate(`document.querySelector('[data-testid=receipt-import]').disabled`),
  `B picks it again: refused - "${await text(b, 'receipt-earlier')}"`);
await click(b, '[data-testid=receipt-cancel]');

// --- A second receipt with a misread amount: flagged, corrected, matched by item number ---------------------------------
await settled(a, 30_000);
const read2 = await pick(a, second);
check(read2.ok && await until(a, `!!document.querySelector('[data-testid=receipt-totals-problem]')`) && await a.evaluate(`document.querySelector('[data-testid=receipt-import]').disabled`),
  `A picks a receipt whose apples read $8.39: flagged, not importable - "${await text(a, 'receipt-totals-problem')}"`);
got = await lines(a);
check(got[1]?.check && !got[0].check && await a.evaluate(`!!${rline(1)}.querySelector('[data-testid=receipt-line-problem]')`), 'the apples line is marked to check, the others not');
check(got[0].match === 'StoreCode' && got[1].match !== 'StoreCode' && got[3].match === 'StoreCode',
  `the misread item number matches nothing, the others by item number: ${got.map(l => l.match).join(', ')}`);
await a.evaluate(`${rline(1)}.querySelector('[data-testid=receipt-line-edit]').click()`);
await until(a, `!!document.getElementById('receipt-edit-amount')`);
await a.evaluate(typeInto('receipt-edit-code', code(2))); await sleep(200);
await a.evaluate(typeInto('receipt-edit-amount', '8.99')); await sleep(200);
await click(a, '[data-testid=receipt-line-save]');
check(await until(a, `!!document.querySelector('[data-testid=receipt-totals-ok]')`) && await until(a, `!document.querySelector('[data-testid=receipt-import]').disabled`),
  'A corrects the item number and the amount to $8.99: it adds up, and can be imported');
// The choice made for the misread ("not matched") is not kept for what is now another item (ReviewChoice.Survives).
got = await lines(a);
check(got[1]?.match === 'StoreCode' && /apples/i.test(got[1].will), `the corrected line is matched afresh, by its item number: "${got[1]?.will}"`);
await click(a, '[data-testid=receipt-import]');
check(await until(a, `!!document.querySelector('[data-testid=receipt-done]')`), `A imports it: "${await text(a, 'receipt-done')}"`);
await settled(a, 30_000);
check(await serverHas(`select count(*) from PriceObservation where Source = 'Receipt' and ProductId in (select Id from Product where StoreCode in ('${code(1)}', '${code(2)}', '${code(5)}'))`, v => v === '6', 30_000) === '6',
  'three more receipt prices, the apples at $8.99');

// --- Real receipts: read with no correction, imported -------------------------------------------------------------------
let realOk = 0;
for (const [i, file] of realReceipts.entries()) {
  const read = await pick(a, file);
  const ok = read.ok && await until(a, `!!document.querySelector('[data-testid=receipt-totals-ok]')`, 5_000);
  const shown = (await lines(a)).length;
  const checks = (await lines(a)).filter(l => l.check).length;
  const earlier = await a.evaluate(`!!document.querySelector('[data-testid=receipt-earlier]')`);
  let imported = earlier;
  if (!earlier) {
    await click(a, '[data-testid=receipt-import]');
    imported = await until(a, `!!document.querySelector('[data-testid=receipt-done]')`);
  } else await click(a, '[data-testid=receipt-cancel]');
  if (check(ok && checks === 0 && imported, `real receipt ${i + 1}: read in ${read.ms} ms, adds up and matches its items sold with no correction, ${shown} lines${earlier ? ' (imported before)' : ', imported'}`)) realOk++;
}
if (realReceipts.length > 0) console.log(`  ${realOk} of ${realReceipts.length} real receipts read with no correction`);

// --- The rows, in all three ---------------------------------------------------------------------------------------------
await settled(a, 30_000); await settled(b, 30_000); await sleep(500);
const q = {
  receipts: `select lower(Id) || '|' || OrderNumber || '|' || PurchasedOn || '|' || TotalCents from Receipt order by Id`,
  lines: `select lower(Id) || '|' || coalesce(StoreCode, '') || '|' || coalesce(lower(ProductId), '') || '|' || Kind || '|' || AmountCents from ReceiptLine order by Id`,
  products: `select lower(Id) || '|' || coalesce(StoreCode, '') from Product order by Id`,
  prices: `select lower(Id) || '|' || PriceCents from PriceObservation where Source = 'Receipt' order by Id`,
  moves: `select lower(Id) || '|' || cast(Quantity as real) || '|' || coalesce(lower(CoversItemId), '') from StockMovement order by Id`,
};
const rows = Object.fromEntries(Object.entries(q).map(([k, sql]) => [k, server(sql)]));
console.log(`  server: ${Object.entries(rows).map(([k, v]) => `${k} ${v ? v.split('\n').length : 0}`).join(', ')}`);
for (const p of [a, b]) {
  // settled() says only that a profile's own outbox is empty, not that it has pulled the other's last import - and B runs
  // with its CPU slowed 4x. So read it again until it agrees, for up to 30 s; a profile still behind after that is a bug.
  let same = false, outbox = '', waited = 0;
  for (const start = Date.now(); !same && Date.now() - start < 30_000; await sleep(1000)) {
    const local = await dump(p);
    same = Object.entries(q).every(([k, sql]) => local(sql).replaceAll('.0|', '|').replace(/\.0$/gm, '') === rows[k].replaceAll('.0|', '|').replace(/\.0$/gm, ''));
    outbox = local('select count(*) from Outbox');
    waited = Date.now() - start;
  }
  check(same && outbox === '0', `${p.name} SQLite holds the same receipts, lines, products, prices and stock as the server, outbox empty (read after ${waited} ms)`);
}
finish();
