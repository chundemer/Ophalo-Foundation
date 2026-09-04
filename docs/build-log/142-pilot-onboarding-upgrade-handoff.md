# Build Log 142 — Pilot Onboarding Upgrade: Decision and Coding Handoff

**Status:** Planned — Session 0 release-gate audit is required before the first mutation session.
**Date:** 2026-09-04
**Authority:** [ADR-496](../decisions/ADR-496-pilot-package-provisioning-and-release-visibility.md),
[ADR-428](../decisions/ADR-428-day-zero-settings-getting-started-redesign.md), and ADR-454.

## Outcome to deliver

A pilot account is provisioned with the Price Book package automatically. The product opens on the
daily request loop—not a setup checklist. Price Book is an intentionally guided next layer; Proposed
Work and Quotes remain unpublished until separately released.

```text
Pilot account created
  -> public link + response-policy defaults are ready
  -> empty Requests workspace: Open customer view | Add your first request
  -> Price Book activation: add the services/materials/fees used by this business
  -> later, when released: Proposed Work and Quotes
```

## Non-negotiable constraints

- Preserve ADR-428's three Settings sections: Public Link & Profile, Response Policy, Team.
- Do not add a completion meter, a seven-step checklist, demo/sample records, or a required team
  step.
- Do not make Price Book a prerequisite for public intake or manual request capture.
- `keep.price_book_quotes_materials` is a package entitlement, not proof that every workflow in the
  package is released. UI hiding alone is insufficient: unreleased write APIs must reject calls.
- Do not claim automatic customer email. Current email actions use `mailto:` and depend on the
  user's configured mail client.
- Preserve account isolation, role/permission checks, session behavior, raw-token handling, and
  public-link semantics.

## Locked rollout choices

- Price Book is visible immediately to entitled Pilot Owner/Admin users, but remains secondary to
  Requests in first-run guidance.
- Proposed Work and Quotes are unreleased until this onboarding upgrade is complete and explicitly
  signed off. Their server-side release gates must remain closed regardless of package enrollment.
- Automatic Pilot enrollment uses explicit `SystemProvisioning` audit provenance. Do not create a
  bootstrap/pseudo AccountUser and do not attribute the automated grant to the new customer owner.

## Code-session list for Gemini

Each session must remain independently compiling, remain under the repository batch-size gate, add
focused tests, and finish with `git diff --check`. Read the named source files before changing them.

### Session 0 — release-gate and surface audit (read-only, required first)

**Goal:** prove the current state before adding automatic pilot enrollment.

- Map every Proposed Work and Quote route, UI entry, command, and server mutation gate.
- Verify whether each unreleased flow is already blocked independently of the package entitlement.
- Map all PWA navigation entries for Getting Started, Requests onboarding, and Price Book.
- Record gaps and a minimal server-owned release-gate proposal in this build log; do not implement
  the gate in this session.

**Key starting points:** `AccountCapabilityPackageEnrollment.cs`,
`AccountFeatureAccessResolver.cs`, `InternalEntitlementsEndpoints.cs`, `Program.cs`,
`web/ophalo-app/src/App.tsx`, `RequestsOnboardingBanner.tsx`, and `Home.tsx`.

**Acceptance:** a written matrix identifies which exact server checks block Proposed Work/Quotes
today and what must change before automatic pilot enrollment is safe.

### Session 1 — atomic pilot package provisioning

**Goal:** migrate the enrollment audit model, then ensure every newly provisioned
`AccountClassification.Pilot` gets one system-provisioned enrolled package row in the same
transaction as the account graph; Production and InternalTest accounts do not.

- Add a `change_source` (or equivalently named) enrollment provenance value with at least
  `InternalUser` and `SystemProvisioning`. Migrate existing rows as `InternalUser`.
- Make `changed_by_account_user_id` nullable only for `SystemProvisioning`; add a database-level
  check constraint and matching entity validation so an `InternalUser` row requires a real actor
  and a `SystemProvisioning` row has none.
- Update `EfAuthCodePersistence.CommitNewAccountExchangeAsync` to persist that row in the existing
  account-creation transaction. Preserve its two-phase Account ↔ AccountUser FK sequence.
- Keep `AccountProvisioningService` pure: no persistence/clock dependency; use its existing UTC
  `nowUtc` input for the enrollment timestamp.
- Add an idempotent operational backfill/recovery path for existing pilots. It records
  `SystemProvisioning`, never overwrites a Disabled row without an explicit re-enable decision,
  and needs no seeded service account or custom bootstrap script.

**Tests:** provenance/actor database constraint matrix; pilot graph includes the system-provisioned
enrolled key/status; non-pilot graph does not; real internal transitions require an actor;
new-account exchange persistence commits atomically; a failed transaction leaves no partial account
or enrollment; duplicate/retry behavior is safe.

### Session 2 — server-owned release gates for unreleased package workflows

**Goal:** entitlement alone never exposes Proposed Work or Quote writes before their release.

- Implement only the minimal release-gate mechanism identified in Session 0, owned by server
  configuration/feature policy—not the PWA.
- Apply it before mutation services/endpoints; return existing safe authorization/unavailable
  behavior without revealing internal rollout detail.
- Keep live Price Book catalog behavior available for entitled pilots.

**Tests:** entitled pilot can use live Price Book reads/writes; the same caller cannot invoke
unreleased Proposed Work/Quote mutations; direct HTTP calls are covered, not only hidden UI.

### Session 3 — request-first PWA onboarding

**Goal:** remove contradictory setup language and make the empty Requests workspace useful.

- Remove the permanent Getting Started nav item/route and migrate only useful readiness content to
  the zero-request Requests workspace.
- Delete `RequestsOnboardingBanner`'s "Set up your request page" checklist treatment.
- Add a compact empty-state panel with a truthful live-link statement, `Open customer view`, and
  `Add your first request`.
- Make the first New Request action an explicit, two-choice decision rather than dropping an
  Owner/Admin straight into the current "Text a Link" handoff panel. Use short cards with a clear
  "what happens next" explanation and supporting icons:
  - **Let the customer submit it** — share/text the public link; no request is created until the
    customer submits the form.
  - **Record it yourself** — for a call, voicemail, walk-in, text, or email already received;
    create the request now.
  This is contextual guidance at the decision point, not a persistent tutorial, tooltip-only
  explanation, or an implication that sending a link creates a request.
- Add passive Settings labels: link `Live`, response policy `Active`, and team `Solo workspace` or
  a truthful server-supplied member state.
- Do not add sample data, a fake request, automatic email, or any new client-trusted readiness
  calculations.

**Tests:** zero-request Owner/Admin state, no checklist/nav regression, link uses configured public
base URL, both New Request paths explain their distinct outcomes and reach the correct existing
flow, CTAs work, Operator/Viewer behavior remains appropriate, keyboard/mobile layout works.

### Session 3b — Team invite clarity

**Goal:** let an Owner/Admin invite a teammate without needing a verbal explanation of roles or
what happens after clicking Invite.

- Before submission, state the invitation lifecycle plainly: "We’ll create an invitation for this
  email. They use the email link to set up/sign in to their own Keep account. They do not have
  access until they accept it."
- Replace the unexplained role dropdown with accessible role choice/help that uses business outcomes
  rather than permissions jargon. Do not offer Owner in this ordinary invite flow:
  - **Admin:** trusted office lead; can manage settings, team members, and the Price Book, in
    addition to daily work.
  - **Operator:** field or operations teammate; can create/manage requests and capture field work,
    but cannot manage team, settings, or the Price Book.
  - **Viewer:** read-only visibility into requests.
- Keep Operator as the sensible default, but make the current selection and its consequences
  visible before sending.
- Correct delivery language. Invite-email delivery is best-effort, so success must say the
  invitation was created and is pending acceptance—not promise that the email was received. Keep
  resend/manual-share recovery visible when an invite remains pending.
- Preserve server-authoritative seat limits, Owner/Admin invite authority, owner-safety rules, and
  existing invite-token secrecy.

**Tests:** role help maps to existing permission boundaries; Owner is unavailable; success is
truthful about pending acceptance; resend/manual-share recovery is discoverable; seat-limit and
role authorization behavior is unchanged; keyboard/mobile usability passes.

### Session 4 — Price Book discovery and first-catalog guidance

- Keep the entitled Price Book nav entry available immediately and refine its empty state into
  concise first-catalog guidance. It is secondary to Requests, but no request-count gate applies.
- Guide the first few catalog entries only. Do not require assemblies or surface Proposed Work/
  Quotes.
- Replace unexplained Price Book nouns with a contextual "what are you setting up?" choice and
  concise, persistent-at-the-point-of-use explanations. Supporting icons may aid scanning, but the
  explanatory sentence—not an icon tooltip—must carry the meaning:
  - **Catalog item:** one individual service, material, equipment item, or fee.
  - **Offering / assembly:** a reusable bundle of catalog items that are added together as the
    starting point for a job; it does not replace the individual items.
  - **Technician suggestions** (current internal/product term: `Nudges`): optional related items to
    suggest after a technician selects a trigger item or assembly. They do not add anything
    automatically and are not part of the assembly bundle. Prefer this plain-language label in the
    PWA; retain the underlying `ScopeNudgeRule`/API terminology unless a separate contract change
    is needed.
  - **Category:** a shelf for organizing and filtering catalog items (for example Plumbing or
    Electrical). It does not create another searchable name for an item.
  - **Search alias:** another name, brand name, abbreviation, or trade term that finds the same
    catalog item. It is not a category and does not create a duplicate item or a separate price.
- Show the category-vs-alias explanation beside those fields and in the relevant empty/create
  states, with a concrete short example. Do not put all five concepts into one first-run modal.

**Tests:** immediate discovery policy, direct URLs, entitlement and role boundaries, empty catalog CTA,
plain-language distinctions for bundle/suggestion and category/alias, and no access regression for
existing entitled pilots.

## Manual acceptance scenarios

1. Create a new Pilot account. Confirm package enrollment is automatic, the public link is live,
   and the empty Request state presents only the two core actions.
2. Create a non-pilot account. Confirm the package is not automatically enrolled.
3. As a pilot Owner/Admin, use Price Book according to the selected discovery policy.
4. Attempt Proposed Work/Quote HTTP mutations before release; confirm they are rejected.
5. Use a Safari device without a configured mail handler and confirm the product does not describe
   `mailto:` as an automatic send.

## Out of scope

Customer notification delivery, sample/demo data, imports, rich catalog onboarding, quote
acceptance, price formulas, and a mobile Settings/admin surface.
