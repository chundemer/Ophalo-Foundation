# BL149 — GAP-038: in-product feedback + Help & Updates loop — implementation build-log

**Status:** 038-1a (content backend) landed 2026-09-08. 038-1b prework resolved 2026-09-08: split
into **038-1b-i** (Help content surface — file gate below, implementation-ready) → **038-1b-ii**
(menus + unread indicator + Requests banner — gate re-run before coding). D1–D8 remain resolved
(D5 = persist-first). 038-2 retains its own exit criteria.
**Date:** 2026-09-07
**Authority:** [ADR-500](../decisions/ADR-500-in-product-feedback-and-help-updates-loop.md) (full
end-to-end contract — Locked), [ADR-501](../decisions/ADR-501-api-route-and-compatibility-policy.md)
(flat Foundation-owned routes — Locked). This build-log only settles the six items ADR-500 §Consequences
defers to it, plus the ready-to-build exit criteria; it does not reopen anything locked.
**Sequencing:** GAP-054 slice 054-1 landed as `4f1caffb` (`feat(ophalo-app): account menu shell`),
so its commit is no longer a GAP-038 blocker. Not a pilot gate.

## What is already locked (do not re-decide)

Feed model, payload schema, single client-computed `keep_updates_watermark`, `--ophalo-accent` dot
placement, the one self-quieting `RequestListContent` banner and its lifetimes, the single sectioned
`#/help` scroll, the light-markdown subset, guides-only inline captioned images, `POST /feedback`
never auto-attaching customer PII, "never a fabricated sent", the ADR-293 no-ticket-lifecycle
boundary, and everything in ADR-500 §Out of scope. Routes are `GET /updates` and `POST /feedback`,
**Foundation**-owned, flat (ADR-501).

## Preflight — repository facts (2026-09-07)

Confirmed present:

- Minimal-API endpoint files per domain area (`src/OpHalo.Api/Accounts/*Endpoints.cs`,
  `.../Auth/AuthEndpoints.cs`); Foundation endpoints live under `src/OpHalo.Api/Accounts` /
  `.../Auth`, not `Keep/`.
- Typed `HttpClient` via `AddHttpClient<TInterface, TImpl>(c => …)` with `BaseAddress` + auth header
  (`Program.cs:190`, `ResendEmailSender`) — the pattern for the outbound content fetch and the
  founder-channel post.
- Options binding via `builder.Services.Configure<T>(Configuration.GetSection("…"))`; `appsettings.json`
  section-per-integration (`Resend`, `Sentry`, `App`, `R2`).
- **R2 object storage already wired (ADR-471):** `IBusinessDocumentStorage`, `R2Settings`,
  dev local-disk fallback (`Program.cs:~201`). No feature consumes it yet.
- `OpHaloDbContext` (Foundation) for any Foundation persistence; `ApplyMigrationsOnStartup:false`.
- Frontend: `App.tsx` hash router (`getRouteFromLocation()` at `App.tsx:61`, `AppRoute` union at
  `App.tsx:~35`), typed `api` object + `apiFetch`/`apiFetchVoid` in `src/lib/apiClient.ts:419`.
- Tokens in `src/styles/app.css`: `--ophalo-accent:#bf6b43` (:26), `--ophalo-attention:#c8741a`
  (:36), `--ophalo-attention-bg:#fff2d7` (:37). `check:tokens` build gate forbids new literals.
- `MobileNavMenu.tsx`, `RequestListContent` region, account-menu shell (GAP-054 slice 054-1,
  pending commit).

Confirmed **absent** (this build-log must resolve or route around):

- No markdown renderer or sanitiser dependency in `web/ophalo-app/package.json`.
- No Content-Security-Policy anywhere — not in `index.html`, `vite.config.ts`, and there is no
  `vercel.json`. The frontend deploys on Vercel (`scripts/resolveDeployment.ts`).
- No founder-channel webhook / Slack / Discord integration in the backend.
- No `feedback` table, entity, or migration.

## Open decisions

### D1 — Remote content source  → **Recommend: reuse the ADR-471 R2 bucket**

Store `updates.json` at a fixed key (proposed `platform/updates.json`) in the existing R2 bucket.
The proxy reads it through `IBusinessDocumentStorage` (or a thin dedicated `IUpdatesContentSource`
over the same `R2Settings` if we want read-only isolation from the business-document surface).
Founder publishes with `wrangler r2 object put` / the Cloudflare dashboard — no deploy.

- *Why:* zero new infra, zero new secret, credentials already provisioned, dev fallback already
  exists. A raw GitHub/gist URL would add a second trust origin and a second failure mode.
- *Tradeoff:* founder edits JSON via CLI/dashboard rather than a git PR (no review gate on content —
  acceptable, matches ADR-294 "no CMS" and the <60s publish goal).
- *Fork for Christian:* separate read-only `IUpdatesContentSource` vs. reuse `IBusinessDocumentStorage`
  directly. Recommend the dedicated interface (keeps "no feature consumes business docs yet" true
  and gives the proxy its own bounded surface).

R2 availability removes infra procurement but **not** the need for a content-publication contract —
see the "Content-publication contract" section below (key convention, publish authority, schema
versioning, cache invalidation, rollback, and the unavailable-content UX).

### D2 — Guide image hosting, asset origin, CSP  → **Recommend: proxy images through the backend**

ADR-500 §4 assumes a CSP baseline exists ("add one `img-src` entry"); it does not. Two paths:

- **(A, recommended) Serve guide images through `GET /updates/guides/img/{*path}`** — backend streams
  the object from the same R2 store, same auth as the workbench. No cross-origin request. The
  proxy is **not** a generic fetcher: it only resolves keys under the fixed prefix
  `platform/updates/guides/img/`, serves only allowlisted image MIME types
  (`image/png`, `image/jpeg`, `image/webp`), enforces an object-size cap (proposed 2 MB), sets
  `Content-Type` from the allowlist (never from the object metadata) plus
  `Content-Disposition: inline` and `Cache-Control: private, max-age=86400`, and never takes an
  upstream URL from the
  JSON — the renderer emits only `/updates/guides/img/<name>` relative URLs and anything else is
  dropped. Exfil / tracking-pixel boundary is held server-side.
- **(B) Direct R2 public URL + first app CSP** — introduces `vercel.json` with a full CSP
  (`default-src`, `script-src`, `img-src` incl. the R2 origin, `frame-ancestors 'none'`, …). Larger
  blast radius (every asset must be enumerated), a new artefact to maintain, and it lands a
  security-sensitive header set inside a not-a-pilot-gate slice.

**Decision: (A), proxy.** Not because the proxy removes the need for a CSP, but because a proper
baseline CSP is deliberate platform-hardening work that should not be rushed under GAP-038's
delivery pressure. Wording for the ADR/DEF trail: *"App-wide CSP baseline intentionally deferred to
a dedicated hardening ticket; this slice prevents arbitrary external image origins by serving all
guide images through a constrained backend proxy."* File the deferred CSP as a DEF entry.

### D3 — Markdown renderer  → **Recommend: `snarkdown` + `dompurify`**

Nothing in the tree today. The subset is tiny (paragraphs, bold, ordered/unordered lists, links,
guide images only). Options:

- **(recommended) `snarkdown` (~1 KB) → `DOMPurify`.** Remote Markdown is **untrusted input**;
  sanitise the HTML string *before* it is inserted into the DOM. DOMPurify config:
  - `ALLOWED_TAGS`: `p, strong, em, ul, ol, li, a, img, br` only.
  - `ALLOWED_ATTR`: `href` (on `a`), `src` + `alt` (on `img`) only. No `style`, no `class`, no
    `id`, no `on*`, no `target`, no `data-*`.
  - `FORBID_TAGS`: `style, script, iframe, object, embed, form`. No raw HTML passthrough
    (`snarkdown` does not emit it; DOMPurify is the backstop).
  - URL schemes: `a[href]` must be `https:` or a relative in-app route; `img[src]` must match
    `^/updates/guides/img/[A-Za-z0-9._-]+$`. Anything else → attribute dropped.
  - Every rendered `a` gets `rel="noopener noreferrer"` and (only if we later allow `target`)
    would need `target="_blank"` added explicitly; v1 opens in-place.
- `markdown-it` (`html:false`) + `DOMPurify` — ~40 KB, more spec coverage we do not want.
- Hand-rolled line renderer — no dependency, but re-implements list nesting and link parsing; more
  risk than a 1 KB library.

*Fork:* accept two new frontend deps (`snarkdown`, `dompurify`) — both widely used, MIT. Approved
conditionally on the sanitiser rules above being implemented and covered by XSS-payload tests.

### D4 — Content-fetch timeout & caching  → **Decision**

Server-side `HttpClient.Timeout = 5s` (or R2 SDK equivalent). Parse + schema-validate, cache the
parsed payload in-process (`IMemoryCache`) for **5 min** with a matching `Cache-Control: private,
max-age=300` on the response. On timeout / fetch error / parse error / schema-validation failure:
serve the last valid payload held in a separate no-TTL slot; empty feed if none was ever held.

**The last-known-good slot is best-effort and per-instance.** It is an `IMemoryCache` entry, not
shared storage — each running app instance holds its own, and a cold instance (deploy, restart,
scale-out) that hits a broken content source before it ever cached a good payload serves an **empty
feed**, not stale content. This is acceptable for a not-a-pilot-gate surface. Cross-instance LKG
durability (backing the slot with R2 or a DB row) is explicitly **not v1** — note it as possible
future hardening, do not describe the current behaviour as a durability guarantee.

Content-source failure in **038-1a** is recorded via structured logging / Sentry only. The
founder-channel alert on repeated content failure lands with **038-2**, when `IFounderNotifier`
ships (see D8) — deferring the alert mechanism one slice does not weaken ADR-500 §1, which is about
*what the user sees* (never a blanked surface), and that is fully held by the LKG slot.

### D5 — Feedback persistence, delivery, retry, retention  → **Decision: minimal `feedback` table, persist-first**

Foundation entity on `OpHaloDbContext`. Strict non-null schema, no backfill (per
[[feedback_no_fabricated_history_strict_schema]] — no prod rows exist):

| column | type | notes |
|---|---|---|
| `id` | uuid PK | doubles as the correlation id in alerts |
| `account_id`, `account_user_id` | uuid | who submitted |
| `message` | text? | the feedback body — **minimised on confirmed delivery** (set null), see retention |
| `category` | text | enum-constrained: `bug/confusing/missing_thing/too_slow/other` |
| `context_json` | jsonb? | route, request id, app build, platform, client timestamp — **no PII**; also nulled on delivery |
| `created_at` | timestamptz | |
| `delivery_state` | text | `pending / delivered / abandoned` |
| `attempt_count` | int | |
| `last_attempt_at` | timestamptz? | |
| `delivered_at` | timestamptz? | |

- **Write path — persist-first, at-least-once delivery:**
  1. Validate the request (reject blank / oversized `message`, unknown `category`).
  2. Persist a `pending` row with a generated delivery id (= `id`) and **commit it** before any
     delivery attempt.
  3. Optionally attempt one immediate synchronous delivery to the founder channel, carrying the
     delivery id in the payload/header so the receiver can dedupe.
  4. On **confirmed** success: set `delivery_state = delivered`, `delivered_at = now`, and **null
     `message` + `context_json` immediately** — raw feedback lives in Postgres only while delivery
     is unresolved, then is scrubbed.
  5. On failure or an ambiguous result (timeout after the receiver may have accepted): leave the
     full `pending` row for the retry worker.
  6. Retry worker re-sends with the same delivery id — **at-least-once**, so a notification may be
     duplicated; the receiver dedupes on the id.
  - *Why persist-first over try-first:* a synchronous webhook can time out *after* the receiver
    accepted, and a subsequent DB write can fail — try-first then cannot tell delivered / lost /
    will-duplicate apart, weakening ADR-500 §5's "nothing dropped". Persist-first is durable and
    still minimised (scrub-on-success), at the cost of a millisecond-scale window where the raw
    body sits in Postgres on the happy path.
- **Response behaviour:**
  - Persistence fails **before** any delivery attempt → **`503`**; the webhook is not called; the
    client may safely retry. This is the only case that is not a "sent".
  - Persistence succeeds, immediate delivery fails → **`202`**; safely queued.
  - Delivery succeeds but the scrub / metadata update fails → keep the `pending` row, retry later;
    may duplicate the notification (at-least-once, stated above).
- **Retry schedule (explicit):** the `pending` row carries a `next_attempt_at`. A hosted
  `BackgroundService` wakes every 60 s and processes rows whose `next_attempt_at` has passed.
  Backoff after each failed attempt: **1 min → 5 min → 15 min → 60 min → 180 min** (5 retries, 6
  deliveries total counting the synchronous one), spanning ~4 h. After the last failure →
  `delivery_state = abandoned`; the body is **kept** (recovery), subject to the 30-day sweep.
- **Backlog alert (explicit trigger):** the sweep posts one founder-channel alert when
  `count(state = pending AND created_at < now - 15 min) ≥ 3`, **or** immediately when any row
  transitions to `abandoned`. Rate-limited to at most one alert per 30 min. The alert payload
  carries **no feedback text** — only: environment, `failure_type` (`delivery_backlog` /
  `delivery_abandoned`), pending count, oldest-pending age, and the correlation id(s).
- **Retention/deletion:**
  - `delivered` rows (metadata only, body already nulled): hard-delete after **7 days**.
  - `abandoned` rows (body retained for recovery): hard-delete after **30 days**. This 30-day
    retention of undelivered feedback content must be reflected in the product privacy posture
    (add a line to the pilot privacy/notice copy — flag for Christian).
  - Nothing here is customer data; no data-subject erasure hook.
- This is **not** a ticket dashboard: no status/resolve/assignee/SLA columns, no operator UI, no
  list endpoint. Matches ADR-500 §5 / ADR-293.

*Founder channel — introduced in 038-2, not 038-1a.* One **generic** `IFounderNotifier` — a typed
`HttpClient` that POSTs a compact JSON body to a single incoming-webhook URL from a new
`FounderChannel:WebhookUrl` config key. **Not Slack- or Discord-shaped**: the abstraction takes a
structured event (`environment`, `type`, `summary`, `count`, `oldest_age`, `correlation`), and a
thin formatter renders it to the generic webhook body. Consumers: `POST /feedback` delivery, the
D5 backlog/abandoned alert, and (retrofitted into the D4 path once it exists) the content-source
failure alert. Fail-soft everywhere — a notifier error never faults a request and never blocks the
sweep. The webhook URL is founder-provisioned like the Sentry DSN (BL140 pattern); which service
sits behind it is a founder operational choice, not a code dependency.

### D6 — Exact visual values  → **Proposed (per "propose visual values first")**

- **Trigger dot:** 6 px circle, `background: var(--ophalo-accent)`, positioned `top: -1px; right:
  -1px` on the account-menu trigger (desktop) and the `MobileNavMenu` trigger; `aria-hidden`, with
  " (updates available)" appended to the trigger's accessible name when unseen > 0.
- **Row suffix:** ` · {N} new` appended to the "Help & Updates" label, `font-size: 0.8125rem`,
  existing muted-text token (confirm token name in the GAP-054 menu component), not a badge pill.
- **Banner:** full-width inside `RequestListContent`, `background: var(--ophalo-attention-bg)`,
  `1px solid var(--ophalo-attention)`, text `var(--ophalo-attention)`, dismiss "×" right-aligned,
  `role="status"`. "N more updates →" link to `#/help` when >1 undismissed highlight.
- **Guide image frame:** `max-width: 480px`, `1px solid` neutral border token, 8 px padding, same
  in both themes; `loading="lazy"`; caption = markdown `alt` rendered below in muted text.

### D7 — Non-help friction entry points  → **Recommend: two, both in the shell**

1. `#/help` page header — "Report a problem" (ADR-500 §4/§5).
2. Account menu + `MobileNavMenu` — a "Send feedback" row (all roles), opens the same lightweight
   sheet/dialog as (1).

**Decision: two entry points for v1, both in the shell.** Per-surface in-workflow entry points
(request-detail overflow, capture surfaces) are deferred until actual feedback data shows contextual
reporting is needed — they need per-surface context wiring and design and are not required for the
loop to function.

### D8 — Slice split (CLAUDE.md batch gate)

The full feature exceeds the gate (2 endpoints + BackgroundService + entity + migration + renderer +
3 frontend surfaces + page). The read-only content loop as one slice is **itself over the gate** —
proxy + image proxy + source interface + schema validator + content types on the backend, then feed
hook + watermark + dot + menu row + `#/help` page + renderer + banner on the frontend. **Decision:
three slices.** No founder-channel / feedback infrastructure enters 038-1a or 038-1b — it is entirely
a 038-2 concern.

- **Slice 038-1a — content backend (no outbound integration):** `GET /updates` proxy +
  `GET /updates/guides/img/<name>` image proxy + `IUpdatesContentSource` (R2 read) + `updates.json`
  JSON-schema validator + best-effort per-instance last-known-good cache + content DTOs. Content-
  source failures → structured log / Sentry only, **no `IFounderNotifier`**. No frontend, no
  persistence, no migration. Verified by proxy / validation / last-known-good / image-rejection
  tests against a fake content source. Ships behind existing auth with no UI consuming it yet —
  acceptable, it is a read endpoint, not a started-but-unconsumed external dependency
  ([[feedback_no_startup_crash_for_unconsumed_dep]] does not apply; nothing faults startup).
- **Slice 038-1b — content frontend:** feed fetch hook + `keep_updates_watermark` +
  `--ophalo-accent` trigger dot (desktop + `MobileNavMenu`) + "Help & Updates" row + `#/help` page +
  `snarkdown`+`DOMPurify` renderer + `RequestListContent` banner + empty/stale/error states. Depends
  on 038-1a being merged.
- **Slice 038-2 — feedback submission through operational completion:** `POST /feedback` +
  `feedback` entity + migration + rate limiting + persist-first at-least-once delivery (D5) +
  generic `IFounderNotifier` + retry `BackgroundService` with the D5 backoff schedule +
  backlog/abandoned alert + retention sweep; "Report a problem" (`#/help` header) and "Send
  feedback" (account menu) entry points + submission dialog. The D4 content-source failure alert is
  retrofitted onto the notifier here.

Order: 038-1a → 038-1b (announcements live before the heavy-change month) → 038-2 immediately after.
Each slice compiles and is independently shippable; each gets its own preflight + file-list gate in
this build-log before it starts. If 038-2 is still over the gate once its file fan-out is counted,
it splits into (endpoint + entity + migration + delivery) and (retry service + alert + sweep +
entry points).

## Content-publication contract

R2 is available, so there is no infra to procure — but the founder-maintained content still needs a
governed publish path. R2 has **no restorable S3-style object versioning** (the `wrangler` version
value is upload metadata, not a rollback feature), so the repository is the canonical source and
audit trail. This slice fixes:

- **Canonical source.** `docs/content/updates.json` is the authoritative feed document, checked into
  the repo. Guide image sources live under `docs/content/guides/img/`. R2 holds a published *copy*;
  the repo is the source of truth and the change history.
- **Object keys.** `platform/updates.json` for the feed document; guide images under
  `platform/updates/guides/img/<name>` where `<name>` matches
  `^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\.(png|jpe?g|webp)$`. The proxy resolves nothing outside these
  prefixes.
- **Immutable, content-addressed guide asset names.** A guide image name embeds a short content hash
  (e.g. `record-actual-work-a1b2c3d4.png`) and is **never overwritten**. Editing a guide image means
  publishing a *new* filename and repointing the guide body at it; the old object may be deleted once
  no committed `updates.json` references it. This is what makes the one-day image cache
  (`max-age=86400`) safe.
- **Publish authority.** The founder only, and may commit directly to the repo (no PR gate on
  content — matches ADR-294). Publishing is a small repo target (`make publish-updates` or
  equivalent) that: (1) validates `docs/content/updates.json` against `updates.schema.json` with a
  real **JSON-Schema validator** (not `jq`, which only checks JSON syntax); (2) fails the publish on
  any schema error; (3) uploads the validated feed to `platform/updates.json` and uploads every
  referenced guide image that is not already present in R2 (`wrangler r2 object put`). The R2 write
  credential is founder-held and separate from the app's read path.
- **Schema + versioning.** `updates.json` carries a top-level `"schema": 1`. The proxy validates
  against the JSON-schema for that version; an unknown/absent `schema` → treated as a validation
  failure → last-known-good served + alert. Schema changes are additive (ADR-501 §3); a breaking
  content-shape change bumps `schema` and ships a proxy that accepts both during the transition.
- **Cache invalidation.** In-process cache, 5-min TTL (D4); a publish is visible within ≤5 min with
  no deploy and no manual bust. (Optional future: a `POST /updates/refresh` founder-only cache-bust
  — not v1.)
- **Rollback.** `git checkout` the prior `docs/content/updates.json` and re-run the publish target.
  Because guide asset names are immutable and content-addressed, the older feed's images are still
  in R2 — no image restore is needed. Independently, an instance that *has* a good payload cached
  keeps serving it across a malformed publish (best-effort — a cold instance in that window serves
  empty, see D4).
- **Content unavailable UX.** No instance has ever held a valid payload → `GET /updates` returns an
  empty feed (`entries: [], guides: []`); the `#/help` page renders a calm "No updates yet" empty
  state per section, the trigger dot is absent, no banner. Instance holds a stale last-known-good →
  serve it silently (users see slightly old content, never an error); failure recorded via
  structured log (founder-channel alert once 038-2's notifier exists). Client-side feed fetch
  failing entirely (network) → `#/help` shows a non-alarming "Couldn't load updates, try again"
  with a retry; the rest of the app is unaffected.

## Ready-to-build exit criteria

Resolved in this doc (pending Christian's sign-off on the doc as a whole):

- [x] D1 — R2 content source; dedicated read-only `IUpdatesContentSource`; key `platform/updates.json`.
- [x] D2 — backend image proxy (option A); CSP baseline deferred to its own DEF/hardening ticket.
- [x] D3 — `snarkdown` + `dompurify` with the fixed allowlist + URL-scheme rules above.
- [x] D4 — 5 s fetch timeout, 5-min in-process cache, best-effort per-instance last-known-good slot
      (explicitly not a durability guarantee); no notifier in 038-1a.
- [x] D5 — **persist-first, at-least-once**, scrub-on-confirmed-success; delivery id for dedup;
      `503` pre-delivery-persist-fail / `202` queued / keep-pending on scrub failure; backoff
      1/5/15/60/180 min; alert triggers carry no body; 7-day metadata / 30-day undelivered-body.
- [x] D6 — visual values proposed (subject to token-name confirmation in the GAP-054 menu component).
- [x] D7 — two shell entry points; in-workflow entry points deferred.
- [x] D8 — three slices: 038-1a content backend → 038-1b content frontend → 038-2 feedback path;
      no founder-channel / feedback infra in 1a or 1b.
- [x] Founder-channel notifier is a generic webhook abstraction; compact non-sensitive payload; 038-2 only.

### Required before 038-1a

#### Readiness package — Christian signed off 2026-09-08 (with four recorded corrections)

The canonical schema is [updates.schema.json](../contracts/updates.schema.json). 038-1a embeds that
exact artifact in the API so the validator cannot drift from the reviewed document. Sign-off carries
four corrections, all folded into the text below:

1. **(critical)** image-name regex is `^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\.(png|jpe?g|webp)$` —
   literal `\.`, not `\\.`.
2. **(critical)** `format: "date-time"` is enforced as a validation **assertion**, not annotation.
3. **(high)** unconfigured-R2 (`Development`/test) yields the empty-feed fallback, never a DI `500`.
4. **(high)** post-schema semantic check rejects duplicate `entries[].id` / `guides[].id`.

Plus one new package approved: `JsonSchema.Net` on `OpHalo.Api.csproj` (see the file gate).

**Non-blocking pressure points, carried into their slices:**

- *Guide image path syntax.* Source markdown uses `guides/img/foo.png`; the renderer contract
  permits `/updates/guides/img/foo.png`. **038-1b** must explicitly transform only the former into
  the latter (or the source syntax is standardized then). Not an 038-1a concern.
- *Founder write credential vs. app read credential.* Existing R2 setup appears read/write-capable.
  Code may proceed against the current credential, but production publishing must use a separate
  founder-held write credential, not the API's read credential — operational verification owed
  before the publish target ships (founder tooling, tracked separately).

**Authorization.** Both Foundation-owned, flat routes use the existing authenticated-app
`RequireAuthorization()` boundary. There is **no role, capability, or account-membership scope**
beyond successful authentication: the feed contains no account-scoped data and is identical for all
authenticated users. Anonymous callers receive `401`; a future global policy may return its normal
`403`. Neither route is mounted on a public customer/tracker host or called by a public page.

**`GET /updates`.** No request body, path parameters, or query parameters. `200 OK` returns JSON
with `Content-Type: application/json; charset=utf-8` and `Cache-Control: private, max-age=300`:

```json
{
  "schema": 1,
  "entries": [{
    "id": "upd-2026-09-07-01", "published_at": "2026-09-07T12:00:00Z",
    "section": "known_issue", "status": "active", "title": "Customer text messages delayed",
    "body": "Some carriers are queuing messages.", "highlight": true,
    "banner_until": "2026-09-30T00:00:00Z"
  }],
  "guides": [{
    "id": "guide-log-visit", "updated_at": "2026-09-06T16:12:00Z",
    "title": "Log a completed visit", "body": "1. Open the request."
  }]
}
```

Source defaults are `status: "active"` and `highlight: false`; omitted `banner_until` stays absent.
The only normal outcomes are `200` and `401`. A timeout, missing object, R2 error, malformed JSON,
unknown schema, or schema failure never leaks `404`/`422`/`502`/`503`: return the per-instance
last-known-good payload, or `{ "schema": 1, "entries": [], "guides": [] }` if none exists. An
otherwise unhandled API fault uses the standard `500` problem response.

**`GET /updates/guides/img/<name>`.** `<name>` is one decoded filename matching
`^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\.(png|jpe?g|webp)$`. Slashes, backslashes, dot-only names,
encoded traversal, arbitrary extensions, and query-controlled keys are rejected. A valid name maps
only to `platform/updates/guides/img/<name>`; the endpoint never follows a feed URL or another R2
prefix. No request body or query parameters are accepted. `200` streams a compliant object with a
forced MIME type (`.png` → `image/png`; `.jpg`/`.jpeg` → `image/jpeg`; `.webp` → `image/webp`),
`Content-Disposition: inline`, `X-Content-Type-Options: nosniff`, and
`Cache-Control: private, max-age=86400`.

The exact cap is **2,097,152 bytes (2 MiB)**, enforced before streaming when object length is known
and while copying when it is not. The allowed MIME set is exactly **`image/png`, `image/jpeg`, and
`image/webp`**; extension and stored MIME must agree. Outcomes: `401` unauthenticated; `404` for an
invalid name or missing object (not distinguished); `413` over cap; `415` missing/disallowed/mismatched
MIME; `503` R2/provider read failure; standard `500` for an unhandled fault. Images have no
last-known-good fallback.

**Failure, cache, and LKG.** Source reads have a cancellation-aware 5-second timeout. A valid parsed
and schema-validated feed populates both a 5-minute per-instance fresh cache and a separate
per-instance LKG slot. Fresh hits do not read R2; a later valid read replaces both slots. Any
read/parse/validation failure retains LKG until a later valid read replaces it. It is neither shared
nor durable: a cold/restarted/scaled-out instance without LKG returns the empty schema-1 feed.
038-1a logs these failures to structured logs/Sentry only; notifier work is 038-2.

**Validation — required configuration and semantic checks (Christian sign-off 2026-09-08).**

- *Validator.* `JsonSchema.Net` (MIT, Draft 2020-12) is added to `src/OpHalo.Api/OpHalo.Api.csproj`
  — the project file is already inside the 038-1a gate; this is the **package gate amendment** the
  gate paragraph below now records. The embedded `updates.schema.json` artifact is validated as-is
  so it cannot drift from the reviewed contract.
- *Date-time is an assertion, not an annotation.* Draft 2020-12 treats `format` as annotation-only
  unless format-assertion is explicitly enabled. Configure the validator so `format: "date-time"`
  **fails validation**. Test: an `entries[].published_at` (and a `guides[].updated_at`) that is not a
  valid RFC 3339 date-time is rejected → LKG fallback.
- *Duplicate IDs (JSON Schema cannot express this).* After schema validation, reject a feed with any
  duplicate value in `entries[].id` or in `guides[].id` (each list unique on its own). Duplicates
  would corrupt client-side banner dismissal and watermark state. Test both lists.
- *Unconfigured R2 (Development/test).* `IUpdatesContentSource` must **always** be resolvable. When
  R2 config is absent, register an unavailable-source implementation that yields the contracted
  empty-feed fallback path — never a DI activation `500`. It may live inside an already-gated
  production file (`R2UpdatesContentSource.cs` or `Program.cs` DI), so it does not expand the file
  count.

**Publishing and rollback.** The canonical feed is `docs/content/updates.json` in the repo; the
founder may commit it directly. A small publish target validates it against `updates.schema.json`
with a real JSON-Schema validator (not `jq`), then, only on success, uploads the feed to
`platform/updates.json` and every referenced-but-not-yet-present guide image to
`platform/updates/guides/img/<name>` with the separate founder-held write credential
(`wrangler r2 object put`). Guide asset names are immutable and content-addressed and are never
overwritten. A publish normally appears within five minutes per instance. Roll back by
`git checkout`-ing the prior `docs/content/updates.json` and re-running the publish target; the
older feed's images are still in R2 because names are immutable. R2 has no restorable object
versioning — the repo history *is* the rollback path, so no R2 bucket setting is a prerequisite.

**038-1a file gate.** `updates.schema.json` and a seed `docs/content/updates.json`
(`{ "schema": 1, "entries": [], "guides": [] }`) are pre-work artifacts and must land with this
reviewed documentation before implementation begins. The publish target
(`Makefile`/script + validator dependency) is founder tooling, tracked separately, and is **not**
part of the 038-1a implementation file count. Excluding the pre-work commit, implementation changes
exactly:

1. `src/OpHalo.Api/Updates/UpdatesEndpoints.cs`
2. `src/OpHalo.Api/Updates/UpdatesContracts.cs`
3. `src/OpHalo.Foundation.Application/Updates/IUpdatesContentSource.cs`
4. `src/OpHalo.Foundation.Infrastructure/Updates/R2UpdatesContentSource.cs`
5. `src/OpHalo.Api/Updates/UpdatesFeedCache.cs` (schema validation, cache, LKG)
6. `src/OpHalo.Api/Program.cs` (DI, `AddMemoryCache()`, endpoint mapping)
7. `src/OpHalo.Api/OpHalo.Api.csproj` (embed schema + `JsonSchema.Net` package reference)
8. `tests/OpHalo.IntegrationTests/Api/UpdatesEndpointsTests.cs`
9. `tests/OpHalo.UnitTests/Api/UpdatesFeedCacheTests.cs`

That is **7 production files, 2 test files, 9 total**, one read-handler family with its
constrained image alias: within the CLAUDE.md limit (8 production / 12 total). The fake
`IUpdatesContentSource` is a nested test-only class in `UpdatesEndpointsTests.cs`, not a separate
file.

**Package gate amendment (Christian sign-off 2026-09-08).** One new package — `JsonSchema.Net`
(MIT, Draft 2020-12) — is approved for `OpHalo.Api.csproj`, which is already in the list above; it
adds no tenth file. This is the only approved package addition. Any *further* new fake, fixture,
package, migration, frontend file, or separate test file requires re-splitting or substitution.

**038-1a tests.** An authenticated integration client with a fake `IUpdatesContentSource` covers
valid proxy/cache headers, anonymous `401`, schema-invalid/unknown-schema LKG fallback, cold empty
fallback, and successful forced image MIME/security headers. Focused cache tests cover the five-minute
fresh cache and LKG replacement. Image cases assert invalid/prefix-escape name → `404`, missing →
`404`, 2,097,153 bytes → `413`, missing/disallowed/mismatched MIME → `415`, provider failure →
`503`, and that the source receives only the fixed prefix—not a client-controlled URL. Validation
tests additionally cover: invalid `published_at` / `updated_at` date-time → rejected (format
assertion enabled); duplicate `entries[].id` → rejected; duplicate `guides[].id` → rejected;
unconfigured-R2 source resolves and returns the empty feed (no DI `500`).

- [x] Rollback path settled: repo is canonical (`docs/content/updates.json`), rollback is
      `git checkout` + re-publish. R2 has no restorable object versioning; **no R2 bucket setting is
      a prerequisite** (founder task removed).
- [x] `GET /updates` and `GET /updates/guides/img/<name>` full request/response contracts + status
      codes written out here.
- [x] `updates.json` JSON-schema (`schema: 1`) + seed `docs/content/updates.json` written and
      committed; guide asset names immutable + content-addressed; publishing rules recorded. The
      founder publish target validates with a real JSON-Schema validator and is tracked separately.
- [x] R2 failure / cache / last-known-good behaviour confirmed (incl. cold-instance empty feed).
- [x] Authz (authenticated-shell only), image size cap, allowed image MIME list — exact numbers.
- [x] 038-1a changed-file list enumerated and checked against the CLAUDE.md batch gate.
- [x] 038-1a test plan: proxy / schema-validation / last-known-good / image prefix+MIME+size
      rejection.

### 038-1a completion record (landed 2026-09-08)

Implemented exactly to the file gate above — **7 production + 2 test files, 9 total, no extra
fake/fixture/package/migration/frontend file.** The one approved dependency,
**`JsonSchema.Net` 9.4.0** (MIT, Draft 2020-12), was added to `src/OpHalo.Api/OpHalo.Api.csproj`;
it is the only package added and it added no tenth file. `updates.schema.json` is embedded from
`docs/contracts/` via a linked `EmbeddedResource` with a pinned `LogicalName`, validated as-is.

- **Seam** (`IUpdatesContentSource`, Application): domain scalars only; feed and image reads each
  take an explicit byte cap; always registered (real R2 adapter when `R2Settings.IsConfigured`,
  `UnavailableUpdatesContentSource` otherwise — no `IsDevelopment` gate, so an unconfigured host
  yields the contracted empty-feed / `503` path, never a DI `500`).
- **R2 adapter** (Infrastructure): fixed keys `platform/updates.json` and
  `platform/updates/guides/img/<name>` only; 5-second cancellation-aware timeout (caller
  cancellation still propagates); capped-copy on both reads — an over-cap feed object is a read
  failure → LKG/empty. Feed cap is 4 MiB (`UpdatesFeedCache.MaxFeedBytes`); image cap is the
  contracted 2 MiB.
- **Feed cache** (`UpdatesFeedCache`, Api): validate-then-deserialize; `RequireFormatValidation`
  makes a bad `date-time` fail; post-schema duplicate-`id` rejection on each list; `IClock`-driven
  5-minute fresh slot in `IMemoryCache` + a separate per-instance LKG string; `SemaphoreSlim`
  stampede guard; `Reset()` for test isolation / a future founder cache-bust. Failures logged only
  (notifier is 038-2).
- **Endpoints** (Api): both flat routes behind `RequireAuthorization()`, no further scope;
  documented guard precedence (regex `404` → missing `404` → unavailable/failed `503` → over-cap
  `413` → MIME disagree `415` → `200`); forced MIME by extension, `Content-Disposition: inline`,
  `X-Content-Type-Options: nosniff`, `Cache-Control` (`max-age=300` feed / `max-age=86400` image;
  Kestrel serialises the parsed header as `max-age=N, private` — directive order is not
  RFC-significant).
- **Tests:** 14 `UpdatesFeedCacheTests` (fresh cache, TTL expiry, LKG replacement, cold empty,
  malformed/schema-invalid/unknown-schema, date-time format assertion ×3, duplicate ids ×2, feed
  over cap); 18 `UpdatesEndpointsTests` (contract headers + defaults, anon `401` ×2, unknown-schema
  and cold-unavailable empty feed, feed over cap, image forced-MIME + security headers + bare-name
  assertion, bad-name `404` ×5, missing `404`, over-cap `413`, MIME missing/mismatched `415` ×3,
  provider `503`). Full unit (1847) + architecture (14) + full integration (1652, pre-cap; Updates
  slice re-verified at 18/18 after the cap change) green.

### 038-1b prework — resolved 2026-09-08 (Christian sign-off)

- [x] Muted-text token confirmed: **`--ophalo-muted`** (`#5d6878`), the token every muted line in
      `AccountMenu.tsx` already uses; defined in `src/styles/app.css` and
      `web/shared/styles/ophalo-tokens.css` (in sync). The row suffix `· N new` is
      `text-[var(--ophalo-muted)]` at `text-[0.8125rem]`. No new token — `--ophalo-accent`,
      `--ophalo-attention`, `--ophalo-attention-bg`, `--ophalo-border` all already defined + synced,
      so `check:tokens` stays green. The app is single-theme (no dark blocks), so the guide-image
      "constant frame in both themes" note is automatically satisfied.
- [x] **038-1b is over the CLAUDE.md batch gate** (12 production files + 2 deps) and splits into two
      independently shippable slices: **038-1b-i** (content surface — the `#/help` page reachable by
      URL, no indicator/banner/feedback button) → **038-1b-ii** (menus + unread indicator + Requests
      banner). Order: 1b-i → 1b-ii back-to-back. Announcements are visible via `#/help` after 1b-i;
      users are nudged to it after 1b-ii. Both land well ahead of the heavy-change month.
- [x] **"Report a problem" / "Send feedback" entry points + the submission dialog stay in 038-2**
      (this doc, "Slice 038-2" and line ~259). The 038-1b-i Help page is **content-only**: sectioned
      scroll + feed last-updated time + per-entry dates, no header action. ADR-500 §4 prose reads as
      if the header action ships with the page; the build-log slice split is authoritative.

Repo facts (preflight 2026-09-08):

- Routing: `AppRoute` union + `getRouteFromLocation()` + `navigate()` in `App.tsx`; content routes
  render from the `route.page ===` ladder in `<main>` (`App.tsx:485–608`). `src/App.actualWorkRoute.test.ts`
  is the precedent for a route-parse test file.
- Data: `@tanstack/react-query` `useQuery` is the app pattern; `api` object + `apiFetch<T>` in
  `lib/apiClient.ts`, types in `apiClient.types.ts`.
- **No `localStorage` helper exists in `src`.** Watermark + dismissed-banner persistence is new code;
  it is **folded into `hooks/useUpdatesFeed.ts`** (not a separate module) to hold the 1b-i file count.
- Menus: `AccountMenu.tsx` (desktop) and `MobileNavMenu.tsx` (hamburger trigger `App.tsx:383` + modal
  list). The "Help & Updates" row is **all-roles** — a new always-present item, not a `sections` entry.
- Banner: `RequestListContent.tsx` is presentational with a fixed props contract, rendered only by
  `pages/Requests.tsx:647`. Insertion = a `banner?: React.ReactNode` slot prop filled by `Requests.tsx`.
- `snarkdown` + `dompurify` absent — 2 runtime deps (ADR-500 §4 locked). **Do not add
  `@types/dompurify`** — deprecated; DOMPurify ships its own declarations.
- Guide-image path transform (`guides/img/foo.png` → `/updates/guides/img/foo.png`, drop everything
  else) is 038-1b-i's job (BL149 non-blocking pressure point).
- **No frontend-observable "stale last-known-good" state:** `GET /updates` deliberately returns the
  same successful shape for fresh and LKG content. 1b-i tests normal successful rendering only; LKG is
  a backend concern already covered in 038-1a.

#### 038-1b-i file gate — Help content surface

Source (6):

1. `src/lib/apiClient.ts` — `getUpdates()` → `GET /updates`
2. `src/lib/apiClient.types.ts` — `UpdatesFeed` / `UpdateEntry` / `UpdateGuide` DTOs
3. `src/hooks/useUpdatesFeed.ts` — react-query fetch + section grouping + feed last-updated +
   `keep_updates_watermark` read/write (localStorage, try/catch, absent-key safe); accepts an
   injected `now` for deterministic `published_at` comparisons
4. `src/components/updates/UpdatesMarkdown.tsx` — `snarkdown` + `DOMPurify` hard allowlist (tags
   `p strong em ul ol li a img br`; attrs `href src alt`), `javascript:`/`data:` href drop, guide
   `![alt](guides/img/x)` → `/updates/guides/img/x` rewrite + non-empty-`alt` requirement +
   `loading="lazy"` + capped max-width + neutral frame, **plus a real `error`-event handler
   (event delegation) that hides a failed `<img>`** — sanitized `dangerouslySetInnerHTML` alone
   cannot degrade a broken image
5. `src/pages/Help.tsx` — single sectioned scroll (Known issues active→resolved → Updates → Coming
   soon → Guides), feed last-updated + per-entry dates, empty ("No updates yet" per section) and
   network-error ("Couldn't load updates, try again" + retry) states; opening the page moves the
   watermark to `feedMax`
6. `src/App.tsx` — `{ page: "help" }` in `AppRoute` + `getRouteFromLocation()` (`#/help`, no params)
   + `navigate()` + render `<Help/>` in the `<main>` ladder

Manifests (2): `web/ophalo-app/package.json`, `web/ophalo-app/pnpm-lock.yaml` (`snarkdown` +
`dompurify` only; no `@types/dompurify`).

Tests (exactly 4 — hard cap; total 6 + 2 + 4 = **12**, at the CLAUDE.md limit):

1. `src/hooks/__tests__/useUpdatesFeed.test.ts` — fetch success shape; section grouping + order;
   feed last-updated; watermark absent → all entries unseen; opening/`markSeen` sets watermark to
   `feedMax` and moves only past `entries` (guides never move it); corrupt/absent localStorage →
   treated as empty, no throw; injected `now` honored
2. `src/components/updates/__tests__/UpdatesMarkdown.test.tsx` — allowed tags survive; `<script>`
   `<iframe>` `onerror=` `<h1>` `<table>` raw HTML dropped/escaped; `javascript:`/`data:` hrefs
   dropped; `guides/img/x.png` → `/updates/guides/img/x.png` with `loading="lazy"`; non-`guides/img/`
   path and empty `alt` → image dropped; **`<img>` `error` event → image hidden, surrounding guide
   text intact**
3. `src/pages/__tests__/Help.test.tsx` — each section renders; network error → retry affordance,
   rest of page unaffected; cold empty feed → calm per-section empty state; mount moves the watermark
4. `src/App.helpRoute.test.ts` — `#/help` parses to `{ page: "help" }`; `navigate` pushes `#/help`;
   an unknown hash still falls back to `{ page: "requests" }`

**6 source + 2 manifest = 8 production files; 12 total.** No new fake/fixture. Any further new file
requires re-splitting.

#### 038-1b-ii — menus + unread indicator + Requests banner (gate re-run before coding)

Scope: extend `useUpdatesFeed.ts` (unseen count; banner-qualifying derivation — `highlight:true` AND
within lifetime [`known_issue` while `status:active`; `whats_new` ≤14d from `published_at`;
`banner_until` hard override] AND not in `keep_dismissed_banners`; **lifetime derivation takes an
injected/current `now` for deterministic 14-day / `banner_until` tests**); `components/updates/UpdatesBanner.tsx`;
trigger-dot (shared tiny component or inline ×2 — decide at preflight); `AccountMenu.tsx` +
`MobileNavMenu.tsx` (row + `· N new` suffix + `--ophalo-accent` dot + " (updates available)" on the
trigger accessible name); `App.tsx` wiring; `pages/Requests.tsx` + `components/requests/RequestListContent.tsx`
(`banner` slot prop). ~7–8 production files — **at the ceiling; re-run the mechanical file/test
fan-out gate before coding and split the banner from the menu-indicator if it counts over.**

1b-ii test plan: unseen count = `entries` with `published_at > watermark`; banner shows most-recent
qualifying entry + "N more updates →" `#/help` link when >1; dismiss "×" adds id to
`keep_dismissed_banners`, hides the banner, does **not** move the watermark; `role="status"`;
lifetime expiry hides without dismissal; no qualifying entries → nothing renders; row present for all
roles → navigates `#/help`; suffix + dot shown only when unseen > 0; zero unseen → no dot, no suffix.

### Required before 038-2

- [ ] D5 persistence/delivery decision signed off (persist-first as specified above).
- [ ] Webhook URL provisioned by the founder (like the Sentry DSN); `FounderChannel:WebhookUrl` added.
- [ ] `POST /feedback` full contract + size/rate limits — exact numbers.
- [ ] Privacy / retention disclosure line added for the 30-day undelivered-feedback window.
- [ ] `feedback` migration plan (strict non-null, no backfill) + EF startup-project check (ADR-049).
- [ ] 038-2 changed-file list enumerated and gate-checked; split further if fan-out is over.
- [ ] 038-2 test plan: validation, persist-then-deliver, `503`/`202` cases, retry-sweep + backoff,
      scrub-on-success, at-least-once dedup, retention sweep, backlog/abandoned alert (no body).

## Not in this build-log / this feature

Everything in ADR-500 §Out of scope. Also: the app-wide CSP baseline (deferred to a dedicated
hardening ticket — file a DEF entry), cross-instance / durable last-known-good storage for the
content proxy (per-instance best-effort is v1), the native-client min-version handshake (ADR-501 §4
/ ADR-236), in-workflow per-surface feedback entry points, an operator feedback UI / list endpoint,
and a `POST /updates/refresh` cache-bust.
