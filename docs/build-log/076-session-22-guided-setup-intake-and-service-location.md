# Build Log 076 — Session 22: Guided Setup, Intake Sharing, And Service Location Plan

**Started:** 2026-07-08
**Status:** Direction changed 2026-07-09 — S22c guided checklist work paused; Settings/Get Started redesign required
**Session name:** S22 day-zero readiness / settings redesign / intake / service-location migration
**Next free ADR before this log:** ADR-428
**Next free ADR after this log:** ADR-430

---

## Purpose

This log captures the product decisions from the Getting Started, Settings, business intake, customer
page, and service-location discussion. It is not a new onboarding foundation from scratch.

Session 12 already delivered the first onboarding foundation in
`docs/build-log/066-session-12-account-settings-and-onboarding.md`:

- Keep setup/profile APIs;
- response policy APIs;
- `KeepProductOpsEvent`;
- `GET /keep/setup/onboarding`;
- manual onboarding mark endpoints;
- a flat checklist UI in `web/ophalo-app/src/pages/Home.tsx`.

This session originally defined the next product migration:

```text
flat onboarding checklist -> guided setup + contextual setup bar
```

The existing Session 12 model answers "which setup signals are complete?" This session answers "how
should owners understand and activate Keep without being forced through confusing admin homework?"

On 2026-07-09, review of the in-progress Getting Started and Settings screens changed the product
direction. The earlier guided checklist direction was still too setup-heavy: it made owners feel like
they had to build Keep from scratch before the product was useful. The revised direction is:

```text
admin setup chores -> day-zero readiness + focused settings tabs + lightweight verification
```

Keep must launch in a functional state by default. Owners visit Settings to adjust how Keep presents
and routes work, not to assemble the basic system.

---

## Next Session Quick Start

Stop the S22c guided checklist/setup-bar direction. It was coded locally but is not approved as the
product path.

Start the redo from the revised direction below:

1. Preserve the S22b backend work only where it remains useful as compatibility/state plumbing.
2. Rework Settings into three focused Owner/Admin tabs:
   - `Public Link & Profile`;
   - `Response Policy`;
   - `Team`.
3. Make the public intake link day-zero ready by auto-provisioning it once the account has a business
   name/profile context. Owners can copy, preview, and later replace it; they should not need to
   create it before Keep works.
4. Replace Getting Started as a seven-step checklist with a lightweight verification/on-ramp surface:
   verify the public link, add/review the first request, and invite teammates only if useful.
5. Build the Public Link & Profile view first, with a live interactive customer intake preview.

Before coding, perform a mechanical preflight against the current uncommitted frontend files and the
S22b backend additions. Present the file-level gate and call out any code that should be reverted,
adapted, or left untouched.

---

## Current-State Findings To Preserve

Confirmed during S22a preflight (2026-07-09):

- `KeepOnboardingService` derives a flat `KeepOnboardingChecklistResult` (10 bool fields) from
  `KeepProductOpsEvent` rows via `IKeepProductOpsPersistence.GetOnboardingDataAsync`.
- `GET /keep/setup/onboarding` is Owner/Admin-only (`Keep.SettingsManage`); 3 manual-mark POSTs
  exist for quick-capture-exercise, tracker-review, and spam-classification.
- `Home.tsx` renders the flat checklist for all authenticated users; `Settings.tsx` has an
  `OnboardingSection` that reads the same endpoint.
- `apiClient.ts` exposes `getOnboardingChecklist`, `markQuickCaptureExercise`,
  `markTrackerReview`, and `markSpamClassification`.
- `EfKeepProductOpsPersistence.GetOnboardingDataAsync` runs five live DB queries (events, intake
  link, non-owner members, devices, requests); no deferred events (`FirstRequestCreated`,
  `FirstOperatorInvited`, `FirstMobileDeviceRegistered`) are recorded yet — only
  `ProfileAndContactSaved`, `PolicySaved`, and the three manual marks.
- `KeepProductOpsEventType` has 17 values; three member/device/engagement signals are marked
  deferred in comments.
- `Account` entity has `BusinessName` and `TimeZone`; no `IntendedTeamSize` field exists anywhere.
- No `KeepAccountSetupPreferences` or equivalent Keep-owned preferences entity exists.
- ADR-295 states automatic intake provisioning. The earlier S22a preflight incorrectly tried to
  reverse this. The 2026-07-09 redesign restores auto-provisioning as the product direction.
- Existing checklist mixes business setup, user learning, response policy, mobile registration, team
  setup, quick capture, tracker review, and spam training as equal-weight items.
- Response-policy controls are useful admin controls but confusing as day-one setup. They belong in a
  focused Settings tab with plain-language business guidance, not in Getting Started.

---

## Locked Product Direction

### Day-Zero Readiness Model

Keep must be useful immediately after signup/account creation. The owner should not have to complete
a setup checklist before the product can accept and manage requests.

Day-zero defaults:

- **Public intake link:** automatically provision one active intake link after the business has a
  usable business name/profile context. Use the business name for the generated slug where possible.
- **Response policy:** pre-populate the balanced V1 defaults from the existing setup policy contract
  (including the 15-minute first response target, 240-minute standard response target, and existing
  status-check threshold default).
- **Business profile:** use the signup/account business name and timezone defaults already captured
  by the auth/provisioning flow. Customer-facing phone/email remain optional and must never default
  to the owner's login email or personal phone unless explicitly provided.

Owner/Admin users visit Settings to adjust a working system, not to make Keep functional.

### Settings Workspace Model

Settings is still one Owner/Admin-only top-level nav item, but the previous single stacked page is
rejected. The Settings workspace must be split into outcome-focused sub-sections:

- `Public Link & Profile` — the business's online front door, public contact/profile, copy/share
  controls, replacement controls, and a live intake-page preview.
- `Response Policy` — customer expectation timing, with compact plain-language guidance beside each
  field.
- `Team` — roster and invite/member controls, with solo-business reassurance and no setup pressure.

Do not keep a visible Settings `Onboarding` section in the primary V1 workspace. Existing
`GET /keep/setup/onboarding` and manual mark endpoints may remain for compatibility/support, but the
main owner UI should not present an onboarding status matrix.

### Getting Started Model

Getting Started becomes a lightweight verification/on-ramp surface, not an administrative checklist
and not a second Settings page.

Primary owner-facing jobs:

1. Verify your public request link.
2. Add or review the first customer request.
3. Invite teammates only if someone else helps manage requests.

Getting Started may show progress, but it should not make optional work feel overdue. It should never
show `Build your team` as mandatory for a solo shop. It should not split `Create intake page` and
`Share intake page` into separate owner chores when the link exists by default.

The setup bar may survive only as a very quiet verification reminder if it proves useful after the
redesign. It must not reintroduce seven-step checklist pressure.

### Superseded Guided Setup Model

The following earlier S22a direction is superseded by the 2026-07-09 redesign and must not guide new
implementation except where explicitly reused as compatibility plumbing.

Getting Started becomes a guided setup hub with separate focused pages, not one flat checklist.

Each page has one job, one primary action, and a `Do later` path unless the user is actively trying
to perform an action that requires missing setup.

Owner/Admin guided setup is web/PWA-owned, but it must not assume a laptop or large screen. New
business owners may sign up and complete first-run setup from a phone browser, tablet, laptop, or
office desktop. Core setup pages must therefore be usable at phone widths. Deeper admin maintenance
can remain denser, but the first-run path must not require a large screen.

Superseded primary Owner/Admin setup:

1. Business info.
2. Add first request.
3. Review customer page.
4. Create/share intake page.
5. Build team when intended team size is greater than one.
6. Use Keep on mobile.

Optional / advanced setup:

- response-policy tuning;
- spam/test handling;
- deeper settings;
- future metrics-led response tuning.

Response policy remains in Settings, but is not first-run setup. Guided response tuning is deferred
until Keep can show response-time and attention metrics that make the tradeoffs understandable.

### Business Setup Versus User Getting Started

Split setup state by meaning:

- **Business Setup** is account-level and Owner/Admin-owned.
- **User Getting Started** is person-level and role-aware.

Business setup examples:

- business info;
- intake page;
- team setup;
- intended team size;
- first shared intake page;
- future response-policy tuning.

User getting-started examples:

- review customer page;
- first capture/request for that user;
- first customer-visible update;
- first contact log;
- mobile operator education.

Operators do not see editable Business Setup. Operator onboarding is mobile-first and deferred to a
dedicated mobile onboarding session. PWA operator onboarding should remain minimal/contextual.

### Setup Bar

Owner/Admin gets a quiet setup reminder bar while core setup remains useful.

Rules:

- shown at top of the PWA shell;
- horizontal on desktop/tablet;
- one compact prompt or horizontal chips on narrow PWA;
- max three visible items;
- `View all` links to the full Getting Started hub;
- no permanent dismiss-all;
- each item can be completed or marked `Do later`;
- completed and `Do later` items disappear from the bar;
- contextually blocking setup overrides normal priority;
- not shown to Operators/Viewers.

On mobile web / narrow PWA, the reminder should collapse to a single compact next-step prompt or
short horizontal chips. It should not become a tall checklist or block the workbench.

Allowed setup bar items:

1. Business info.
2. Add first request.
3. Review customer page.
4. Create intake page.
5. Share intake page.
6. Build team.
7. Use mobile.

Excluded:

- response policy;
- spam/test training;
- advanced settings;
- audit history;
- billing/commercial;
- broad help/tutorial content.

Priority:

1. Blocking setup for the current attempted action.
2. Business info.
3. Add first request.
4. Review customer page.
5. Create intake page.
6. Share intake page.
7. Build team.
8. Use mobile.

### Do Later And Completion

Use `Do later`, not `Skip` or `Complete later`.

`Do later`:

- defers a step without marking it complete;
- removes it from urgent/primary prompts for now;
- keeps it available in Getting Started;
- can return contextually when the action becomes useful again.

Setup blockers:

- show only when the user attempts an action that requires missing setup;
- link directly to the required guided page;
- return the user to the attempted action after completion;
- never force unrelated setup.

### Business Info

Business info includes:

- business display name;
- customer-facing phone;
- customer-facing email;
- explicit `Do not show a public phone or email`;
- timezone.

Rules:

- business display name is required;
- public phone/email are optional;
- never publish owner login email or personal phone by default;
- if no public contact is chosen, customer pages simply omit fallback contact;
- do not show "No public contact listed" to customers;
- business info is complete when display name and timezone are confirmed and either public contact is
  provided or no-public-contact is explicitly chosen.

Business display name changes update current customer-facing pages, including existing customer
request pages. URL stability is separate from display-name changes.

Business info changes are internally audited:

- actor;
- timestamp;
- changed field;
- safe old/new values or safe summary.

Audit history is stored for V1 and can be queried for support/internal investigation. A dedicated
Owner/Admin audit UI is deferred unless it falls out cheaply from Settings work.

### Intended Team Size

Collect intended Keep team size inside Getting Started / Business Setup, not signup.

Options:

- `Just me`;
- `2-5 people`;
- `6+ people`.

Behavior:

- `Just me` hides/demotes Build team and excludes it from core setup completion;
- `2-5 people` / `6+ people` show Build team as recommended setup;
- team management remains available in Settings regardless;
- answer can change later;
- seat limits and billing still come from entitlements, not this answer.

Subscription / seat-limit guardrail:

- intended team size is a setup-planning answer, not a commercial entitlement, Stripe quantity, seat
  override, or billing-plan source of truth;
- future Stripe/subscription state must continue to flow through the commercial/entitlement boundary,
  especially `AccountEntitlements.MaxUserSeats` / server-provided `seatUsage`;
- team invite and activation flows must keep enforcing the server-reported seat limit, regardless of
  the intended team-size answer;
- the guided Build team step may explain that the current plan has a team-member limit, but it must
  not promise that selecting `2-5 people` or `6+ people` increases available seats;
- avoid storing intended team size directly on Foundation `Account` unless a later ADR deliberately
  promotes it to product-neutral account profile. Preferred S22 storage is Keep-owned setup state or
  Keep-owned business setup preferences, separate from billing and entitlements.

If future subscription plans limit team members, guided setup should behave as:

- `Just me`: Build team is demoted/optional.
- Team intent larger than one and seats available: Build team is recommended.
- Team intent larger than one and seat limit reached: Build team remains available, but the primary
  action routes to Team with server-provided seat-limit copy such as `Team limit reached`; upgrading
  or subscription management remains a later billing surface, not part of first-run setup.

### Mobile Setup

Mobile is required for core setup completion, but not required to create the account or first request.

Rationale: the PWA is the business monitor/control center, but mobile is where service-business
customer contact, SMS/call handoff, field updates, on-site intake sharing, and fast contact logging
will happen.

For V1 setup completion, at least one Owner/Admin mobile device should be registered. Future team
setup may require at least one Operator mobile device, but that is a later mobile-onboarding decision.

### Intake Page Lifecycle And Sharing

The intake page is a public job-capture channel, not a private admin link.

V1 keeps the shared URL token-based. The link should be created automatically through the Keep setup
boundary once the account has a usable business name/profile context. The generated slug should come
from the business name where possible, with collision-safe suffixing. The owner does not need to
claim a visible public handle during first-run setup. Rich public handle management remains deferred.

V1 intake setup:

- active by default;
- preview before sharing or replacing;
- no logo upload or brand color in V1;
- business logo upload deferred to V1.1 personalization;
- brand colors deferred beyond V1.1 unless accessibility-safe customization is designed;
- Owner/Admin can copy/share/preview and explicitly replace for recovery;
- Operator can preview/copy/share an active intake page where a share utility is exposed;
- Viewer gets no V1 share controls;
- mobile may expose copy/share for an active intake page, but mobile does not create/replace the
  intake page.

The Public Link & Profile Settings tab should make the link tangible:

- editable safe business profile/contact fields;
- copy link;
- open/preview link;
- live interactive customer intake preview in a phone-sized panel;
- replacement/regeneration behind explicit confirmation and stale-link warning.

Important backend gap:

- V1 needs a way for permitted users to copy/share the active intake page later without forcing link
  replacement.
- Preferred implementation is a retrievable public intake page identifier/link that is safe to show
  later while preserving raw-token safety.
- Fallback is Owner/Admin retrieval of the active intake URL with strict token-redaction/logging
  posture.

Create/replace audit:

- actor;
- timestamp;
- lifecycle action;
- successor/revoked link row identity;
- never log or store the raw token.

### Public Intake Fields

Public intake and authenticated business-created request forms require service location.

Public intake fields:

- name;
- phone;
- service address line 1;
- service address line 2 optional;
- city;
- state;
- ZIP optional;
- request details;
- email optional.

State is a required US dropdown in V1, stored as a normalized two-letter code. International/localized
address support is deferred.

Service-location helper copy:

```text
Shared with {Business} only. Not shown on your request page.
```

### Service Location Privacy And Staff Behavior

Service location is internal-only operational data:

- visible only to authenticated staff;
- never shown on unauthenticated customer request pages;
- never included in public metadata or share previews;
- never treated as customer-facing tracker content.

Authenticated staff can edit/correct service location after request creation when permitted.
Customers cannot directly edit service location from the public customer page; they may message
corrections, and staff applies them internally.

Service-location changes are internally audited with actor, timestamp, and safe changed-field summary.

Business-created requests may use an explicit `Service location unknown` path when staff do not have
the address yet. This creates the request successfully and records an internal signal/cue:

```text
Service location needed
```

The cue is internal, may contribute to Needs Attention by server policy, and clears when service
location is filled in.

List/control rows may show city/state only. Request detail shows full service location to
authenticated staff.

Service location should be searchable by city/state/ZIP in V1. Office/branch/service-region routing
is deferred to V1.1+.

### Public Intake Success And Customer Page

After public intake submission, the customer immediately gets:

- confirmation that the request was sent;
- reference code;
- link/button to view the customer request page;
- simple copy explaining that they can check updates or send more details there.

No customer account, login, email verification, or phone verification is required in V1.

Success state:

- primary action: `View your request page`;
- secondary action: `Save this link`;
- offer a device/browser save hint:
  `Tip: save this page to your bookmarks, home screen, or messages so you can find it again.`

Customer page:

- preserve current V1 action model: send update/question, request update, ask for a call, share
  availability, add details, share page, request history, feedback after close where supported;
- add a compact capability hint, not a tutorial;
- prefer `No longer needed` over `Cancel request`.

Suggested cancellation confirm copy:

```text
Mark this as no longer needed?
We'll let {Business} know you no longer need help with this request.
```

Suggested capability hint:

```text
What you can do here
Check the latest status, send more details, or ask a question about your request.
```

---

## S22a Preflight Output

Completed 2026-07-09. The S22a decisions below are preserved for history, but the checklist/setup-bar
UX direction was superseded later the same day by ADR-428. Treat any conflicting S22a decision as
overridden by the day-zero readiness model above.

### Locked Decisions

**D1 — Do later storage: `KeepSetupDeferral` table**

New entity: `(Id, AccountId, Step KeepSetupStep, DeferredAtUtc, ClearedAtUtc?, DeferredByAccountUserId)`.
Cleared automatically when the corresponding completion signal fires. Queryable, auditability-lite,
supports contextual reappearance without event-log archaeology.

**D2 — Hybrid setup contract**

`KeepProductOpsEvent` rows carry durable completion signals. `KeepSetupDeferral` carries UI deferral
state. Neither bleeds into the other's domain — events record "this meaningful thing happened,"
deferral records "the user chose not to address this now."

**D3 — Ship account-level Business Setup; define user-level track only**

S22 implements Business Setup (account-level, Owner/Admin-owned). User Getting Started (person-level)
is defined in contract/doc shape but not persisted this session. No `user_setup_state` entity in S22.

**D4 — Superseded: explicit intake creation; ADR-295 restored by ADR-428**

This earlier S22a decision is superseded. ADR-295 automatic provisioning is restored and amended by
ADR-428. The public intake link should exist by default after account/business profile provisioning.
The owner verifies, copies, previews, customizes safe display/profile fields, and can explicitly
replace the link for recovery. The primary UI must not require "Create intake page" before Keep is
usable.

**D5 — Setup bar in authenticated PWA shell**

Setup bar renders in `App.tsx` authenticated shell, gated on Owner/Admin role and remaining useful
setup. Global setup query. Page-level blockers link into the same setup model. Not page-specific.

**D6 — `IntendedTeamSize` is Keep-owned setup guidance, not Foundation account state**

`IntendedTeamSize` is stored in a new `KeepAccountSetupPreferences` entity (Keep layer, one row per
account), introduced in S22c alongside the business info "no public contact" preference. It must not
be added to `Account` in `OpHalo.Foundation.Core` unless a later ADR deliberately promotes it to a
product-neutral account profile field.

`IntendedTeamSize` affects only setup bar display priority:
- `JustMe`: Build team demoted/optional in setup bar.
- `TwoToFive` / `SixPlus`: Build team shown as recommended.

Team invite and seat-activation flows must continue enforcing `AccountEntitlements.MaxUserSeats`
regardless of `IntendedTeamSize`. Selecting `TwoToFive` or `SixPlus` must never imply seats are
available. When the seat limit is reached, Build team routes to Team management with server-provided
copy ("Team limit reached") — it does not surface upgrade flows or billing.

### Superseded V2 Setup Contract

The contract below was implemented as S22b/S22c setup plumbing, but it no longer represents the
approved product experience. Keep it only if it helps compatibility, deferral state, or a future
lightweight verification prompt. Do not build new owner UX around a seven-step checklist.

**New enum `KeepSetupStep` (Core, S22b)**

```
BusinessInfo = 1
AddFirstRequest = 2
ReviewCustomerPage = 3
CreateIntakePage = 4
ShareIntakePage = 5
BuildTeam = 6
UseMobile = 7
```

**Completion signal mapping**

| Step | Source | Notes |
|---|---|---|
| BusinessInfo | `ProfileAndContactSaved` event (existing) | S22b may sharpen to cover explicit no-contact decision |
| AddFirstRequest | `HasRequest` live query (existing) | Already recorded |
| ReviewCustomerPage | `FirstCustomerPageView` event (= 9, deferred) | V1: always-pending; links to customer page — no manual mark |
| CreateIntakePage | `IsIntakeLinkActive` live query (existing) | Superseded: active link should exist by default |
| ShareIntakePage | New `IntakeLinkShared = 18` event | Added in S22d (intake slice) when share action is built |
| BuildTeam | `HasNonOwnerActiveMember` live query (existing) | Demoted when `IntendedTeamSize = JustMe` |
| UseMobile | `HasDeviceRegistered` live query (existing) | Deferred event now actively derived |

**New response DTO `KeepBusinessSetupResult`**

```csharp
public sealed record KeepBusinessSetupResult(
    bool BusinessInfoComplete,
    bool AddFirstRequestComplete,
    bool ReviewCustomerPageComplete,
    bool CreateIntakePageComplete,
    bool ShareIntakePageComplete,
    bool BuildTeamComplete,
    bool UseMobileComplete,
    IReadOnlyList<KeepSetupStep> DeferredSteps,
    IntendedTeamSize? IntendedTeamSize);   // null until S22c backend

public enum IntendedTeamSize { JustMe = 1, TwoToFive = 2, SixPlus = 3 }
```

`IntendedTeamSize` enum lives in Keep Core; returned as null from `GET /keep/setup/guided` until
`KeepAccountSetupPreferences` is introduced in S22c.

**New endpoints (S22b)**

- `GET /keep/setup/guided` → `KeepBusinessSetupResult` (Owner/Admin only, `Keep.SettingsManage`)
- `POST /keep/setup/guided/defer/{step}` → 204

**Endpoint added in S22c**

- `PUT /keep/setup/guided/team-size` → 204

### Backward-Compatibility Plan

| Existing surface | Action |
|---|---|
| `GET /keep/setup/onboarding` | Keep intact — Settings.tsx still reads it |
| 3 manual-mark POSTs | Keep intact — no callers removed this session |
| `KeepOnboardingService` | Untouched |
| `Home.tsx` | Replace flat checklist with guided hub (links to future step pages) |
| `Settings.tsx` OnboardingSection | Untouched this session; migrated in S22g cleanup |

### File-Level Implementation Plan

#### S22b-backend — Guided Setup Domain and Deferral (first coding slice)

3 mutation families, 8 production files, within batch gate.

**Mutation family 1 — Core domain**

| File | Change |
|---|---|
| `src/OpHalo.Keep.Core/Entities/Enums/KeepSetupStep.cs` | New enum (7 steps) |
| `src/OpHalo.Keep.Core/Entities/Enums/IntendedTeamSize.cs` | New enum (3 values, Keep-owned) |
| `src/OpHalo.Keep.Core/Entities/KeepSetupDeferral.cs` | New entity |

**Mutation family 2 — Application service**

| File | Change |
|---|---|
| `src/OpHalo.Keep.Application/Setup/IKeepSetupDeferralPersistence.cs` | New interface |
| `src/OpHalo.Keep.Application/Setup/KeepBusinessSetupService.cs` | New service: GetAsync, DeferAsync |
| `src/OpHalo.Keep.Application/Setup/IKeepProductOpsPersistence.cs` | Add `GetBusinessSetupDataAsync` |

**Mutation family 3 — Infrastructure and API**

| File | Change |
|---|---|
| `src/OpHalo.Keep.Infrastructure/Persistence/EfKeepSetupDeferralPersistence.cs` | New persistence impl |
| `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepSetupDeferralConfiguration.cs` | New EF config |

Plus (not counted against 8-file limit): `Program.cs` adds 2 endpoint registrations; EF migration
adds `KeepSetupDeferral` table. `KeepProductOpsEventType` is not modified this slice
(`IntakeLinkShared = 18` deferred to S22d).

`IntendedTeamSize` storage (`KeepAccountSetupPreferences`) deferred to S22c, where the
business-info "no public contact" preference also lands. `GET /keep/setup/guided` returns
`IntendedTeamSize: null` until S22c.

**S22b-backend tests (12-file total gate includes these)**

- `tests/OpHalo.UnitTests/Keep/KeepBusinessSetupServiceTests.cs` — new
- `tests/OpHalo.IntegrationTests/Api/KeepGuidedSetupApiTests.cs` — new

Test coverage required:
- `GetAsync` returns correct step completions per event/DB state.
- Owner/Admin: 200. Operator/Viewer: 403. Unauthenticated: 401.
- `DeferAsync` records `KeepSetupDeferral` row; re-defer is idempotent.
- `DeferAsync` with an invalid step value returns `400`.
- `IntendedTeamSize` null until S22c (`GetAsync` returns null cleanly).
- No response-policy step appears in `KeepBusinessSetupResult`.
- `BuildTeamComplete` derives from `HasNonOwnerActiveMember` (server-authoritative); it is not
  influenced by `IntendedTeamSize` on the server — that flag is presentation-only on the client.

#### Superseded S22c-frontend — Setup Bar and Guided Home UI

This slice was coded locally but is paused. Do not continue polishing this exact design without
Christian approval. The redo should adapt or replace these files around the day-zero readiness model
and focused Settings tabs.

Original file plan:

| File | Change |
|---|---|
| `web/ophalo-app/src/components/SetupBar.tsx` | New: max 3 items, role-gated, `Do later` per item, narrow/compact mode |
| `web/ophalo-app/src/App.tsx` | Add `SetupBar` to authenticated shell above page content |
| `web/ophalo-app/src/pages/Home.tsx` | Replace flat checklist with guided hub — step status cards, links |
| `web/ophalo-app/src/lib/apiClient.ts` | Add `KeepBusinessSetupResult` type, `getGuidedSetup`, `deferSetupStep` |

Frontend tests:
- `SetupBar` renders for Owner; hidden for Operator/Viewer.
- Shows at most 3 items; applies priority order from D5 rule.
- `Do later` calls `POST .../defer/{step}` and optimistically removes item from bar.
- Narrow/phone-width: collapses to compact single-prompt or short chip row — no tall checklist.
- `BuildTeam` item shows server-provided "Team limit reached" copy when `seatUsage.atLimit` is
  true, regardless of `IntendedTeamSize` value (which may not be set yet in S22c).
- `Home.tsx` renders guided hub cards, not old flat checklist.

**Seat-limit boundary proof points (required before S22c is approved)**

- `SetupBar` never derives seat availability from `IntendedTeamSize`; it reads `seatUsage` from
  the existing `GET /keep/setup` response.
- Integration: `PUT /keep/setup/guided/team-size` with body `"six_plus"` does not modify
  `AccountEntitlements.MaxUserSeats` — assert entitlements row unchanged after call.
- Integration: account at seat limit — `POST /keep/team/invite` returns seat-limit error regardless
  of `IntendedTeamSize` value stored.

#### S22c-backend — Business Info Preferences (third coding slice, same session or S22d)

Introduces `KeepAccountSetupPreferences` entity when business-info "no public contact" decision is
also needed. Adds `PUT /keep/setup/guided/team-size` endpoint. Deferred until S22c product work
confirms the exact preference fields needed together.

### User-Level Track Definition (deferred implementation)

The **User Getting Started** track is person-level and role-aware. It is not implemented in S22.
Define it here so future sessions have the contract boundary.

Conceptual steps (not persisted in S22):
- First capture/request for that user.
- First customer-visible update posted.
- First contact log.
- Mobile operator education (Operator-specific; deferred to dedicated mobile onboarding session).

If persisted in a future session, the entity would be `KeepUserSetupEvent` or similar — scoped to
`AccountUserId`, not `AccountId`. It must not be merged with account-level `KeepSetupDeferral` or
`KeepProductOpsEvent`.

---

## Implementation Session Plan

Each session below needs a mechanical preflight pass before coding. The preflight confirms named
symbols exist, enumerates compile-impact callers, and presents the file-level gate. S22a preflight
is complete; subsequent slices begin at mechanical-preflight stage only.

### S22a — Onboarding V2 / Guided Setup Preflight

Goal: migrate from flat checklist thinking to guided setup without throwing away Session 12.

Preflight must inspect:

- `KeepOnboardingService`;
- `KeepProductOpsEventType`;
- `IKeepProductOpsPersistence`;
- `/keep/setup/onboarding` endpoints;
- `web/ophalo-app/src/pages/Home.tsx`;
- `web/ophalo-app/src/pages/Settings.tsx` onboarding section;
- ADR-375 / ADR-383 / ADR-295 mismatch.

Output:

- v2 setup contract recommendation;
- account-level versus user-level state gaps;
- `Do later` storage recommendation;
- setup bar data model;
- intended-team-size storage recommendation that stays separate from `AccountEntitlements`,
  `seatUsage`, and future Stripe/subscription state;
- mobile-web/narrow-width guided setup requirements;
- backward-compatibility plan for existing checklist fields.

### S22b-redo — Public Link & Profile Settings

Goal: replace the monolithic Settings page with the first focused tab: Public Link & Profile.

Preflight must inspect:

- `GET /keep/setup`;
- `PUT /keep/setup/profile`;
- `KeepBusinessProfile`;
- `Account.BusinessName` / `Account.TimeZone`;
- customer page and intake page usage of business name/contact;
- `KeepPublicIntakeLink`;
- `KeepIntakeSetupService`;
- `GET /keep/setup/intake`;
- `POST /keep/setup/intake/ensure`;
- `POST /keep/setup/intake/replace`;
- current `web/ophalo-app/src/pages/Settings.tsx`;
- current uncommitted `SetupBar.tsx` / `Home.tsx` changes;
- audit/event infrastructure.

Output:

- Settings tab/sub-navigation file-level plan;
- auto-provision/link-ensure plan that preserves token safety;
- active-link retrieval/copy/share plan that does not leak raw tokens into logs;
- live interactive preview component plan;
- customer-facing fallback-contact behavior;
- tests required for no accidental owner-login contact exposure.

### S22c-redo — Response Policy Settings

Goal: move response policy out of first-run setup and make it understandable as business guidance.

Preflight must inspect:

- `GET /keep/setup`;
- `PUT /keep/setup/policy`;
- `KeepResponsePolicy`;
- existing policy defaults and validation;
- current Settings policy section.

Output:

- plain-language helper copy for each policy input;
- compact layout plan for desktop and narrow widths;
- validation/error mapping;
- proof that defaults make new accounts usable without owner edits.

### S22c-redo-follow-up — Team Settings

Goal: move team management into a focused roster surface and remove solo-shop pressure.

Preflight must inspect:

- `GET /accounts/me/members`;
- invite/member mutation endpoints;
- `seatUsage`;
- current Settings team section.

Output:

- roster layout plan;
- solo-owner reassurance copy;
- invite-at-limit behavior;
- preservation of server-authoritative member/seat rules.

### S22d — Service Location Request Model

Goal: add internal-only service location to public intake and business-created requests.

Preflight must inspect:

- `KeepRequest` entity;
- public intake DTO/service/persistence;
- business-created request DTO/service/persistence;
- request list/detail DTOs;
- customer page DTO/mapper;
- mobile API DTOs/hooks;
- request search/filter implementation;
- existing Spam/Test and attention signal/event model.

Output:

- schema design for service-location fields;
- unknown-location signal design;
- no-public-leak proof points;
- list/detail/search/mobile API changes;
- migration and tests.

### S22e — Customer Page Language And Success Flow

Goal: preserve current page behavior while aligning labels/copy with the decisions above.

Preflight must inspect:

- customer tracker page implementation;
- public intake success state;
- customer message/intent endpoints;
- cancel/no-longer-needed handling;
- customer page status/event visibility.

Output:

- exact copy changes;
- accessibility checks;
- confirmation behavior;
- regression list for existing customer actions.

### S22f — Mobile Carry-Forward Preflight

Goal: avoid creating new mobile scope while carrying service-location and active-intake sharing into
already-approved mobile surfaces.

Preflight must inspect:

- build log 074 / ADR-418..424;
- mobile request detail;
- mobile Quick Capture;
- mobile Account tab;
- mobile API client;
- native share/contact posture.

Output:

- list of service-location fields mobile must consume;
- active-intake share utility feasibility;
- confirmation that no mobile settings/admin scope is added;
- future mobile operator onboarding questions.

### S22g — Documentation Reconciliation

Goal: update decision index and stale docs after preflight confirms the final contract.

Must reconcile:

- ADR-295 automatic intake provisioning restored/amended by ADR-428;
- ADR-375 flat product-ops checklist versus lightweight verification/on-ramp;
- ADR-383 Settings/Onboarding section language superseded by the three-tab Settings workspace;
- response policy placement;
- mobile setup requirement versus Session 20 boundaries;
- logo upload deferred to V1.1;
- brand color deferred.

## Revised S22 Redo Sessions

The 2026-07-09 redo splits the work into small, reviewable slices. Do not collapse these into one
large Settings rewrite.

### S22r0 — Redo Preflight And File Disposition

Goal: inspect the current dirty worktree and confirm which S22c checklist/setup-bar files are
adapted, replaced, or removed.

Preflight must inspect:

- `web/ophalo-app/src/App.tsx`;
- `web/ophalo-app/src/components/SetupBar.tsx`;
- `web/ophalo-app/src/pages/Home.tsx`;
- `web/ophalo-app/src/pages/Settings.tsx`;
- `web/ophalo-app/src/lib/apiClient.ts`;
- current `GET /keep/setup/intake`, `ensure`, and `replace` contracts;
- `ophalo-web` public intake route shape;
- public base URL configuration available to `ophalo-app`.

Output:

- file-level gate for the first coding slice;
- explicit disposition for `SetupBar.tsx` and the guided Home changes;
- confirmation that no copy/open UI will generate customer links from `window.location.origin`;
- decision on whether the first slice is UI-only tabs or includes slug routing.

### S22r1 — Settings Tabs Skeleton

Goal: remove the monolithic Settings scroll and install the three-section workspace without claiming
new durable link behavior yet.

Scope:

- remove/unmount the paused setup bar from the authenticated shell;
- convert Settings from scroll/highlight sections to tabs/subnav:
  - `Public Link & Profile`;
  - `Response Policy`;
  - `Team`;
- map old navigation intents:
  - `company` and `intake` -> `public-profile`;
  - `team` -> `team`;
  - new `policy` -> `policy`;
- remove the onboarding status matrix from the primary Settings render;
- preserve existing profile, policy, intake, and team forms as tab content where useful.

Acceptance:

- Settings no longer looks like one administrative data monolith.
- `Public Link & Profile` may show active/intake status, but durable copy/open buttons are gated
  until S22r2 slug routing is live.
- No backend behavior changes are required for this slice.

### S22r2 — Slug-Based Public Intake Routing

Goal: make the public intake URL permanently constructable from `publicSlug` without breaking old
published slug URLs when the owner edits the link name.

Why this is required:

- `KeepPublicIntakeLink` stores only `TokenHash`; the raw token is intentionally unrecoverable.
- `GET /keep/setup/intake` returns `publicSlug`.
- The owner must be able to return to Settings later and copy/open the same active public request
  link without replacing it.
- Once a slug is published to customers, ordinary slug edits must not silently turn old shared links
  into 404s.

Scope:

- add a slug-alias persistence model/migration (`KeepPublicIntakeSlugAlias` or equivalent);
- update `ophalo-web` public intake route to accept slug-based URLs;
- update `OpHalo.Api` public-intake resolution to find an active link by current slug or active alias
  where appropriate;
- preserve existing token safety, account/access gates, rate limits, abuse posture, and token
  redaction;
- keep raw-token replacement/create one-time behavior only where still needed for compatibility;
- ensure `ophalo-app` has an explicit public web base URL for customer-facing link construction.

Rules:

- Do not construct public customer links with `window.location.origin` from the authenticated PWA.
- Do not expose raw tokens in logs, diagnostics, persisted frontend state, or long-lived UI state.
- Current slugs resolve first; active aliases redirect or internally resolve to the latest active
  slug.
- V1 should prefer `302`/`307` redirects for aliases until production behavior is proven.
- Replacement/regeneration disables old links/aliases tied to the revoked link; ordinary link-name
  edits do not.
- If both token and slug paths exist during migration, tests must prove the intended route resolves,
  aliases behave correctly, and invalid/revoked/deleted links fail closed.

Acceptance:

- `GET /keep/setup/intake` plus public base URL is enough for `ophalo-app` to construct the active
  public request link.
- Returning to Settings in a later browser session still allows copy/open without replacing the link.
- An old slug from a normal link-name edit redirects/resolves to the current active slug.
- A slug tied to a replaced/revoked link no longer accepts requests.
- Cross-account slug and alias collisions are rejected.

### S22r3 — Public Link & Profile Polish

Goal: make the Public Link & Profile tab feel like the owner's public front door.

Scope:

- show the active public request link with copy/open controls using public base URL + `publicSlug`;
- show a phone-sized live intake-page preview using current business profile/contact fields;
- keep business name/contact/timezone editing in this tab where appropriate;
- expose friendly copy/action for `Edit link name`;
- replace/regenerate link only behind explicit confirmation and stale-link warning;
- use copy that describes the link as ready/working, not something the owner must create.

Acceptance:

- A business owner can understand what customers will see.
- Copy/open works across sessions.
- Editing the link name explains that old names keep working and redirect customers to the current
  link.
- Replacing the link feels exceptional, not routine setup, and warns that old shared links break.

### S22r4 — Response Policy Tab

Goal: make response timing configurable without making it day-one homework.

Scope:

- move policy fields into the `Response Policy` tab;
- add compact plain-language helper copy for each field;
- preserve existing validation and defaults.

Acceptance:

- Policy settings are understandable in business terms.
- Defaults make the account usable without edits.

### S22r5 — Team Tab And Lightweight Getting Started

Goal: remove team pressure and replace the old checklist with a small verification/on-ramp.

Scope:

- make Team a clean roster/invite surface;
- add solo-owner reassurance copy;
- preserve server-authoritative `seatUsage` and member mutation rules;
- replace Getting Started with:
  - verify public link;
  - add/review first request;
  - invite teammates only if useful.

Acceptance:

- Solo businesses do not feel blocked or behind.
- No `Create intake page` / `Share intake page` split appears as owner chores.
- No seven-step setup bar/checklist pressure returns.

### S22r6 — Backend Auto-Provision Hardening

Goal: move day-zero intake readiness from frontend-on-tab-load workaround to backend-owned behavior.

Scope:

- ensure one active public intake link is created through the Keep setup boundary after account/profile
  provisioning has a usable business name/profile context;
- preserve collision-safe slug generation;
- keep replacement exceptional and explicit;
- add tests proving new accounts have a durable active link without opening Settings.

Acceptance:

- A new owner can ignore Settings and still have a public request link ready when the product needs
  to show/share it.
- Auto-provisioning does not couple Foundation registration directly to Keep internals in a way that
  violates existing boundaries.

## Claude Handoff — Redo Brief

Give Claude this direction before it codes:

```text
We changed direction on Session 22 after reviewing the in-progress Getting Started and Settings
screens. Do not continue the seven-step Getting Started/setup-bar design as the product path.

Product goal:
- Keep works by default after signup.
- Settings is not one administrative scroll. Split it into Public Link & Profile, Response Policy,
  and Team.
- Getting Started becomes lightweight verification/on-ramp, not homework.
- Auto-provision the public intake link by default; the owner verifies/copies/previews it rather
  than creating it from scratch.
- Durable public intake copy/open uses slug-based public routing. Do not use the unrecoverable raw
  token as the long-term share URL. Do not generate customer links from window.location.origin in
  ophalo-app; use configured public web base URL.
- Ordinary public link-name edits must preserve old shared slugs as aliases. Replacement/regeneration
  is the destructive/security action and must warn that old shared links break.

First implementation target:
1. Inspect current uncommitted files:
   - web/ophalo-app/src/App.tsx
   - web/ophalo-app/src/components/SetupBar.tsx
   - web/ophalo-app/src/pages/Home.tsx
   - web/ophalo-app/src/pages/Settings.tsx
   - web/ophalo-app/src/lib/apiClient.ts
2. Preserve S22b backend additions only if useful. They exist, but the UX must not be a seven-step
   checklist.
3. Rework Settings into tabs/subnav:
   - Public Link & Profile
   - Response Policy
   - Team
4. Build Settings tabs first, then implement slug-based public intake routing plus slug-alias
   persistence before claiming durable copy/open works across sessions.
5. Build Public Link & Profile:
   - show the active public link;
   - copy/open/preview actions;
   - business name/contact/timezone fields as appropriate;
   - live phone-sized intake-page preview using current form/profile data;
   - replace/regenerate link only behind explicit confirmation and stale-link warning.
6. Response Policy gets compact helper copy explaining each input in business terms.
7. Team is a clean roster; solo owners see reassurance, not pressure.

Do not:
- show Create intake page and Share intake page as separate chores;
- make Build your team feel mandatory;
- put the old onboarding status matrix in primary Settings;
- show a slug URL copy/open button as guaranteed live before ophalo-web/API can resolve publicSlug;
- make ordinary link-name edits break already-published slug URLs;
- leak raw public-intake tokens in logs, visible state, or persisted frontend state;
- infer seat availability client-side.

Before editing, present a file-level gate and say which existing S22c files will be adapted,
reverted, or replaced.
```

---

## Batch / Slice Guidance

Do not implement this as one large change.

Suggested order:

1. S22r0 redo preflight and file-level gate.
2. S22r1 Settings tabs skeleton.
3. S22r2 slug-based public intake routing.
4. S22r3 Public Link & Profile polish with live preview.
5. S22r4 Response Policy tab with business guidance.
6. S22r5 Team tab and lightweight Getting Started.
7. S22r6 backend auto-provision hardening.
8. Service location.
9. Customer page copy.
10. Mobile carry-forward.
11. Docs/index reconciliation.

Each coding slice should stay within the normal batch gate unless Christian explicitly splits/expands
it:

- at most 3 mutation families;
- at most 8 production files;
- at most 12 files total including tests/docs.

---

## Completed Slices

### S22p2 — Intake Urgency Field

`IntakeUrgency` enum (`Routine/Soon/Urgent`, default `Routine`) added as persisted metadata on
`KeepRequest`. Piped through public intake submission: `PublicIntakeRequest.Urgency` (nullable;
API treats null as Routine), `CreateKeepPublicIntakeCommand.IntakeUrgency`, factory
`KeepRequest.CreateFromCustomerIntake`. EF configuration added; migration pending (Christian runs
`dotnet ef migrations add AddUrgencyToKeepRequest --startup-project
src/OpHalo.Keep.Infrastructure`).

Frontend: urgency select in `IntakeForm.tsx` between description and service location. Options:
Routine / Soon / Urgent with helper copy listing active examples and a safety disclaimer.

**Operator display deferred.** `IntakeUrgency` is persisted in S22p2; surfacing it in the operator
request view is the next display slice. This field is not invisible product debt — it is in the DB
and readable by any query.

Tests: 3 new unit tests (default Routine, explicit Urgent, explicit Soon) + 4 new integration tests
(Routine/Soon/Urgent values + omitted field). Integration tests require the migration before they
pass.

---

## Deferred

- Business logo upload: V1.1 personalization.
- Brand color selection: deferred beyond V1.1 unless accessibility-safe customization is designed.
- Rich public handle/custom-slug management beyond generated collision-safe slugs.
- Embedded map preview.
- Office/branch/service-region routing.
- Metrics-led response-policy tuning.
- Dedicated audit UI.
- Full mobile Operator onboarding / gesture education.
