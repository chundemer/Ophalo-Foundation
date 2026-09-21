# Session Log — OpHalo Foundation

**Next-session pointer only.** Read this first, then the canonical [workboard](workboard.md) for
scope, sequencing, gates, and deferrals. Locked decisions are in
[decision-index](decisions/decision-index.md); completed-work evidence is in `docs/build-log/`.

**Updated 2026-09-21.** The foundation-first sequence is active. GAP-039 Batch 4, GAP-038,
GAP-093, GAP-095, GAP-098, GAP-073, GAP-094, GAP-099, GAP-063, GAP-048, GAP-049, and GAP-092 are
complete. GAP-100 is the active item (batches 1-4 done — see below). The canonical scope, order,
gates, and deferrals are in the [workboard](workboard.md).

## Start here — GAP-100

GAP-100 (promoted from DEF-025 — office-hours-aware signals the current pilot requires). ADR-505
is locked and indexed. Batches 1-4 are done and pushed (defaults reconciliation, shared timezone
validation, policy/calendar schema, audit-backed atomic settings persistence — see the workboard's
evidence index for the full trail, including 4a/4b/4c). Railway production table-presence
verification confirmed all four GAP-100 settings tables after deployment on 2026-09-20.

Next: **ADR-507 closure-label implementation — 7a-1 done (`3ef69461`); 7a-2 write/audit done (`90b40dbb`); 7b API done (`2431f46b`); 7c frontend done (`57b6cf3b`; 7b+7c deploy together after a manual live check) — then 6c (policy timing-basis controls) — done (`017dd6c6`); the `CalendarSection` error-code fix is done (`924f282a`); pure business clock done (`a7c79cda`); public-intake writer (slice 8) done (`18a38872`); slice 9a (shared response-timing snapshot/resolver) done (`879764c7`); slice 9b (customer-message writer) done (`8ea427f9`); slice 10 (feedback writer) done (`494d5ff0`); slice 11 (inbound-external-contact writer) done (`b085f237`); GAP-100 acceptance closure in order: viewer-independence test (A, done `32933aa1`), then cleanup C1a (code-complete, awaiting review, uncommitted), C1b, then the duration-overload architecture guard (G); details in the workboard** — 6b calendar section is done (`a8c9d03b`), but its dates-only closure contract is superseded for V1 by [ADR-507](decisions/ADR-507-closure-labels-and-object-calendar-contract.md): object-native `{ date, reason }` entries, no legacy adapter because there are zero live web clients, reason included in the opaque `settingsVersion` and deterministic audit diff, and reason never affects the business clock or public display. 6a-2b backend profile timezone version enforcement is done (`88eb993b`); earlier: settings backend contract, then settings UI — the first real API/UI surface for
`KeepResponsePolicyService`'s governed policy, calendar, and timezone operations. ADR-506 locks
the route family, full-snapshot request shape, opaque stale-save protection with an explicit UI
refresh recovery path, and deterministic audit granularity/content. Batch 5 (backend) is split
into three slices to stay under the batch-size gate (preflight complete 2026-09-20; **5a done
(`3316c997`), 5b-1 done (`7bfc9d27`), 5c is re-sliced: 5c-1a (calendar persistence version check + closure audit format) done (`2fad6272`); 5c-1b (`PUT /keep/setup/calendar` + mapper) done (`1cbc1c4a`); 5c-2 (profile timezone versioning) is deferred entirely until batch 6, like 5b-2**, each with its own gate; **5b-2 and 5c-2 are deferred entirely until batch 6**, so batch 5's remaining backend work rides with the settings UI): **5a** `settingsVersion` hash + extended `GET /keep/setup` (explicit string timing bases; absent policy hashes a distinct `unset` marker), no writes, no error mapping; **5b-1** (done) governed policy persistence — version check, omitted-basis preservation, changed-fields-only `field: before -> after` audit format, once-per-account `PolicySaved` event, `SettingsVersionMismatch` error; route unchanged; **5b-2** (deferred to batch 6: the route rewire returns 409 to the current PWA policy save, which sends no `settingsVersion`; no frontend patch) `PUT /keep/setup/policy` moved to the governed service via `KeepSetupService`, string bases, and explicit 409/422 mapping; the dead `SavePolicyAsync` cleanup is a separate later slice; **5c-1a/5c-1b/5c-2** `PUT /keep/setup/calendar` (full-snapshot diff, closure audit format, version check; persistence first, then route + mapper) plus the profile-write version check — see the workboard for the split. Locked for 5c: `settingsVersion` is required on a profile save only when the requested timezone differs from the stored one. Batch 6 is the settings
UI, sequenced 6a-1 (**done** — `3b58e50c` backend, `60c10cfc` frontend, live check passed, deploy hold lifted), 6a-2 (split: 6a-2a frontend pass-through done, then 6a-2b backend enforcement), 6b (**done** — `a8c9d03b`), 6c (timing-basis controls); `settingsVersion` is temporarily optional in the TS result type, guarded at runtime, then tightened to required in its own fixture slice (see workboard). After that: pure business
clock, then one writer family at a time. Public intake is its own fork-worthy writer slice because
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
