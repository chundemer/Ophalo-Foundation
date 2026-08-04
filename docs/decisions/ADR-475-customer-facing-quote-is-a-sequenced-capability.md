# ADR-475 — Customer-Facing Quote Is A Sequenced Capability

**Status:** Locked  
**Date:** 2026-08-04  
**Related:** ADR-473; ADR-474; DEF-088

## Decision

Keep will need a customer-facing quote capability. It is a planned next-stage outcome of the Price
Book, Quotes & Materials package, not an optional feature hypothesis or a reason to weaken the
current internal quote foundation.

V1 remains as ADR-473 defines it: request-bound, office-controlled, internally approved quotes
only. Customer quote delivery, customer decision/acceptance, signature, delivery/open tracking, and
multi-option presentation are not pulled into the current scope.

Before customer delivery is designed or implemented, Keep must first prove the underlying record:

1. request-bound scope capture and office review;
2. controlled catalog/pricing publication;
3. immutable, revisioned quote and line-item price snapshots;
4. recipe/assembly source and grouping history under ADR-474; and
5. clear Owner/Admin pricing and internal-approval authority.

The later customer-facing capability will render a specific immutable quote revision as a
customer-safe grouped scope and price presentation. Its customer access, delivery, revision,
acceptance, signature, legal/audit, and multi-option rules require a dedicated follow-up decision;
they must not be inferred from internal approval.

## Rationale

Customers ultimately need to see and decide on a clear proposal. Building that surface before the
quote's catalog, pricing, revision, grouping, and authority foundations are trustworthy would make
the public experience fragile and expensive to unwind. Establishing the foundation now is therefore
the direct path to a credible customer quote, not a retreat from it.
