# Build Log 076 — Session 22: Guided Setup, Intake Sharing, And Service Location Plan

**Started:** 2026-07-08
**Status:** Ready handoff — S22a preflight next; no coding before slice gate
**Session name:** S22 guided setup / intake / service-location migration
**Next free ADR before this log:** ADR-428
**Next free ADR after this log:** TBD during S22 documentation reconciliation

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

This session defines the next product migration:

```text
flat onboarding checklist -> guided setup + contextual setup bar
```

The existing Session 12 model answers "which setup signals are complete?" This session answers "how
should owners understand and activate Keep without being forced through confusing admin homework?"

---

## Next Session Quick Start

Start with **S22a — Onboarding V2 / Guided Setup Preflight**.

Do not start implementation in the next session. First audit the current Session 12 onboarding
foundation and return a narrow file-level plan for the first coding slice.

Preflight focus:

1. Confirm the current `GET /keep/setup/onboarding` contract, product-ops event model, and PWA
   checklist rendering.
2. Decide whether guided setup v2 extends `KeepProductOpsEvent`, adds new account/user setup state,
   or combines both.
3. Separate account-level **Business Setup** from person-level **User Getting Started**.
4. Define how `Do later` is stored and when deferred steps can reappear contextually.
5. Define the setup bar data model, priority rules, and narrow/mobile PWA behavior.
6. Identify exact doc conflicts to reconcile after the contract is locked, especially ADR-295,
   ADR-375, and ADR-383.

Expected S22a output:

- current-state findings with file references;
- proposed v2 setup contract;
- migration/backward-compatibility plan from the existing flat checklist;
- file-level implementation slice proposal within the normal batch gate;
- test plan covering Owner/Admin-only setup, Operators/Viewers, mobile-width UI behavior, and
  no accidental response-policy promotion into first-run setup.

ADR/doc note: ADR-427 is already allocated in the decision index. Use ADR-428+ for new S22 decisions
only after preflight confirms the final contract.

---

## Current-State Findings To Preserve

- Existing onboarding endpoint is Owner/Admin-only through `Keep.SettingsManage`.
- Existing onboarding is account-level only.
- Existing checklist mixes business setup, user learning, response policy, mobile registration, team
  setup, quick capture, tracker review, and spam training as equal-weight items.
- Existing response-policy controls are useful admin controls, but confusing as day-one setup.
- Existing public intake setup requires explicit Owner/Admin `ensure` / `replace`; the raw intake URL
  is only returned on create/replace.
- Decision index currently contains an older ADR-295 statement about automatic intake provisioning.
  This conflicts with the explicit setup flow now preferred and must be reconciled before coding.

---

## Locked Product Direction

### Guided Setup Model

Getting Started becomes a guided setup hub with separate focused pages, not one flat checklist.

Each page has one job, one primary action, and a `Do later` path unless the user is actively trying
to perform an action that requires missing setup.

Owner/Admin guided setup is web/PWA-owned, but it must not assume a laptop or large screen. New
business owners may sign up and complete first-run setup from a phone browser, tablet, laptop, or
office desktop. Core setup pages must therefore be usable at phone widths. Deeper admin maintenance
can remain denser, but the first-run path must not require a large screen.

Primary Owner/Admin setup:

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

### Mobile Setup

Mobile is required for core setup completion, but not required to create the account or first request.

Rationale: the PWA is the business monitor/control center, but mobile is where service-business
customer contact, SMS/call handoff, field updates, on-site intake sharing, and fast contact logging
will happen.

For V1 setup completion, at least one Owner/Admin mobile device should be registered. Future team
setup may require at least one Operator mobile device, but that is a later mobile-onboarding decision.

### Intake Page Lifecycle And Sharing

The intake page is a public job-capture channel, not a private admin link.

V1 keeps the shared URL token-based. The business does not claim a visible public handle during
first-run setup. Public handles/custom slugs are deferred.

V1 intake setup:

- preview before create;
- no logo upload or brand color in V1;
- business logo upload deferred to V1.1 personalization;
- brand colors deferred beyond V1.1 unless accessibility-safe customization is designed;
- Owner/Admin can create/replace;
- Operator can preview/copy/share an active intake page where a share utility is exposed;
- Viewer gets no V1 share controls;
- mobile may expose copy/share for an active intake page, but mobile does not create/replace the
  intake page.

After intake creation, immediately transition into a share moment:

- intake page ready;
- copy link;
- share link;
- preview page.

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

## Implementation Session Plan

Each session below needs a Codex preflight pass before coding. The preflight should audit existing
contracts and tests, identify mismatches with this log, and produce a narrow file-level implementation
plan.

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
- mobile-web/narrow-width guided setup requirements;
- backward-compatibility plan for existing checklist fields.

### S22b — Business Info / Contact / Timezone

Goal: guided Business info page and Settings cleanup.

Preflight must inspect:

- `GET /keep/setup`;
- `PUT /keep/setup/profile`;
- `KeepBusinessProfile`;
- `Account.BusinessName` / `Account.TimeZone`;
- customer page and intake page usage of business name/contact;
- audit/event infrastructure.

Output:

- explicit no-public-contact storage decision;
- audit storage plan;
- customer-facing fallback-contact behavior;
- tests required for no accidental owner-login contact exposure.

### S22c — Intake Page Setup, Preview, And Sharing

Goal: guided intake setup and repeatable share/copy without unsafe raw-token leakage.

Preflight must inspect:

- `KeepPublicIntakeLink`;
- `KeepIntakeSetupService`;
- `KeepIntakeSetupPersistence`;
- `GET /keep/setup/intake`;
- `POST /keep/setup/intake/ensure`;
- `POST /keep/setup/intake/replace`;
- `web/ophalo-web/src/app/keep/intake/[token]/`;
- Settings intake section.

Output:

- safe retrievable share URL plan;
- preview-before-create UI plan;
- Owner/Admin versus Operator permissions;
- token logging/redaction test plan;
- ADR-295 reconciliation recommendation.

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

- ADR-295 automatic intake provisioning versus explicit guided setup;
- ADR-375 flat product-ops checklist versus guided setup v2;
- ADR-383 Settings/Onboarding section language;
- response policy placement;
- mobile setup requirement versus Session 20 boundaries;
- logo upload deferred to V1.1;
- brand color deferred.

---

## Batch / Slice Guidance

Do not implement this as one large change.

Suggested order:

1. S22a preflight and contract lock.
2. S22b Business info.
3. S22c Intake setup/share.
4. S22d Service location.
5. S22e Customer page copy.
6. S22f Mobile carry-forward.
7. S22g docs/index reconciliation.

Each coding slice should stay within the normal batch gate unless Christian explicitly splits/expands
it:

- at most 3 mutation families;
- at most 8 production files;
- at most 12 files total including tests/docs.

---

## Deferred

- Business logo upload: V1.1 personalization.
- Brand color selection: deferred beyond V1.1 unless accessibility-safe customization is designed.
- Public intake handles/custom slugs.
- Embedded map preview.
- Office/branch/service-region routing.
- Metrics-led response-policy tuning.
- Dedicated audit UI.
- Full mobile Operator onboarding / gesture education.
