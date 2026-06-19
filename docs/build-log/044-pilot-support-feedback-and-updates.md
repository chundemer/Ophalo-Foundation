# Build Log 044 — Pilot Support Feedback + Updates

**Phase:** V1 pilot support / pre-go-live implementation guide
**Date:** 2026-06-19
**Status:** Decisions locked. These are to be built before pilot go-live.
**Build log preceding this:** `043-keep-v1-product-scope-and-freshness-lock.md`
**ADRs locked:** 293..294
**Next free ADR:** ADR-295

---

## Purpose

Pilot teams are busy running their businesses. If reporting friction requires leaving Keep, opening
email/text, remembering context, or waiting for the weekly champion conversation, most useful product
learning will disappear.

V1 needs a tiny in-app feedback loop and a tiny in-app communication surface so pilot users can tell
us what hurts and we can tell them what changed without making anyone leave the product.

This is not a helpdesk product, support ticketing system, CMS, public status page, roadmap portal, or
analytics platform.

---

## Locked Decisions

### ADR-293 — Pilot feedback intake uses 1-tap Friction Flash

V1 includes an in-app `Report friction` / `Send feedback` hook.

User flow:

```text
Tap Report friction -> type short note -> send
```

The UI should be reachable from the operator/admin app without leaving the current workflow. It may
use a small modal, sheet, or prominent help action. The action should feel like a hotline to the
founder during pilot, not a formal ticket form.

Payload shape:

- required short message: "What got in your way?";
- optional lightweight category such as `bug`, `confusing`, `missing_thing`, `too_slow`, `other`;
- authenticated account/user context;
- current route/screen;
- request id when already on a request detail surface;
- app version/build;
- platform/device;
- timestamp.

Privacy/security rules:

- client must not call a Slack/Discord webhook directly;
- client posts to an authenticated OpHalo API endpoint;
- backend owns webhook secrets, rate limiting, payload validation, and delivery;
- do not auto-attach customer message bodies, phone numbers, emails, page tokens, internal notes, or
  broad logs in V1;
- feedback delivery is fail-soft and must not affect normal Keep workflows.

Routing:

- backend forwards a compact private notification to founder/internal pilot-support channel
  (Slack/Discord/email or equivalent);
- no durable in-app ticket dashboard, assignment lifecycle, SLA clock, or customer-visible artifact
  in V1;
- severe/security/privacy issues discovered through this path are manually promoted into the right
  bugfix/ADR/deferred-topic flow.

Reason: the pilot needs raw operational signal at the moment of pain. A few seconds in-app beats a
perfect support workflow no one uses.

### ADR-294 — Pilot Updates page communicates known issues and changes in-app

V1 includes a simple in-app `Pilot Updates` / `Help & Updates` page.

Content sections:

```text
Known Issues
What's New
Coming Soon
Report Friction
```

Rules:

- content is written in plain operator/admin language, not engineering release-note language;
- known issues should include short workaround text when available;
- What's New should explain pilot-visible changes after deploys;
- Coming Soon should include only near-term pilot-relevant items, not a broad roadmap promise;
- Report Friction opens the ADR-293 feedback hook;
- include a visible last-updated timestamp;
- a small "new since last viewed" indicator is allowed if cheap, but not required for the first
  slice.

Implementation posture:

- use founder-maintained static markdown/JSON or a tiny API-served content file for V1;
- no CMS, public status page, feature-voting board, roadmap portal, or admin editor before pilot
  go-live unless manual updates become a proven blocker;
- no customer/request data belongs in this content surface.

Reason: pilot users need to know "is this a known issue?" and "what changed?" inside the app. The
same surface should also remind them how to send friction while the moment is fresh.

---

## Coding Session Guidance

Build these as a narrow pre-go-live pilot-support slice after core workflow stability, or alongside
web/native shell work if that is more efficient.

Suggested split:

```text
1. Backend friction endpoint and founder-channel forwarding.
2. Web/native Report Friction modal/sheet with route/app/device context.
3. Pilot Updates content contract and simple page.
4. Lightweight QA: auth, rate limit, no PII auto-attachment, failure behavior, content rendering.
```

Verification expectations:

- authenticated users can submit friction from ordinary app surfaces;
- anonymous/public customer pages do not expose the pilot-support feedback hook unless separately
  decided;
- webhook/channel failures do not break the user's workflow;
- payload validation rejects blank/oversized messages;
- rate limiting prevents accidental spam;
- known issue/update content can be updated without schema/migration work;
- no sensitive customer data is automatically shipped to external channels.
