---
title: How the app is deployed
kind: reference
status: current
module: infrastructure
verified: 2026-10-22
code:
  - Dockerfile
  - .dockerignore
  - .github/workflows/docker-publish.yml
  - repository.yaml
  - mealplanner
  - Charts/mealplanner
  - AshersLab.MealPlanner.Grpc/Program.cs
  - AshersLab.MealPlanner.Grpc/Features/Ingress
  - AshersLab.MealPlanner.Grpc/Features/Auth
  - AshersLab.MealPlanner.Tests/Grpc/AuthTests.cs
  - .claude/tools/standalone-sim
  - .claude/tools/kind-sim
  - AshersLab.MealPlanner.Grpc/Data/DatabaseSnapshot.cs
  - AshersLab.MealPlanner.Tests/Grpc/DatabaseSnapshotTests.cs
  - AshersLab.MealPlanner.WASM/Extensions/GrpcServiceExtensions.cs
  - .claude/tools/ingress-sim
---

# How the app is deployed

One image, built by GitHub Actions, run as a Home Assistant add-on on a Raspberry Pi and reached through
Home Assistant's sidebar - or standalone, on Kubernetes or plain Docker, behind a proxy that signs people in. Read this
before touching the `Dockerfile`, the workflow, `mealplanner/config.yaml`, the chart, anything that decides who may
connect, or anything that builds a URL. Why it is one image and not the GSBC repos' four is in the
[deployment design](../../archive/2026-10-deployment.md); how the process itself is laid out is
[hosting](hosting.md).

## Traps first

- **The image with no authentication configured does not start.** Outside Development it needs exactly one of
  `Ingress__TrustedProxy` (the add-on's `config.yaml` sets it), `Auth__Mode=TrustedHeader` with `Auth__TrustedProxies`,
  or a deliberate `Auth__Mode=None` - otherwise it exits 1 before touching the database, saying which to set
  (`Grpc/Features/Auth/AuthOptions.cs`). The same image with nothing set used to serve the household's data to anyone
  who found its port.
- **A host that fails to start exits; it must not hang.** On an unhandled exception .NET calls `abort()`, and as a
  container's PID 1 (plain Docker, Kubernetes) the kernel ignores that SIGABRT: the error is printed and the container
  sits "Up", serving nothing, indefinitely. Measured 2026-10-22: with no handler, still running after 2 minutes; under
  `docker run --init`, exit 134 in 2 s. `Program.cs` now exits 1 itself from `AppDomain.UnhandledException`. Home
  Assistant starts add-ons under an init, so the Pi never showed it.
- **Kubernetes' probes come from the node, not through the sign-in proxy.** `/healthz` and `/healthz/ready` are answered
  before every authentication gate (`Grpc/Features/Auth/HealthProbes.cs`) and say only "ok"; anywhere behind the gate
  every probe is a 403 and the pod restarts forever.

- **A gRPC-Web client must be given the base path twice.** `GrpcChannel` keeps only the scheme, host and port
  of its address and sends every call to `/{service}/{method}` from the root. Under ingress that is a 404 —
  the page loads, every call fails "Unimplemented", the sync chip says "Sync problem". `BasePathHandler` in
  `WASM/Extensions/GrpcServiceExtensions.cs` puts the prefix back. At `/` (local development, the test host)
  the bug is invisible; only a run behind a prefix shows it. Found on 2026-10-21 by the simulation below.
- **`index.html` is rewritten per request, never served static** — see [hosting](hosting.md). The token in the
  ingress path differs per install, so nothing can bake it in at build time.
- **`config.yaml`'s `version:` must name a tag that exists on GHCR.** Only CI writes it, after the push. A
  hand-bumped version makes Home Assistant offer an update that fails to pull.
- **The GHCR package must be public** or the Supervisor cannot pull it — it has no registry credentials.
  After the first push (run 39451233134, 2026-10-21) an anonymous registry token could already fetch the
  `0.0.1` index, so the package appears to have taken this public repository's visibility through its
  `org.opencontainers.image.source` label. If an install ever fails to pull, check Package settings →
  visibility first.
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
- `-p:Version=` is the semantic version, which the Server card on Diagnostics (`/settings/diagnostics`) shows.
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
| `environment: Ingress__TrustedProxy: 172.30.32.2` | Turns on the ingress handling below. Set here, not in the image, so the same image elsewhere believes nobody's headers - and, with no `Auth__Mode` either, does not start (below). |
| `options: {}` | Nothing to configure until version 2's Actual Budget settings. |
| `backup: hot`, `backup_pre`, `backup_exclude` | A consistent hot backup - below. |

No icon or logo yet — Home Assistant shows a placeholder.

## Backups

**The live SQLite file is never in a Home Assistant backup.** A hot backup tars `/data` while the add-on runs; the
database copied mid-write, or without the `-wal` that holds its newest commits, can be unusable or silently old. So:

| Step | What | Where |
|---|---|---|
| Before each hot backup | The Supervisor runs `backup_pre` **inside the container**: `dotnet /app/AshersLab.MealPlanner.Grpc.dll snapshot`. It writes `/data/mealplanner.snapshot.db` with SQLite's `VACUUM INTO` - one read transaction, so exactly the commits before it started; the app keeps writing meanwhile - checks it with `PRAGMA integrity_check`, and only then replaces the previous snapshot. It never starts the web host. | `Grpc/Program.cs`, `Grpc/Data/DatabaseSnapshot.cs` |
| The backup | `backup_exclude` leaves out `mealplanner.db`, `-wal`, `-shm` and a failed `.partial`. | `mealplanner/config.yaml` |
| A restore | The Supervisor replaces `/data` with the backup's, so there is a snapshot and no live database; the next start copies the snapshot into place. | `DatabaseSnapshot.RestoreIfMissing` |

Read from the Supervisor's source (`supervisor/apps/app.py`, 2026-10-21) and the add-on configuration reference:

- `backup_pre`/`backup_post` run only for `backup: hot` (`cold` stops the add-on and ignores them). **A command that
  exits non-zero fails the add-on's backup** - so a snapshot that cannot be written is a failed backup in Home
  Assistant's list, never a backup without the data.
- `backup_exclude` globs are tested with Python's `PurePath.match` against the full path, i.e. **from the right**:
  `mealplanner.db` matches `/data/mealplanner.db` and nothing called `mealplanner.snapshot.db`. Renaming the
  snapshot to something ending `mealplanner.db` would exclude it too, and every backup would be empty.
- A restore removes the add-on's data folder and copies the backup's in (`remove_data`, then `copytree`).

The command needs ~1-2 s of .NET start-up on the Pi on top of the copy. `backup: cold` was the fallback - hooks
exist, so it is not used: it stops the app, and the planner's open pages, for every nightly backup.

Measured 2026-10-21 on a Mac: the built host binary's `snapshot`, five times against a copy of the dev database while
one `sqlite3` process committed continuously (two 2 KB rows and a counter per transaction, WAL, about 40 000
transactions between the first and last snapshot): every copy `integrity_check` ok, rows exactly twice the counter
(no transaction torn or half-seen), `journal_mode` delete (self-contained), and the source's own rows intact.
`Tests/Grpc/DatabaseSnapshotTests.cs` repeats that in the suite and covers the restore. In the arm64 image (Docker on
the Mac, `/data` a host folder): `docker exec <container> dotnet /app/AshersLab.MealPlanner.Grpc.dll snapshot` - the
exact `backup_pre` line - exited 0 beside the running app and wrote an `ok` snapshot; with the live files deleted, the
container started, logged the restore and served. **Not yet seen on the Pi** -
a real backup and restore through Home Assistant; the steps are in
[home-assistant-ingress](../../open-questions/home-assistant-ingress.md).

A server reinstalled with **no** backup is not this - see "The server came back empty" in
[offline sync](../sync/offline-sync.md).

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
   later slice. The Supervisor drops a browser's own copies of these headers before adding its own
   (`supervisor/api/ingress.py`, `_REQUEST_HEADERS_FILTER`, read 2026-10-21); a header that still arrives twice
   means something in between did not, and the request is treated as nobody's (`IngressRequest.UserFrom`).

`Ingress:TrustedProxy` that is not an IP address stops the host at startup ("Ingress:TrustedProxy is not an IP
address.", `ValidateOnStart` in `Program.cs`) rather than failing every request. **IPv4 must be written out in full**
(`IngressOptions.Address`): .NET's `IPAddress.TryParse` also takes the old shorthands, so a dropped octet
(`172.30.32` is 172.30.0.32), a padded one (`172.30.32.010`) or a bare number would otherwise be accepted, and a typo
could trust some other machine and refuse the Supervisor - every page a 403 instead of a host that will not start.
IPv6 is taken as written (`IngressTests.A_trusted_proxy_is_an_ipv6_address_or_an_ipv4_one_in_full`). Reviewed 2026-10-21 against the
Supervisor's source: the only trusted input is the peer address, and the base path is held to plain path segments
before it is written into HTML; no other defect found.

## Standalone (Kubernetes, Docker)

The same image, told who to believe by configuration. `AuthOptions.Resolve` picks one mode:

| Configured | Mode | Who may connect | Who they are |
|---|---|---|---|
| `Ingress__TrustedProxy` | Home Assistant | the Supervisor's address only (above) | `X-Remote-User-*` |
| `Auth__Mode=TrustedHeader`, `Auth__TrustedProxies` | a sign-in proxy | the proxy's addresses only; anyone else 403 | `Auth__UserHeader` (default `X-Forwarded-User`), and the name shown from `Auth__DisplayNameHeader` if set; none, empty or twice: 401 |
| `Auth__Mode=None` | nobody checked | anyone - a warning is logged at start | nobody |
| nothing, in Development | nobody checked | anyone | nobody - the AppHost and the test host |
| nothing, otherwise; both of the first two; an unknown mode; a proxy that is not an address | **the host does not start** | | |

`Grpc/Features/Auth/TrustedHeaderRequest.cs` is the same rule as Home Assistant's: **a header is believed only on a
connection that could only have come from the proxy**. It gives the same claims, so the Diagnostics page's Server card cannot
tell the two apart. Two things rest on the operator:

- **The proxy must replace the browser's own copy of the user header**, not pass it on. oauth2-proxy and Authentik's
  outposts set it from the session; an nginx `auth_request` setup needs `proxy_set_header`. A header that still arrives
  twice is believed as nobody.
- **In a cluster `Auth__TrustedProxies` is usually the whole pod network** - the ingress controller's pod address moves -
  and then any pod could claim to be the proxy. The chart's NetworkPolicy (`networkPolicy.enabled`, **on by default**)
  admits only the ingress controller's namespace (`fromNamespaceLabels`), narrowed to its pods by `fromPodLabels` -
  needed on k3s, whose Traefik lives in `kube-system` beside everything else. `helm template` refuses TrustedHeader with
  the policy off while `auth.trustedProxies` holds a network (`/`); single addresses are allowed. **The policy cannot
  stop traffic from the nodes themselves** - NetworkPolicy does not apply to it, and on flannel a node reaches pods from an
  address inside the pod network - so a `hostNetwork` pod, or anything with a shell on a node, can still pass as the proxy.
  On a cluster where that matters, name the ingress controller's pod addresses instead of the network.
- **The display-name header is believed whenever it arrives once**, so it must be one the proxy sets on every request;
  one it does not set, a browser can fill in. It is only the name on the Server card.

`Auth__TrustedProxies` is comma-separated addresses or networks, IPv4 written out in full as for the Supervisor. **.NET's
`IPNetwork.TryParse` quietly masks bits past the prefix** - `10.42.1.0/16` parses as `10.42.0.0/16` - so such an entry is
refused rather than guessed (meant /16 or /24?). A `/0` (`0.0.0.0/0`, `::/0`) is refused too: it trusts everyone, which
is `Auth__Mode=None` without its warning. Served at the host's root only; there is no sub-path support outside
ingress.

### The chart

`Charts/mealplanner` - a chart rather than plain manifests because auth, Litestream, the ingress and the NetworkPolicy are
each optional and each several lines. Not published anywhere; install it from a checkout.

| What | Why |
|---|---|
| One replica, `strategy: Recreate`, not configurable | SQLite has one writer: two pods on the file corrupt it, and a rolling update is two pods. |
| PVC for `/data`, `ReadWriteOnce`, `helm.sh/resource-policy: keep` | The household's data survives an uninstall; delete the claim by hand to start again. |
| Runs as 1654 (the aspnet image's `app`), `fsGroup` 1654, read-only root, all capabilities dropped, `/tmp` an `emptyDir` | The image runs as root only for Home Assistant's root-owned `/data`; the PVC is made the app's own instead. |
| startup / liveness `/healthz`, readiness `/healthz/ready` (the database opens) | Migration runs before the host listens, so startup allows two minutes. |
| requests 25m CPU, 96 MiB; limit 256 MiB; `DOTNET_gcServer=0` | Measured in Docker on 2026-10-22: 76 MiB after serving the app and a two-browser sync. Workstation GC: the server GC's heap per core is memory a Pi-class node lacks, for throughput a household never needs. |
| `helm template` refuses a missing `auth.trustedProxies`, an unknown `auth.mode`, a trusted network with the NetworkPolicy off, Litestream without `bucket` or `existingSecret` | The same refusals the app makes at start - a crash-looping pod says less. |

### Litestream

Optional (`litestream.enabled`): SQLite cannot live *on* S3, so Litestream (`litestream/litestream:0.3.13`) streams the
WAL to any S3-compatible store as a sidecar on the same PVC, and an init container runs `restore -if-db-not-exists
-if-replica-exists` - **a pod on an empty PVC starts from the replica; a PVC with a database keeps it**, never overwritten.
Credentials come only from a Secret (`litestream.existingSecret`, keys `LITESTREAM_ACCESS_KEY_ID` and
`LITESTREAM_SECRET_ACCESS_KEY`); the ConfigMap holds the bucket, path, region and endpoint. A restore brings back the
sync epoch with the data, so the browsers carry on as after any restore ([offline sync](../sync/offline-sync.md)) - up to
the last second or so of writes, which they push back.

### Proven

On 2026-10-22, with the image built locally (arm64):

- **Docker** (`.claude/tools/standalone-sim/run.sh`, nginx as the sign-in proxy, SeaweedFS as S3 - MinIO no longer
  publishes images): with nothing set, the container exits 1 in 2 s with the message above; `Ingress__TrustedProxy=172.30.32`
  likewise. Straight at the app's port: 403, forged `X-Forwarded-User` or not; `/healthz` and `/healthz/ready` 200.
  Through the proxy: shell and `_framework/` 200, the Server card "The Planner (planner@example.com)", and
  `chrome-sync-check.mjs` passing in full - two profiles, the change stream, offline renames converging, rows read from
  both browsers' SQLite and the container's `/data`. Litestream wrote WAL segments to the bucket; `--restore-drill`
  (container stopped, `/data` emptied, started again) came back with the same rows and op log.
- **kind** (`.claude/tools/kind-sim/run.sh`, a throwaway cluster on its own kubeconfig, NetworkPolicy and Litestream on):
  the pod 2/2 ready with no restarts, running as 1654 with `/data` owned by it and `/app` read-only; a pod in another
  namespace timed out reaching the app (NetworkPolicy), one in the proxy's namespace without a user got 401; through the
  proxy, the shell and the user as above, and a new ingredient reached the second profile by the change stream. Then
  `helm uninstall` (the PVC kept), the PVC deleted, reinstalled: the init container restored from S3 and the database had
  the same ingredient, op seq and sync epoch.

Not proven: a real cluster, a real sign-in proxy (oauth2-proxy, Authentik), real S3.

### Deploying it for real (owner)

Nothing here has been run against any real cluster, registry setting or secret.

1. **The sign-in proxy first.** oauth2-proxy or Authentik in front of the ingress, set to **replace** the user header on
   every request. Note its header name and the ingress controller's namespace.
2. **The pod network** for `auth.trustedProxies` (k3s: `10.42.0.0/16`; `kubectl get nodes -o
   jsonpath='{.items[*].spec.podCIDR}'`), and the ingress controller's namespace label for
   `networkPolicy.fromNamespaceLabels` (the policy is on by default). On k3s's Traefik that namespace is `kube-system`:
   also set `networkPolicy.fromPodLabels` to `app.kubernetes.io/name: traefik`. Without the policy any pod in the cluster
   can sign in as anyone; even with it, anything on a node can (above).
3. **For Litestream**, a bucket and a key limited to it, then the Secret - never in values, and never on a command line
   (shell history, the process list): write the two lines `LITESTREAM_ACCESS_KEY_ID=…` and `LITESTREAM_SECRET_ACCESS_KEY=…`
   to a file only you can read, `kubectl -n <ns> create secret generic mealplanner-s3 --from-env-file=<file>`, delete it.
4. Install from a checkout, pinning the version: `helm upgrade --install mealplanner Charts/mealplanner -n <ns>
   --set image.tag=<version> --set auth.trustedProxies=<pod network> --set auth.userHeader=<header> --set
   networkPolicy.fromNamespaceLabels."kubernetes\.io/metadata\.name"=<ingress namespace> --set
   ingress.enabled=true --set ingress.host=<host> …` plus the proxy's annotations under `ingress.annotations` (the values
   file has an ingress-nginx + oauth2-proxy example). Keep proxy buffering off and the read timeout long: the change
   stream is one long response.
5. Open the host's `/settings/diagnostics`: the Server card should name you. Then `kubectl exec` a request to the pod from a pod
   outside the ingress namespace - it should time out.

The cluster pulls from GHCR without credentials only while the package is public - the add-on needs that too; see the
trap above. Nothing in the chart sets `imagePullSecrets`.

## CI

`.github/workflows/docker-publish.yml`, on a push to `master` that touches more than `docs/`, `.claude/` or
markdown, one run at a time (the first run, 2026-10-21, took 2 m 23 s to build both platforms):

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

1. Confirm the package is public: GitHub → profile → Packages → `asherslab-mealplanner` → it should say
   Public; if not, Package settings → Change visibility → Public.
2. Make sure a "Release add-on … [skip ci]" commit is on `master` (the first was `0.0.1`).
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
- Diagnostics (`/settings/diagnostics`; it was the home page when this was seen) shows the fake HA user from the
  headers; the sync chip goes online;
- `.claude/tools/chrome-sync-check.mjs` (`APP_URL`, `SERVER_DB` pointed at the simulation) passes in full: two
  profiles, the change stream, an offline rename syncing on reconnect, concurrent renames converging, rows
  read from both browsers' SQLite and from the container's `/data`;
- the `Changes` stream held for more than a minute without reconnecting.

What it cannot prove — the real Supervisor's and Home Assistant Core's proxies, the Pi, the companion app — is
listed in [home-assistant-ingress](../../open-questions/home-assistant-ingress.md).
