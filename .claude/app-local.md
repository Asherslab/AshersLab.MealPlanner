# AshersLab.MealPlanner — local run specifics

Resolves the `{{PLACEHOLDER}}` values in the global `run-and-inspect-app` skill
(`~/.claude/skills/run-and-inspect-app/SKILL.md`). Read the skill for the procedure; read this for the
values. Where the two disagree, **this file wins** — it is written against the running app.

**This stack is smaller than the one the skill describes.** One Aspire resource, no YARP, no WASM dev
server, no containers, no Postgres. Several sections of the skill therefore do not apply — see "What
differs from the skill" below before following it.

| Placeholder | Value |
|---|---|
| `{{REPO_ROOT}}` | `/Users/asherp/Personal-Projects/Git/AshersLab.MealPlanner` |
| `{{SOLUTION_PREFIX}}` | `AshersLab.MealPlanner` |
| `{{APPHOST_PROJECT}}` | `AshersLab.MealPlanner.AppHost` |
| `{{API_PROJECT}}` | `AshersLab.MealPlanner.Grpc` — also serves the WASM app |
| `{{WASM_PROJECT}}` | `AshersLab.MealPlanner.WASM` — never run on its own |
| `{{YARP_PROJECT}}` | none — there is no YARP (see `docs/archive/2026-10-deployment.md`) |
| `{{TESTS_PROJECT}}` | `AshersLab.MealPlanner.Tests` |
| `{{RUN_CONFIG}}` | `AshersLab.MealPlanner.AppHost: https` |
| `{{APP_PORT}}` | `7283` (https) / `5280` (http) — the gRPC host, and the only port to open in a browser |
| `{{DASHBOARD_MCP_PORT}}` | `16056` — but see below, the dashboard MCP is not wired up |
| `{{ASPIRE_MCP_NAME}}` | none yet — no `.mcp.json` entry exists for this stack |
| `{{POSTGRES_PORT}}` | none — SQLite, see Database |
| `{{DB_NAME}}` | none — the database is one file, see Database |
| `{{AUTHED_PAGE}}` | none — the AppHost runs in Development, where no auth is configured and every page is open; the image refuses to start that way (see the standalone sim below) |

The run configurations are **shared files** in `.run/` (committed), so Rider lists them as soon as the
solution is open: `AshersLab.MealPlanner.AppHost: https` and `: http`. Rider also offers per-project
launch-profile configurations (`AshersLab.MealPlanner.Grpc: https`); do not use those — they start the
host without the AppHost's `DataDirectory`, so it writes its database under `AshersLab.MealPlanner.Grpc/app-data/`.

## Running the tests

The `AshersLab.MealPlanner.Tests` run configuration (Rider's project configuration — xunit v3 test
projects are executables) runs the whole suite and prints a summary. Run it with
`execute_run_configuration` and `waitForExit: true`. Rider's test-runner gutter points are not exposed
through the MCP for this project, so this is the way. It takes no arguments (no `-method` filter through the MCP).

**`build_solution` can build from Rider's stale view of a file edited outside it** - "success", and the old code.
Seen repeatedly on 2026-10-21 while mutation-testing: an edit made with the Edit tool, then a build seconds later
that compiled nothing. Call `get_file_problems` on each edited file first (it makes Rider re-read it), then build,
and when it matters check that `AshersLab.MealPlanner.Tests/bin/Debug/net10.0/<Project>.dll` is newer than the
source (`[ dll -nt source ]`). A test run on a stale build reports the old code's result.

**The WASM bundle can keep a referenced project's old code.** Seen 2026-10-21: after a `Shared.Domain` change, an
incremental build updated `Shared.Domain/bin` and the tests, but not the WASM's
`wwwroot/_framework/AshersLab.MealPlanner.Shared.Domain.*.wasm` (webcil) - the browser ran the old code for half an
hour of checks. After changing a project the WASM references, compare that file's time with the source's, and when it
is older, stop the app and `build_solution` with `rebuild: true`.

## Ports

Both GSBC stacks run at the same time as this one. Every fixed port here differs from theirs —
ImpactKids holds 7263, 5250, 17259/15014, 21191, 22297, 16036, 19276, 20239 and 60535–60537;
GSBC.Accounting holds 7273/5242, 5260, 7068/5279, 17269/15024, 21201, 22307, 16046, 19286, 20249,
60546–60547. All of the below were verified free and then listening on 2026-10-20.

| Port | What | Pinned in |
|---|---|---|
| 7283 / 5280 | gRPC host — **the app's address**, API and WASM on one origin | `AshersLab.MealPlanner.Grpc/Properties/launchSettings.json` |
| 17279 / 15034 | Aspire dashboard | AppHost `launchSettings.json`, `.run/*.run.xml` |
| 21211 / 19296 | Aspire OTLP (https / http profile) | same |
| 22317 / 20259 | Aspire resource service (https / http profile) | same |
| 16056 | Aspire dashboard MCP | same |

As with the GSBC stacks, 7283 is Aspire's DCP **proxy**; the host process itself binds a random port.
Opening that random port works for the page but nothing else should be pointed at it.

The AppHost sets both endpoints' `TargetHost` to `0.0.0.0`, so the proxy listens on every interface (DCP binds
each address separately - loopback, the LAN address, a VPN's) and a phone on the same network can open
`https://<the Mac's LAN address>:7283`. Use **https**: plain http on a LAN address is not a secure context, so
OPFS and Web Locks are missing and the browser database cannot open. The dev certificate is for `localhost`,
so the phone warns once and has to be told to continue. Development has no sign-in: anyone on the network
can use the app while it runs.

## What differs from the skill

- **No YARP, no separate WASM resource.** The gRPC host serves `_framework/`, `_content/` and
  `index.html` itself. The skill's "WASM changes need a full restart" still holds — the host snapshots
  the WASM's static-web-assets manifest at startup.
- **Restarting.** Kill by built binary path, AppHost first, then wait for the host to go:

  ```bash
  ps ax -o pid=,command= | awk '$2 ~ /AshersLab.MealPlanner\.[A-Za-z.]*\/bin\/Debug/ {print $1, $2}'
  kill <apphost pid>    # the Grpc host follows within a few seconds; kill it too if it does not
  ```

  Never `pkill -f AshersLab.MealPlanner` — agent sessions carry the name in their command lines.
- **No service worker yet**, so the skill's stale-service-worker section does not apply until the PWA
  slice adds one.
- **The dashboard MCP is not configured.** There is no `AppHost:McpApiKey` in this AppHost's user
  secrets and no `.mcp.json`. Until someone adds both (per the skill), read the dashboard at
  `https://localhost:17279` — the login URL with its token is printed in the run configuration's
  output — or read the host's own answers with `curl`.
- **A `_framework/` file answering 500** means `app.UseRouting()` has been moved or removed from
  `AshersLab.MealPlanner.Grpc/Program.cs`; see the comment there. The page shows "Loading…" forever.
  `curl -sk -o /dev/null -w '%{http_code}\n' https://localhost:7283/_framework/blazor.webassembly.js`
  should be 200. The test host cannot catch this.

## Driving the app

Chrome (`mcp__claude-in-chrome__*`) works against `https://localhost:7283` with no sign-in. The home
page is where the planner lands: `home-today` (today's entries `home-today-entry`, or `home-today-empty`), `home-no-people`
while nobody is on People, and the buttons `home-calendar`, `home-shopping`, `home-meals`, `home-receipts`. The wiring
check is on **Diagnostics** (`/settings/diagnostics`): its Server card calls `IServerInfoService.Get` over gRPC-Web and
shows the server version, server time and the newest applied migration. The refresh icon (aria-label "Check the
server again") repeats the call — the server time changing is the evidence that it went to the server.

**Navigation** (all from `Layout/NavModel.cs`): on a phone-sized viewport the bottom bar `bottom-nav` with
`bottom-nav-today|plan|shop|meals|more` (an `<a>`; the current one has class `active` and `aria-current=page`); `/more`
rows `more-pantry|receipts|prices|ingredients|plans|settings`; `/settings` rows `settings-people|slots|meal-groups|
categories|stores|import|diagnostics`; on any page under More or Settings the app bar's back arrow `app-back`. No
drawer on a phone. Desktop-wide: no bottom bar; the drawer's `nav-<key>` testids sit on a `div` around the link -
`.click()` the `a` inside it, not the div.

**Driving it as the phone does**, one step at a time rather than a whole check script: the 2026-10-22 acceptance walk
(`docs/work/2026-10-v1-acceptance.md`) used a small throwaway HTTP driver over two `launch()`ed profiles - touch events at
an element's centre (`Input.dispatchTouchEvent` with `Emulation.setTouchEmulationEnabled`), text by `Input.insertText`,
a screenshot after each step - which finds what a script's `.click()` hides (a drawer left open over the page). Native
`<select>`s still have to be set by value and `change`; the OS picker cannot be driven. A meal's ingredient sheet offers
`meal-ingredient-new` and the calendar entry sheet `cal-new-meal` for a name nothing has yet.

In the network panel the call is `POST https://localhost:7283/gRPC/AshersLab.MealPlanner.ServerInfo/Get`.

**Ingredients** (`/ingredients`, `nav-ingredients`): the "New ingredient, or search" box (`InputId`
`ingredient-new-name` - it also filters the list) and `ingredient-add`, which opens the editor; per row
`ingredient-row` (`data-id`; tapping it opens the editor), `ingredient-name`, `ingredient-detail` ("counted in each ·
Costco"), `ingredient-staple-chip`; group headings `ingredient-group`. The editor (a MudDialog): `ingredient-edit-name`
(input id), `ingredient-unit-Gram|Millilitre|Each` (buttons), `ingredient-category` / `ingredient-store` (native
`<select>`s - set `.value` and dispatch `change`), `ingredient-staple` (the checkbox input itself), `ingredient-save`,
`ingredient-cancel`, `ingredient-delete` (then a message box over the editor: click "Delete" in the *last*
`.mud-dialog`). `ingredients(b)` in `.claude/tools/lib/profiles.mjs` drives all of it. The sync chip at the right of the app bar is `data-testid` `sync-status`, with
`data-state` (`connecting|online|offline|failed|refused|waitingforchoice`) and `data-pending` (outbox count, including ops accepted
but not yet pulled back). Sync calls are
`/gRPC/AshersLab.MealPlanner.Sync/Push|Pull|Changes`; `Changes` stays open.

**Stores** (`/stores`; on a phone More → Settings → Stores, `settings-stores`) is a `NameList`:
`store-new-name` (input id too) and `store-add`; per row `store-row` (`data-id`), `store-name`, `store-rename` →
`store-rename-input` / `store-rename-save`, `store-delete` (opens a MudBlazor message box - not a browser dialog -
with a "Delete" button; or, when something uses it, shows `store-in-use` instead). **Categories** (`/categories`,
`nav-categories`) is the same component with the prefix `category`, plus `category-up` / `category-down`.

**Prices** (`/prices`, `nav-prices`): `price-search` (input id too), `price-new-product`, store chips
`price-store-chip` (`data-id`, empty for "All stores"); per ingredient `price-ingredient` (`data-id`) with
`price-ingredient-name`, `price-ingredient-unit-price` ("$16.66/kg at Costco", "unpriced") and
`price-ingredient-add` (+); per product `price-product-row` (`data-id`; tapping it opens
the editor), `price-product-name`, `price-product-detail` ("Costco · 2 kg · #12345"). The product editor:
`product-ingredient` / `product-store` (native selects), `product-edit-name`, `product-edit-pack`, `product-edit-code`
(input ids), `product-edit-price` (new products only), `product-save`, `product-cancel`, `product-delete`. Tapping a
product opens the price sheet: `price-sheet-price` / `price-sheet-per-kg` (input ids), `price-sheet-date` (a native
date input - set `.value` and dispatch `change`), the form `price-sheet-form` (`requestSubmit()` is Enter),
`price-sheet-save`, `price-sheet-edit-product` (to the editor), history rows `price-history-row` with
`price-history-price` and `price-history-delete`. A row's latest price is `price-product-price`. `node .claude/tools/chrome-prices-check.mjs <scratch>/prices`
(ports 19345/19346) drives all of it from two profiles.

**Meal groups** (`/meal-groups`, `nav-meal-groups`) is a `NameList` with the prefix `meal-group`, reorderable like
Categories. **Meals** (`/meals`, `nav-meals`): `meal-new-name` (input id too) and `meal-add`; group headings
`meal-list-group`; per meal `meal-row` (an `<a>` to `/meals/{id}`, `data-id`), `meal-name`, `meal-detail`. A meal's
page: `meal-title`, `meal-summary` ("Beef · makes 4 serves"), `meal-notes`, `meal-edit`, `meal-back`, or `meal-gone`.
The editor: `meal-edit-name`, `meal-edit-serves`, `meal-edit-fixed-cost`, `meal-edit-notes` (a textarea - use the
`HTMLTextAreaElement` value setter) as input ids, `meal-group` (native select), `meal-kind-Cooked|Simple|Leftovers|NoCost|FixedCost`
(click the `input` inside), `meal-save`, `meal-cancel`, `meal-delete`. On the page, the cost box `meal-cost` holds
`meal-cost-batch` and `meal-cost-serve` (or `meal-cost-note`); `meal-ingredient-add`; per line `meal-ingredient-row`
(`data-id`; tapping it opens the sheet), `meal-ingredient-name`, `meal-ingredient-detail`, `meal-ingredient-cost`. The
sheet (`meal-ingredient-sheet`): `meal-ingredient-search` (input id), choices `meal-ingredient-choice`, then
`meal-ingredient-quantity` / `meal-ingredient-note` (input ids) in the form `meal-ingredient-form` (`requestSubmit()` is
Enter), `meal-ingredient-save`, `meal-ingredient-cancel`, `meal-ingredient-remove`. **Wait for a saved row before
navigating**: `go()` is a full reload, and a write still in flight when the dialog closes is lost with the page.
`node .claude/tools/chrome-meals-check.mjs <scratch>/meals`
(ports 19347/19348) drives all of it from two profiles. `node .claude/tools/chrome-edit-roundtrip-check.mjs <scratch>/rt`
(port 19351, after a prices check has left a product in grams) saves a line and a pack of 1234.5 g, reopens each and
saves untouched: no op, still 1234.5, on the server and in the profile's SQLite. **Wait on the server's rows, not the
chip**, after a save: the chip reads "online, 0 waiting" before a write made a moment ago is even queued.

**Import** (`/import`, `nav-import`): the file box `import-file` (an `<input type=file>` - set it with the DevTools
protocol's `DOM.setFileInputFiles`; the Chrome extension's `file_upload` only takes files shared with the session, and
the real sheet is in `private/`), then `import-summary` (or `import-error`), `import-run`, `import-done` /
`import-nothing`; questions `import-ingredient-question` / `import-serves-question` / `import-group-question` /
`import-category-question` (`data-key`), `import-household-store` and `import-price-store-all` (labels around a native
select), per price `import-price` (`data-id`) with `import-price-store`, `import-price-pack`, `import-price-note`,
`import-price-state` ("Will be imported", "Already here", "Left out: ..."); guessed units a price contradicts
`import-unit-question` (`data-key`, `data-says` = the unit the price says) with `import-unit-Gram|Millilitre|Each`; new
ingredients `import-new-ingredient` with the same buttons; `import-left-out`. On Ingredients, a guessed unit is
`ingredient-unit-guessed` (chip) and `ingredient-unit-guessed-note` (in the editor).
`node .claude/tools/chrome-import-check.mjs <scratch>/import <sheet.xlsx> [<later-sheet.xlsx>]` - the planner's exports
are kept out of git (see AGENTS.md); pass their absolute paths -
(ports 19352/19353, about 3 minutes with a later sheet) drives it from two profiles and prints counts only; with a later
download of the sheet it imports that on top and checks nothing already there changed. Start from a fresh server
database (stop the app, delete `mealplanner.db*`, start it) - against one already holding the import it can only say
"nothing new".

The import's review also asks about plan names (`import-plan-meal-question`, `data-key`, a native select whose empty value
is "a new single-serve meal"), and the done message links to the plan (`import-plan-link`). The check reports the plan's
weeks, entries and choices from the server, and with a later sheet includes the plan's rows in "nothing changed".

**People** (`/people`, `nav-people`): `person-new-name` (input id too) and `person-add`; per row `person-row` (`data-id`;
its `[role=button]` opens the inline editor) and `person-name`; the editor: `person-edit-name` (input id),
`person-colour` (`data-colour`), `person-save`, `person-cancel`, `person-delete`, `person-in-use`. **Slots**
(`/slots`, `nav-slots`) is a `NameList` with the prefix `slot`. **Plans** (`/plans`, `nav-plans`): `plan-new-name`,
`plan-new-weeks` (input ids), `plan-add`; per plan `plan-row` (an `<a>`, `data-id`), `plan-name`, `plan-detail`. A
plan's page: `plan-title`, `plan-summary`, `plan-edit`, `plan-week` (`data-week`); on a desktop-wide viewport
`plan-grid`, on a phone `plan-day` (`data-day`) and `plan-day-view`; either way `plan-cell` (`data-day`, `data-slot`)
holding `plan-entry` (`data-id`) with `plan-entry-meals` / `plan-entry-people`, and `plan-cell-add`. The entry sheet:
`entry-where`, `entry-search` (input id), `entry-meal-option` (`data-id`), `entry-choice` with `entry-choice-first` /
`entry-choice-remove`, `entry-everyone`, `entry-person` (`data-id`), `entry-save`, `entry-cancel`, `entry-remove`,
`entry-error`. The plan editor: `plan-edit-name`, `plan-edit-weeks`, `plan-edit-hidden`, `plan-save`, `plan-delete`.
**Headless Chrome's default window is 800 px - the phone layout**; set a desktop size with
`Emulation.setDeviceMetricsOverride` to see the grid. `node .claude/tools/chrome-plans-check.mjs <scratch>/plans`
(ports 19354/19355, about a minute) drives all of it, A desktop-sized and B phone-sized.

**Calendar** (`/calendar?week=yyyy-mm-dd`, `nav-calendar`): `cal-week` (the title), `cal-prev`, `cal-next`, `cal-this-week`,
`cal-plan-weeks`, `cal-apply` (opens the fill dialog: native selects `apply-plan` (value = plan id), `apply-week`,
`apply-count`, then `apply-run`), `cal-applied` (the result). Desktop-wide: `cal-grid`; phone: `cal-day` (`data-date`) and
`cal-day-view`. Either way `cal-cell` (`data-date`, `data-slot`) holding `cal-entry` (`data-id`, `data-skipped`) with
`cal-entry-meal` / `cal-entry-cost`, and `cal-cell-add`; `cal-replan` with `cal-replan-entry`. The sheet: `cal-sheet-where`,
`cal-sheet-source`, `cal-sheet-skipped`, `cal-choice` (`data-id` = meal), `cal-sheet-meal`, `cal-search` (input id),
`cal-meal-option`, `cal-everyone`, `cal-person`, `cal-move-date` (native date - set `.value`, dispatch `change`),
`cal-move-slot`, `cal-remove`, `cal-skip` / `cal-unskip`, `cal-cancel`, `cal-save`, `cal-error`.
`node .claude/tools/chrome-calendar-check.mjs <scratch>/cal` (ports 19356/19357, about 2 minutes) drives all of it, A
desktop-sized and B phone-sized, including both filling the same week offline. Earlier runs' entries share the dates, so
it looks only at entries whose meal carries its run id.

**Serves** on the calendar: under an entry, `cal-entry-serves` (class `cal-serves-problem` when it has less than it needs);
below the week `cal-serves` holding `cal-serves-note` (`data-problem`). In the sheet, `cal-serves-made`
(input id; a MudNumericField with `Immediate`, so `typeInto` works) for a cooked meal, `cal-eats-from` (native select, value
= the cooked entry's id, empty for whatever is left) for a leftovers meal. `node .claude/tools/chrome-serves-check.mjs
<scratch>/srv` (ports 19358/19359, about 2 minutes) drives it from two profiles. **It picks the first week with no calendar
entries on the server**: leftovers eat any batch cooked before them, so an earlier run's stew in the same week is eaten
first and every count is off. And read ids back with its `serverHas` poll, not once after `settled` - the chip says
"0 waiting" before a write made a moment ago is queued.

**Pantry** (`/pantry`, `nav-pantry`): `pantry-search` (input id too); group headings `pantry-group`; per ingredient with stock
`pantry-row` (`data-id`; tapping it opens the sheet) with `pantry-name`, `pantry-detail` ("runs out Sun 25 Oct") and
`pantry-level` ("about 1 kg", `data-state` = `InStock|Out|ProbablyOut`); while searching, `pantry-untracked` (`data-id`) for
ingredients with no stock yet; staples `pantry-staple` (`data-id`) with `pantry-staple-name` and three chips
`pantry-staple-state` (`data-state` = `Have|RunningLow|Out`, `data-chosen`). The sheet (`pantry-sheet`): `pantry-sheet-now`,
`pantry-out`, `pantry-quarter`, `pantry-half`, `pantry-pack`, `pantry-bought-1`, `pantry-bought-2` (pack buttons only when a
product gives a pack), `pantry-amount` (input id) with `pantry-amount-count` / `-bought` / `-threw`, `pantry-sheet-use`,
`pantry-history-row` (`data-id`) with `pantry-history-delete`, `pantry-cancel`. `node .claude/tools/chrome-pantry-check.mjs
<scratch>/pantry` (ports 19360/19361, about a minute) drives all of it from two profiles. **Stock is "now"**: it plans its
meals on tomorrow and the day after next, and expects nothing else on the server to use its run-named mince. Compare
`StockMovement.Quantity` with `cast(Quantity as real)` - decimal as text is "1000.0" on the server and "1000" in a browser.

**Shopping** (`/shopping?from=yyyy-mm-dd&to=yyyy-mm-dd`, `nav-shopping`; without the query, the list covering today, else
this week Monday to Sunday):
`shop-range`, `shop-range-edit` (opens `shop-range-form` with native dates `shop-from` / `shop-to` and `shop-range-save`),
`shop-spend`, `shop-eaten`, `shop-progress`; store headings `shop-store` (`data-id`), categories `shop-category`; per line
`shop-line` (`data-id` = the ingredient, `data-tick` = `None|HaveIt|Bought`, `data-need` = `Buy|InPantry|Staple`) holding
`shop-line-tap` (the whole tap target - bought), `shop-line-name`, `shop-line-detail`, `shop-line-cost` and the button
`shop-have`; extras `shop-extra` (`data-id`, `data-tick`) with `shop-extra-tap`, `shop-extra-name`, `shop-extra-cost`,
`shop-extra-more` (a MudMenu: click its `button`, then `shop-extra-delete` in the popover); the add form `shop-extra-form` (`shop-extra-new-name` / `shop-extra-new-price` input ids,
`shop-extra-new-store` native select, `shop-extra-add`); "In the pantry" `shop-pantry-line` (`data-id`, `data-tick`) with
`shop-pantry-detail`; `shop-list-link`. Under the calendar's week: `cal-week-cost` with `cal-week-spend`, `cal-week-eaten`,
`cal-shopping`. `node .claude/tools/chrome-shopping-check.mjs <scratch>/shop` (ports 19362/19363, about two minutes)
drives it from two profiles, B phone-sized and offline for its ticks. **It needs a fresh server database** (stop the app,
delete `mealplanner.db*`, start it): it checks Spend and Eaten to the cent, and any other meal or running-low staple on
the same days adds to them.

**Receipts** (`/receipts`, `nav-receipts`): the file box `receipt-file` (an `<input type=file>` - set it with
`DOM.setFileInputFiles`), then `receipt-summary` (`data-order`, `data-date`) or `receipt-error`; `receipt-totals`,
`receipt-totals-ok` or `receipt-totals-problem`, `receipt-earlier` (already imported),
`receipt-show-file` and `receipt-file-view` (its pages as canvases); per line `receipt-line` (`data-position`,
`data-match` = `None|Alias|StoreCode`, `data-choice`, `data-kind`) with `receipt-line-desc`, `receipt-line-amount`,
`receipt-line-matched`, `receipt-line-suggest`, `receipt-line-choice` (native select: `""`, `p:<product>`,
`n:<ingredient>`, `h`, `x` - lower-case ids), `receipt-new-name-<position>` / `receipt-new-pack-<position>` (input ids),
`receipt-line-cover` (`data-on`), `receipt-line-will`; `receipt-import` (`disabled` until it can), `receipt-cancel`,
`receipt-done`; imported receipts `receipt-row` (`data-id`). `node .claude/tools/chrome-receipts-check.mjs <scratch>/rcpt
[<invoice.pdf> ...]` (ports 19364/19365, about two minutes with seven invoices) drives it from two profiles: a synthetic
invoice dated today (written to the scratch directory by `lib/synthetic-invoice.mjs`) for the alias, the refusal and the
tick a purchase replaces, then each real invoice passed - their paths are under `private/`, pass them, never copy them -
printing counts only. Names carry the run id, so it needs no fresh database; a real invoice already imported is refused,
so pass those only to a database that has not seen them. The fees are `receipt-fee` spans inside `receipt-totals`.

A **Costco receipt** (a picture, read by OCR) adds: `receipt-ocr-progress` while reading; `receipt-summary`'s `data-store`
(the seeded store id); `receipt-items-sold`; `receipt-total-edit` → `receipt-total-form` with `receipt-edit-total` /
`receipt-edit-sold` (input ids), `receipt-edit-date` (native date), `receipt-total-save`; per line `data-code` (item number),
`data-check` (present when it is marked), `receipt-line-picture` (its strips: canvases, `data-drawn` once drawn),
`receipt-line-problem`, `receipt-line-edit` → `receipt-line-form` with `receipt-edit-desc` / `-code` / `-qty` / `-price` /
`-amount` (input ids), `receipt-line-save`; `receipt-edit-problem`. `node .claude/tools/chrome-costco-check.mjs
<scratch>/costco [<receipt.pdf> ...]` (ports 19366/19367, about two minutes) drives it from two profiles, B phone-sized
with its CPU slowed 4×: a synthetic image-only receipt (`lib/synthetic-costco.mjs`, drawn by the headless page as a JPEG
and wrapped in a two-page PDF like the Costco app's), a second with a misread amount to correct, then each real Costco
receipt passed (`private/receipts/receipt_*.pdf`), which must add up with no correction; timings and counts only. Item
numbers are new each run, so it needs no fresh database. **Headless Chrome's OCR timing is not a phone's**: DevTools CPU
throttling is a stand-in; the real iPhone has not been timed.

**`build_solution` after a source edit sometimes needs running twice** before the test project's copies of `Shared.*.dll`
are newer than the source (seen repeatedly on 2026-10-22, also with `get_file_problems` first). Check `[ dll -nt source ]`
before trusting a test run, above all a mutation run - a stale one passes.

The Chrome extension cannot resize a full-screen window, so phone-width checks use headless Chrome's
`Emulation.setDeviceMetricsOverride` (`phone()` in `.claude/tools/lib/profiles.mjs`).

Typing into a MudTextField from a script needs the native `value` setter and an `input` event (see
`typeInto` in `.claude/tools/chrome-sync-check.mjs`); assigning `.value` alone is not seen by Blazor.

### Two profiles, offline, concurrent edits

```bash
node .claude/tools/chrome-sync-check.mjs <scratch>/sync     # about 40 s, prints PASS/FAIL per check
```

Two separate headless Chromes on fresh profiles under `<scratch>/sync` (ports 19341/19342 - if taken, a
previous run's Chrome is alive: `pkill -f "user-data-dir=<scratch>/sync"`). Offline is the DevTools protocol's
`Network.emulateNetworkConditions`, the same switch as DevTools' Offline. It checks create → other profile,
an offline rename syncing on reconnect, and two concurrent offline renames ending on the later one, then copies
each profile's SQLite out of OPFS (directly over the DevTools protocol - no receiver needed) and prints its rows
beside the server's. Ingredients are named with the run id, so runs pile up in the database; wipe it when that
matters. The Chrome extension cannot do this: it has no offline switch and drives only one profile.

### The catalogue pages, two profiles

```bash
node .claude/tools/chrome-catalogue-check.mjs <scratch>/cat     # about 30 s, PASS/FAIL per check
```

Ports 19343/19344. Every catalogue edit on profile A seen on B, then both profiles' SQLite and the server's read
back; saves phone-sized screenshots (`<run>-catA-<page>.png`) under `<scratch>/cat`. Its harness -
launching, `until`, `typeInto`, the OPFS dump, a `NameList` driver - is `.claude/tools/lib/profiles.mjs`; new checks
should import it rather than copy it.

### The server wiped, with both profiles open

```bash
node .claude/tools/chrome-server-reset-check.mjs <scratch>/reset     # run_in_background; ports 19349/19350
```

Profile A has two windows, B one. It prints `WIPE NOW` twice with a signal file: stop the app (the kill commands
above - in zsh, kill each pid on its own; `kill $(…)` with two pids on separate lines fails *after* you have deleted
the files under a running app), delete `mealplanner.db`, `-wal`, `-shm`, start the AppHost run configuration, wait
for `_framework/blazor.webassembly.js` to answer 200, `touch` the file. About three minutes. The dialog is
`server-replaced` (`server-replaced-send`, `-clear`, then `-clear-confirm` or `-back`; `-count`, `-clear-warning`).
**A second window must be a window, not a tab**: Chrome throttles a hidden tab's timers and its rounds run minutes
apart, so it never asks in time (seen on the first run).

### A server restore, with both profiles open

```bash
node .claude/tools/chrome-restore-check.mjs <scratch>/restore     # run_in_background; same ports as above
```

Halfway it backs up the server's SQLite (`sqlite3 .backup`), makes the edits the restore will lose, takes profile
A offline and prints `RESTORE NOW` with a backup path and a signal file. Then, by hand: stop the app (the kill
commands above), delete `mealplanner.db-wal` and `-shm`, copy the backup over `mealplanner.db`, start the AppHost
run configuration, wait for `_framework/blazor.webassembly.js` to answer 200, and `touch` the signal file. About
two minutes in all; prints PASS/FAIL per check. Profile B usually pushes back before you touch the file, which
is why the script compares chains rather than the head seq. Read the backup with plain `sqlite3`, not
`-readonly`: it is in WAL mode with no `-shm`, which a read-only open cannot create ("unable to open database
file").

## The image, behind a simulated Home Assistant ingress

The AppHost serves the app at `/`, which hides every base-path bug. To see what the add-on sees, build the
image and run it behind nginx playing the Supervisor (why and what it proves: `docs/modules/infrastructure/deployment.md`):

```bash
docker buildx build --platform linux/arm64 -t mealplanner:local --load .     # ~80 s cold
DATA_DIR=<scratch>/sim-data sh .claude/tools/ingress-sim/run.sh               # --down to stop
```

| Port | What |
|---|---|
| 18299 | the fake Supervisor — open `http://localhost:18299/api/hassio_ingress/sImUlAtEdToKeN/` |
| 18298 | the app's own port — must answer 403 |

The server database is `<scratch>/sim-data/mealplanner.db`. The two-profile check runs against it with
`APP_URL=http://localhost:18299/api/hassio_ingress/sImUlAtEdToKeN/ SERVER_DB=<scratch>/sim-data/mealplanner.db`.
After a rebuild, `--down` and start again; a page left open on the old container logs 503s/404s until reloaded.

## The image standalone: behind a sign-in proxy, with Litestream

What the Kubernetes chart runs, in Docker (why: `docs/modules/infrastructure/deployment.md`, "Standalone"). nginx plays
the sign-in proxy (every request is `planner@example.com`, "The Planner"), SeaweedFS plays S3, Litestream replicates:

```bash
DATA_DIR=<scratch>/sa-data sh .claude/tools/standalone-sim/run.sh     # --down to stop and wipe
DATA_DIR=<scratch>/sa-data sh .claude/tools/standalone-sim/run.sh --restore-drill   # wipe /data, restart, restored from S3
```

| Port | What |
|---|---|
| 18396 | the sign-in proxy — open `http://localhost:18396/` |
| 18397 | the app's own port — must answer 403 (`/healthz` and `/healthz/ready` answer 200) |
| 18398 | SeaweedFS's S3 API |

The two-profile check: `APP_URL=http://localhost:18396/ SERVER_DB=<scratch>/sa-data/mealplanner.db`.

**The chart in a throwaway kind cluster:** `KC=<scratch>/kind-kubeconfig sh .claude/tools/kind-sim/run.sh up` (and
`down`). **Always a kubeconfig file of its own** — the default kubectl context on this machine is a real cluster, and
every `kubectl`/`helm` call must name `--kubeconfig "$KC" --context kind-mealplanner-sim`. Port-forward the proxy to
18399: `kubectl --kubeconfig "$KC" --context kind-mealplanner-sim -n proxy port-forward svc/proxy 18399:8080`. The pod has
no `sqlite3`; read its database with the app's own `snapshot` verb (`kubectl exec … -- dotnet
/app/AshersLab.MealPlanner.Grpc.dll snapshot`), `kubectl cp` the snapshot out, and delete it from `/data` afterwards.

**If `docker pull` hangs** (seen 2026-10-22: every daemon pull, Docker Hub and MCR alike, stalled while containers and the
host reached the registries fine), the sims' images can be fetched with `curl` from the registry API on the host and
`docker load`ed; restarting Docker Desktop is the real fix. MinIO is not an option either way: it no longer publishes
to Docker Hub.

## Database

**SQLite, one file, outside the repo:**

```
~/Library/Application Support/AshersLab.MealPlanner/mealplanner.db
```

The AppHost sets `DataDirectory` to that folder (`AppHost.cs`). Read it directly:

```bash
sqlite3 "$HOME/Library/Application Support/AshersLab.MealPlanner/mealplanner.db" \
  'select * from "__EFMigrationsHistory";'
```

The host migrates at startup, so a missing table means the host has not started, not that a
migration job is pending. Sync's tables: `Op` (the log, by `Seq`, each with its `Chain`; a new database's
starts with the catalogue seeds at clock zero), `Ingredient`, `Store`, `Category` (its
`Position` is decimal stored as text - `1.0` here, `1` in a browser's copy; compare with `cast(Position as real)`),
`FieldClock`, `SyncEpoch` (one row - a new value after a wipe means every browser forgets what it synced and takes a
snapshot on its next pull). Nothing in it is worth keeping until the household uses the app — delete
the folder to start over (stop the app first; SQLite holds `-wal`/`-shm` files beside it).

## The browser database

How it works is in `docs/modules/sync/browser-database.md`. How to look at it:

- **In the page** — the "This device" card on Diagnostics (`/settings/diagnostics`; `data-testid` `device-id`,
  `device-open-count`, `device-storage`, `device-role`, or `device-not-open` under the gate's `local-db-state` when it
  could not open) reads the `Device` row
  through EF. In the console, `mealPlannerDatabase.role()` and `mealPlannerDatabase.events` show what the
  cross-window shim did.
- **Independently, with `sqlite3`** — copy the file out of OPFS. The Chrome extension's output filter
  refuses base64 *and* hex, so the bytes cannot come back through `javascript_tool`'s result; the page
  POSTs them to a receiver on 127.0.0.1 instead:

  ```bash
  python3 -I .claude/tools/opfs-receive.py <scratch>/opfs      # run_in_background
  ```

  then run `.claude/tools/opfs-dump.js` in the page with `javascript_tool` (set its `label`), and:

  ```bash
  mkdir -p <scratch>/read && cp <scratch>/opfs/<label>-mealplanner.db <scratch>/read/m.db \
    && cp <scratch>/opfs/<label>-mealplanner.db-wal <scratch>/read/m.db-wal \
    && sqlite3 <scratch>/read/m.db 'select * from Device;'
  ```

  Copy the `-wal` too: committed rows usually live there until a checkpoint.
- **Several windows** — `node .claude/tools/chrome-multiwindow-check.mjs <scratch>/profile` drives a
  separate headless Chrome through sharing, leader hand-overs, reloads, simultaneous starts and dropped
  downloads, and prints PASS/FAIL per check (about a minute; give each run a fresh profile directory). A
  failure is classified `boot-download`, `boot-unknown` or `shim-stall`; only `shim-stall` is a bug in
  `shared-database.js`, and it prints that window's shim events and Web Locks. A brand-new headless profile
  drops requests (`ERR_CERT_VERIFIER_CHANGED`) for about its first second, so the script waits 3 s first
  (`FAST_FIRST=1 WARMUP_MS=0` reproduces it). If it says the port is taken, a headless Chrome from an earlier
  run is still alive: `pkill -f "user-data-dir=<scratch>"`.
  The Chrome extension cannot do this: closing the first tab of its tab group drops the whole group, and
  the other tabs can no longer be driven.
- **Across a browser restart** — never quit the user's Chrome. `.claude/tools/chrome-restart-check.mjs`
  launches a separate headless Chrome on a throwaway profile, reads the card on Diagnostics, and ends the browser with
  `Browser.close` or `SIGKILL`; run it repeatedly on the same profile directory.
- **Wiping it** — when it cannot be opened, the page's own **Reset this device's copy** (`local-db-reset`)
  does it with other windows open. Otherwise DevTools → Application → Storage → "Clear site data", or in the page
  `await (await navigator.storage.getDirectory()).removeEntry('databases', {recursive: true})` with every
  app tab closed but one, then reload.
- **A copy that cannot be opened, reset** — `node .claude/tools/chrome-device-reset-check.mjs <scratch>`
  (port 19351; `UNREADABLE=1` also drops the outbox). Needs the app restarted after a WASM change, like every
  check. About a minute.

Browser-database migrations are generated in `AshersLab.MealPlanner.LocalData`, not the host:

```bash
cd AshersLab.MealPlanner.LocalData && dotnet ef migrations add <WhatItDoes> -o Migrations
```

## The `wasm-tools` workload

The WASM project relinks the .NET runtime (`WasmBuildNative`), which needs `wasm-tools`. This machine's
SDK is Homebrew's 10.0.401 (runtime 10.0.12), which lags the newest workload set, so the workload is pinned
to the SDK's own set:

```bash
dotnet workload install wasm-tools
dotnet workload update --version 10.0.401    # after any SDK upgrade, use the new SDK's version
```

The symptom of a mismatch is a successful build and a page stuck on "Loading…" with
`RuntimeError: function signature mismatch` in the console.

## Migrations

`dotnet ef` from `AshersLab.MealPlanner.Grpc`, which has an `IDesignTimeDbContextFactory`:

```bash
cd AshersLab.MealPlanner.Grpc && dotnet ef migrations add <WhatItDoes> -o Data/Migrations
```

Name a migration for what it does, never a timestamp.
