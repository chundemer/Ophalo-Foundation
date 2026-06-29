# Session Log — OpHalo Foundation

**Last updated:** 2026-06-29 (S13c complete; S13d next)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 939 unit · 14 arch · 713 integration = 1,666 total, 0 failures (1 pre-existing KeepG5 fluke excluded)
**Next free ADR:** ADR-385
**Current session:** Session 13c — Command Center Request List

---

## Session Protocol

This file is the current execution brief, not the historical build archive. Completed implementation
detail lives in `docs/build-log/`; authoritative decisions live in
`docs/decisions/decision-index.md`.

For every implementation slice:

- Classify the work explicitly: discovery when pre-work is incomplete; mechanical implementation
  preflight when the current brief is marked pre-work complete.
- Use targeted `rg` during preflight to confirm named signatures and compile-impact callers. Do not
  rediscover already-locked architecture, scope, tests, or decisions.
- Inspect current signatures, endpoint/persistence patterns, failure modes, and tests before editing.
- Present the file-level gate before writing.
- Keep the hard slice gate unless explicitly split: at most 3 mutation families, 8 production files,
  and 12 total changed files including tests/docs.
- Preserve fail-closed account, row, action, membership, and public-token behavior.
- Add focused authorization/regression tests and run the proportionate broader suite.
- Self-review for policy drift, accidental visibility expansion, token leakage, untested direct-ID
  paths, stale docs, and unrelated scope.
- Commit only after Christian approves the completed diff.

---

## Current Work

**Current build log:** `docs/build-log/067-session-13-pwa-workbench.md`
**Last completed build log:** `docs/build-log/067-session-13-pwa-workbench.md` (S13c)
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 13d — Request Detail Workbench

**S13a status: complete.** (details in build-log 067)

**S13b status: complete.**

- Backend: `GET /keep/requests/lookup?phone={digits}` — authenticated, mirrors create posture (Viewer/OffSeason → 403).
- `IKeepBusinessRequestPersistence.FindActiveRequestsByCustomerIdAsync` added; returns non-terminal requests
  ordered by `max(LastBusinessActivityAt, LastCustomerActivityAt) ?? CreatedAtUtc DESC`.
- `LookupKeepRequestByPhoneService` — full auth stack, phone normalization via `PhoneNormalizer`, customer
  lookup, active request projection (max 3 + `hasMoreActiveRequests` flag), co-located result types.
- Frontend: `App.tsx` shell — left sidebar `+ New Request` button (desktop) + sticky FAB (mobile); state
  hoisted at shell level; `Home.tsx` receives `onStartCapture` callback and shows inline "Try it now" action
  on the Quick Capture checklist item.
- `QuickCapture.tsx` — Phone Lookup Gate (digit strip, auto-fire at 10 digits, Contact Picker icon, clipboard
  affordance) → Lookup Result cards (max 3 active requests, navigate-on-tap) → Capture Form (locked phone,
  prefilled name/email when known, source picker, description) → Success Panel (Copy Tracker Link at
  `{VITE_PUBLIC_BASE_URL}/keep/r/{pageToken}`, View Request Workbench, Capture Another). Mobile success
  navigates to `/keep/requests/{requestId}` via `window.location.href`.
- Shell-level role/commercial preflight is deferred: current `GET /keep/setup/onboarding` is
  SettingsManage-gated, so it cannot distinguish Operator from Viewer or reliably surface past-due
  state for non-settings roles. Quick Capture still handles backend-denied states inside the drawer:
  Viewer/OffSeason lookup or create returns 403; commercial block returns 402 when emitted by the
  lookup/create endpoint. Future shell-disabled Viewer buttons and persistent pre-submit 402 banners
  require role/commercial state from session claims or a dedicated role-neutral access endpoint.
- `apiClient.ts`: `PhoneLookupResult`, `CreateRequestBody`, `KeepRequestDetailResult` DTOs; `lookupRequestByPhone`,
  `createRequest` functions.
- Verification: 29 existing unit tests green; 31 integration tests green (6 new lookup tests); `pnpm typecheck`
  clean; `pnpm build` succeeds.

**S13c status: complete.**

- Backend prerequisite: `GET /auth/me` now returns `accountRole` ("owner" | "admin" | "operator" | "viewer") loaded
  fresh from DB via `IMemberManagementPersistence.GetAccountUserRoleAsync`.
- Frontend: role-based tab set (Owner/Admin: 6 tabs; Operator: 4 tabs with "My Promises" label); text search + active
  status dropdown filter; attention badge rendering (danger/urgent/waiting/neutral exhaustive mapping); `NeedsShare`
  callout; 30s polling on page 1 / paused on page 2+ with staleness banner + manual Refresh; `viewCounts` from list
  response drives sidebar tab counts; cursor pagination with Previous/Next; 7 view-specific empty states; row
  navigation via `window.location.href` (consistent with S13b QuickCapture pattern; S13d wires full routing).
- New files: `web/ophalo-app/src/pages/Requests.tsx`, `web/ophalo-app/src/components/RequestRow.tsx`.
- Verification: 939 unit · 14 arch · 713 integration (2 new auth role tests) = 1,666 total, 0 failures;
  `pnpm typecheck` clean; `pnpm build` succeeds.

**S13d next: pre-work complete per build-log 067.** Use the locked S13d decisions (detail timeline, actions,
`AvailableActionsMetadata.CanRecordShareIntent`, versioned writes) directly.

---

## Carry-Forward Boundaries

- Real APNs/FCM provider implementation remains future work.
- Demo scenario packs, demo reset UI, and admin/internal classification management remain deferred.
- Classification is operational/reporting/safety posture, separate from commercial lifecycle.
- Public signup cannot create Demo/InternalTest accounts.
- Production push delivery must stay suppressed for Demo/InternalTest accounts.
- Keep sends no backend SMS/email to customers in V1; native `sms:`, `tel:`, and `mailto:` handoff
  remains operator-initiated on the user's device.

---

## Operational Watch-Outs

- GitHub remote `origin` is configured; push local commits daily when green.
- Integration tests reset PostgreSQL schema and run migrations.
- Testing environment intentionally skips runtime rate limiting; production-like proof exists in
  `RateLimitTesting` (G8a/S7b).
- Deployment still requires correct Cloudflare/Railway trusted-proxy and token-redaction
  configuration even though application-level proofs are complete.
- Persistent local PostgreSQL setup/migration/smoke runbook is verified against local `ophalo_local`
  in Docker; guarded reset remains documented but was not exercised.
