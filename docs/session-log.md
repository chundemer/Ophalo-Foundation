# Session Log — OpHalo Foundation

**Last updated:** 2026-07-19 (GAP-020 Slice A implemented, uncommitted; blocked on migration)
**Branch:** `main` tracking `origin/main`
**Deployment posture:** Not deployment-ready. The active launch gate is
`docs/pilot-readiness-bug-tracker.md`.
**Current work:** GAP-034 is resolved in `45cea22` (completion docs `dfa554a`). GAP-020 — replacing
the raw-phone desktop call QR with an opaque short-lived handoff — is in progress: Slice A
(backend) is implemented and uncommitted in the working tree. Slice A is not yet closed out —
see "GAP-020 Slice A Status" below for the exact next step before commit.

Detailed historical implementation evidence belongs in `docs/build-log/`; active requirements and
status belong in `docs/pilot-readiness-bug-tracker.md`; decisions belong in
`docs/decisions/decision-index.md`.

## Session Protocol

For every implementation slice:

- Read this file and the relevant active tracker entry before editing.
- Preflight named signatures, authorization, failure modes, and focused tests with `rg`.
- Present the file-level gate before production edits. Unless Christian explicitly overrides it,
  keep to three mutation families, eight production files, and twelve total changed files.
- Preserve fail-closed account, membership, action-policy, public-token, privacy, and concurrency
  behavior.
- Add focused regression coverage, run proportionate checks, self-review visibility/token policy,
  and commit only after Christian approves the completed diff.

## Current Baseline

- **GAP-035 / R90c-1 through R90c-4:** complete in `8aba5dc`, `0f70437`, `3490cb1`, and
  `c1a1379`. All normal browser auth/invite/recovery states now use `AuthShell`; mobile exchange
  uses `AuthShell bare` and remains ADR-390 sterile.
- **GAP-034 `/start` conversion redesign:** resolved in `45cea22` (completion docs `dfa554a`) in
  `web/ophalo-web/src/app/start/page.tsx`. Contained two-panel desktop composition (navy identity/
  value panel + white form panel, compact stacked header on narrow widths), truthful `Start your
  Keep pilot` copy and CTA, friendly editable time-zone display, and a client-side `/auth/me`
  session check that redirects an already-authenticated business user to `NEXT_PUBLIC_APP_BASE_URL`
  with no start-form flash and an explicit retry state on session-check failure. `AuthShell.tsx` and
  the backend `/auth/start` contract were not changed. Live authenticated-redirect verified.
- **GAP-033 / R90b public trust:** public identity fields and projections are complete. Known public
  intake/tracker/expired pages can render business name, hosted logo, website, and public phone;
  unknown tokens remain non-enumerating. Successful public intake goes directly to the tracker with
  a one-time welcome state. Automatic tracker-link email was removed.
- **GAP-033 deployment evidence still required:** actual browser intake submission, an actual
  expired-tracker visual check, and a known-business OffSeason behavior/banner decision.
- **GAP-036 / GAP-036a + GAP-036b:** resolved in `8085971` and `014bae5`, with focus-containment
  follow-up `aae9257`. Owner/Admin can configure
  Logo URL/Website URL with a shared unsaved-draft customer preview (GAP-036a). Public-link
  replacement now requires an exact, case-sensitive `REPLACE` value in an accessible, focus-trapped
  destructive dialog (Tab contained, Escape/backdrop blocked while in flight, focus returns to the
  trigger on close); the server independently re-validates the same confirmation before revoking the
  active link — a missing or empty request body still reaches that gate rather than being rejected
  by model binding (GAP-036b). Live authenticated desktop/mobile keyboard verification is complete.
- **GAP-042:** newly recorded authenticated-workspace identity gap. Request List and Request Detail
  need restrained account business-name context so work visibly belongs to the business, without
  leaking it to public routes or repeating it on each request row.
- **GAP-043:** newly recorded request-list scale/readiness gap. Cursor paging already exists (50-row
  default, protected next cursor, Previous/Next stack); decide and verify its real-work UX rather
  than replacing it on the assumption that no pagination exists. Coordinate any implementation with
  GAP-041’s stable first-load queue transition.
- **GAP-044 through GAP-046:** newly recorded request-list operating gaps. Expose authorized
  closed/cancelled history through the existing protected contract; replace opaque `Default Queue`
  language with factual owner orientation; and make applied search/filter state visible and easy to
  recover. GAP-041 now explicitly includes arrow-key/roving-focus tab behavior.
- **GAP-047 through GAP-051:** newly recorded Request Detail/customer-continuity gaps: never fail a
  priority mutation silently; make email tracker sharing a deliberate attested action; bound
  follow-up prefill; show compact same-customer related work; and format North American phone entry
  and display while preserving ADR-444 canonical storage. GAP-020 remains the separate P0 raw-phone
  desktop QR blocker. GAP-039 now includes `VITE_PUBLIC_BASE_URL` release configuration validation.

## Locked Public-Trust Boundaries

- Public pages are business-first; OpHalo Keep is secondary. Hosted logo/website/public phone are
  allowed only for known businesses. Never expose public email.
- URLs are input-validated absolute HTTPS values, not ownership-verified domains. Unknown/invalid
  capability tokens never disclose business identity or contact data.
- No raw tokens in metadata, logs, diagnostics, persisted frontend state, or durable UI. Tracker
  pages remain `noindex` with restrictive referrer handling.
- Keep does not send backend customer SMS, ingest SMS replies, or automatically email a tracker link
  after intake. A business may use its own later communication to share the tracker link.

## V1 Sequence After GAP-036

1. GAP-020 P0 opaque desktop call-handoff replacement, then resolve remaining selected P0/P1
   request-capture/detail/list safety and trust gaps in bounded slices.
2. GAP-037 founder/internal weekly value report; GAP-038 PWA feedback/help; GAP-039 redacted
   production observability; GAP-040 marketing accuracy/assets/deployment readiness; GAP-042
   authenticated business context; GAP-043 deliberate request-list scale UX; GAP-044 history
   access; GAP-045 queue orientation; GAP-046 search/filter clarity; GAP-047 through GAP-051
   Request Detail reliability, continuity, and phone-quality work.
3. Finish or explicitly defer all selected P0/P1 tracker items; collect the remaining GAP-033
   evidence; run Build 089 desktop, then real-device mobile PWA verification.
4. Deploy and validate the V1 production candidate: marketing and app HTTPS/domain/cookie/DNS,
   monitoring, and production smoke checks.
5. Lock and submit the native release against the stable deployed service. The store gate includes
   signing/EAS builds, permissions/privacy labels, screenshots, reviewer access/notes,
   Universal/App Links, direct account deletion, GAP-038 native parity, and real-device production
   checks. Christian forms the LLC and completes business setup during store review. Go live only
   after store approval and the final production-readiness decision.

## GAP-020 Slice A Status — Opaque Desktop Call Handoff (Backend)

**Status:** Implemented, uncommitted. Migration `20260719212740_AddKeepCallHandoffs` applied
(Christian authorized Claude to run `dotnet ef migrations add`/`database update` as a one-time
override of the normal Christian-runs-migrations policy). All tests pass: `KeepCallHandoffApiTests`
8/8, sibling `KeepIntakeSmsHandoffApiTests` 14/14 (regression-checked), unit tests 36/36. Awaiting
Christian's diff review and the file-count re-confirmation below before commit. Read ADR-448 for the
locked contract.

**Bug found and fixed during migration verification:** `EfKeepCallHandoffPersistence` and the
pre-existing `EfKeepSmsHandoffPersistence` both filtered on `!h.IsDeleted`, a computed
(non-mapped) property on `BaseEntity` that EF Core cannot translate to SQL — this was latent in
the SMS sibling too (no prior test exercised its `GET /keep/share-sms/{token}` resolve success
path). Fixed both to filter on `h.DeletedAtUtc == null` directly, per Christian's approval. This
adds `EfKeepSmsHandoffPersistence.cs` as a 10th production file (15th total) beyond the
originally agreed 8/13 — Christian approved this specific fix but has not yet re-confirmed the
final count for commit.

**Locked decisions (2026-07-19, carried into ADR-448):** distinct `KeepCallHandoff` entity (not an
extension of `KeepSmsHandoff`); Slice C will extract a narrow shared call-handoff QR component/hook
used by both `CustomerContactStrip` and the Log external contact modal, without making either modal
call the other; the sibling `/keep/share-sms/{token}` resolver is repaired in this same slice
(redaction, `Cache-Control: no-store, private`, and `public-intake` rate limiting), not deferred.

**Files changed (uncommitted):**

- New: `src/OpHalo.Keep.Core/Entities/KeepCallHandoff.cs`,
  `src/OpHalo.Keep.Application/Requests/IKeepCallHandoffPersistence.cs`,
  `src/OpHalo.Keep.Application/Requests/CreateCallHandoffService.cs`,
  `src/OpHalo.Keep.Infrastructure/Persistence/EfKeepCallHandoffPersistence.cs`,
  `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepCallHandoffConfiguration.cs`,
  `docs/decisions/ADR-448-opaque-desktop-call-handoff.md`,
  `tests/OpHalo.UnitTests/Keep/CreateCallHandoffServiceTests.cs`,
  `tests/OpHalo.IntegrationTests/Api/KeepCallHandoffApiTests.cs`.
- Edited: `src/OpHalo.Api/Keep/KeepEndpoints.cs` (new `POST /keep/requests/{requestId}/call-handoff`
  and `GET /keep/share-call/{handoffToken}`; added `Cache-Control`/rate-limiting to the existing
  `GET /keep/share-sms/{handoffToken}`), `src/OpHalo.Api/Keep/KeepServiceCollectionExtensions.cs`
  (DI registration), `src/OpHalo.Api/Helpers/PublicTokenPathRedactor.cs` (redact `/keep/share-call/`
  and `/keep/share-sms/`), `src/OpHalo.Api/Helpers/ErrorHttpMapper.cs` (map the 3 new
  `KeepRequest.CallHandoff*` error codes — found unregistered during implementation; without this
  fix they fell through to the mapper's default status), `docs/decisions/decision-index.md` (ADR-448
  row, next-free-id bumped to ADR-449), `tests/OpHalo.UnitTests/PublicTokenPathRedactorTests.cs`
  (redaction cases for both token paths).
- Gate note: this is 9 production files / 14 total (one over the originally agreed 8/13 count),
  because the `ErrorHttpMapper` fix was a genuine correctness gap discovered mid-slice, not scope
  creep. Christian has not yet re-confirmed this final count.

**Verified so far:** `dotnet build` clean for `OpHalo.Api`, `OpHalo.Keep.Infrastructure`, and both
test projects. `CreateCallHandoffServiceTests` + `PublicTokenPathRedactorTests`: 36/36 passing.
`KeepCallHandoffApiTests` compiles and correctly reaches the test database, but every test currently
fails with EF Core's `PendingModelChangesWarning` — expected, since `keep_call_handoffs` has no
migration yet. This is the only known blocker; it is not a code defect.

**Exact next step (before commit):** Christian runs
`dotnet ef migrations add <Name> --startup-project src/OpHalo.Keep.Infrastructure` (per
`reference_ef_migration_commands` — never `Api` or `Foundation` alone), applies it, then Claude
re-runs `dotnet test tests/OpHalo.IntegrationTests --filter FullyQualifiedName~KeepCallHandoffApiTests`
to confirm all cases pass (auth boundary, not-found, success/no-raw-phone-in-url, resolve success,
resolve Cache-Control, invalid-token 404, and the `/keep/share-sms/` sibling Cache-Control
regression). Only after that passes and Christian reviews the diff should Slice A be committed.

**After Slice A is committed:** proceed to Slice B (public `share-call` resolver page in
`ophalo-web`, mirroring `web/ophalo-web/src/app/keep/share-sms/[handoffToken]/page.tsx` +
`SmsHandoffView.tsx`, but launching `tel:` instead of `sms:`), then Slice C (wire
`CustomerContactStrip.tsx`'s `CallQrModal` and `RequestDetail.tsx`'s Log external contact modal to
call `POST /keep/requests/{requestId}/call-handoff` and encode the returned `handoffUrl` — via the
locked shared QR component/hook — instead of raw `tel:{phone}`). Each is its own session per the
batch-size gate.
