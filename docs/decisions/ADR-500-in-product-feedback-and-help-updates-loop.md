# ADR-500 — In-product feedback + Help & Updates loop (GAP-038)

**Status:** Locked
**Date:** 2026-09-07
**Context:** GAP-038; supersedes the "build later" posture of ADR-293 / ADR-294 (BL044) and
consolidates the full end-to-end contract into one document.
**Related:** ADR-293 (Friction Flash intake), ADR-294 (Pilot Updates page), ADR-499 (account menu —
reserves the Help & Updates entry), ADR-495 (Sentry telemetry boundary — same PII discipline),
ADR-236 (per-user product settings attach to `AccountUser`, not identity), DEF-072, DEF-073

## Decision

GAP-038 ships one authenticated pilot-support loop with **zero database impact on the read/awareness
path** — no schema change for content or per-user read state, no `AccountUser` column, no migration.
It has five parts: a content feed, a passive awareness model, an active banner, the Help & Updates
page (including guides with screenshots), and the friction submission path.

### 1. Content feed — remote document via an authenticated proxy route

- Frontend calls **`GET /api/v1/updates`** (authenticated; same auth as the rest of the workbench).
- The backend fetches a single founder-maintained JSON document from a **remote object source**
  (object store or raw file URL — exact source chosen in the implementation build-log), **validates
  it against the schema**, caches the parsed result for **≈5 minutes**, and returns it with a
  matching client cache header.
- **Fail-soft with last-known-good:** on a fetch error, a parse error, or a schema-validation
  failure, the route serves the **last valid payload it held** (empty feed only if it has never
  held one) and posts a compact alert to the founder channel. One malformed edit never blanks the
  surface for pilot users.
- The founder edits one JSON file (and, for guides, uploads images to the same store) and publishes
  — including an urgent Known Issue — with **no code deployment**.
- No CMS, admin editor, feature voting, public status page, or roadmap portal (ADR-294 holds).
- The route and every surface below are **mounted only inside the authenticated app shell**. Public
  customer request/tracking pages never call it and never render any of it.

**Payload schema:**

```json
{
  "entries": [
    {
      "id": "upd-2026-09-07-01",
      "published_at": "2026-09-07T12:00:00Z",
      "section": "known_issue",
      "status": "active",
      "title": "Customer text messages delayed 3-5 minutes",
      "body": "Some carriers are queuing messages. **Workaround:** call the customer for anything time-critical.",
      "highlight": true,
      "banner_until": "2026-09-30T00:00:00Z"
    },
    {
      "id": "upd-2026-09-05-01",
      "published_at": "2026-09-05T09:30:00Z",
      "section": "whats_new",
      "title": "Clearer needs-attention states on Requests",
      "body": "Overdue and customer-waiting requests now stand out without opening each one.",
      "highlight": false
    }
  ],
  "guides": [
    {
      "id": "guide-log-visit",
      "updated_at": "2026-09-06T16:12:00Z",
      "title": "Log a completed visit from the field",
      "body": "1. Open the request, tap **Record actual work**.\n2. Add each line and who performed it.\n\n![Record actual work button](guides/img/record-actual-work.png)\n\n3. Add a completion note, then **Submit for review**."
    }
  ]
}
```

- **`entries[]`** — dated, stream-like items:
  - `section` ∈ `known_issue | whats_new | coming_soon`. An unknown value renders under a neutral
    fallback heading, never crashes.
  - `status` ∈ `active | resolved` (default `active`). Meaningful for `known_issue`; a `resolved`
    known issue renders in a muted "Recently resolved" group and **auto-hides 7 days after its
    latest `published_at`** (or when the founder prunes it, whichever is first).
  - `highlight` (default `false`) is the founder's "make this loud" lever — see §3.
  - `banner_until` (optional ISO date) overrides the default banner lifetime in §3.
  - `body` is plain operator language, short, rendered with the **light markdown subset** in §4.
- **`guides[]`** — stable reference content, keyed by **`updated_at`** (not news-dated): `id`,
  `title`, `body` (light markdown subset, §4, including inline images).
- **No `latest_date` field.** The watermark is computed client-side (§2) so a backdated or corrected
  edit cannot hide or re-surface items incorrectly.
- No customer or request data may ever appear in any `body`, `title`, or image.

### 2. Passive awareness — single client-computed watermark

- One localStorage key: **`keep_updates_watermark`** = an ISO timestamp string.
- On each load the client computes **`feedMax` = the maximum `published_at` across the fetched
  `entries`** (guides do not move the watermark — an in-place typo fix must not ping everyone).
- **Unseen count** = number of `entries` with `published_at > keep_updates_watermark`
  (all `entries` unseen when the key is absent).
- Opening the **Help & Updates page** sets `keep_updates_watermark = feedMax`.
- **No per-section tracking.** One global watermark keeps evaluation synchronous and easy to reason
  about; the cost is that opening the page clears the indicator for every section at once, accepted.
- **No server-side read state.** The watermark is per-device. A new browser or cleared storage
  re-shows the indicator once — accepted for a non-gate surface, and the reason no migration is
  needed.

**Visual treatment (existing `--ophalo-*` tokens only — no new Tailwind literals):**

1. When unseen count > 0, a small dot in **`--ophalo-accent`** (#bf6b43) sits on the
   **account-menu trigger** (desktop) and the **mobile menu trigger**. Decorative; the trigger's
   accessible name gains " (updates available)".
2. Inside the open account menu and `MobileNavMenu`, the **Help & Updates** row shows a muted
   "· N new" suffix.
3. Exact dot size/position and the row-suffix styling are proposed as concrete values in the
   implementation build-log before coding (per "propose visual values first") — this ADR fixes the
   token and the placement, not the pixels.

The **Help & Updates** row is added to the desktop account menu and `MobileNavMenu` for **all roles**
(not Owner/Admin-gated), routing to a new **`#/help`** page (§4).

### 3. Active banner — founder-flagged entries only

- `entries` with `highlight: true` render a **single dismissible inline banner** at the **top of the
  Requests list only** (`RequestListContent` region) — not a modal, not a toast, not on request
  detail, field/capture surfaces, or public pages.
- **Banner lifetime:**
  - `known_issue` — visible while `status: active`.
  - `whats_new` — visible for **14 days** from `published_at`.
  - `banner_until`, when set, overrides both (hard stop date).
- If multiple qualifying entries are undismissed, the banner shows the most recent plus an
  "N more updates" link to `#/help`.
- Colours use the app's attention token: background **`--ophalo-attention-bg`** (#fff2d7),
  border/text **`--ophalo-attention`** (#c8741a).
- Dismissal is per-entry in localStorage **`keep_dismissed_banners`** (JSON array of entry `id`s).
  Dismissing the banner does **not** move the §2 watermark — the dot/row count stays until the page
  is opened.
- Rationale: the passive dot alone still lets pilot users file duplicate reports for issues we
  already know about. The banner suppresses that for founder-judged high-signal items, with the
  judgement kept entirely in the JSON file and self-quieting so a forgotten flag cannot nag forever.

### 4. Help & Updates page (`#/help`) and content rendering

- **Single scrolling page, sectioned — not tabbed.** Order: **Known issues** (active first, then a
  muted "Recently resolved" group) → **Updates** (what's new, newest first) → **Coming soon** →
  **Guides**. Tabs are deferred until a section's length actually justifies them.
- A **"Report a problem"** action is persistently reachable from the page header (§5), plus the
  feed's last-updated time and each entry's date.
- **Light markdown subset** for every `body` (entries and guides): paragraphs, **bold**, ordered and
  unordered lists, and links. No headings inside a body, no tables, no raw HTML, no arbitrary
  embeds. Rendered through a sanitising renderer; anything outside the subset is escaped or dropped,
  never executed.
- **Inline images (guides only, v1):**
  - Images are hosted in the **same remote store** as the content, under a `guides/img/…` path,
    served from an **allowlisted asset origin** (one `img-src` entry added to the app CSP).
  - The proxy route **rejects any image URL not on the allowlisted origin** at validation time — the
    JSON may not reference arbitrary external image URLs (tracking-pixel / exfil boundary).
  - Markdown `![alt](url)` requires non-empty `alt`. Rendered with `loading="lazy"`, a capped
    max-width, and a constant neutral frame/padding in both themes so a single-theme screenshot does
    not glare on the dark page.
  - **Inline screenshots with captions only** — no galleries, annotation editor, or multi-column
    image layouts. Entry bodies (`known_issue` / `whats_new` / `coming_soon`) remain **text-only**;
    images are a guides affordance.
  - Founder workflow for v1: upload the file to the store, reference it by path in the guide body. A
    drop-folder or upload helper is a later convenience, not v1.
  - Guidance (build-log): prefer tight cropped shots of one control over full-screen captures — they
    survive UI change far longer during the heavy-change month this feature exists for.

### 5. Friction submission path (ADR-293, restated and bound here)

- **`POST /api/v1/feedback`** (authenticated, rate-limited, fail-soft).
- Reachable from the `#/help` header and from a lightweight in-workflow entry point (a "Report a
  problem / Send feedback" action); exact non-help entry points enumerated in the build-log.
- Request body: required short `message` ("What got in your way?"), optional `category`
  (`bug | confusing | missing_thing | too_slow | other`), and client context: current route,
  `request id` when on a request-detail surface, app build, platform/device, timestamp.
- Server owns webhook secret, payload validation (reject blank/oversized), rate limiting, and
  delivery of a **compact private notification to the founder channel**.
- **Never auto-attached:** customer message bodies, phone numbers, emails, page tokens, internal
  notes, broad logs (ADR-293 + ADR-495 boundary).
- **Delivery failure must not lose the note.** On a channel error the backend persists the payload
  to a **durable, retrievable store** and still returns success to the client — no fabricated
  "sent" over a black hole. A minimal `feedback` table is acceptable and is **not** the "ticket
  dashboard" ADR-293 prohibits (no assignment, status lifecycle, or SLA clock); exact mechanism in
  the build-log. This is the one part of GAP-038 that may touch persistence.
- Severe/security/privacy reports are manually promoted into the bug-tracker / ADR / deferred-topics
  flow.

## Consequences

- **No migration on the content or awareness path.** All per-user state is localStorage; all content
  is one remote document plus guide images in the same store. The only possible persistence is a
  minimal failed-delivery `feedback` capture (§5).
- The founder gets a <60-second, no-deploy publish path for known issues, changes, and guides
  (screenshots included) during the heavy-change month, with a quiet indicator and an opt-in,
  self-quieting loud banner.
- Per-device watermark means the indicator is not de-duplicated across a user's devices; accepted.
- Two new authenticated routes (`GET /api/v1/updates`, `POST /api/v1/feedback`), one new page
  (`#/help`), account-menu + `MobileNavMenu` additions, a Requests-list banner slot, a sanitising
  light-markdown renderer, and one CSP `img-src` entry for the guides asset origin.
- Implementation is a **not-a-pilot-gate** slice that follows GAP-054 (account menu lands first,
  ADR-499). The build-log settles: remote-source and asset-origin choice, exact visual values, the
  markdown renderer/library, the failed-delivery capture mechanism, and non-help friction entry
  points. If the slice exceeds the CLAUDE.md batch gate it splits into (a) feed + awareness + page
  and (b) friction path.

## Out of scope

CMS / admin editor / upload UI, public status page, roadmap portal, feature voting, in-app ticket
lifecycle, notification preferences, email or push digests of updates, per-section or server-side
read tracking, image annotation / galleries / multi-column image layouts, images in dated entries,
localization of content, and any surfacing of this content on public / anonymous pages.
