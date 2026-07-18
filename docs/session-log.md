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
- **R90b-2b / GAP-033 Tracker/terminal identity projection:** complete, committed in `b2e3b92`.
  `KeepRequestPageLookup` and `GetRequestByPageTokenAsync` now carry the same
  `LogoUrl`/`WebsiteUrl`/`Phone` identity (never email), threaded through
  `KeepPublicCustomerContext` → `KeepCustomerPageResult` and populated in both
  `KeepCustomerPageMapper.BuildExpiredResult` and `BuildActiveResult`, so a known business retains
  identity at a terminal tracker state. `KeepCustomerPageTests`: 17/17 integration tests pass
  (adds 3 new identity-projection tests); the four named `IKeepRequestDetailPersistence` fakes:
  65/65 unit tests pass. The same commit also fixed a stale `KeepCustomerPageTests` assertion
  (introduced in `474fc7d`, copy-pasted from the operator-detail boundary test) that incorrectly
  expected `feedbackComment` absent from the customer page response — it is the customer's own
  submitted comment (S84/`292d03d`) and is consumed by `CustomerTrackerView.tsx`.
- **R90a delivery-alignment correction:** complete. `CreateKeepPublicIntakeService` no longer sends
  an automatic tracker-link email after public intake commit; `TrySendTrackerLinkEmailAsync` and its
  call site are removed, along with the now-unused `IEmailSender`/`IOptions<MagicLinkSettings>`
  constructor dependencies. `KeepPublicIntakeServiceTests` dropped the three tracker-email tests and
  their fakes (`NoOpEmailSender`, `RecordingEmailSender`, `FailingEmailSender`); 67/67 unit tests
  pass. R90a is now fully decision-complete.
- **R90b-3a / GAP-033 Tracker identity rendering:** complete, committed in `fcd3152`. The tracker
  routes (`r/[pageToken]`) now render the logo/website/phone identity the API already returned
  (R90b-2b) but the frontend previously dropped: `KeepBusinessHeader` shows a hosted logo when
  configured (initials fallback otherwise); a new `KeepConfiguredContact` renders `tel:`/`https://`
  recovery links only for known-business responses, never unknown/invalid tokens. Both routes gained
  dynamic, token-free `generateMetadata` titles (falls back to a generic title when the business is
  unknown), preserving `noindex`/`no-referrer`. `TrackerExpiredView` is now shared between the SSR
  410 path and the client-side expiry transition. Verified via `tsc --noEmit`, `next build`, and
  manual checks against a live (restarted) API: active state, unknown-token non-enumeration,
  no-logo/no-contact fallback. Not yet visually confirmed with a business that has logo/website/phone
  configured, or a genuinely expired (30+ day) tracker — both share the same components already
  exercised by the no-identity checks and by backend `KeepCustomerPageTests` coverage.
- **Deferred:** explicit customer-facing OffSeason banner. See
  `docs/pilot-readiness-bug-tracker.md` GAP-033 for the pre-deployment follow-on decision — the
  public customer-page contract has no `IsOffSeason` field today, so R90b-3 does not add one.

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

## Next Selected Code Slice — R90b-3b

**Goal:** Intake identity rendering — form + submitted-receipt screens on the two public-intake
routes (`intake/[token]`, `s/[slug]`), completing the rendering work R90b-3a started on the tracker
routes. See `docs/build-log/` history for 3a; expected surface is `intake/[token]/page.tsx`,
`s/[slug]/page.tsx`, and `intake/[token]/IntakeForm.tsx` (3 production files), reusing the
`KeepBusinessHeader`/`KeepConfiguredContact` components 3a already extended.

## R90b Follow-On Order

1. **R90b-2a:** intake-info identity projection — complete.
2. **R90b-2b:** tracker and known-business terminal identity projection, including expired/active
   mapper branches and non-enumeration coverage — complete.
3. **R90a delivery-alignment correction:** remove automatic tracker-link email — complete.
4. **R90b-3a:** tracker-route identity rendering (logo/website/phone, dynamic token-free titles) —
   complete, committed in `fcd3152`.
5. **R90b-3b:** intake-route identity rendering (form + submitted receipt) — next.
6. **R90c / GAP-035:** normal-browser auth-entry shell and recovery states, preserving ADR-390
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
