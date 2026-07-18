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
- **R90a / GAP-033 Public Intake Trust:** `df07717` added intake trust/validation work and
  `cc33ec3` removed the misleading tracker-email claim and timer/interstitial behavior. The
  post-submit continuity decision has since been revised: successful intake navigates directly to
  the private tracker, which shows a one-time dismissible welcome banner with request reference and
  tracker-use guidance. Later business messages repeat the same tracker link.
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
  manual checks against a live API: active state, unknown-token non-enumeration, no-logo/no-contact
  fallback; a real configured phone was later confirmed rendering correctly (R90b-3b verification
  below). Not yet visually confirmed on a genuinely expired (30+ day) tracker — shares the same
  `TrackerExpiredView` component already exercised by backend `KeepCustomerPageTests` coverage.
- **R90b-3b / GAP-033 Intake identity rendering + post-submit continuity:** complete, committed in
  `7c95dc2`. Supersedes the earlier three-file submitted-receipt plan per the revised post-submit
  decision: successful intake now navigates directly to `/keep/r/{pageToken}?welcome=1` (the
  pageToken was already in the create-intake response, previously unused) instead of rendering a
  separate receipt screen; `IntakeForm`'s dead `success` stage is removed. The tracker
  (`CustomerTrackerView`) shows a one-time, dismissible welcome banner when `welcome=1` is present;
  the query param is stripped via `router.replace` on load so refreshes/later visits never re-show
  it. `KeepConfiguredContact` was moved onto intake's pre-entry identity block (both
  `intake/[token]` and `s/[slug]`) rather than a terminal screen, per GAP-033's
  identity-before-form-entry requirement. Also fixes stale intake-form copy that predated the R90a
  email removal and falsely claimed an automatic tracker-link email. Verified via `tsc --noEmit`,
  `next build`, and manual checks against a live API: form-stage identity with real logo/website/phone
  data (confirmed `tel:901-888-8888` rendering), stale-copy removal, live intake→pageToken contract,
  welcome banner present only with the query param and absent on ordinary visits. Not yet visually
  confirmed via an actual browser form submission (verified via direct API/URL checks simulating the
  same data flow) — Christian can spot-check the end-to-end browser redirect if desired.
- **R90b-3b polish follow-up:** complete, committed in `2e4dac8`, per Christian's design review.
  Intake form sections reordered so customer-identifying fields come first (Your name, Mobile phone,
  Email, then Preferred contact method), followed by request details and service address. All
  required fields (name, phone, street address, city, state) now show a red asterisk plus visible
  "Required" text instead of asterisk-only, for accessibility; email shows "Optional" by default and
  the same required treatment when Email is the chosen contact preference. Mobile phone formats live
  as typed (`(512) 555-0199`). The tracker welcome banner is redesigned from a floating white card to
  a full-width navy band with white text and a "Got it" dismiss button at the far right (not sticky);
  copy simplified to "Welcome to your request page" / "Use this page to see updates, add details, ask
  a question, or request a call from {Business}."; the reference code was removed from the banner
  (stays in the status card only, where it already belonged). Verified via `tsc --noEmit`,
  `next build`, and manual checks against a live API confirming field order, required/optional marks,
  address-disclosure placement, and banner content/styling.
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
- A configured business phone is a secondary public recovery/contact route; it does not replace the
  tracker as the post-submit customer destination.

## Next Selected Code Slice — R90c / GAP-035

**Goal:** Normal-browser auth-entry shell and recovery states, preserving ADR-390 sterile
mobile-handoff restrictions. Pre-work not yet confirmed complete — requires discovery before an
implementation-ready file-level gate.

## R90b Follow-On Order

1. **R90b-2a:** intake-info identity projection — complete.
2. **R90b-2b:** tracker and known-business terminal identity projection, including expired/active
   mapper branches and non-enumeration coverage — complete.
3. **R90a delivery-alignment correction:** remove automatic tracker-link email — complete.
4. **R90b-3a:** tracker-route identity rendering (logo/website/phone, dynamic token-free titles) —
   complete, committed in `fcd3152`.
5. **R90b-3b:** intake-route identity rendering + direct post-submit tracker navigation with
   one-time welcome banner — complete, committed in `7c95dc2`.
6. **R90c / GAP-035:** normal-browser auth-entry shell and recovery states, preserving ADR-390
   sterile mobile-handoff restrictions — next.

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
