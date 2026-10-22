---
title: Blazor WASM behind Home Assistant ingress
kind: investigation
status: open
module: infrastructure
opened: 2026-10-20
verified: 2026-10-21
code:
  - mealplanner/config.yaml
  - AshersLab.MealPlanner.Grpc/Features/Ingress
  - AshersLab.MealPlanner.Grpc/Data/DatabaseSnapshot.cs
  - .claude/tools/ingress-sim
---

# Blazor WASM behind Home Assistant ingress

> **2026-10-22: the add-on no longer uses ingress.** The owner chose to reach it at its own HTTPS address through Nginx
> Proxy Manager, signing in with passkeys in the app itself, so it can be installed on a phone and opened offline
> ([signing in](../modules/infrastructure/accounts.md), [deployment](../modules/infrastructure/deployment.md#the-add-on-behind-nginx-proxy-manager)).
> What is below about the panel, ingress headers and the companion app no longer decides anything. **Still open on the
> Pi**, unchanged: whether the Supervisor pulls the multi-arch image (3), first-load time and RAM (4), and a real backup
> and restore (6) - plus, new, the real Nginx Proxy Manager in front and a real iPhone's passkeys, per the add-on's `DOCS.md`.

Does the app load and work as a Home Assistant add-on panel, on a Raspberry Pi, in the HA companion app on
an iPhone? Slice 3 of [the v1 scope](../work/2026-10-v1-scope.md) built everything it needs and proved what
can be proved without the Pi; the rest waits on the owner installing the add-on. How it is built is in
[deployment](../modules/infrastructure/deployment.md).

## Proven on 2026-10-21, against a simulated Supervisor

`.claude/tools/ingress-sim/run.sh`: the arm64 image on a `172.30.32.0/23` network, nginx at `172.30.32.2`
stripping `/api/hassio_ingress/<token>` and adding the ingress headers. Evidence in the deployment doc's last
section. In short: base path and deep links, `_framework/` and MudBlazor files, Brotli, unary and streaming
gRPC-Web (the stream held 115 s without a reconnect), two profiles converging with offline edits, the user
from `X-Remote-User-*`, and direct requests refused with 403.

It also found the one real bug: gRPC-Web calls dropped the prefix and 404'd. Fixed (`BasePathHandler`).

## Still to prove on the Pi, each with evidence

1. **The real proxies.** The browser's request goes through Home Assistant Core's ingress proxy and then the
   Supervisor's — not nginx. Repeat the checks above through them: the panel loads, a deep link reloads, the
   sync chip goes online, and an ingredient added on one device appears on another. The Supervisor streams any
   response without a `Content-Length` (it sets `X-Accel-Buffering: no`), so the `Changes` stream is expected
   to hold; Core's proxy is the unknown. Leave a panel open for several minutes and watch for reconnects.
2. **Identity.** The home page's "Signed in as" row shows the HA user. Confirm the header names, and that
   the Supervisor's source address is `172.30.32.2` — if it is not, the add-on answers 403 to everything and
   the add-on log has nothing; check `docker logs` / the add-on's Log tab and the 403 body "Only reachable
   through Home Assistant."
3. **Image reference.** Whether the Supervisor pulls the multi-arch manifest from
   `image: ghcr.io/asherslab/asherslab-mealplanner` on aarch64, or needs `{arch}` in the name (which would mean
   CI pushing per-arch images too).
4. **Performance on the Pi.** First load time of the WASM bundle through ingress on a phone, and the
   container's startup RAM (`docker stats` or the add-on's Info tab).
5. **The companion app.** It loads and works in the HA iOS app's web view, not only in desktop Safari —
   including OPFS (the browser database; see [sqlite-in-the-browser](sqlite-in-the-browser.md)). Note
   whether a service worker registers there (expected: no).

6. **A backup and a restore.** The add-on backs up with a pre-backup snapshot and leaves the live database out
   ([deployment](../modules/infrastructure/deployment.md#backups)); only the real Supervisor can prove it. With the
   add-on running and a few ingredients entered:
   1. Settings → System → Backups → Create backup → Partial → tick only **Meal Planner**. It must finish without an
      error (a failed snapshot fails the backup). The add-on's Log tab shows nothing for it; the Supervisor log
      (Settings → System → Logs → Supervisor) shows the pre-backup command ran.
   2. Download the backup (⋮ → Download). Inside the `.tar`, the add-on's inner archive's `data/` must hold
      `mealplanner.snapshot.db` and **no** `mealplanner.db`, `-wal` or `-shm`. Optional, on a computer:
      `sqlite3 mealplanner.snapshot.db 'PRAGMA integrity_check; select count(*) from Ingredient;'` - `ok` and the count.
   3. Add one more ingredient (so the restore visibly loses it), then restore the backup: ⋮ → Restore → Meal
      Planner only. The add-on restarts; its Log tab shows `No database at /data/mealplanner.db; restored it from the
      backup snapshot`.
   4. Open the app on a device that was open during step 3: the extra ingredient is back (the browsers give back what
      a restore lost - [offline sync](../modules/sync/offline-sync.md)), and the chip says "Synced".
   5. Create a second backup and check its `data/` again: the snapshot is new, the live files still absent.

## Owner prerequisites

In order — none of them can be done from the repo:

1. Done 2026-10-21: the first CI run was green and published `0.0.1` for amd64 and arm64, and the
   "Release add-on 0.0.1 [skip ci]" commit set `version:` to it.
2. Confirm the GHCR package `asherslab-mealplanner` shows **Public**. An anonymous pull of the `0.0.1` index
   already worked on 2026-10-21, so this is expected to need nothing.
3. In Home Assistant: Settings → Add-ons → Add-on store → ⋮ → Repositories → add
   `https://github.com/Asherslab/AshersLab.MealPlanner`, then install and start **Meal Planner**, with "Show in
   sidebar" on.

Record the answers in `docs/modules/infrastructure/deployment.md` and delete this file.
