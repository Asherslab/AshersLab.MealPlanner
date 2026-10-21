// Meal groups, meals and their ingredients through the running app, from two browser profiles: every edit made on
// profile A is seen on profile B, and the rows are read back from both profiles' SQLite and the server's. Saves
// phone-sized screenshots under <scratchDir>.
//
//   node .claude/tools/chrome-meals-check.mjs <scratchDir>
//
// Needs the app running (AppHost: https) and sqlite3 on the PATH. Fresh profiles each run (ports 19347/19348); names
// carry the run id. APP_URL / SERVER_DB as in chrome-sync-check.mjs.
import { RUN, launch, check, finish, settled, server, dump, nameList, phone, screenshot, sleep } from './lib/profiles.mjs';

const [scratch] = process.argv.slice(2);
if (!scratch) { console.error('usage: chrome-meals-check.mjs <scratchDir>'); process.exit(2); }
setTimeout(() => { console.error('FAIL gave up after 5 minutes'); process.exit(3); }, 300_000).unref();

const a = await launch(scratch, 'mealA', 19347, 'meal-groups');
const b = await launch(scratch, 'mealB', 19348, 'meal-groups');
console.log(`run ${RUN}: both profiles online`);
const up = id => id.toUpperCase();

// --- Meal groups --------------------------------------------------------------------------------------------------
{
  const ga = nameList(a, 'meal-group'), gb = nameList(b, 'meal-group');
  await gb.has('Snack', 30_000);
  const seeded = await gb.names();
  check(['Beef', 'Chicken & egg', 'Fish', 'Other', 'Individual', 'Side', 'Snack'].every((n, i) => seeded[i] === n),
    `a new profile has the seven seeded groups in the spreadsheet's order (${seeded.slice(0, 7).join(', ')})`);

  const group = `Pasta ${RUN}`;
  check(await ga.add(group), 'A adds a group');
  check(await gb.has(group), 'B shows it');
  check((await gb.names()).at(-1) === group, 'it is last on B');
  await ga.move(group, 'up');
  check(await (async () => { for (let i = 0; i < 50; i++) { const n = await gb.names(); if (n.at(-2) === group) return true; await sleep(200); } return false; })(),
    'A moves it up one; B shows it second to last');
  check(await ga.rename(group, `${group} night`), 'A renames it');
  check(await gb.has(`${group} night`), 'B shows the new name');
  const id = await gb.id(`${group} night`);

  await settled(a); await settled(b);
  const expected = `${group} night|0`;
  check(server(`select Name || '|' || Deleted from MealGroup where Id = '${up(id)}'`) === expected, 'the server holds the renamed group');
  for (const p of [a, b]) {
    const q = await dump(p);
    check(q(`select Name || '|' || Deleted from MealGroup where Id = '${up(id)}'`) === expected
      && q(`select count(*) from MealGroup where not Deleted`) === server(`select count(*) from MealGroup where not Deleted`)
      && q('select count(*) from Outbox') === '0', `${p.name} SQLite holds it and as many groups as the server, outbox empty`);
  }

  await phone(b); await sleep(500);
  console.log(`  phone screenshot: ${await screenshot(b, 'meal-groups')}`);
  await phone(b, false);

  check(await gb.remove(`${group} night`) === 'deleted', 'B deletes it after the confirmation');
  check(await ga.gone(`${group} night`), 'A drops it');
}

finish();
