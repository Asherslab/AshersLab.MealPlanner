// The catalogue pages (stores, categories, ingredients) through the running app, from two browser profiles: every
// edit made on profile A is seen on profile B, and the rows are read back from both profiles' SQLite and the
// server's. Also saves phone-sized screenshots of each page under <scratchDir>.
//
//   node .claude/tools/chrome-catalogue-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19343/19344); names
// carry the run id, so runs do not disturb each other. APP_URL / SERVER_DB as in chrome-sync-check.mjs.
import { RUN, launch, check, finish, settled, server, dump, nameList, phone, screenshot, sleep } from './lib/profiles.mjs';

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

finish();
