# Session Log — OpHalo Foundation

**Next-session pointer only.** Read this first, then the canonical [workboard](workboard.md) for
scope, sequencing, gates, and deferrals. Locked decisions are in
[decision-index](decisions/decision-index.md); completed-work evidence is in `docs/build-log/`.

**Updated 2026-09-17.** The foundation-first sequence is active. GAP-039 Batch 4, GAP-038,
GAP-093, GAP-095, GAP-098, GAP-073, GAP-094, GAP-099, GAP-063, GAP-048, GAP-049, and GAP-092 are
complete. The canonical scope, order, gates, and deferrals are in the [workboard](workboard.md).

## Start here — GAP-100

GAP-092 (business-timezone display) is fully complete, including slice (3) cleanup: `helpers.ts`'s
`formatDate`/`formatEventTime` now take a required `timeZone: string | null` (the `undefined`
compatibility branch is gone), and `buildAttentionGuidance` (called from `DetailPanels.tsx`'s
`HeroAttentionBanner`, threaded through `RequestDetailWorkCanvas.tsx`) was folded into the same
migration — it was still calling the old formatters with one argument. Repo-wide grep confirmed no
remaining one-argument call sites. See the workboard's Done/evidence index for the full trail.

Take up **GAP-100** next (promoted from DEF-025 — office-hours-aware signals the current pilot
requires). ADR-505 is locked and indexed. Its implementation plan was code-validated before
starting: no live or development `KeepResponsePolicy` row has the obsolete 15-minute first-response
target, so the first 15→60 default reconciliation changes only the unsaved-policy path. The
calculator must be a pure function of one writer-loaded policy/calendar snapshot; it must not issue
a second persistence read. The ADR's DST wording fixes the calculator test behavior, rather than
leaving a further design choice.

Batches 1-3 are done (defaults reconciliation, shared timezone validation, policy/calendar schema
— see the workboard's evidence index). Next: audit-backed atomic settings persistence — this is
where the cross-aggregate validation (staffed-hours target needs ≥1 weekly interval; can't remove
the last interval while any target is staffed-hours) gets built, in a new app-layer
`KeepResponsePolicyService`. Then settings backend and UI separately; pure business clock; and one
writer family at a time. Public intake is its own fork-worthy writer slice because
`CreateFromCustomerIntake` has 37 positional test call sites across 12 files. Keep ADR-451
voicemail promises out of GAP-100; its calendar-aware replacement is recorded as DEF-097.

## Next several sessions

1. Continue the workboard's foundation-first order: GAP-100, then DEF-037
   needs-status-check resurfacing and DEF-063's Request Detail closeout warning.
2. Run the **Proposed Work & Commercial Quotes** decision session. Its first deliverable is a
   decision record, not code; use its workboard Decision Queue entry and the cited ADRs/build logs.
3. Begin **GAP-069** only at the release-readiness trigger: about two weeks before Keep becomes the
   authoritative pilot record, after Railway Pro daily backups/PITR are enabled and the first PITR
   recovery window exists. See [authoritative-pilot-release-readiness.md](runbook/authoritative-pilot-release-readiness.md).

## Pilot posture

The controlled parallel field pilot keeps the existing system authoritative for estimates,
invoices, payments, and accounting; Keep is the factual field record. See
[BL131](build-log/131-next-week-parallel-field-pilot-plan.md).

## Current hot blockers

- No current platform blocker. GAP-099, GAP-063, and GAP-048 are closed and pushed. The
  release-readiness trigger for GAP-069 is documented in the workboard and the authoritative-pilot
  release runbook.
- Native GAP-091 review, S18, and S19 are deferred; they must not displace the foundation-first
  closed-loop sequence.
