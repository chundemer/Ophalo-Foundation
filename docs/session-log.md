# Session Log — OpHalo Foundation

**Next-session pointer only.** Read this first, then the canonical [workboard](workboard.md) for
scope, sequencing, gates, and deferrals. Locked decisions are in
[decision-index](decisions/decision-index.md); completed-work evidence is in `docs/build-log/`.

**Updated 2026-09-13.** GAP-094, GAP-095, and GAP-098 are reviewed, merged, and deployed to
`main`. Feedback / Help & Updates is code-complete: 038-R0/R1/R2 are complete. GAP-039 Batch 4
(production-candidate verification) is in progress — see below.

## Start here — GAP-039 Batch 4 (production-candidate verification)

Railway/Vercel/Sentry console configuration is done per
[sentry-configuration.md](runbook/sentry-configuration.md): both Sentry projects created, DSNs set,
`/health/ready` healthcheck live, source maps uploading and confirmed private, and founder-email
alert rules (new issue + regression) live and test-confirmed on both `ophalo-api` and
`workbench-pwa`.

The authenticated-PWA controlled-error test is done and passed, after finding and fixing a real
defect: `scrubBrowserEvent`'s invariant check treated Sentry's `"?"` unresolved-function-name
placeholder as a leaked query string and silently discarded the whole event — fixed in `7a87e41a`
(commit `0c3c8b3e`, tests updated). Re-verified live in production: the issue now reaches Sentry
with correct release/environment tags, an empty message (no PII), and the founder alert email
arrived.

The controlled **API** error test is done and passed. Mechanism: a temporary founder-only
`GET /diagnostics/throw` route (`64ae48b3`), gated on `Diagnostics:FounderAccountUserId` and
returning 404 for every non-founder case (unauthenticated or wrong account) so it stayed
indistinguishable from an absent route; reverted the same session after verification. Confirmed in
production: correct release/environment tags, correlation-id tag present, no query/body/identity
leakage, resolved server stack, and the founder alert email arrived.

Remaining for Batch 4:

1. Invalid-`VITE_PUBLIC_BASE_URL` fail-safe test.
2. Record the evidence plus named incident roles (release owner, technical responder,
   customer-communication owner, alert recipient, rollback path) in
   [sentry-configuration.md](runbook/sentry-configuration.md).

Full checklist and completion standard: [BL140](build-log/140-gap-039-sentry-implementation-handoff.md)
Batch 4.

038-R3 (Feedback / Help & Updates founder verification) is still pending — see the concise
[founder guide](founder/feedback-and-updates-guide.md) and the
[full operations guide](runbook/feedback-help-updates-operations.md). Do not start another feedback
implementation batch unless R3 exposes a concrete defect. Keep the API at one replica until GAP-081
worker/cache hardening is delivered.

## Next several sessions

1. Finish GAP-039 Batch 4 above, then 038-R3.
2. Make the **GAP-069** durable API-key-store, ownership, rotation, restore, and proxy-trust
   decision. Both GAP-039 and GAP-069 must complete before a customer-facing pilot.
3. Run the **Proposed Work & Commercial Quotes** decision session. Its first deliverable is a
   decision record, not code; use its workboard Decision Queue entry and the cited ADRs/build logs.
4. Resume the ordered pilot workboard queue: **GAP-099** (team-member access-control clarity and
   production parity), then **GAP-063**, **GAP-048**, **GAP-049**, and **GAP-092**. GAP-040 remains
   deliberately deferred until the underlying application stabilizes.

## Pilot posture

The controlled parallel field pilot keeps the existing system authoritative for estimates,
invoices, payments, and accounting; Keep is the factual field record. See
[BL131](build-log/131-next-week-parallel-field-pilot-plan.md).

## Current hot blockers

- GAP-039 Batch 4: console configuration, PWA capture/redaction/alert verification, and the
  browser-scrubber defect fix are done; remaining is the API controlled-error test (mechanism
  undecided), the invalid-base-URL fail-safe test, and recording evidence in the runbook.
- GAP-069: founder decision and implementation of durable API-key storage and narrowly trusted
  Railway proxy headers.
