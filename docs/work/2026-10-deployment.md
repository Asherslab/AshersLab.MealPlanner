---
title: Deployment design
kind: plan
status: in-progress
module: infrastructure
opened: 2026-10-20
verified: 2026-10-21
code:
  - Dockerfile
  - mealplanner/config.yaml
  - .github/workflows/docker-publish.yml
---

# Deployment design

How one image runs as a Home Assistant add-on on a Raspberry Pi and as a plain container on
Kubernetes, and how CI produces it. Expands the "Hosting", "Images" and "Storage" rows of
[the v1 scope](2026-10-v1-scope.md).

**The image, the add-on and CI landed in slice 3 (2026-10-21)** and are described as built in
[deployment](../modules/infrastructure/deployment.md) — read that, not the sections below, for how they work.
Differences from this plan: the add-on folder is `mealplanner/`; the Actual Budget options are not reserved
yet (an empty `options:` until version 2 needs them); no icon or logo yet. What remains here is the
standalone (Kubernetes) section, slice 14, and `actual-http-api`, version 2. Fold this doc when slice 14
lands.

## Why one image, and why that departs from the GSBC repos

The GSBC apps ship four images (grpc, wasm/nginx, yarp, migrations) and a Helm chart, because they run
on a cluster with Postgres. A Home Assistant add-on is **one container** that the Supervisor starts;
there is no pod, no sidecar, no Job. So:

| GSBC component | Here | Why |
|---|---|---|
| YARP BFF | **Gone in production.** | Its jobs were one origin for WASM + API, and cookie auth. The gRPC host serves the WASM files itself (`UseBlazorFrameworkFiles`/static web assets) from the same origin; auth comes from the proxy in front (HA ingress, or the k8s ingress). Keep YARP out of local dev too unless Aspire needs it to put both on one origin. |
| nginx WASM image | **Gone.** | Same reason. |
| Migrations Job | **Gone.** | SQLite, one process: migrate at startup, before serving. |
| Postgres | **SQLite** file under the data directory. | A Pi; no database server to run. |
| Aspire AppHost | **Kept, local dev only.** | Same dev loop as the other repos. |

Image: `ghcr.io/asherslab/asherslab-mealplanner` (GHCR names must be lower case), tags `latest` and
the semantic version, platforms `linux/amd64,linux/arm64`. arm64 is the one that matters: the
production target is a Raspberry Pi.

## Home Assistant add-on

The repository doubles as a Home Assistant add-on repository: `repository.yaml` at the root and an
add-on folder (`addon/` or `mealplanner/`) with `config.yaml`, `DOCS.md`, `icon.png`, `logo.png`,
`translations/en.yaml`. The owner adds the GitHub URL in Settings → Add-ons → Add-on store →
Repositories. HACS is **not** how this installs — HACS is for integrations and cards; the optional
HACS integration in version 2 lives in a separate repo or folder.

`config.yaml` essentials:

- `arch: [aarch64, amd64]`, `image: ghcr.io/asherslab/asherslab-mealplanner` (check whether the
  Supervisor accepts a multi-arch manifest or wants `{arch}` in the name — see
  [home-assistant-ingress](../open-questions/home-assistant-ingress.md)).
- `ingress: true`, `ingress_port: <app port>`, `panel_icon: mdi:food`, `panel_title: Meals`.
- `options`/`schema` for: Actual server URL, Actual password, budget sync id, Groceries category name
  (all version 2, but reserve them), optional S3 backup settings.
- `version:` must equal an image tag that exists. CI bumps it — see below.

What ingress means for the app:

- **Base path.** The app is served under `/api/hassio_ingress/<token>/`. The Supervisor sends the
  prefix in the `X-Ingress-Path` header. `<base href>` must be set from it at request time (serve
  `index.html` through a small endpoint that rewrites it, rather than as a static file), and the
  gRPC-Web channel's address must be built from `NavigationManager.BaseUri`, never `/`.
- **Auth.** Ingress only lets through users logged in to Home Assistant, and passes
  `X-Remote-User-Id`, `X-Remote-User-Name`, `X-Remote-User-Display-Name`. Trust them **only** from
  the Supervisor's address (`172.30.32.2`). The app's own `Person` records are linked to HA users
  once, in settings.
- **Data** lives in `/data` (SQLite, plus Litestream state). It is included in Home Assistant
  backups.

### `actual-http-api` (version 2)

`actual-http-api` is a separate Node service. In the add-on it runs **inside the same image** as a
second process (s6-overlay, as Home Assistant base images use), listening on localhost only. In k8s
it is a sidecar container. Either way the app reads `ActualHttpApi:BaseUrl`, so pointing it at an
existing instance also works. Image size and RAM on the Pi matter — measure the bundled image before
committing to it.

## Standalone (Kubernetes / Docker)

Same image, configured by environment variables:

- Data directory on a PVC (`ReadWriteOnce`, one replica — SQLite is single-writer).
- Auth: a configurable trusted header (e.g. `X-Forwarded-User` from oauth2-proxy / Authentik) from a
  configurable proxy address, or a built-in single shared password for the simplest setups. Never
  "no auth" except in Development.
- Optional **Litestream** sidecar (or in-process) replicating the SQLite file to S3. SQLite cannot
  live *on* S3; Litestream is how S3 gets involved.
- A Helm chart under `Charts/` in the GSBC shape, published the same way — or plain manifests under
  `deploy/k8s/` if a chart is overkill for one Deployment. Decide in that slice.

## CI (GitHub Actions)

Modelled on `GSBC.Accounting/.github/workflows/docker-publish.yml`, minus the Argo step:

1. **version** — `paulhatch/semantic-version`, `fetch-depth: 0`, push `v<version>`.
2. **build** — one image, `linux/amd64,linux/arm64`, to GHCR using `GITHUB_TOKEN`
   (`packages: write`). **Cross-compile, do not emulate**: `FROM --platform=$BUILDPLATFORM` for the
   SDK stage and `dotnet publish -r linux-arm64` / `linux-x64` from `$TARGETARCH`. Publishing the WASM
   app under QEMU is very slow. If the native SQLite WASM build needs `wasm-tools`, install the workload
   in the SDK stage.
3. **add-on version** — rewrite `version:` in the add-on's `config.yaml` to the new version and commit
   it to `master` (with `[skip ci]`), **after** the image push succeeds. That commit is what makes
   Home Assistant offer the update. Committing it before the image exists makes the update fail to
   pull.

The GHCR package must be **public** for Home Assistant to pull it without credentials — a one-time
setting on the package page after the first push. Ask the owner; it is outside the repo.
