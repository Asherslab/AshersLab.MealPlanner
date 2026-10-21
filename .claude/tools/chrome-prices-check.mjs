// The Prices page (products, manual prices, an ingredient's unit price) through the running app, from two browser
// profiles: every edit made on profile A is seen on profile B, and the rows are read back from both profiles' SQLite
// and the server's. Saves phone-sized screenshots under <scratchDir>.
//
//   node .claude/tools/chrome-prices-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19345/19346); names
// carry the run id. APP_URL / SERVER_DB as in chrome-sync-check.mjs.
import { RUN, launch, check, finish, settled, server, dump, nameList, ingredients, click, clickDialogButton, typeInto,
  phone, screenshot, sleep, until } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-prices-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 5 minutes'); process.exit(3); }, 300_000).unref();

const a = await launch(scratch, 'priceA', 19345, 'ingredients');
const b = await launch(scratch, 'priceB', 19346, 'prices');
console.log(`run ${RUN}: both profiles online`);
const up = id => id.toUpperCase();

/** The Prices page: products by their name; the editor; the price sheet. */
function prices(p) {
  const productRow = name => `[...document.querySelectorAll('[data-testid=price-product-row]')]
    .find(r => r.querySelector('[data-testid=price-product-name]')?.textContent.trim() === ${JSON.stringify(name)})`;
  const ingredientBlock = name => `[...document.querySelectorAll('[data-testid=price-ingredient]')]
    .find(r => r.querySelector('[data-testid=price-ingredient-name]')?.textContent.trim() === ${JSON.stringify(name)})`;
  const pick = (testid, text) => p.evaluate(`(() => { const s = document.querySelector('[data-testid=${testid}]');
    const o = [...s.options].find(o => o.textContent.trim() === ${JSON.stringify(text)});
    if (!o) return false; s.value = o.value; s.dispatchEvent(new Event('change', { bubbles: true })); return true; })()`);
  const editorOpen = () => until(p, `!!document.getElementById('product-edit-name')`, 10_000);
  const editorClosed = () => until(p, `!document.getElementById('product-edit-name')`, 10_000);
  async function fill(o) {
    if (o.ingredient !== undefined) await pick('product-ingredient', o.ingredient);
    if (o.store !== undefined) await pick('product-store', o.store);
    if (o.name !== undefined) await p.evaluate(typeInto('product-edit-name', o.name));
    if (o.pack !== undefined) await p.evaluate(typeInto('product-edit-pack', o.pack));
    if (o.code !== undefined) await p.evaluate(typeInto('product-edit-code', o.code));
    if (o.price !== undefined) await p.evaluate(typeInto('product-edit-price', o.price));
    await sleep(150);
  }
  return {
    has: (name, ms) => until(p, `!!${productRow(name)}`, ms),
    gone: (name, ms) => until(p, `!${productRow(name)}`, ms),
    id: name => p.evaluate(`${productRow(name)}?.dataset.id`),
    detail: name => p.evaluate(`${productRow(name)}?.querySelector('[data-testid=price-product-detail]')?.textContent.trim()`),
    ingredientText: name => p.evaluate(`${ingredientBlock(name)}?.textContent.replace(/\\s+/g, ' ').trim()`),
    unitPrice: name => p.evaluate(`${ingredientBlock(name)}?.querySelector('[data-testid=price-ingredient-unit-price]')?.textContent.trim()`),
    /** Taps + on the ingredient and fills the editor. */
    async addUnder(ingredient, o) {
      if (!await until(p, `!!${ingredientBlock(ingredient)}`, 30_000)) return false;
      await p.evaluate(`${ingredientBlock(ingredient)}.querySelector('[data-testid=price-ingredient-add]').click()`);
      if (!await editorOpen()) return false;
      await fill(o);
      await click(p, '[data-testid=product-save]');
      return (await editorClosed()) && this.has(o.name);
    },
    tap: name => p.evaluate(`${productRow(name)}?.click()`),
    /** Tapping a product opens its price sheet; the editor is behind its "Edit product". */
    async openEditor(name) {
      await this.has(name, 30_000);
      await this.tap(name);
      if (!await until(p, `!!document.querySelector('[data-testid=price-sheet-edit-product]')`, 10_000)) return false;
      await click(p, '[data-testid=price-sheet-edit-product]');
      return editorOpen();
    },
    editorOpen, editorClosed, fill,
    save: () => click(p, '[data-testid=product-save]'),
  };
}

const ingredient = `mince ${RUN}`, product = `Beef mince 8x250g ${RUN}`;

// --- Products ---------------------------------------------------------------------------------------------------
{
  check(await ingredients(a).add(ingredient, { unit: 'Gram', store: 'Costco' }), 'A adds an ingredient counted in grams, usual store Costco');
  await a.go('prices');
  const pa = prices(a), pb = prices(b);

  check(await pa.addUnder(ingredient, { name: product, pack: '8x250g', code: '12345' }), 'A adds a product under it, pack typed as 8x250g');
  check(await pa.detail(product) === 'Costco · 2 kg · #12345', `A shows it as "Costco · 2 kg · #12345" (${await pa.detail(product)})`);
  const id = await pa.id(product);
  check(await pb.has(product), 'B shows the product');
  check(await pb.detail(product) === 'Costco · 2 kg · #12345', 'B shows the same detail');
  check(server(`select Name || '|' || StoreId || '|' || StoreCode || '|' || cast(PackQuantity as real) || '|' || (IngredientId is not null) from Product where Id = '${up(id)}'`)
    === `${product}|00000000-5EED-7000-8000-000000000102|12345|2000.0|1`, 'the server holds the product: Costco, 2000 g, the code, an ingredient');

  // A pack typed in the wrong kind of unit is refused, never converted.
  await pb.openEditor(product);
  await pb.fill({ pack: '1.5L' });
  await pb.save(); await sleep(300);
  check(await b.evaluate(`!!document.getElementById('product-edit-pack') && document.body.textContent.includes('It is counted in g')`),
    'B: a pack of "1.5L" for an ingredient in grams is refused, saying why');
  await pb.fill({ pack: '1.5kg' });
  await pb.save();
  await pb.editorClosed();
  check(await until(a, `[...document.querySelectorAll('[data-testid=price-product-detail]')].some(e => e.textContent.trim() === 'Costco · 1.5 kg · #12345')`),
    'B changes the pack to 1.5kg and A shows it');

  // Things that use it cannot be deleted.
  await a.go('ingredients');
  await ingredients(a).has(ingredient, 30_000);
  await ingredients(a).open(ingredient);
  await click(a, '[data-testid=ingredient-delete]');
  check(await until(a, `document.body.textContent.includes('is used by 1 product: ${product}')`, 5000), 'A cannot delete the ingredient: the product uses it');
  await clickDialogButton(a, 'OK'); await ingredients(a).cancel();
  await a.go('stores');
  check(await nameList(a, 'store').remove('Costco') === 'in-use'
    // Earlier runs' products pile up and only five are named, so check the count is there, not this run's name.
    && await a.evaluate(`/\\d+ products?: /.test(document.querySelector('[data-testid=store-in-use]').textContent)`),
    'A cannot delete Costco: it sells products (counted and named)');

  await settled(a); await settled(b);
  for (const p of [a, b]) {
    const q = await dump(p);
    check(q(`select cast(PackQuantity as real) || '|' || StoreCode || '|' || Deleted from Product where Id = '${up(id)}'`) === '1500.0|12345|0'
      && q('select count(*) from Outbox') === '0', `${p.name} SQLite holds the product at 1500 g, outbox empty`);
  }
  console.log(`  server Op for it:\n${server(`select Seq, Kind, substr(DeviceId, 1, 8), FieldsJson from Op where EntityId = '${up(id)}' order by Seq`)}`);

  await a.go('prices');
  await phone(b); await sleep(500);
  console.log(`  phone screenshot: ${await screenshot(b, 'prices')}`);
  await pb.openEditor(product); await sleep(400);
  console.log(`  phone screenshot: ${await screenshot(b, 'product-editor')}`);
  await click(b, '[data-testid=product-cancel]'); await pb.editorClosed();
  await phone(b, false);

  globalThis.productId = id;
}

// --- Prices -----------------------------------------------------------------------------------------------------
{
  const pa = prices(a), pb = prices(b);
  const id = globalThis.productId;
  const price = name => a.evaluate(`[...document.querySelectorAll('[data-testid=price-product-row]')]
    .find(r => r.querySelector('[data-testid=price-product-name]')?.textContent.trim() === ${JSON.stringify(name)})
    ?.querySelector('[data-testid=price-product-price]')?.textContent.trim()`);
  const priceOn = (p, name) => p.evaluate(`[...document.querySelectorAll('[data-testid=price-product-row]')]
    .find(r => r.querySelector('[data-testid=price-product-name]')?.textContent.trim() === ${JSON.stringify(name)})
    ?.querySelector('[data-testid=price-product-price]')?.textContent.trim()`);
  const showsPrice = (p, name, text) => until(p, `[...document.querySelectorAll('[data-testid=price-product-row]')]
    .find(r => r.querySelector('[data-testid=price-product-name]')?.textContent.trim() === ${JSON.stringify(name)})
    ?.querySelector('[data-testid=price-product-price]')?.textContent.trim() === ${JSON.stringify(text)}`);
  const sheetOpen = p => until(p, `!!document.getElementById('price-sheet-price')`, 10_000);
  const sheetClosed = p => until(p, `!document.getElementById('price-sheet-price')`, 10_000);
  /** Taps the product, types the price (and optionally the day and per-kg), and presses Enter (submits the form). */
  async function enter(p, name, text, o = {}) {
    await prices(p).has(name, 30_000);
    await prices(p).tap(name);
    if (!await sheetOpen(p)) return false;
    // Focused on open: the planner types straight away.
    const focused = await until(p, `document.activeElement?.id === 'price-sheet-price'`, 3000);
    if (!focused) console.log(`  focus is on: ${await p.evaluate(`(e => e?.tagName + '#' + e?.id + '.' + e?.className + ' hasFocus=' + document.hasFocus())(document.activeElement)`)}`);
    await p.evaluate(typeInto('price-sheet-price', text));
    if (o.perKg) await p.evaluate(typeInto('price-sheet-per-kg', o.perKg));
    if (o.date) await p.evaluate(`(() => { const i = document.querySelector('[data-testid=price-sheet-date]');
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(i, ${JSON.stringify(o.date)});
      i.dispatchEvent(new Event('change', { bubbles: true })); })()`);
    await sleep(150);
    await p.evaluate(`document.getElementById('price-sheet-form').requestSubmit()`);
    return { focused, closed: await sheetClosed(p) };
  }

  const r = await enter(a, product, '24.99');
  check(r.focused && r.closed, 'A taps the product: the price box is focused, typing 24.99 and Enter saves');
  check(await showsPrice(a, product, '$24.99'), 'A shows $24.99 on the row');
  check(await showsPrice(b, product, '$24.99'), 'B shows $24.99');

  check((await enter(b, product, '22.50', { date: '2026-10-01' })).closed, 'B back-fills $22.50 seen on 1 Oct');
  await sleep(1500);
  check(await priceOn(a, product) === '$24.99' && await priceOn(b, product) === '$24.99',
    'the row still shows $24.99 on both: an older day does not replace a newer price');

  check((await enter(a, product, '18.73', { perKg: '14.99' })).closed, 'A enters $18.73 at $14.99/kg (variable weight)');
  check(await showsPrice(b, product, '$18.73'), 'B shows $18.73');

  // A typo is refused, saying why, and nothing is written.
  await pb.tap(product); await sheetOpen(b);
  await b.evaluate(typeInto('price-sheet-price', '1,50')); await sleep(150);
  await b.evaluate(`document.getElementById('price-sheet-form').requestSubmit()`); await sleep(400);
  check(await b.evaluate(`!!document.getElementById('price-sheet-price') && document.body.textContent.includes('Type a price like 24.99')`),
    'B: "1,50" is refused with "Type a price like 24.99."');
  const history = await b.evaluate(`[...document.querySelectorAll('[data-testid=price-history-price]')].map(e => e.textContent.trim())`);
  check(JSON.stringify(history) === JSON.stringify(['$18.73', '$24.99', '$22.50']), `B's history, newest day first: ${history.join(', ')}`);
  await phone(b); await sleep(500);
  console.log(`  phone screenshot: ${await screenshot(b, 'price-sheet')}`);
  await phone(b, false);
  await b.evaluate(`document.querySelector('[data-testid=price-history-delete]').click()`);
  await until(b, `document.querySelectorAll('.mud-dialog').length > 1`, 5000);
  await clickDialogButton(b, 'Delete'); await sheetClosed(b);
  check(await showsPrice(a, product, '$24.99'), 'B deletes $18.73 from the history; A falls back to $24.99');

  // A new product with its first price in the same dialog.
  const small = `Mince 500g ${RUN}`;
  check(await pa.addUnder(ingredient, { name: small, store: 'Woolworths', pack: '500g', price: '7.50' }), 'A adds a Woolworths product with its first price');
  check(await showsPrice(b, small, '$7.50'), 'B shows it at $7.50');

  await settled(a); await settled(b);
  const rows = `select PriceCents || '|' || ObservedOn || '|' || Source || '|' || ifnull(PerKgCents, '-') || '|' || Deleted from PriceObservation
    where ProductId = '${up(id)}' order by ObservedOn, PriceCents`;
  const onServer = server(rows);
  console.log(`  server PriceObservation for it:\n${onServer}`);
  const today = new Date().toLocaleDateString('en-CA');
  check(onServer === [`2250|2026-10-01|Manual|-|0`, `1873|${today}|Manual|1499|1`, `2499|${today}|Manual|-|0`].join('\n'),
    'the server holds all three, the deleted one as a tombstone');
  for (const p of [a, b]) {
    const q = await dump(p);
    check(q(rows) === onServer && q('select count(*) from Outbox') === '0', `${p.name} SQLite holds the same prices, outbox empty`);
  }
}

finish();
