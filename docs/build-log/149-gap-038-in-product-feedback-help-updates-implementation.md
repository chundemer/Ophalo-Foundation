# BL149 — GAP-038: in-product feedback + Help & Updates loop — implementation build-log

**Status:** Discovery / spec — D1–D8 resolved (2026-09-07, incl. two review passes; D5 = persist-
first). Per-slice provisioning + contract items owed before each slice starts (see exit criteria).
Not yet approved to build.
**Date:** 2026-09-07
**Authority:** [ADR-500](../decisions/ADR-500-in-product-feedback-and-help-updates-loop.md) (full
end-to-end contract — Locked), [ADR-501](../decisions/ADR-501-api-route-and-compatibility-policy.md)
(flat Foundation-owned routes — Locked). This build-log only settles the six items ADR-500 §Consequences
defers to it, plus the ready-to-build exit criteria; it does not reopen anything locked.
**Sequencing:** follows the GAP-054 account-menu commit (ADR-499). Not a pilot gate.

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
  `Content-Disposition: inline` and a long `Cache-Control`, and never takes an upstream URL from the
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
governed publish path. This slice fixes:

- **Object keys.** `platform/updates.json` for the feed document; guide images under
  `platform/updates/guides/img/<name>` where `<name>` matches `[A-Za-z0-9._-]+` with an image
  extension. The proxy resolves nothing outside these prefixes.
- **Publish authority.** The founder only. Publishing = overwrite the object in R2 (Cloudflare
  dashboard or `wrangler r2 object put`). No CMS, no PR gate on content (matches ADR-294). The R2
  write credential is founder-held and separate from the app's read path.
- **Schema + versioning.** `updates.json` carries a top-level `"schema": 1`. The proxy validates
  against the JSON-schema for that version; an unknown/absent `schema` → treated as a validation
  failure → last-known-good served + alert. Schema changes are additive (ADR-501 §3); a breaking
  content-shape change bumps `schema` and ships a proxy that accepts both during the transition.
- **Cache invalidation.** In-process cache, 5-min TTL (D4); a publish is visible within ≤5 min with
  no deploy and no manual bust. (Optional future: a `POST /updates/refresh` founder-only cache-bust
  — not v1.)
- **Rollback.** R2 object versioning is enabled on the bucket (confirm — founder task); a bad
  publish is rolled back by restoring the previous object version. Independently, an instance that
  *has* a good payload cached keeps serving it across a malformed publish (best-effort — a cold
  instance in that window serves empty, see D4).
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

- [ ] R2 bucket object-versioning / rollback confirmed enabled (founder task).
- [ ] `GET /updates` and `GET /updates/guides/img/<name>` full request/response contracts + status
      codes written out here.
- [ ] `updates.json` JSON-schema (`schema: 1`) written and committed; publishing rules recorded.
- [ ] R2 failure / cache / last-known-good behaviour confirmed (incl. cold-instance empty feed).
- [ ] Authz (authenticated-shell only), image size cap, allowed image MIME list — exact numbers.
- [ ] 038-1a changed-file list enumerated and checked against the CLAUDE.md batch gate.
- [ ] 038-1a test plan: proxy / schema-validation / last-known-good / image prefix+MIME+size rejection.

### Required before 038-1b

- [ ] 038-1b changed-file list enumerated and gate-checked.
- [ ] GAP-054 menu component merged; muted-text token name confirmed for the row suffix.
- [ ] 038-1b test plan: watermark, banner lifetime/dismissal, empty/stale/error states, renderer
      sanitiser tests (XSS / disallowed-tag / bad-scheme payloads dropped).

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
