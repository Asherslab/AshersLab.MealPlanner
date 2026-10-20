---
title: SQLite in the browser
kind: investigation
status: open
module: sync
opened: 2026-10-20
verified: 2026-10-21
code:
  - AshersLab.MealPlanner.WASM/Data/LocalDatabase.cs
  - AshersLab.MealPlanner.WASM/Components/ThisDeviceCard.razor
  - AshersLab.MealPlanner.WASM/wwwroot/js/shared-database.js
---

# SQLite in the browser

Can the Blazor WASM app keep an EF Core SQLite database that survives a reload, on Chrome desktop
**and** Safari on iOS, without hurting load time on a phone? The offline design
([offline sync](../work/2026-10-offline-sync.md)) assumes yes. Settled by slice 1 of
[the v1 scope](../work/2026-10-v1-scope.md).

**Answered for Chrome desktop; Safari on iOS still needs the owner's phone.** What was built and measured
is in [the browser database](../modules/sync/browser-database.md). Delete this file once the iOS check
below passes, moving its result into that doc.

## Answers so far

1. **Approach** — SqliteWasmBlazor 0.9.4-pre: EF Core over the official sqlite-wasm in a Web Worker,
   stored in OPFS through the SAHPool VFS. Works on .NET 10 with `WasmBuildNative` and the `wasm-tools`
   workload (pinned to the SDK's workload set — see the module doc's traps). Its documentation claims
   Safari 16.4+ including iOS; not yet seen.
2. **Cost** — about 2 MB brotli extra on first load (1.4 MB of it EF Core). Database ready about 2.0 s
   after navigation on desktop Chrome, Release build, with the page itself rendered at 0.4 s. Phone
   timing: pending.
3. **Losing the last write** — not seen in Chrome: five reloads fired the instant a start's write
   landed, a tab closed and reopened, two browser quits and a `SIGKILL` all kept every write.
4. **Building in the Dockerfile** — not yet tried: there is no Dockerfile until slice 3. The build
   stage needs `dotnet workload install wasm-tools` at the workload set matching the image's SDK.

## Found along the way

- **One tab per browser profile, on its own.** OPFS access handles are exclusive. The owner wants two
  windows side by side, so the app now shares the database across windows — see "Several windows" in
  [the browser database](../modules/sync/browser-database.md). Proven in Chrome only.
- iOS may freeze a backgrounded Safari tab while it is the window running the database. Other tabs' requests
  would then wait on it. Whether that happens is part of the check below.

## The iOS check — for the owner

Needs: the Mac running the app from Rider, the iPhone, and Tailscale signed in on both (OPFS needs real
HTTPS, which `tailscale serve` provides inside the tailnet with a trusted certificate; nothing is exposed
to the internet).

1. On the Mac, start **AshersLab.MealPlanner.AppHost: https** in Rider.
2. Start Tailscale on the Mac (it is installed, currently stopped). In the Tailscale admin console make
   sure **MagicDNS** and **HTTPS certificates** are enabled for the tailnet.
3. In a terminal on the Mac:
   ```bash
   /Applications/Tailscale.app/Contents/MacOS/Tailscale serve --bg https+insecure://localhost:7283
   ```
   It prints the address — `https://<this-mac>.<tailnet>.ts.net/`.
4. On the iPhone, with Tailscale connected, open that address in **Safari**. Wait for the "This device"
   card. Note the **Device** id and **Opened** count. Note roughly how long the card took to fill.
5. Pull down to reload. **Pass:** same id, count +1.
6. Swipe Safari away in the app switcher, reopen it, open the address again. **Pass:** same id, count +1.
7. Restart the phone, open the address again. **Pass:** same id, count +1.
8. Open the address in a **second Safari tab** while the first is still open. **Pass:** same id, count +1,
   and "Held by" says "Another window of the app - shared". Switch back to the first tab: its count shows
   the same number. Then close the first tab and reload the second. **Pass:** count +1, "Held by" says
   "This window".
8a. Leave the first tab in the background for a few minutes (lock the phone), then open the second tab.
   **Pass:** its card fills. If it hangs, note how long and whether switching to the first tab unsticks it.
9. Optional: Share → **Add to Home Screen**, open it from there, and check the **Storage** row says "Kept
   by the browser". (A home-screen app has its own storage, so expect a new device id there.)
10. Turn it off: `/Applications/Tailscale.app/Contents/MacOS/Tailscale serve reset`.

Report back the pass/fail of steps 5–8a, the load time from step 4, and the Storage row.

## If it does not work

Fall back to IndexedDB as a document store (one object store per entity, JSON values) behind the
same repository interface. Loses SQL querying in the browser; costing and shopping-list computation
then run over in-memory collections, which at this data size (hundreds of rows) is fine. Record the
answer in the [browser database](../modules/sync/browser-database.md) doc and delete this file.
