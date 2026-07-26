# ADR-449 — Owner/Admin Request List Work Queue

**Status:** Locked  
**Date:** 2026-07-25  
**Related:** ADR-435, ADR-436, ADR-447, GAP-042, GAP-045

## Decision

Keep is a request-and-continuity product, not field-service-management software. Owner/Admin
workspace language uses **Requests** and **Work**, never **Jobs**.

The Owner/Admin Request List presents:

- page heading: `Requests for {Business name}`;
- primary queue label: `All work`;
- supporting copy: `Open requests and feedback requiring review, ranked with customer promises needing attention first.`

`All work` is visually organized into server-authoritative sections:

1. `Needs attention`, rendered only when matching rows exist; then
2. `Open work`, containing the remaining non-terminal rows.

The existing Needs Attention tab remains the focused queue. Section membership and order remain
server-owned; the browser may not fabricate urgency or reorder rows.

## Rationale

`Job` implies dispatch, calendars, estimates, invoices, and related field-service-management scope
that Keep does not provide. `Active requests` inaccurately excludes post-close feedback requiring
Owner/Admin review. A compact business-name page heading provides account context without bloating
the tab bar at narrow widths.

The two-section queue gives an owner an immediate triage anchor while retaining a single workbench
for routine work. Empty attention sections must not render a header, shell, or reserved space.

## Consequences

- The protected `view=default` API name and its server-owned membership, ranking, counts,
  authorization, cursors, and quick-action metadata remain unchanged.
- Section headers are quiet labels, not dashboard banners. Any count is page-scoped unless the API
  explicitly supplies a truthful section total.
- Terminology must match in tabs, headings, empty/loading/result-state copy, and accessible labels
  at desktop and mobile widths.
