---
title: How the app is deployed
kind: reference
status: current
module: infrastructure
verified: 2026-10-21
code:
  - Dockerfile
  - .dockerignore
  - .github/workflows/docker-publish.yml
  - repository.yaml
  - mealplanner
  - AshersLab.MealPlanner.Grpc/Features/Ingress
  - AshersLab.MealPlanner.WASM/Extensions/GrpcServiceExtensions.cs
  - .claude/tools/ingress-sim
---

# How the app is deployed

One image, built by GitHub Actions, run as a Home Assistant add-on on a Raspberry Pi and reached through
Home Assistant's sidebar. Read this before touching the `Dockerfile`, the workflow, `mealplanner/config.yaml`
or anything that builds a URL. Why it is one image and not the GSBC repos' four is in the
[deployment design](../../work/2026-10-deployment.md); how the process itself is laid out is
[hosting](hosting.md).

## Traps first

- **A gRPC-Web client must be given the base path twice.** `GrpcChannel` keeps only the scheme, host and port
  of its address and sends every call to `/{service}/{method}` from the root. Under ingress that is a 404 —
  the page loads, every call fails "Unimplemented", the sync chip says offline. `BasePathHandler` in
  `WASM/Extensions/GrpcServiceExtensions.cs` puts the prefix back. At `/` (local development, the test host)
  the bug is invisible; only a run behind a prefix shows it. Found on 2026-10-21 by the simulation below.
- **`index.html` is rewritten per request, never served static** — see [hosting](hosting.md). The token in the
  ingress path differs per install, so nothing can bake it in at build time.
- **`config.yaml`'s `version:` must name a tag that exists on GHCR.** Only CI writes it, after the push. A
  hand-bumped version makes Home Assistant offer an update that fails to pull.
- **The GHCR package must be public** or the Supervisor cannot pull it — it has no registry credentials. A
  new package is private; this is a one-time setting on the package page.
- **Nothing in the image's final stage may `RUN`.** The arm64 image is assembled on an amd64 runner without
  QEMU; a `RUN` there fails with `exec format error`.

## The image

`Dockerfile` at the root, context the whole repo (`.dockerignore` keeps `private/`, local databases, tests and
docs out of it).

- **The build stage runs on the builder's own platform** (`FROM --platform=$BUILDPLATFORM`) and publishes
  portable — no `-r`. The host is IL, the WASM files are platform-free, and SQLite's native library is under
  `runtimes/linux-x64` and `runtimes/linux-arm64`, both kept. One publish feeds both images; the final stage
  only copies. A local arm64 build took 80 s on 2026-10-21.
- **`wasm-tools` is installed in the build stage**, with `python3` for its Emscripten. The browser's SQLite
  relinks the WASM runtime (see [the browser database](../sync/browser-database.md)).
- `-p:Version=` is the semantic version, which the home page's Server card shows.
- **Runs as root.** The Supervisor mounts `/data` root-owned and offers no way to change it; as the base
  image's `app` user the first migration fails "unable to open database file".
- `ASPNETCORE_HTTP_PORTS=8099` (the add-on's `ingress_port`), `DataDirectory=/data`. Plain HTTP/1.1 —
  gRPC-Web needs nothing more, and the Supervisor speaks nothing more.
- Brotli: `MapStaticAssets` serves the publish's precompressed `.br` files (`Content-Encoding: br` on
  `_framework/dotnet.js`, measured through the simulated ingress).
- Size: 77 MB of app on the `aspnet:10.0` base.

## The add-on

The repository is a Home Assistant add-on repository: `repository.yaml` at the root, the add-on in
`mealplanner/` (`config.yaml`, `DOCS.md` shown on the add-on's Documentation tab, `README.md`).

| `config.yaml` | Why |
|---|---|
| `image: ghcr.io/asherslab/asherslab-mealplanner`, no `{arch}` | One multi-arch manifest. Whether the Supervisor pulls it by platform is still to be seen on the Pi — [home-assistant-ingress](../../open-questions/home-assistant-ingress.md). |
| `arch: [aarch64, amd64]` | The Pi, and anything else. |
| `ingress: true`, `ingress_port: 8099`, no `ports:` | Nothing published on the host: the Supervisor is the only client the app ever sees. |
| `environment: Ingress__TrustedProxy: 172.30.32.2` | Turns on the ingress handling below. Set here, not in the image, so the same image elsewhere believes nobody's headers. |
| `options: {}` | Nothing to configure until version 2's Actual Budget settings. |

No icon or logo yet — Home Assistant shows a placeholder.

## Ingress

The Supervisor proxies `/api/hassio_ingress/<token>/…` to `http://<add-on>:8099/…` with the prefix
**stripped**, and sends the prefix in `X-Ingress-Path` and the logged-in user in `X-Remote-User-Id`,
`-Name` and `-Display-Name`. Ingress lets through only users logged in to Home Assistant.

`Grpc/Features/Ingress/IngressRequest.cs`, first in the pipeline, does nothing unless `Ingress:TrustedProxy` is
set. When it is:

1. **A request from any other address is a 403** ("Only reachable through Home Assistant."), files and gRPC
   alike — so another add-on on the hassio network, or a port someone publishes by hand, cannot send a forged
   `X-Remote-User-*`. An IPv4 peer reported as `::ffff:172.30.32.2` counts.
2. `X-Ingress-Path` becomes the shell's `<base href>` — only if it is an absolute path of plain segments
   (`IngressPath.TryParse`). Anything else, including `//host`, `..` and quotes, falls back to `/`.
3. `X-Remote-User-*` becomes `HttpContext.User` (`Name` = the HA login, a `display_name` claim). The home
   page's Server card shows it, via `ServerInfo.UserName` / `UserDisplayName`. Linking it to a `Person` is a
   later slice.

Behind any other proxy (Kubernetes) the image has **no auth** today; slice 14 adds it.

## CI

`.github/workflows/docker-publish.yml`, on a push to `master`, one run at a time:

1. **version** — `paulhatch/semantic-version` over the tag history (`fetch-depth: 0`), pushes `v<version>`.
2. **build** — `linux/amd64,linux/arm64` to `ghcr.io/asherslab/asherslab-mealplanner:<version>` and `:latest`,
   logged in with `GITHUB_TOKEN` (`packages: write`). The `org.opencontainers.image.source` label links the
   package to this repository. No QEMU.
3. **addon-version** — after the push succeeds, rewrites `version:` in `mealplanner/config.yaml` and commits
   "Release add-on <version> [skip ci]" to `master` as `github-actions[bot]`. That commit is what Home Assistant
   sees as an update. **Pull before working**: every release lands a commit on `master` behind you.

No repository secret or variable is needed — the GSBC repos' Docker Hub login and Argo token have no
counterpart here. The workflow's own `permissions:` blocks grant `contents: write` and `packages: write`, which
works with the repository's default read-only token setting.

## Installing it (owner, once)

1. After the first green run, make the package public: GitHub → profile → Packages →
   `asherslab-mealplanner` → Package settings → Change visibility → Public.
2. Wait for the "Release add-on … [skip ci]" commit on `master`.
3. Home Assistant → Settings → Add-ons → Add-on store → ⋮ → Repositories → add
   `https://github.com/Asherslab/AshersLab.MealPlanner`.
4. Install **Meal Planner**, start it, and turn on "Show in sidebar". It opens as **Meals**.

## Proving it without a Pi

`.claude/tools/ingress-sim/run.sh` runs the image on a Docker network laid out like the hassio network
(`172.30.32.0/23`), with nginx standing in for the Supervisor at `172.30.32.2`: prefix stripped, the
`X-Ingress-Path` and `X-Remote-User-*` headers added, responses unbuffered. Through it on 2026-10-21, with the
arm64 image:

- the shell under `/api/hassio_ingress/<token>/` and a deep link (`…/ingredients`) both carry the prefixed
  base; `_framework/`, `_content/MudBlazor/` load; direct requests to the app's own port are 403;
- the home page shows the fake HA user from the headers; the sync chip goes online;
- `.claude/tools/chrome-sync-check.mjs` (`APP_URL`, `SERVER_DB` pointed at the simulation) passes in full: two
  profiles, the change stream, an offline rename syncing on reconnect, concurrent renames converging, rows
  read from both browsers' SQLite and from the container's `/data`;
- the `Changes` stream held for more than a minute without reconnecting.

What it cannot prove — the real Supervisor's and Home Assistant Core's proxies, the Pi, the companion app — is
listed in [home-assistant-ingress](../../open-questions/home-assistant-ingress.md).
