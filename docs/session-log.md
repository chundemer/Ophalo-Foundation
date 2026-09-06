# Session Log — OpHalo Foundation

**Last updated:** 2026-09-06 (GAP-051 public-web configured business-phone display — the configured business phone is now formatted `(XXX) XXX-XXXX` on the intake info endpoint (token + slug) and the customer tracker (active + expired) via the display-only `PhoneDisplayFormatter`; canonical storage/API/`tel:` unchanged; native parity still deferred to Session 14. See [BL147](build-log/147-gap-051-public-web-business-phone-display.md). Prior: GAP-016 / GAP-021 closure, [BL146](build-log/146-gap-016-gap-021-phone-validation-closure.md))

**Current scope:** GAP-039 (production observability) and GAP-068 (multi-workspace sign-in +
invited-user display name) are both fully implemented and accepted. GAP-039's founder-owned Batch
4 production-candidate verification gate is still paused (see
[BL140](build-log/140-gap-039-sentry-implementation-handoff.md)). GAP-068's manual
browser/network verification checklist is done, including a production-verified fix for a
membership-status data issue that was blocking multi-workspace selection for one email (see
[BL143](build-log/143-multi-workspace-signin-and-invited-name-handoff.md)).

GAP-033 (public-intake trust and tracker event-feed truthfulness) is complete and merged
(`11c19d3d`, `89a776d8`): Slice A replaced the post-submit auto-redirect with an ADR-446-compliant
stable confirmation screen, retired the "private page"/"private link" copy, and added a Terms link
to the shared public footer; Slice B replaced the `Visibility == All` event filter in
`KeepCustomerPageMapper` with an explicit default-deny allowlist (`StatusChanged` and
customer/business `MessageAdded` only; unknown/future event types excluded). Real-phone review is
folded into the BL089 launch pass. See
[BL145](build-log/145-gap-033-public-intake-trust-and-event-feed-allowlist.md).

Pilot Onboarding Upgrade Session 0 (audit) and Session 1 (server-owned Proposed Work release gate)
are complete, tested, merged to `main` (`eb33f6a5`, plus follow-up `226778af`), and **deployed**
(verified 2026-09-05). Session 2 Slice A (enrollment provenance schema) is accepted, merged to
`main`, and applied to Christian's local development database.
On 2026-09-06, ADR-496 was amended: the uncommitted automatic-Pilot-provisioning and blanket
backfill slices are superseded. Price Book is an operator-selected per-business capability during
the pilot, not a Pilot-classification side effect. Session 3 Slice A (retire Getting Started, add
the Requests empty-state panel) is complete, tested, and merged to `main` (`4d9f81ac`). Session 3
Slice B (two-choice New Request decision) is complete and tested. Session 3 Slice C (passive
Settings readiness labels) is complete and tested — Session 3 is now fully complete. Session 3b
(Team invite clarity) is complete and tested: the invite form states the invitation lifecycle,
shows business-outcome role descriptions (Admin/Operator/Viewer, Operator default, no Owner), and
success copy no longer promises email delivery. Session 4 (Price Book discovery and first-catalog
guidance) is complete and tested across three slices: A (empty-catalog "What are you setting up?"
first-item type guidance, `d52b2452`), B (Category vs. Search alias field guidance, `f8704628`),
and C (PWA "Technician suggestions" terminology, replacing "Nudges" in all user-facing copy while
keeping the `ScopeNudgeRule` API/domain term, `eee46c00`). BL142 is now fully complete. See
[BL142](build-log/142-pilot-onboarding-upgrade-handoff.md).

**Purpose:** active handoff only. Completed implementation detail belongs in Git history and the
relevant build log.

## Authoritative sources

- Release priority and acceptance status: [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md)
- Product decisions: [decision index](decisions/README.md) and individual ADRs
- Request Detail / queue execution sequence: [BL137](build-log/137-request-detail-and-queue-usability-handoff.md)
- Current Request Detail interaction contract: [Request UI Upgrade 1.1](ux-design/v2/request-ui-upgrade-1.1.md)
- Actual Work closeout/replacement contract: [ADR-494](decisions/ADR-494-actual-work-paper-compatible-pilot-upgrade.md), [ADR-493](decisions/ADR-493-actual-work-office-financial-resolution-and-billing-revisions.md), and [BL136 P](build-log/136-P-preflight.md)

## Current repository state

- GAP-065 is complete: Request Detail pending-review discovery, the wide financial-review
  continuation flow, server-authoritative request-row counts, the Owner/Admin row cue, and the
  persistent Actual Work Review destination. See
  [BL138](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md).
- Request UI Upgrade 1.1 implementation is complete
  ([BL139](build-log/139-request-ui-upgrade-1.1-implementation.md)); its product-owner visual
  acceptance pass is still outstanding.
- The controlled pilot keeps the contractor's existing system authoritative for estimates,
  invoices, payments, and accounting. Keep is the factual field record; the existing-ticket
  workflow remains the outage fallback.
- GAP-039 is fully implemented (API + PWA error capture, founder console configuration); only the
  founder-owned Batch 4 production-candidate verification gate remains before any customer-facing
  pilot. See [BL140](build-log/140-gap-039-sentry-implementation-handoff.md).
- GAP-068 is fully implemented (Slices 1–4: continuation foundation, sign-in/exchange branching,
  invite acceptance name gate, frontend screens). See
  [BL143](build-log/143-multi-workspace-signin-and-invited-name-handoff.md).

## Next implementation sequence

GAP-016 and GAP-021 are resolved: the ADR-444 normalized ten-digit North American path
(`PhoneNormalizer` → `KeepCustomer.Create` / `LookupKeepRequestByPhoneService`; web Quick Capture
ten-digit gate + draft-preserving Change; public-intake leading-`1` slicing) is consolidated across
the backend and every web client path, and stale "7–15 digit" / E.164 wording in the
`KeepCustomer` message, `KeepCustomerConfiguration` comment, and `KeepCustomerTests` was corrected.
Native parity is deferred to Session 14 (ADR-236).

GAP-051's public-web scope is done (BL147): configured business-phone display is formatted on
every public projection; the intake form input path was already ADR-444-compliant. Its remaining
native-parity scope is deferred to Session 14 (ADR-236).

**Next approved coding session: phone and capture integrity — GAP-025** (ADR-492 request-phone-only
customer continuity; see the pilot/release gate order in Deferred next work). Do not begin
GAP-070/GAP-071 optional-module UI work.

**Separate founder-owned prerequisite:** GAP-039 Batch 4 production-candidate verification remains
required before any customer-facing pilot. It is an operational verification gate, not the next
coding session.

The numbered items below are the completed release-safety sequence, retained for reference.
Items 1 and 2 are done; the next coding session is named above.

1. **Pilot onboarding — request-first PWA completion** (ADR-496) — **complete** — see
   [BL142](build-log/142-pilot-onboarding-upgrade-handoff.md):
   - **Slice A — schema + domain provenance** — done, accepted. 7 production files (including a
     required nullable-DTO compatibility fix); migration verified against real Postgres and
     applied to Christian's local dev database. See BL142 for full delivery record.
   - **Slices B/C — automatic Pilot provisioning/backfill** — superseded; do not commit or
     implement. Per-business pilot grants use the existing authorized internal entitlement path.
   - **Session 3 — request-first PWA onboarding**, split into slices (exceeds the single-session
     batch gate):
     - **Slice A — retire Getting Started, add Requests empty-state panel** — done, accepted,
       merged (`4d9f81ac`). Removed the Getting Started nav/route/page; new
       `RequestsEmptyStatePanel` (state-correct heading: checking/live/being-set-up) shown only
       in the zero-request Requests workspace, gated on `addFirstRequestComplete`. Deleted the
       superseded `RequestsOnboardingBanner` checklist. Full app suite 1075/1075 passed.
     - **Slice B — two-choice New Request decision** — done, accepted. Added `Stage: "choice"` to
       `quick-capture/utils.ts` and a new `ChoicePanel.tsx`; Owner/Admin's `QuickCapture` initial
       stage now opens on `choice` instead of jumping straight to `handoff`. Each card states its
       outcome ("Let the customer submit it" / "Record it yourself") and routes into the existing
       `handoff`/`lookup` stages unchanged. Non-Owner/Admin and `followUpPrefill` entry paths are
       untouched. 3 production files; new tests `ChoicePanel.test.tsx` and `QuickCapture.test.tsx`.
       Full app suite 1080/1080 passed.
     - **Slice C — passive Settings readiness labels** — done, accepted. Added a passive badge to
       each Settings section heading: `Live` on Public Link (gated on `hasActiveLink` +
       `publicSlug`, never shown falsely), `Active` on Response Policy (unconditional once setup
       loads), and on Team either `Solo workspace` (exactly one non-removed member) or a factual
       `N team members` count — all server-supplied via the existing `api.getIntake`/`api.getSetup`/
       `api.listMembers` queries, no new backend read. 3 production files; extended
       `Settings.v2Shell.test.tsx`. Full app suite 1083/1083 passed. Also visually verified
       against Christian's real local account (all three tabs, close-up zoom) — all three badges
       render correctly with no layout issues.
   - Session 3 (request-first PWA onboarding) is now fully complete. **Session 3b — Team invite
     clarity** — done, accepted. `InviteForm` (`TeamSection.tsx`) now states the invitation
     lifecycle before submission, replaces the unexplained role dropdown's implicit meaning with a
     visible business-outcome description per role (Operator remains default, Owner still
     unavailable), and corrects post-submit copy to say the invitation is pending acceptance
     rather than promising email delivery. 1 production file; new
     `TeamSection.inviteClarity.test.tsx` (5 tests). Focused suite (9 tests across both
     TeamSection test files) and `tsc --noEmit` passed. **Session 4 — Price Book discovery and
     first-catalog guidance** — done, accepted, three slices. Slice A: empty-catalog "What are you
     setting up?" panel offers the four existing catalog-item types (Material/Equipment/
     Service/Fee), each with a one-line plain-language description, opening `CatalogItemDrawer`
     pre-set to that type via a new `initialType` prop; guidance shown only in the empty-catalog
     panel, not the standard drawer. 2 production files; `PriceBook.test.tsx` and
     `CatalogItemDrawer.test.tsx` updated/extended. Merged `d52b2452`. Slice B: shared field-level
     guidance (`catalogFieldHelp.ts`) distinguishing Category from Search alias everywhere either
     is editable (new-item drawer, edit drawer, item detail page); create-drawer's alias field
     relabeled "Search alias" to match the term used elsewhere. 4 production files, 3 test files.
     Merged `f8704628`. Slice C: renamed every user-facing "Nudge(s)" string in the PWA to
     "Technician suggestions" (tab label, Add/Edit/Delete suggestion rule, empty-state copy),
     leaving the `ScopeNudgeRule` API/component/query-key terminology untouched; tightened the
     Offerings & Assemblies empty-state subtext to state an assembly doesn't replace its individual
     items. 2 production files, 1 test file. Merged `eee46c00`. BL142 (ADR-496 pilot onboarding
     upgrade) is now fully complete — Sessions 0 through 4 all done, accepted, and merged.
   BL142 (ADR-496 pilot onboarding upgrade) is complete.
2. **GAP-033 — public-intake trust and tracker access truthfulness** (P1) — **complete and
   merged** (`11c19d3d`, `89a776d8`). See
   [BL145](build-log/145-gap-033-public-intake-trust-and-event-feed-allowlist.md) and the
   resolution note in
   [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md#gap-033--public-intake-does-not-establish-sufficient-customer-trust-or-return-continuity).

Remaining tracker order for release safety: GAP-039 (Batch 4 verification, founder-owned) → GAP-040
(marketing-site accuracy). GAP-033 is done. See `pilot-readiness-bug-tracker.md`.

Do not begin GAP-042 implementation until GAP-067 passes its screenshot/acceptance review (its
read-only placement preflight remains valid).

Request UI Upgrade 1.1 still needs its product-owner visual acceptance pass — a review task, not
a coding batch.

## Deferred next work

- **Optional module UI:** P0 gaps GAP-070 and GAP-071 are deferred until the request-first pilot
  onboarding phase is complete. They do not authorize a guided setup wizard, self-service billing,
  or a customer entitlement-write endpoint.
- **4g pilot request-close advisory:** preflight after the safety/usability sequence above. An
  advisory on outstanding Actual Work with a structured `Close anyway` pilot exception, not a hard
  Resolved→Closed gate. See [BL136 P](build-log/136-P-preflight.md).
- **Pilot/release gate order:** production observability (GAP-039, complete pending founder
  verification) → pilot onboarding (ADR-496, complete) → public-intake trust (GAP-033, complete) →
  phone integrity (GAP-016/021/051) → remaining tracker order.
- **Minimum Office Closeout:** Billing Revision, handoff, and correction/adjustment design resume
  only after the controlled-pilot and rehearsal gates; see
  [BL135](build-log/135-minimum-office-closeout-mechanical-preflight.md).
- **Settings & Getting Started V2 UI upgrade:** all three frontend-only slices (A, B, C) are
  delivered; the product-owner screenshot-acceptance pass for Public Link & Profile and Team is
  still pending. Slice A's Getting Started/Home acceptance item is superseded by BL142 Session 3
  (ADR-496 request-first onboarding removes that page). See
  [BL144](build-log/144-settings-and-getting-started-v2-upgrade.md).
- **Price Book direct-cost visibility:** next after the Settings & Getting Started V2 upgrade
  completes acceptance. Extend the authorized Catalog Items list read contract with current
  published direct cost; add a **Direct cost** column/secondary value (desktop/narrow); `—` when
  no current cost exists. Preserve existing Owner/Admin authorization/entitlement checks; no field
  workflow exposure, no mutation/version/snapshot semantic changes, no supplier "last paid"
  implication, no inventory/accounting behavior.
- **Workbench background brand alignment:** `ophalo-app` uses an off-spec cool gray page
  background; align to Canvas `#F8F6F1` (`--ophalo-canvas`) per [BRAND.md](brand-kit/BRAND.md),
  with a token audit for other hardcoded grays. Internal staff surface only — not a pilot blocker,
  run as its own small pass after the customer request page ships.

## Guardrails

- The responsive staff PWA is the active field surface; native parity is not implied.
- Do not infer authority for pricing, invoicing, payments, QuickBooks, inventory, or fleet from
  Request Detail work.
- Use disposable local data for mutable acceptance; never seed founder production data.
- Preflight current code and the controlling tracker/ADR/build log. Stop for product direction when
  server data or authorization cannot truthfully support the requested UI.
