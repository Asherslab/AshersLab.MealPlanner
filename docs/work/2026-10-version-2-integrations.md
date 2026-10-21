---
title: Version 2 integrations
kind: discussion
status: proposed
module: integrations
opened: 2026-10-20
verified: 2026-10-20
code: []
---

# Version 2 integrations

Notes for the two outside data sources deferred from [the v1 scope](2026-10-v1-scope.md): Actual
Budget and Woolworths live prices. Written so version 1's model does not paint them into a corner;
nothing here is to be built before version 1 is in use.

## Actual Budget

- The household runs Actual Budget (self-hosted, reachable over HTTPS; URL in
  `private/LOCAL-NOTES.md`). One category, **Groceries**, covers both stores.
- Actual has no REST API of its own; its official client is the Node library `@actual-app/api`.
  The owner chose **`actual-http-api`** (community REST wrapper around that library) so the .NET
  server can call it over HTTP. Bundling, as planned in the [deployment design](../archive/2026-10-deployment.md):
  it is a separate Node service. In the add-on it runs **inside the same image** as a second process (s6-overlay, as
  Home Assistant base images use), listening on localhost only; on Kubernetes it is a sidecar in
  `Charts/mealplanner`, beside Litestream. Either way the app reads `ActualHttpApi:BaseUrl`, so pointing it at an
  existing instance also works. Image size and RAM on the Pi matter — measure the bundled image before committing to
  it. **Watch the PID-1 trap** ([deployment](../modules/infrastructure/deployment.md)): with a supervisor as PID 1 the
  app is no longer PID 1, which changes how its failures surface.
- Needed config: actual-http-api base URL + its API key; behind it, the Actual server URL, server
  password and the budget's sync id. All secrets — add-on options / k8s Secret, never committed.
- What to read: for the current month, the Groceries category's budgeted, spent and balance. Later,
  that category's transactions (payee Woolworths / Costco, date, amount) to tick receipts off against
  card charges and spot a shop with no receipt imported.
- A server job fetches it (on a schedule and on demand) and writes `BudgetMonth` as server ops; the
  browser shows the last fetched figure with its time, so it works offline.

## Woolworths live prices

Undocumented endpoints used by woolworths.com.au. Verified working on 2026-10-20 from a Mac with a
plain browser User-Agent after first loading the home page for cookies:

- `GET https://www.woolworths.com.au/apis/ui/Search/products?searchTerm=<q>&pageNumber=1&pageSize=<n>`
- `GET https://www.woolworths.com.au/apis/ui/product/detail/<stockcode>`

A search result product carries `Stockcode`, `Name`, `DisplayName`, `Price`, `WasPrice`,
`IsOnSpecial`, `CupPrice` + `CupMeasure` (unit price, e.g. per 1KG), `PackageSize`, `IsInStock`,
and for variable-weight items `Unit: "KG"` with a per-kg `Price`.

Constraints:

- Server-side only (CORS blocks the browser), and only a handful of requests per refresh — one per
  product with a stockcode, weekly. Bot protection is in front of the site; behave like one person.
- The endpoints can change without notice. A failed refresh leaves the last observation in place
  and says so; it never blanks a price.
- Woolworths invoices do not carry stockcodes. Products learn theirs by a search on the invoice
  description, confirmed by the planner once — the same match-once-remember pattern as receipts.
- Prices can vary by store/region; the household's store is not yet configured anywhere. Find out
  whether the endpoints need a store id before trusting them for specials.
