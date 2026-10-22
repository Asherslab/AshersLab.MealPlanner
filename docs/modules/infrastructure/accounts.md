---
title: Signing in
kind: reference
status: current
module: infrastructure
verified: 2026-10-23
code:
  - AshersLab.MealPlanner.Grpc/Features/Accounts
  - AshersLab.MealPlanner.Grpc/Features/Auth/AuthOptions.cs
  - AshersLab.MealPlanner.Grpc/Features/Auth/AddOnOptions.cs
  - AshersLab.MealPlanner.Grpc/Program.cs
  - AshersLab.MealPlanner.WASM/Account
  - AshersLab.MealPlanner.WASM/wwwroot/js/passkeys.js
  - AshersLab.MealPlanner.LocalData/Sync/SyncSchedule.cs
  - AshersLab.MealPlanner.Tests/Grpc/AccountsTests.cs
  - AshersLab.MealPlanner.Tests/Grpc/AddOnOptionsTests.cs
  - mealplanner/config.yaml
  - mealplanner/DOCS.md
  - .claude/tools/addon-sim
  - .claude/tools/chrome-pwa-check.mjs
---

# Signing in

The Home Assistant add-on is reached from the internet at its own address, with Nginx Proxy Manager doing HTTPS and
nothing else - so **the app signs people in itself, with passkeys and nothing else** (`Auth:Mode=Accounts`). Read before
touching anything that decides who may sync, the `/account` endpoints, or the add-on's options. The other two ways the
image is told who is asking - Home Assistant ingress, a sign-in proxy's header - are in [deployment](deployment.md).

## Why passkeys, and why in the app

The owner wanted it reachable from outside, and running an identity provider (Authelia, Authentik) beside it was not an
option. A passkey is a key pair: the phone keeps the private half behind Face ID or its PIN, the server keeps the public
half. Nothing the server stores lets anyone in, nothing can be phished (a passkey only works on the address it was made
for), and there is no password to forget or reuse. ASP.NET Core Identity in .NET 10 does the WebAuthn ceremonies and
stores the passkeys; `Features/Accounts` is the rest.

## Traps first

- **Passkeys are bound to `public_url`'s host for good.** The relying party id is the host (`IdentityPasskeyOptions.ServerDomain`),
  and every ceremony must come from exactly that origin (`ValidateOrigin`). Change the address and every passkey stops
  working: everyone sets up again from a `reset_access` link. Behind a proxy the request's own Host is not trusted for this
  - it is configuration, never a header.
- **The cookie's signing keys are in `/data/keys`** (Data Protection, `Accounts.AddAccounts`). Kept anywhere the container
  forgets, every restart would sign out every phone. They are in Home Assistant backups with the database, so a restore
  keeps everyone signed in. Unencrypted at rest (the log says so at start): whoever has `/data` has the data anyway.
- **Every POST must carry this app's origin** (`Accounts.UseAccounts`), before anything reads it: a page on another site
  cannot make a phone's browser sync, sign in or invite, cookie or not. Browsers send `Origin` on every POST fetch, same
  site included, so a POST with none is refused too. With the cookie `SameSite=Strict`, that is two locks on one door.
- **A sync call with no session is a 401, never a redirect** (`OnRedirectToLogin`): gRPC-Web reads it as `Unauthenticated`,
  which the chip shows as **Signed out** ([offline sync](../sync/offline-sync.md)). A redirect would look like no signal.
- **The schema version is set twice.** Identity's passkeys table exists only in schema version 3; the host sets it, and
  `AccountsDbContextFactory` sets it for `dotnet ef` - without that, a new migration drops the passkeys table.
- **Accounts live beside the sync model, not in it**: `AccountsDbContext`, the same SQLite file (so the backup snapshot
  holds both), its own `__AccountsMigrationsHistory`. Members are not ops and never reach a phone; the household's People
  (who eats what) are a different thing.

## Who can sign in

| | How | Lasts |
|---|---|---|
| The first member | The add-on's log at start, when nobody can sign in: *"Nobody can sign in yet. Open this link ..."* - `/welcome#<token>` | until used, or a week; each start replaces it |
| Someone new | A member's **Make an invitation link** on Settings → Who can sign in, with their name or for them to write | until used, or a day |
| Nobody can sign in at all | `reset_access` on the add-on's Configuration tab: a new setup link at every start, members or not | until turned off |

A link's token is 32 random bytes; **only its SHA-256 is stored** (`AccountRules.Hash`), so the database alone signs nobody
up. The token is in the URL's fragment, which never reaches the proxy's or the server's request logs; the welcome page
reads it and posts it, and takes it off the address bar once used.

**Accepting a link** (`/account/invitation/options`, then `/accept`): the server fixes the new member's id on the invitation,
asks for a passkey for exactly that id, and stores the result **only if the passkey names that id** - so a passkey can never
be attached to someone else. The member is created, the passkey added, the invitation marked used, and the phone signed in.
Two phones racing one link: the second fails to create the same member.

## A session

`Accounts.AddAccounts`: Identity's application cookie, `mealplanner.session` - HttpOnly, Secure, SameSite=Strict, 90 days
that renew with use (`AccountRules.Session`), so a phone in daily use stays signed in. Each request rechecks the member's
security stamp every minute (`RecheckEvery`): **sign out on all my devices** changes it, and so does removing a passkey;
**removing a member** deletes them. Either way their devices' next round is a 401 within about a minute.

Sign-in and sign-up are rate-limited together, 20 a minute for everyone (`Accounts.RateLimit`) - behind the proxy every
request comes from the proxy, so there is no per-address limit to be had, and two people sign in a few times a season.
User verification is **required** (Face ID, a fingerprint or the PIN - a tap is not enough) and passkeys are discoverable,
so signing in asks for no name.

## What is public

The app shell and its files - they hold no data. A phone's own copy of the data is the phone's: signed out, the app still
opens and works offline, and its changes wait in the outbox until someone signs in. `/account/me` (who is signed in, or
nobody), the sign-in and invitation endpoints, and the health probes. **Every gRPC service requires a signed-in member.**

## What the planner sees

- **Signed out** - the chip, and a banner across every page: *"You're signed out. Changes stay on this device and are sent
  once you sign in."* with **Sign in**. A fresh phone opens to it.
- **Sign in** (`/sign-in`): one button, *Sign in with a passkey*, then Face ID. Where the app has no sign-in of its own
  (`/account` is a 404 - ingress or a proxy), it says to reload instead.
- **Welcome** (`/welcome#...`): the inviter's name for them, or a box for their own; *Make my passkey*; *You're in*.
- **Settings → Who can sign in** (`/settings/access`): each member and their passkeys (*synced to their other devices* or
  *this device only*), remove a passkey (never a member's last - remove the member and invite them again) or a member
  (never the last one); **Make an invitation link** and *Send it* (the phone's share sheet); **Add a passkey on this device**,
  **Sign out on this device**, **Sign out on all my devices**.

## As seen

2026-10-23. `Tests/Grpc/AccountsTests.cs` through the real host in Production: before anyone signs in a gRPC-Web call is
`Unauthenticated`; POSTs from another origin, from `http://` for an `https://` address and with no origin are 403, and ours
gets request options naming the host as relying party; `/account/me` says nobody and the member list is 401; a fresh
install makes one setup link, stored as a 64-character hash; a code that is not one is refused; the address rules; links
random, hashed, single-use and expiring. `AddOnOptionsTests`: only `public_url` and `reset_access` are read - an option
naming the mode is ignored.

`.claude/tools/chrome-pwa-check.mjs` against the published image in `.claude/tools/addon-sim` (the hassio-like network,
`options.json`, plain nginx as the proxy), two headless Chromes with virtual platform authenticators - see the check's own
header for its steps; results are in [deployment](deployment.md#the-add-on-behind-nginx-proxy-manager).
