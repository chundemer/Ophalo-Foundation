# Feedback, Help & Updates — Founder Operations Guide

**Purpose:** operate the authenticated feedback and Help & Updates loop without turning it into a
support-ticket system or publishing claims/content that the product cannot support.

**Authority:** [ADR-500](../decisions/ADR-500-in-product-feedback-and-help-updates-loop.md) and
[BL149](../build-log/149-gap-038-in-product-feedback-help-updates-implementation.md). This guide
does not change their product, privacy, or delivery decisions.

## What customers and businesses get

The loop has two directions, both inside the authenticated Keep application:

1. **OpHalo → businesses:** Help & Updates (`#/help`) provides known issues, product updates,
   coming-soon notices, and concise how-to guides. The account menu and mobile navigation expose
   the same destination; an unread indicator and an optional Requests-list banner call attention
   to important entries.
2. **Businesses → OpHalo:** "Report a problem" in Help and "Send feedback" in the account menu
   open the feedback dialog. A submission is stored before delivery is attempted, then sent to the
   founder channel. Failed delivery is retried automatically; this is not an operator-facing ticket
   queue or customer-support portal.

Customer-facing public intake and request-tracking pages do not show this surface.

## Locked operating decisions

| Topic | Operating rule |
| --- | --- |
| Content source | The repository file `docs/content/updates.json` is canonical. R2 holds the published copy at `platform/updates.json`. |
| Content format | JSON schema version `1`; entries and guide bodies are sanitized Markdown, not arbitrary HTML. |
| Publishing | Founder-controlled; validate first, then overwrite the one feed object. No API or application deployment is needed for a content-only publish. |
| Visibility | The API cache can take up to five minutes to show a valid new feed. |
| Guide images | Only PNG, JPEG, or WebP; store under `platform/updates/guides/img/`. Reference them only as `/updates/guides/img/<immutable-name>` from a guide body. |
| Image naming | Use immutable, content-addressed names such as `send-update-7f3a91c2.png`; never overwrite an existing guide image. |
| Feedback delivery | Persist-first and at-least-once. The founder channel must tolerate duplicate delivery IDs. Successful records are minimized promptly; failed/abandoned records follow the locked retention policy. |
| Privacy | Do not put customer PII, access tokens, or raw request content into an Update entry, guide, or operational log. |

## Current activation status

As checked on 2026-09-11:

- Railway production has `Feedback__Enabled=true` and a non-empty `FounderChannel__WebhookUrl`.
- `pnpm --dir web/ophalo-app validate:updates` validates the canonical feed against its JSON Schema;
  the PWA test and production-build commands run it automatically.
- The remaining content bootstrap is the R2 object `platform/updates.json`.
- The current production PWA is already built with `VITE_FEEDBACK_ENABLED=true`; R2 publishing does
  not require a Vercel redeploy. R3 verifies that the live UI remains visible and functional.
- The current `content_source_failure` alert/failure streak is expected until R1 succeeds: the R2
  object is absent, so the API serves the intentional empty-feed fallback. A valid read after R1
  resets the streak.

Do not treat the feature as live until the launch checklist below has passed end to end.

## One-time access setup

### Cloudflare R2: founder publishing access

Use a separate founder-held **write** credential for publishing. Do not reuse, expose, or copy the
API's R2 read credential from Railway.

1. Sign in to the Cloudflare account that owns `ophalo.com`.
2. Open **Storage & databases → R2** and select the existing
   `ophalo-business-documents` bucket.
3. Create an Account API token named, for example, `ophalo-updates-publisher` with **Object Read &
   Write** permission scoped only to that bucket. Store it in the password manager. Do not commit
   it, place it in a tracked `.env` file, or paste it into chat.
4. Prefer the Cloudflare dashboard for the first publish. It makes the bucket/object target visible
   and avoids introducing a local credential file. A founder may later use `wrangler` with that
   same narrowly scoped credential, but only after a small reviewed publish command exists.
5. Before replacing an existing feed, download its current object as a rollback copy. Repository
   history remains the source of truth; R2 is not the editorial system of record.

### Vercel: PWA feature flag

`VITE_FEEDBACK_ENABLED` is a build-time Vite variable. Changing it requires a new production build;
it cannot be toggled at runtime in an already-served browser bundle.

1. Sign in to the Vercel team/project that deploys `app.ophalo.com` (the `web/ophalo-app` PWA), not
   the separate public marketing site unless it is explicitly the same project.
2. Open **Settings → Environment Variables**.
3. Add or update `VITE_FEEDBACK_ENABLED` with value `true` for the **Production** environment.
4. Redeploy the `main` revision. Confirm the production domain is the intended PWA domain before
   promoting it.
5. Rollback is a new build with `VITE_FEEDBACK_ENABLED` unset or `false`; do this before disabling
   the API feedback flag if an incident requires the feature to be withdrawn.

### Railway: verify, do not duplicate

The production API already has the two required variables. In Railway, verify only their names and
presence unless a deliberate secret rotation is needed:

- `Feedback__Enabled=true`
- `FounderChannel__WebhookUrl=<sealed founder webhook>`

The API fails production startup if feedback is enabled without a valid webhook. Do not disclose the
webhook value in a ticket, document, terminal output, or chat.

## First R2 publish

The seed feed is valid and intentionally empty:

```json
{
  "schema": 1,
  "entries": [],
  "guides": []
}
```

Publish it once to establish the read path and clear the missing-object fallback.

1. Start from the committed `docs/content/updates.json`; do not hand-edit a copy in the R2 editor.
2. Run `pnpm --dir web/ophalo-app validate:updates`. It validates the file against
   `docs/contracts/updates.schema.json`, including required fields, length limits, allowed values,
   and ISO date-time formats. Do not publish when this command fails; JSON syntax alone is not
   sufficient.
3. In the R2 bucket, upload it with the exact object key **`platform/updates.json`**. Preserve the
   lower-case spelling and slash; `updates.json` at the bucket root is not read by the application.
4. Wait up to five minutes, then sign in to the PWA and open `#/help`. A healthy initial feed shows
   the calm empty state rather than an error.
5. Check Railway logs for the absence of new `content_source_failure` alerts after the valid read.

If the feed is malformed, a warm API instance serves its last known good payload; a cold instance
can show an empty feed. Correct the repository source, republish it, and verify again.

## Pilot constraints to keep explicit

- **R1/R2 are independent.** The API may have feedback enabled before the PWA build exposes its
  feedback actions. The current PWA already has `VITE_FEEDBACK_ENABLED=true`, so no Vercel action
  is needed for R1; only R3 declares the feature live after the feed and UI are verified.
- **Founder-channel outage is a known pilot risk.** Feedback delivery and its backlog/abandoned
  alerts use the same founder-notifier webhook path. A complete Google Chat/webhook outage cannot
  independently notify that same channel of its own failure. Before R3, the founder must explicitly
  accept this posture or nominate an independent fallback; do not silently assume the alert is
  independent.
- **One API replica only.** Do not scale `Ophalo-Foundation-API` beyond one replica without first
  addressing both feedback-worker leasing and Help-content cache/last-known-good coherence. The
  current per-instance cache and in-process worker are deliberate one-replica pilot tradeoffs.
- **Retention disclosure is already shipped.** The feedback dialog includes the locked notice that
  undelivered feedback and limited context may be retained for up to 30 days; successfully delivered
  feedback is minimized promptly and metadata is deleted after seven days.

## Publishing updates and guides

### Use entries for short, time-sensitive communication

Use `entries[]` for a known issue, a concise shipped change, or a carefully worded coming-soon
notice. Each entry needs a stable ID, ISO-8601 UTC `published_at`, section, title, and Markdown
body. The supported V1 sections are:

- `known_issue` — mark `status: "resolved"` when it is fixed; use `highlight: true` only when a
  Requests-list banner is genuinely warranted.
- `whats_new` — a released, observable change; never announce a feature that has not shipped.
- `coming_soon` — a bounded direction, not a promise of scope, date, price, or automatic outcome.

Use a new entry ID for a materially new announcement. Keep resolved issues as history unless the
content becomes misleading or no longer useful.

### Use guides for durable how-to help

V1 guides live in `guides[]` inside `updates.json`. Their body is sanitized Markdown, so do **not**
create or publish arbitrary standalone HTML files. The application deliberately allows only a small
safe Markdown/HTML subset; this prevents remote content from becoming executable page content.

Start with short, task-oriented guides that match shipped behavior:

1. **Receive and manage a customer request** — where it appears, what the status means, and what
   the customer can see.
2. **Send a customer update** — explain that it records/sends the product's supported update flow;
   do not imply SMS/email delivery unless the interface confirms it.
3. **Use the customer tracking link responsibly** — explain informed sharing and link privacy once
   GAP-048 is delivered.
4. **Record actual work clearly** — only after the exact field workflow and permissions are stable.

Do not publish a Proposed Work, estimate, quote, invoice, scheduling, payment, or CRM guide until
that workflow is actually released and its product language is locked. This keeps the Help surface
truthful while Proposed Work is being scoped.

Guide writing pattern:

1. State the job in one sentence.
2. Give 3–7 numbered steps using the current labels visible in the product.
3. State the expected result and one recovery path.
4. Add one annotated screenshot only when it removes real ambiguity. Keep it free of real customer
   data, upload it first with an immutable hashed filename, then reference the proxied image path.
5. Have a second person verify the steps against production before publishing.

For now, keep guide drafting/review in the repository alongside the canonical JSON change. A richer
founder preview/validate/upload editor is deliberately future work (GAP-087), not a reason to add a
CMS or raw HTML publishing path today.

## Feedback operating loop

1. A business submits a category plus a 1–4,000-character message from Help or the account menu.
2. The API accepts it only after persistence; it may return `202` even if immediate notification is
   uncertain because retry is already scheduled. A `503` means it was not persisted and may be
   retried by the business.
3. The founder receives the webhook event and reviews it in the founder channel. Delivery can be
   duplicated, so use the delivery ID to deduplicate if the channel/tool supports it.
4. Triage the signal, not the person: group recurring issues, redact any copied PII before creating
   a work item, and record a concrete decision in the workboard/ADR/build log as appropriate.
5. Close the communication loop with a `known_issue` or `whats_new` entry when it helps affected
   businesses. Do not promise an individual response time or turn the feed into a case-management
   system.

A simple founder cadence is sufficient for pilot: review new feedback on business days, publish an
active known issue when it materially affects multiple businesses, and review any retry/backlog
alert promptly because an abandoned record retains its message only for the locked recovery window.

## Production launch checklist

- [ ] `pnpm --dir web/ophalo-app validate:updates` passes for the canonical feed (038-R0).
- [ ] `docs/content/updates.json` passes schema validation and is committed/reviewed as the
      canonical source.
- [ ] It is uploaded to `ophalo-business-documents/platform/updates.json`.
- [ ] Railway has feedback enabled and the sealed founder webhook present.
- [x] The current PWA production build has `VITE_FEEDBACK_ENABLED=true`; redeploy only if that
      setting changes or R3 exposes a stale build.
- [ ] An authenticated non-production/test business account can see Help & Updates and the feedback
      actions on desktop and mobile.
- [ ] `#/help` loads a valid feed within five minutes of publication.
- [ ] Submit one harmless activation test (`category: other`) and confirm the founder channel
      receives it; do not include customer information.
- [ ] Founder has explicitly accepted the single-founder-channel outage posture or documented an
      independent fallback.
- [ ] Confirm the API remains healthy and no content-source or feedback-delivery alert is firing.

## Rollback and incident response

- **Bad update/guide content:** restore the previous committed `updates.json`, republish it, then
  verify Help after the cache window. Do not edit only R2 and leave the repository inaccurate.
- **Bad guide image:** republish the feed pointing to a new immutable image object; do not overwrite
  the old cached image.
- **Feedback UI incident:** deploy the PWA with `VITE_FEEDBACK_ENABLED=false` first, then disable
  Railway feedback only if necessary. This avoids leaving visible UI that cannot accept a report.
- **Founder webhook failure:** leave the persisted/retry path intact, fix or rotate the sealed
  webhook in Railway, redeploy, and monitor the backlog/abandoned alert. Do not delete feedback
  records to suppress an alert.

## What follows feedback activation

After this checklist passes, the next product-planning focus is **Proposed Work & Commercial
Quotes**. It is currently in the workboard decision queue: choose the pilot posture (Estimate,
Fixed-Price Quote, and/or T&M Authorization), finish-line, and sequencing before implementation.
The Help surface can later explain that released workflow, but it must not get ahead of the product
decision or ship date.
