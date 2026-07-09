# ADR-429 — Public Intake Slug Aliases And Replacement Boundary

**Date:** 2026-07-09  
**Status:** Locked  
**Source:** Session 22 slug-routing review; ADR-428

## Context

ADR-428 chooses slug-based public intake URLs so the business can return to Settings and copy/open the
same durable public request link without recovering an unrecoverable raw token.

That creates a second owner-facing risk: if a business edits its public link name after publishing it
on a website, Google Business profile, email signature, QR code, truck decal, or printed card, old
links must not silently break. For a service business, a broken public request link is a customer
acquisition and trust failure.

The product therefore needs a clear boundary between:

- a friendly public link-name edit; and
- a security/recovery replacement that intentionally invalidates old links.

## Decision

Public intake slug changes preserve old shared slug URLs by default.

When an Owner/Admin edits the public link name/slug, the old slug becomes an alias for the same
account's current active public intake link. Visiting an old slug should redirect to the latest
current slug or internally resolve to the same active intake page. V1 should prefer a temporary
redirect (`302`/`307`) over a permanent redirect until production behavior is proven.

Replacing/regenerating the public intake link remains the destructive/security action. Replacement
revokes the old active link and should invalidate old slugs associated with that revoked link unless
a later ADR creates a deliberate migration grace policy. Replacement requires explicit confirmation
and stale-link warning copy.

Visible product language must separate the two actions:

- friendly action: `Edit link name`;
- destructive/recovery action: `Replace public link` or `Regenerate public link`.

Recommended copy for link-name edits:

`Changing the link name keeps previous names working and redirects customers to the current link.`

Recommended warning for replacement:

`Replacing this public link will break old links you have shared on your website, texts, QR codes, or printed materials. Only do this if the old link is compromised or receiving unwanted requests.`

## Implementation Direction

Add a durable slug-alias model instead of overwriting history in place only.

Preferred shape:

- existing `KeepPublicIntakeLink` remains the active link/security lifecycle row;
- add `KeepPublicIntakeSlugAlias` or equivalent with account/link relationship, slug, current/alias
  state, creation time, retirement time, and actor/audit fields where available;
- enforce uniqueness across active current slugs and active aliases so two accounts cannot claim the
  same reachable slug;
- public intake resolution checks current slug first, then active alias;
- alias redirects/resolves only when its associated link/account remains active and permitted by
  account/access/OffSeason gates;
- replacement revokes the old link and disables aliases tied to that link.

V1 does not need a visible alias-history management UI. Settings may show only the current public link
name and explanatory copy. Support/internal tooling can inspect alias history later if needed.

## Rationale

Editing a public business URL is a normal polish action. It should not punish the owner by breaking
every link already shared with customers.

Replacement is different: it exists for compromised, spammed, or intentionally retired public links.
That action should be explicit and scary because breaking old shared links is the point.

The alias model gives Keep owner-friendly memorable URLs without treating the public intake front
door like a private token and without introducing a hidden footgun around printed or widely shared
links.

## Consequences

- S22 slug-routing work now requires a backend migration/table for slug aliases.
- Public intake route tests must cover current slug, old alias redirect/resolve, revoked-link alias
  failure, deleted/unavailable account behavior, slug collision, and cross-account isolation.
- Public Link & Profile copy must explain that editing the link name preserves old names.
- Replacement confirmation must warn that old shared links will break.
- The first backend slug-routing slice should be split if the alias migration pushes it beyond the
  normal batch gate.

## Deferred

- Visible owner UI for alias history.
- Permanent redirect (`301`/`308`) after production confidence.
- Grace policy for aliases after full link replacement.
- Custom domain support.
