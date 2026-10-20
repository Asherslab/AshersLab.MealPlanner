---
title: Blazor WASM behind Home Assistant ingress
kind: investigation
status: open
module: infrastructure
opened: 2026-10-20
verified: 2026-10-20
code: []
---

# Blazor WASM behind Home Assistant ingress

Does the app load and work as a Home Assistant add-on panel, on a Raspberry Pi, in the HA companion
app on an iPhone? [The deployment design](../work/2026-10-deployment.md) assumes yes. Settled by
slice 3 of [the v1 scope](../work/2026-10-v1-scope.md), deliberately before any feature work.

## What to prove, each with evidence

1. **Base path.** The app loads under `/api/hassio_ingress/<token>/` with `<base href>` taken from
   `X-Ingress-Path`: the `_framework/` files, MudBlazor's CSS/JS, and deep links (reload on a nested
   route) all resolve.
2. **gRPC-Web through ingress.** A unary call and a server-streaming call (the sync `Changes()`
   stream) both work, and the stream survives longer than a minute or reconnects cleanly.
3. **Identity.** Which headers the Supervisor actually sends (expected `X-Remote-User-Id`,
   `-Name`, `-Display-Name`), from which source address (expected `172.30.32.2`), and that a request
   from anywhere else is refused.
4. **Image reference.** Whether the Supervisor pulls a multi-arch manifest from
   `image: ghcr.io/asherslab/asherslab-mealplanner`, or needs `{arch}` in the image name (which would
   mean CI pushing per-arch images too).
5. **Performance on the Pi.** First load time of the WASM bundle through ingress on a phone, with
   compression (Brotli) on. Startup RAM of the container.
6. **The companion app.** It loads and works in the HA iOS app's web view, not only in desktop
   Safari. Note whether a service worker registers there (expected: no — offline cold start inside HA
   is already accepted as unsupported).

## Owner prerequisites

The Pi runs HA OS. Installing the add-on needs the owner to add the repository URL in Home Assistant
and make the GHCR package public — ask, do not assume either has been done.

Record the answers in `docs/modules/infrastructure/` and delete this file.
