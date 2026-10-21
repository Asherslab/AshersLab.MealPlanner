// The catalogue pages (stores, categories, ingredients) through the running app, from two browser profiles: every
// edit made on profile A is seen on profile B, and the rows are read back from both profiles' SQLite and the
// server's. Also saves phone-sized screenshots of each page under <scratchDir>.
//
//   node .claude/tools/chrome-catalogue-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19343/19344); names
// carry the run id, so runs do not disturb each other. APP_URL / SERVER_DB as in chrome-sync-check.mjs.
import { RUN, launch, check, finish, settled, server, dump, nameList, phone, screenshot, sleep, until } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-catalogue-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 5 minutes'); process.exit(3); }, 300_000).unref();

const a = await launch(scratch, 'catA', 19343, 'stores');
const b = await launch(scratch, 'catB', 19344, 'stores');
console.log(`run ${RUN}: both profiles online`);
const up = id => id.toUpperCase();

// --- Stores ---------------------------------------------------------------------------------------------------
{
  const sa = nameList(a, 'store'), sb = nameList(b, 'store');
  check(await sb.has('Woolworths') && await sb.has('Costco'), 'a new profile has the seeded stores');

  const added = `Butcher ${RUN}`, renamed = `The butcher ${RUN}`;
  await sa.add(`  Butcher   ${RUN} `);
  check(await sa.has(added), 'A adds a store (typed with stray spaces) and shows it normalised');
  const id = await sa.id(added);
  check(await sb.has(added), 'B shows the new store');
  check(await sa.add(`costco`) === false
    && await a.evaluate(`document.body.textContent.includes('There is already a store called costco')`),
    'A refuses a duplicate name ("costco") and says why');
  await a.evaluate(`document.getElementById('store-new-name') && (() => { const i = document.getElementById('store-new-name');
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(i, ''); i.dispatchEvent(new Event('input', { bubbles: true })); })()`);

  check(await sa.rename(added, renamed), 'A renames it');
  check(await sb.has(renamed), 'B shows the rename');
  check(server(`select Name from Store where Id = '${up(id)}'`) === renamed, 'the server holds the rename');

  await settled(a); await settled(b);
  for (const p of [a, b]) {
    const q = await dump(p);
    check(q(`select Name || '|' || Deleted from Store where Id = '${up(id)}'`) === `${renamed}|0`
      && q('select count(*) from Outbox') === '0', `${p.name} SQLite holds the renamed store, outbox empty`);
  }

  check(await sb.remove(renamed) === 'deleted', 'B deletes it after confirming');
  check(await sa.gone(renamed), 'A no longer shows it');
  check(server(`select Deleted from Store where Id = '${up(id)}'`) === '1', 'the server keeps it as a tombstone');
  await settled(a);
  const q = await dump(a);
  check(q(`select Deleted from Store where Id = '${up(id)}'`) === '1', `${a.name} SQLite has the tombstone`);
  console.log(`  server Op for it:\n${server(`select Seq, Kind, substr(DeviceId, 1, 8), FieldsJson from Op where EntityId = '${up(id)}' order by Seq`)}`);

  await phone(a); await sleep(500);
  console.log(`  phone screenshot: ${await screenshot(a, 'stores')}`);
  await phone(a, false);
}

// --- Categories -----------------------------------------------------------------------------------------------
{
  await a.go('categories'); await b.go('categories');
  const ca = nameList(a, 'category'), cb = nameList(b, 'category');
  const seeded = ['Meat', 'Dairy', 'Fruit', 'Vegetables', 'Carbs/sides', 'Pantry', 'Fridge', 'Freezer', 'Household'];
  await cb.has('Household');
  const startB = await cb.names();
  check(seeded.every(s => startB.includes(s)), `B has every seeded category (${startB.join(', ')})`);

  const added = `Bakery ${RUN}`;
  check(await ca.add(added), 'A adds a category');
  check(await cb.has(added), 'B shows it');
  let names = await ca.names();
  check(names.at(-1) === added, 'it goes at the end');

  // Up twice: it passes the two categories above it.
  const above = names.slice(-3, -1);
  await ca.move(added, 'up'); await sleep(400); await ca.move(added, 'up');
  const expectA = [...names.slice(0, -3), added, ...above];
  check(await until(a, `JSON.stringify([...document.querySelectorAll('[data-testid=category-name]')].map(e => e.textContent.trim())) === ${JSON.stringify(JSON.stringify(expectA))}`),
    `A moved it up two places (above ${above.join(', ')})`);
  check(await until(b, `JSON.stringify([...document.querySelectorAll('[data-testid=category-name]')].map(e => e.textContent.trim())) === ${JSON.stringify(JSON.stringify(expectA))}`),
    'B shows the same order');

  // Concurrent moves of different categories offline on both: each keeps its move.
  await a.offline(true); await b.offline(true);
  const first = expectA[0], second = expectA[1];
  await ca.move(second, 'up');                         // A: the second to the top
  await sleep(300);
  const last = expectA.at(-1);
  await cb.move(last, 'up');                           // B: the last up one
  await a.offline(false); await b.offline(false);
  await settled(a); await settled(b); await sleep(1500);
  const finalA = await ca.names(), finalB = await cb.names();
  check(finalA[0] === second && finalA[1] === first && finalA.at(-2) === last, `both offline moves survive on A (${finalA.join(', ')})`);
  check(JSON.stringify(finalA) === JSON.stringify(finalB), 'B ends on the same order');

  const id = await ca.id(added);
  check(await ca.rename(added, `Bread ${RUN}`), 'A renames it');
  check(await cb.has(`Bread ${RUN}`), 'B shows the rename');

  await settled(a); await settled(b);
  // As numbers: the server's SQLite provider stores decimal 1 as '1.0', the browser's as '1'.
  const serverOrder = server(`select Name || '=' || cast(Position as real) from Category where Deleted = 0`);
  console.log(`  server Category:\n${serverOrder}`);
  for (const p of [a, b]) {
    const q = await dump(p);
    const local = q(`select Name || '=' || cast(Position as real) from Category where Deleted = 0`);
    check(local.split('\n').sort().join() === serverOrder.split('\n').sort().join() && q('select count(*) from Outbox') === '0',
      `${p.name} SQLite holds the same names and positions as the server, outbox empty`);
  }

  check(await cb.remove(`Bread ${RUN}`) === 'deleted', 'B deletes it');
  check(await ca.gone(`Bread ${RUN}`), 'A no longer shows it');
  check(server(`select Deleted from Category where Id = '${up(id)}'`) === '1', 'the server keeps the tombstone');

  await phone(a); await sleep(500);
  console.log(`  phone screenshot: ${await screenshot(a, 'categories')}`);
  await phone(a, false);
}

finish();
