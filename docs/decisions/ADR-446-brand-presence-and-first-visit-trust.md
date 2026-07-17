# ADR-446 — Brand Presence And First-Visit Trust Across OpHalo Surfaces

**Status:** Locked  
**Date:** 2026-07-17  
**Related:** ADR-367, ADR-368, ADR-379, ADR-431, GAP-033, GAP-034, build-log/089-launch-verification-pass

## Context

Pilot scope may limit features and permit deliberate manual operations, but it does not lower the
quality bar for the pages where a person decides whether to trust OpHalo, submit a service request,
or begin a Keep account. The reviewed `/start` business account-start page demonstrated the failure
mode: a technically functional, left-drifting form on a large canvas with no visible product mark or
clear product value reads as an unfinished internal tool rather than a credible subscription entry
point.

The public intake review exposed the related customer-side risk: a person who receives an unfamiliar
link by text and is then asked for their home address and phone must be able to recognize both the
business they intended to contact and the platform handling the workflow. A generic security seal or
unsupported encryption claim is not a substitute for real identity, clear data-use boundaries, and
accurate product behavior.

ADR-367 and ADR-368 establish the shared OpHalo/Keep design system. This decision makes
first-visit identity, hierarchy, and trust a required product/launch concern rather than optional
visual polish.

## Decision

Every externally reachable OpHalo surface must make its owner and product context recognizable
before asking a first-time person to act or provide information. The implementation uses an
appropriate OpHalo mark or wordmark and a consistent surface-specific hierarchy; it does not mean a
large or repeated logo on every screen.

| Surface | Required identity hierarchy |
|---|---|
| Marketing, account start, sign-in, magic-link/check-email, and invite entry | OpHalo / Keep is primary. The page must show a real product mark or wordmark, state the next outcome accurately, and provide the appropriate support/recovery route. |
| Authenticated Keep PWA | Persistent Keep / OpHalo identity belongs in the app shell. It must remain recognizable without displacing operational work. |
| Public intake and private customer request pages | The customer-facing business is primary; OpHalo Keep is a clear but secondary platform endorsement. Business identity must be sufficient for a cautious recipient of an inbound link to recognize the intended business. |
| Public unavailable, expired, error, and consent-relevant states | Show the same applicable business/OpHalo identity hierarchy and a safe next/support path. Do not leave a person on an anonymous error/form shell. |
| Transactional email and link-entry pages | Identify the business and relevant OpHalo product context clearly enough that the recipient can recognize the relationship before following or acting on a link. |

First-visit pages must make their value and next action truthful. For example, account start must not
say `Request access` if submitting sends an email sign-in link to finish setup; customer intake must
not promise a delivery channel that the product does not provide. Trust copy must state actual
privacy/use boundaries and must not use decorative `verified`, `secure`, or `end-to-end encrypted`
claims unless they are technically, operationally, and legally substantiated.

## Required Product Practice

- Treat entry, registration, sign-in, public customer, and terminal public states as conversion and
  trust surfaces, not leftover forms to be made merely functional for pilot.
- Use the shared OpHalo brand tokens, type system, and self-hosted brand assets from ADR-367 through
  ADR-379. Do not create one-off visual identities for individual flows.
- Preserve hierarchy: business identity leads on customer pages; OpHalo Keep leads when a business
  owner evaluates, enters, or manages the product. Co-branding must clarify ownership rather than
  compete for equal prominence.
- Pair visual identity with specific, product-supported value/outcome copy. Do not rely on decorative
  panels, generic enterprise language, or unverified social/security badges as a substitute.
- When the public contract safely identifies a known business, retain the applicable business-first
  identity and recovery path on post-submit, expired, unavailable, OffSeason, and error states. An
  unknown or invalid capability link must remain non-enumerating and must not reveal business
  identity. Browser/document titles may identify the known business and safe page purpose, but must
  never contain a capability token or private request/customer data.
- Make success and recovery states controllable rather than fleeting: no render-time redirect or
  automatic navigation may prevent a person from reading confirmation, preserving return access, or
  choosing the next action.
- Auth/account-entry forms must provide visible keyboard focus and programmatically associated or
  announced validation/error feedback. Normal browser entry surfaces expose applicable Privacy,
  Terms, and support routes; security-constrained handoff pages retain their dedicated restrictions.
- Include a skeptical-first-time-person review in every new or materially changed externally
  reachable surface: Can the person identify who owns the page? Why should they proceed? What happens
  after the action? What personal data is being requested and why? How do they recover or get help?
- Record meaningful failures from that review as bounded tracker items with visual, content, and
  behavioral acceptance criteria; do not defer them as cosmetic polish when they affect submission,
  signup, or confidence.

## Launch Verification

Launch verification must include desktop and real-phone screenshot/manual review of all current
first-visit surfaces: marketing-to-account start, sign-in and magic-link/check-email states, invite
acceptance/error, public intake from an inbound link, private customer request page, and relevant
unavailable/error states. Reviewers verify the hierarchy above, factual value/outcome copy, data-use
clarity before sensitive entry, accessible keyboard/zoom/error behavior, safe browser-title identity,
controllable success/return access, and no empty/unfinished visual composition at the target
viewport. A failure is a launch finding to triage, not an aesthetic note to silently waive.

## Consequences

- GAP-033 and GAP-034 are launch-significant trust/conversion work, not optional redesign work.
- New public/auth-entry work has explicit brand and first-visit acceptance criteria from the start.
- The implementation must create or reuse durable, accessible OpHalo/Keep identity assets rather
  than using text placeholders or relying on background color alone.
- This decision does not authorize unsupported security, privacy, SMS, verification, or customer
  delivery claims. Those remain governed by actual product contracts and applicable review.
