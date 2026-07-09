# ADR-428 — Day-Zero Settings And Getting Started Redesign

**Date:** 2026-07-09  
**Status:** Locked  
**Source:** Session 22 product review; in-progress Getting Started/Settings screenshot review; build-log 076

## Context

The in-progress S22 Getting Started and Settings UI followed the earlier checklist/setup-bar plan, but
review showed a product problem: the owner experience still felt like administrative homework. The
Settings page had become a dense vertical collection of company data, policy fields, intake controls,
team management, and onboarding status. Getting Started presented several chores as equal-weight
steps, including team setup and intake creation/share work that should not block a solo owner from
using Keep.

For a service-business owner, Keep should feel ready to use. The owner may be handling customers,
calls, and staff at the same time; first-run UI must reduce decisions, not expose the internal setup
model.

This ADR amends ADR-295 and supersedes the S22a explicit-intake-creation direction. It also amends
ADR-383's Settings section layout.

## Decision

Keep launches in a day-zero functional state.

- A public intake link is auto-provisioned by default after account/business profile provisioning has
  a usable business name/profile context.
- Response policy uses balanced defaults by default, including the existing V1 timing defaults.
- The owner visits Settings to adjust the working system, not to assemble it before Keep can be used.

The authenticated PWA keeps one Owner/Admin-only top-level `Settings` nav item, but the workspace is
split into three focused sections:

- `Public Link & Profile`;
- `Response Policy`;
- `Team`.

`Public Link & Profile` owns the public-facing business profile and the active intake link. It should
show the link as a tangible asset with copy/open/preview actions and a live phone-sized intake-page
preview. Link replacement/regeneration remains an explicit recovery action with confirmation and
stale-link warning.

The permanent shareable intake URL uses the active link's `publicSlug`, not the unrecoverable raw
intake token. The public intake route and API resolution must support slug-based lookup before the UI
claims copy/open works for returned sessions. The authenticated PWA must construct customer-facing
links from the configured public web base URL, not `window.location.origin`, because the workbench
host and public customer host differ in production.

Slug rename and replacement behavior is governed by ADR-429. Ordinary link-name edits preserve old
shared slugs as aliases; replacement/regeneration remains the destructive action that can break old
links.

`Response Policy` owns expectation timing. Each field should have compact plain-language business
guidance so owners understand the effect of the number they are choosing.

`Team` owns team member roster/invite/member-management controls. Solo owners should see reassuring
copy that team setup is optional and useful later, not overdue setup work.

Getting Started becomes a lightweight verification/on-ramp surface. It should focus on:

- verifying the public request link;
- adding or reviewing the first customer request;
- inviting teammates only if someone else helps manage requests.

Getting Started must not present `Create intake page` and `Share intake page` as separate required
chores when the link exists by default. It must not make `Build your team` feel mandatory for a solo
business. A setup bar may survive only as a quiet verification reminder after redesign; it must not
reintroduce seven-step checklist pressure.

The existing onboarding checklist endpoints and product-ops events may remain for compatibility,
support, or later analytics. They should not be exposed as a primary Settings status matrix in the V1
owner workspace.

## Rationale

Auto-provisioning makes the product feel ready. The owner can immediately understand, copy, and share
a working request link instead of first learning an internal setup lifecycle.

Slug-based routing matches the nature of the intake page. The intake URL is a public acquisition
asset intended to be shared widely, while the raw token is intentionally not stored and cannot be
reconstructed after creation/replacement. Returning the stable public slug lets the owner copy and
open the same public request link later without weakening raw-token safety.

Focused Settings sections map to owner outcomes: public front door, customer expectations, and team
access. This is easier to scan than a single administrative scroll and better matches how a business
owner thinks under time pressure.

Response policy fields are operationally important but not self-explanatory. Plain-language guidance
turns configuration into business advice without adding a separate tutorial.

Team setup is not universally required. For small and solo shops, pressuring team setup weakens trust
because it implies the product is incomplete without extra staff.

## Consequences

- ADR-295 auto-provisioning is restored/amended: one active public intake link should be created by
  default through the Keep setup boundary. Replacement remains exceptional recovery.
- ADR-383 Settings layout is amended: the primary V1 Settings workspace becomes Public Link &
  Profile, Response Policy, and Team. The old Company/Team/Onboarding vertical model is superseded.
- S22c guided checklist/setup-bar frontend work is paused and must be adapted or replaced before
  commit.
- Slug-based public intake resolution is required for durable copy/open behavior. Until the public
  route/API can resolve active links by slug, frontend copy/open controls must not present a
  constructed slug URL as guaranteed live.
- Customer-facing URLs in `ophalo-app` must use an explicit public web base URL configuration, not
  the current authenticated app origin.
- Public-link UI must preserve public-token safety: raw tokens are not logged, persisted in visible
  state beyond the intentional one-time/retrieval surface, or exposed through unsafe diagnostics.
- Seat limits remain server-authoritative. The Team UI must not infer availability from intended team
  size or visible row counts.

## Deferred

- Rich public handle management beyond generated/collision-safe slugs.
- Business logo upload and brand color customization.
- Owner/Admin audit history UI.
- Mobile admin/settings management; PWA remains the administrative surface per ADR-424.
