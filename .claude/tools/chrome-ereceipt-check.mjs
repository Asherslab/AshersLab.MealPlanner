// A Woolworths in-store eReceipt through the running app, from two browser profiles: A desktop-sized, B phone-sized.
//
// 1. B picks a synthetic eReceipt (.claude/tools/lib/synthetic-ereceipt.mjs, written to the scratch directory - every name
//    and number in it invented): read as a receipt from Woolworths, its weighed, counted and offer lines placed, adding up to
//    the total and its items sold. B imports it as read (nothing matched); the server holds the receipt under its till
//    number and its lines, with the offer taken off the item before it.
// 2. A picks the same file: refused, naming the earlier import.
// 3. Each real eReceipt given on the command line is read and imported by A. Only counts are printed - never a name or
//    number from a real receipt.
// Then the receipt rows are read back from both profiles' SQLite and the server's.
//
//   node .claude/tools/chrome-ereceipt-check.mjs <scratchDir> [<eReceipt.pdf> ...]
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19366/19367).
import { join } from 'node:path';
import { mkdirSync } from 'node:fs';
import { RUN, launch, check, finish, settled, server, dump, click, phone, screenshot, sleep, until } from './lib/profiles.mjs';
import { eReceiptPdf } from './lib/synthetic-ereceipt.mjs';

const [scratch, ...real] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-ereceipt-check.mjs <scratchDir> [<eReceipt.pdf> ...]'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 5 minutes'); process.exit(3); }, 300_000).unref();
mkdirSync(scratch, { recursive: true });

const a = await launch(scratch, 'ercptA', 19366, 'receipts');
const b = await launch(scratch, 'ercptB', 19367, 'receipts');
await a.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
await phone(b);
console.log(`run ${RUN}: both profiles online, A desktop-sized, B phone-sized`);
async function serverHas(sql, ok = v => v !== '', ms = 20_000) {
  for (const end = Date.now() + ms; Date.now() < end; await sleep(250)) { const v = server(sql); if (ok(v)) return v; }
  return server(sql);
}
const text = (p, testid) => p.evaluate(`document.querySelector('[data-testid=${testid}]')?.textContent.trim().replace(/\\s+/g, ' ')`);
async function pick(p, file) {
  await p.evaluate(`document.querySelector('[data-testid=receipt-cancel]')?.click()`);
  await until(p, `!!document.querySelector('[data-testid=receipt-file]')`, 10_000);
  const doc = await p.send('DOM.getDocument');
  const input = await p.send('DOM.querySelector', { nodeId: doc.result.root.nodeId, selector: '[data-testid=receipt-file]' });
  await p.send('DOM.setFileInputFiles', { nodeId: input.result.nodeId, files: [file] });
  return until(p, `!!document.querySelector('[data-testid=receipt-summary], [data-testid=receipt-error]')`, 30_000);
}

// --- B: the synthetic eReceipt --------------------------------------------------------------------------------------------
const now = new Date();
const date = `${String(now.getDate()).padStart(2, '0')}/${String(now.getMonth() + 1).padStart(2, '0')}/${now.getFullYear()}`;
const iso = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
const trans = String(Date.now()).slice(-5);
const file = eReceiptPdf(join(scratch, `${RUN}-ereceipt.pdf`), {
  store: '1234', pos: '004', trans, date, time: '10:41', count: 6, subtotal: '$25.30', total: '$25.30', items: [
    { name: `Synthetic banana ${RUN}`, kilos: '1.250', perKg: '$4.00', amount: '5.00' },
    { name: `Synthetic cola ${RUN} 4Pk`, qty: 2, each: '$9.00', amount: '18.00', marker: '#' },
    { offer: 'COLA MULTIBUY OFFER', amount: '-4.00' },
    { name: `Synthetic milk ${RUN} 2L`, amount: '3.40' },
    { name: `Synthetic butter ${RUN} 470g`, amount: '2.65', marker: '^' },
    { name: 'Woolworths Paper Bag', amount: '0.25', marker: '#' },
  ] });
const number = `${iso} 10:41 1234 004 ${trans}`;

await pick(b, file);
check(await until(b, `!!document.querySelector('[data-testid=receipt-totals-ok]')`),
  `B reads it: "${await text(b, 'receipt-totals')}" - "${await text(b, 'receipt-totals-ok')}"`);
check(await b.evaluate(`document.querySelector('[data-testid=receipt-summary]').dataset.order`) === number,
  `its number is the till's stamp: "${number}"`);
check(/Woolworths/.test(await b.evaluate(`document.querySelector('[data-testid=receipt-summary] h2, [data-testid=receipt-summary] .pad-head')?.textContent ?? ''`))
  && /^Receipt /.test(await b.evaluate(`document.querySelector('[data-testid=receipt-summary] .pad-head p')?.textContent.trim() ?? ''`)),
  'worded as a Woolworths receipt, not an order');
check(await b.evaluate(`document.querySelectorAll('[data-testid=receipt-line]').length`) === 5, 'five lines: the offer is not a line of its own');
console.log(`  phone screenshot (review): ${await screenshot(b, 'ereceipt-review')}`);
await click(b, '[data-testid=receipt-import]');
check(await until(b, `!!document.querySelector('[data-testid=receipt-done]')`), `imported: "${await text(b, 'receipt-done')}"`);
const id = await serverHas(`select lower(Id) from Receipt where OrderNumber = '${number}'`);
check(!!id && server(`select PurchasedOn || '|' || TotalCents from Receipt where Id = '${id.toUpperCase()}'`) === `${iso}|2530`,
  `the server holds the receipt for ${iso}, $25.30`);
const rows = await serverHas(`select Position || '|' || Quantity || '|' || coalesce(UnitPriceCents, '') || '|' || AmountCents || '|' || DiscountCents || '|' || coalesce(WeightGrams, '')
  from ReceiptLine where ReceiptId = '${id?.toUpperCase()}' order by Position`, v => v.split('\n').length === 5);
check(rows.split('\n').map(r => r.replace(/\.0+(?=\||$)/g, '')).join(' ; ') === '0|1||500|0|1250 ; 1|2|900|1400|400| ; 2|1|340|340|0| ; 3|1|265|265|0| ; 4|1|25|25|0|',
  `its lines: the bananas weighed, two colas less the offer, milk, butter, the bag (${rows.split('\n').join(' ; ')})`);

// --- A: the same file, refused --------------------------------------------------------------------------------------------
await settled(a, 30_000);
await pick(a, file);
check(await until(a, `!!document.querySelector('[data-testid=receipt-earlier]')`) && await a.evaluate(`document.querySelector('[data-testid=receipt-import]').disabled`),
  `the same file on A: "${await text(a, 'receipt-earlier')}"`);

// --- Real eReceipts -------------------------------------------------------------------------------------------------------
for (const [i, path] of real.entries()) {
  await pick(a, path);
  const ok = await until(a, `!!document.querySelector('[data-testid=receipt-totals-ok]')`, 5_000);
  const shown = await a.evaluate(`document.querySelectorAll('[data-testid=receipt-line]').length`);
  const sold = /(\d+) items sold/.exec(await text(a, 'receipt-totals') ?? '')?.[1];
  const before = Number(server(`select count(*) from ReceiptLine`));
  let imported = false;
  if (ok && !await a.evaluate(`document.querySelector('[data-testid=receipt-import]').disabled`)) {
    await click(a, '[data-testid=receipt-import]');
    imported = await until(a, `!!document.querySelector('[data-testid=receipt-done]')`);
  }
  const already = await a.evaluate(`!!document.querySelector('[data-testid=receipt-earlier]')`);
  const after = Number(await serverHas(`select count(*) from ReceiptLine`, v => Number(v) >= before + shown, imported ? 20_000 : 0));
  check(ok && (already || (imported && after === before + shown)),
    `real eReceipt ${i + 1}: adds up to its ${sold} items sold, ${shown} lines${already ? ' (imported already)' : ' imported'}`);
}

// --- The rows, in all three -----------------------------------------------------------------------------------------------
await settled(a, 30_000); await settled(b, 30_000); await sleep(500);
const q = {
  receipts: `select lower(Id) || '|' || OrderNumber || '|' || TotalCents from Receipt order by Id`,
  lines: `select lower(Id) || '|' || Position || '|' || AmountCents || '|' || DiscountCents from ReceiptLine order by Id`,
};
const held = Object.fromEntries(Object.entries(q).map(([k, sql]) => [k, server(sql)]));
for (const p of [a, b]) {
  const local = await dump(p);
  check(Object.entries(q).every(([k, sql]) => local(sql) === held[k]) && local('select count(*) from Outbox') === '0',
    `${p.name} SQLite holds the same receipts and lines as the server, outbox empty`);
}
finish();
