# Session Log — OpHalo Foundation

**Last updated:** 2026-07-17
**Branch:** `main` tracking `origin/main`
**Deployment posture:** Not deployment-ready. Active launch gaps remain in
`docs/pilot-readiness-bug-tracker.md`.
**Current work:** Public-trust remediation under GAP-033. Build/archive detail belongs in
`docs/build-log/`; decisions belong in `docs/decisions/decision-index.md`.

## Session Protocol

For every implementation slice:

- Read this file and the relevant active tracker/build brief before editing.
- Preflight named signatures, permissions, failure modes, and focused tests with `rg`.
- Present the file-level gate before production edits. Unless Christian explicitly overrides it,
  keep to three mutation families, eight production files, and twelve total changed files.
- Preserve fail-closed account, membership, action-policy, public-token, and concurrency behavior.
- Add focused regression coverage, run proportionate checks, self-review visibility/token policy,
  and commit only after Christian approves the completed diff.

## Current Baseline

- **Build 086 Request Detail launch pass:** complete and committed; its evidence is in
  `docs/build-log/086-request-detail-v1-launch-pass.md`.
- **GAP-027 Request List parity:** complete and committed; its decisions/evidence are in Build 087
  and the tracker.
- **R90a / GAP-033 Public Intake Trust:** the customer-facing correction is complete. `df07717`
  added intake trust/validation work; `cc33ec3` removed the success-page tracker CTA, save-link
  guidance, tracker-email claim, and redirect. The page now gives a stable business-branded receipt
  confirmation and safe reference. `tsc --noEmit`, `next build`, and Christian's visual acceptance
  passed.
- **R90b-1 / GAP-033 Public identity settings:** committed in `333bcd7`. It adds optional
  `LogoUrl` and `WebsiteUrl` to `KeepBusinessProfile`, validated as absolute HTTPS URLs; they are
  Owner/Admin settings only and are not publicly projected yet. There is no upload pipeline and no
  domain-ownership verification. EF migration `20260718013055_R90b1AddBusinessProfilePublicIdentity`
  (adds nullable `logo_url`/`website_url` on `keep_business_profiles`) has been generated, verified,
  and applied to the local database.
- **R90b-2a / GAP-033 Intake-info identity projection:** complete. `IKeepIntakePersistence` now
  returns `KeepPublicIntakeInfo(BusinessName, LogoUrl, WebsiteUrl, Phone)` from
  `GetPublicIdentityByTokenHashAsync`/`GetPublicIdentityBySlugAsync`; both
  `GET /keep/public-intake/{token|slug}/info` endpoints return
  `{ businessName, logoUrl, websiteUrl, phone }` (never email). 70/70 unit tests and 38/38
  `KeepIntakeApiTests` integration tests pass, including seeded-identity and unknown/revoked-token
  non-enumeration coverage.

## Locked Public-Trust Decisions

- Public-facing identity is business-first with secondary OpHalo Keep attribution. When configured,
  it may show a hosted logo URL, website URL, and customer-facing phone; never public email.
- Logo/website values are input-validated absolute HTTPS URLs, not DNS/ownership-verified. Do not
  label a business or website as "verified" or imply OpHalo independently verified it.
- The existing polished-initials fallback is used when no logo exists.
- Known-business terminal states retain safe identity and a configured-contact recovery route.
  Unknown/invalid capability tokens remain non-enumerating and disclose neither business identity
  nor contact data.
- Browser titles/metadata may identify a known business and outcome but must never contain a token,
  address, customer name, or other private request data. Tracker pages retain `noindex` and
  restrictive referrer behavior.
- Keep does not send backend customer SMS or ingest SMS replies in V1. Broad customer
  messaging/notification workflows remain deferred.

## Active Decision-Alignment Finding — R90a Delivery

The locked GAP-033 direction requires the business's first real email/text to deliver the tracker
link. Preflight found that `CreateKeepPublicIntakeService` still calls
`TrySendTrackerLinkEmailAsync` immediately after public intake commits. This backend behavior is not
shown in the corrected UI but remains inconsistent with the decision. After the in-progress R90b-2a
batch is completed/committed, run a narrow R90a delivery-alignment corrective slice to remove that
automatic tracker-email call and its now-unused dependencies/tests. Do not represent R90a as fully
decision-complete until that correction lands.

## Next Selected Code Slice — R90b-2b / GAP-033 Tracker/Terminal Identity Projection

**Goal:** Extend the known-business tracker/terminal customer-page response with the same
public-safe identity (name/logo/website/phone, never email), populated in both the expired and
active mapper branches so a known business does not go anonymous at a terminal state. Under the
hard batch gate this is a separate slice from R90b-2a (already complete).

**Expected surface (six production files):** `IKeepRequestDetailPersistence`,
`EfKeepRequestDetailPersistence`, `KeepPublicCustomerContext`, `KeepPublicCustomerAccessGuard`,
`KeepCustomerPageResult`, `KeepCustomerPageMapper`. Test fan-out includes four existing
`IKeepRequestDetailPersistence` fakes (`KeepRequestDetailServiceTests`,
`KeepPushCustomerIntentHookTests`, `KeepCreateBusinessRequestServiceTests`,
`KeepPushAssignmentHookTests`) needing the new lookup fields, plus non-enumeration coverage for
unknown page tokens.

**Out of scope:** public rendering/titles/recovery UI (R90b-3), file uploads, email exposure,
automated messaging, customer login, and unrelated schema changes.

## R90b Follow-On Order

1. **R90b-2a:** intake-info identity projection — complete.
2. **R90b-2b:** tracker and known-business terminal identity projection, including expired/active
   mapper branches and non-enumeration coverage — next.
3. **R90b-3:** public rendering, safe titles, and configured-contact recovery UI.
4. **R90c / GAP-035:** normal-browser auth-entry shell and recovery states, preserving ADR-390
   sterile mobile-handoff restrictions.

## Standing Technical Boundaries

- Keep internal notes, internal timing, service location, and public customer email out of public
  customer surfaces.
- Do not change server action policy or lifecycle transitions merely to simplify PWA presentation.
- Do not disclose raw tokens in logs, diagnostics, persisted frontend state, or long-lived UI state.
- `ophalo-app` is the authenticated workbench; `ophalo-web` is the public/customer surface;
  `OpHalo.Api` remains the authority for auth, authorization, rate limiting, email, and persistence.

**Topology:** production `ophalo.com`/`www.ophalo.com` → `ophalo-web`, `app.ophalo.com` →
`ophalo-app`, `api.ophalo.com` → `OpHalo.Api`. Local: web `:3000`, app `:5173`, API `:5092`.

## Remaining Path To Production

1. Complete or explicitly defer the selected P0/P1 launch slices, beginning with the R90b sequence.
2. Run the Build 089 desktop operational gate, then the real-device mobile PWA gate.
3. Deploy and validate a Vercel production candidate; complete iPhone/Android and production smoke
   verification before promotion.
