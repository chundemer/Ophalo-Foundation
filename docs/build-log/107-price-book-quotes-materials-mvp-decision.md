# Build Log 107 — Price Book, Quotes & Materials: MVP Decision

**Status:** Direction locked — implementation not yet authorized
**Date:** 2026-07-30
**Scope:** Multi-trade, entitlement-controlled price-book, field proposed-scope capture,
office-owned quote review, and actual-material history capability
**Related:** Build 101; Build 103; Build 104; Build 108; ADR-453; ADR-454; ADR-455; ADR-456;
ADR-457; ADR-458; ADR-459; ADR-460; ADR-461

## Why this decision exists

The supplied contractor price workbook demonstrates a real need to move known prices and work
details from a spreadsheet and handwritten field notes into Keep. It must not turn Keep into a
customer-specific HVAC application or an Excel formula interpreter.

The immediate business outcome is:

```text
Office publishes known prices
  -> field staff capture proposed work/materials simply
  -> office turns the submitted scope into a reviewed formal quote
  -> approved quoted price and actual work remain retained on the Keep request
```

This record locks the smallest reusable outcome and its boundaries. It does not authorize
implementation or change Build 104 launch gates without customer confirmation.

## Decision

Keep will offer a first-party, entitlement-controlled **Price Book, Quotes & Materials** capability
package. It is a multi-trade module, not an HVAC-specific feature and not a generic third-party
plugin system.

The package uses generic concepts only:

```text
catalog item
price-book version
proposed work scope
quote and quote section
quote line
actual work/material line
review action
```

Trade vocabulary and details, such as tonnage, water-heater size, circuit amperage, pipe type, or
roofing square, are client-configured categories/attributes or item names. They are not core table
names, universal fields, or required workflow rules.

## Day-1 workflow

```text
Authorized staff imports a source sheet
  -> maps it into staged catalog rows
  -> resolves validation exceptions
  -> explicitly publishes an account-wide price-book version

Technician creates a request-linked proposed work scope
  -> selects primary work/equipment or a known item
  -> verifies default associated items and records exceptions, notes, or photos
  -> sends scope to office

Office reviews the proposed scope
  -> validates/adjusts the customer-facing scope and published prices
  -> creates a formal quote for Owner/Admin approval where required

Technician records actual work/materials after the job
  -> actual lines are retained separately from the approved quote
```

The technician does not create, see, or approve customer prices. Submitted scopes create a
**Proposed scope needs office review** signal. Formal quotes are office-owned and require
Owner/Admin review in the first release. Automatic approval thresholds, margin gates, and exception
rules are deliberately deferred until actual quote volume and policy needs are known.

## Core model and boundaries

### Catalog

A catalog item supports the reusable types `material`, `equipment`, `service`, and `fee`. The
minimum published fields are:

- display name and contractor external key/SKU when available;
- type, category, unit of measure, active state, and currency;
- current internal cost and/or customer sell price as supplied;
- optional source inputs such as labor hours, consumables allowance, or source tax amount;
- source workbook, tab, row, import version, and effective/published time.

The supplied workbook's leading-letter groups are import/source classifications only. They must be
confirmed by the customer before becoming user-facing categories.

One account-wide published price book is sufficient for the MVP. Multiple branch, department,
customer-class, or location-specific price books are deferred.

### Import and publish

Import is account-scoped and permissioned:

```text
upload -> staging -> map -> validate -> preview/review -> explicit publish -> immutable audit
```

Publish is all-or-nothing. Invalid, missing-price, duplicate, or ambiguous rows enter an exception
queue and never alter the live catalog automatically. Each import and manual catalog-price change
records actor, time, old/new available values, and source (`import` or `manual`).

The MVP imports the spreadsheet's calculated values as a price snapshot. It does **not** parse,
execute, preserve live cell dependencies for, or attempt to repair arbitrary Excel formulas.

### Quote

A proposed work scope belongs to one Keep request and contains primary work/equipment selection,
default associated items, technician adjustments, notes, and permitted photos. It is internal;
it is not a customer quote and has no customer-facing price authority.

A proposed-scope line may link to zero or more internal evidence records. This is a generic
association, not a single `PhotoId` field: a line may have multiple photos or future evidence
types. Evidence supports office review of the recommended work; it is not customer-visible unless
a later, deliberate customer-communication capability authorizes it.

An office-created quote belongs to one Keep request and contains:

- customer-facing scope/summary;
- simple display sections/groups, such as Equipment, Installation, or Optional Work;
- catalog-backed or ad-hoc lines with description, quantity, unit price, line total, and source
  price-book snapshot when applicable;
- author, reviewer, actions, timestamps, total, and immutable revisions.

The first workflow statuses are:

```text
Technician scope: Draft -> Submitted to office -> Office reviewed

Office quote: Draft -> Submitted for approval -> Approved
                                      \-> Changes requested -> Draft
```

Any post-approval quote edit creates a new revision and returns it to approval. The MVP does not
create special bypasses for removing optional items, changing a price, or adding a line.

Quotes are **fixed-price** in the initial scope. Actual work/material use is internal operational
history and job-costing context; it does not silently change the agreed customer quote total.
Time-and-materials billing, customer acceptance/signature, automated customer-facing quote delivery,
and Good/Better/Best option presentations are separate later decisions. The office may use its
existing text, email, and phone workflow to communicate an approved quote; Keep may later provide
a user-initiated copy-summary affordance without claiming delivery occurred.

### Static associated-item assemblies

The MVP includes office-built, static associated-item assemblies to reduce field friction. They are
not presented to field staff as a large, ambiguous list of "bundles." Instead, a technician selects
a recognizable primary work/equipment offering and Keep expands its default associated scope.

```text
Primary offering: 50-gallon water-heater replacement
  -> water pan
  -> standard fittings/flex lines
  -> pipe allowance
  -> optional expected installation-time allowance
```

The same generic relationship supports HVAC, plumbing, electrical, roofing, landscaping, and other
trades. It belongs to an office-owned **offering/assembly**, not permanently to a catalog item: the
same water heater or condenser may be sold individually, itemized as part of one offering, or
included in a different fixed-price offering.

Every assembly declares one explicit price treatment:

- **Summed assembly** — the office quote totals the published prices of its priced parent/child
  lines.
- **All-inclusive package** — the primary offering has one published fixed price; associated child
  lines are shown as `Included in package` and are linked to the priced parent, not converted to
  zero-priced catalog items or added again to the total.

This explicit treatment prevents double charging when a published equipment/package price already
includes standard materials or labor. Advanced nested assemblies, conditional components,
compatibility engines, automatic selection, and option-pricing remain deferred.

### Technician field escape ladder

Field item selection is a progressive escape ladder, not a single search box or a flat catalog
browser. Each rung is tried in order; a technician only drops to the next rung when the current one
does not have what they need. All rungs are equally subject to the field pricing rule: no rung ever
shows price, cost, margin, tax, inventory, or formula/import detail.

```text
1. Primary offering
2. Common Items
3. Client-configured Categories
4. Deterministic Name/SKU/Alias Search
5. Always-available Off-Catalog Item
```

1. **Primary offering.** The technician selects one recognizable primary work/equipment offering
   (for example "50-gallon water-heater replacement"). Selecting it expands the office-defined
   default associated items for that offering/assembly (see Static associated-item assemblies,
   above). This is the fastest path for recognizable, pre-configured jobs and requires no catalog
   knowledge from the technician.
2. **Common Items.** A short, office-curated list of items used often enough to deserve one-tap
   access without a named offering or a search — for example frequently added materials that do
   not belong to a specific assembly. Configuration requirement: Owner/Admin marks specific catalog
   items as "Common" (an account-scoped, orderable flag on the catalog item), independent of
   category or assembly membership; the list must stay short by product convention (a long "common"
   list defeats its purpose) but the MVP does not need a hard enforced cap.
3. **Client-configured Categories.** Trade-specific groupings the office defines to browse the
   catalog when neither the primary offering nor Common Items has the item — for example
   "Refrigerant," "Fittings," "Water Heaters" for HVAC/plumbing, or trade-equivalent categories for
   electrical, roofing, or landscaping. Configuration requirement: catalog category is an
   account-owned, free-text-named entity (see Proposed entities in Build 108) with items assigned
   to zero or one category; categories are client-configured, never a fixed trade taxonomy shipped
   by Keep.
4. **Deterministic Name/SKU/Alias Search.** A search box over the published catalog matched by
   exact/prefix/substring text against display name, SKU/external key, and technician-facing
   aliases/search terms — never AI/fuzzy/semantic matching. Configuration requirement: catalog
   items may carry zero or more alias/search-term strings (for example a common trade nickname or
   an alternate part name) that the office maintains as part of catalog data; search must return
   deterministic, explainable matches so a technician is never given a machine-guessed physical
   part.
5. **Always-available Off-Catalog Item.** When none of the first four rungs has the item, the
   technician can always add a one-off off-catalog line: description and quantity required,
   receipt/photo optional. It never blocks the workflow, and it always requires office review before
   any catalog promotion (see Actual work and ad-hoc materials, below).

The ladder is deliberately generic: no rung is trade-specific, no rung is hard-coded to a fixed
category list, and every rung composes with the same server-side price-hiding and authorization
rules. The full data model for Common Items marking, category ownership, and alias/search-term
storage is detailed in Build 108's ERD preflight.

### Actual work and ad-hoc materials

Quoted lines describe proposed work and price. Actual lines describe what was used or performed.
They are distinct records even when an approved quote seeds the initial actual-work list.

An authorized technician may add an ad-hoc actual-material line when an item is absent from the
published catalog. Description and quantity are required; receipt/photo and cost/price are optional
where allowed. Such a line always requires office review and is never silently promoted into the
live catalog.

An off-catalog item remains a one-off by default. During office review, an authorized catalog user
may choose **Create catalog draft from this item**. That action creates a draft carrying traceable
source context; it does not promote or publish the item automatically. The office must supply the
normal catalog requirements and complete the normal review/publish path before it becomes a live
catalog item.

### Authorization constraints

Keep must preserve room for an optional, generic request authorization constraint supplied by a
business workflow, such as a not-to-exceed amount, customer budget, insurer limit, or approval
reference. It is not an HVAC or property-management-specific field and does not require an
accounting/invoicing engine.

When an applicable constraint is present, office quote review may show a clear, non-blocking
warning if the quote total exceeds the authorized amount. The MVP does not automatically block
approval, obtain external approval, calculate a remaining balance, or synchronize a property
manager/accounting system. Those require their own authority and failure-handling decisions.

### Tax and prices

The customer requires **tax-included** quote presentation. For the MVP, Keep displays and snapshots
the published item price and resulting quote total as tax included. It does not add a separate tax
line or dynamically calculate tax at the job/quote level.

The workbook's `Tax` input appears within the contractor's cost calculation before its sell-price
markup. That fact does not establish the contractor's customer-sales-tax, exemption, invoicing, or
accounting-export treatment. Keep therefore retains imported source inputs when available but does
not infer or reverse-engineer a tax split.

Job-level tax calculation, tax exemptions, jurisdiction lookup, tax remittance, and accounting tax
exports are deferred pending the contractor's accounting and legal/tax workflow confirmation.

## Keep integration and entitlement boundary

Keep Core continues to own requests, their primary lifecycle, and the unified attention surface.
The capability module owns catalog, imports, pricing snapshots, quotes, actual lines, review state,
and its audit history.

```text
Price Book, Quotes & Materials module
  -> emits proposed-scope-submitted-for-review signal through an explicit contract
  -> Keep attention surface presents “Proposed scope needs office review” to authorized office users
```

Core does not inspect module tables or implement trade/pricing logic. The module must own its
domain tables/migrations, application services/endpoints, PWA/mobile surfaces, permissions, account
feature gate, audit, and disable/degradation behavior.

The server must enforce both the account entitlement and user permission for every capability
action. Hiding navigation or buttons is not authorization. On entitlement removal, creation and
editing stop; historical catalog import, quote, review, and actual-work snapshots remain retained
and read-only for authorized historical/audit access.

`Proposed scope needs office review` is an actionable internal work signal. It must not change
customer-visible request status, claim customer acceptance, or be confused with an invoice/payment
state.

## Package activation and implementation plan

### How a customer receives the feature

**Price Book, Quotes & Materials** is a capability package, not a normal Business Settings toggle.
It changes the account's operational workflow, permissions, retained records, and commercial
subscription; an Owner/Admin must not be able to casually enable or disable it without the
corresponding commercial/onboarding action.

```text
Customer selects/accepts the package
  -> authorized commercial/onboarding action grants account entitlement
  -> server permits the package
  -> Owner/Admin configures it in Business Settings
  -> authorized field staff use it on eligible requests
```

The account capability key is `keep.price_book_quotes_materials`. It is evaluated server-side in
addition to user permission and request/state policy; it is never inferred from a client route,
visible navigation item, or plan-name check at a call site.

For the pilot, OpHalo enables the package through the authorized account-entitlement administration
path after customer agreement. Keep does **not** build self-service checkout, billing changes, or
an Owner/Admin enablement switch in this slice. A later subscription/billing experience may allow
an Owner to select the package, confirm commercial terms, and grant the same entitlement through a
reviewable commercial workflow.

Once the account is entitled, Business Settings provides a **Price Book & Quotes** workspace for
configuration and operation, not subscription control:

```text
Catalog
Imports
Quote/material review queue
Package status
```

The first policy is intentionally fixed: one account-wide published catalog, tax-included quoted
prices, and Owner/Admin review for every submitted quote. Later policy settings require their own
decision; the workspace must not expose controls for unimplemented policy behavior.

### Build sequence

Implementation proceeds as bounded slices, each with server authorization, account scope, audit,
and focused tests:

1. **Capability foundation** — register the feature key; add the server-side entitlement and
   permission guards; expose a read-only package status to authorized Owner/Admin users.
2. **Catalog and import** — module-owned catalog/import/version/audit model; staged mapping,
   validation, exception review, and atomic publish.
3. **Proposed scope, assembly, and office quote** — module-owned scope, offering/assembly,
   quote, revision, section, line, and review model; request link; explicit price treatment;
   internal proposed-scope attention contract.
4. **Actual work/materials** — separate actual-line capture, ad-hoc-item review posture, and
   immutable historical snapshots.
5. **Entitled surfaces and degradation** — PWA/mobile experiences for the relevant roles; disabled
   account behavior blocks all module mutations while retaining authorized read-only history.

The module owns all catalog/quote/material persistence and services. Keep Core owns the request and
attention surface. The integration is an explicit contract, never direct module-table access or
pricing logic embedded in Core.

Initial user-action boundaries are:

```text
Owner/Admin: catalog/import/publish, office quote review/approval, package workspace
Authorized field staff: proposed-scope creation/submission, actual-material recording
```

Exact permission-key names and any further delegation are implementation decisions, but every
action must compose account entitlement, active membership permission, and record/state policy.

## Simplicity is the product constraint

The capability is built first from the working perspective of the business owner and field
technician. Architecture, audit, entitlement, pricing history, and import complexity may exist
behind the scenes, but must not be exported as routine user work. Complexity in incumbent
field-service systems is a recurring business-owner complaint; avoiding that complexity is a
product requirement, not cosmetic polish.

The normal owner/admin workflow must remain short and recognizable:

```text
Enable package
  -> import or maintain the price list
  -> create common primary offerings and associated items
  -> review submitted field scope and formal quotes
  -> see quoted, approved, and actual work/material history
```

The normal field workflow must remain short and interruption-tolerant:

```text
Open request
  -> select primary work/equipment or add a known item
  -> verify the few default associated items needed for this work
  -> record an exception, short scope note, or photo
  -> submit proposed scope to office
  -> record actual work/materials
```

Field staff must not be expected to configure categories, imports, prices, pricing formulas, tax
handling, price-book versions, approval rules, inventory, or margin logic. Owner/Admin users must
not be required to configure an ERP-like system before staff can prepare a quote. Advanced or
infrequent controls belong behind deliberate owner/admin paths and must not crowd normal field
work.

Every implementation/design review for this package must answer:

1. Does this remove a real owner or technician step, or add one?
2. Can a newly trained field user understand the next action without knowing the internal model?
3. Can an Owner/Admin correct a price or common bundle without support or technical knowledge?
4. Does the visible flow present only the decision needed now, with advanced detail progressively
   disclosed?
5. Does the capability preserve a truthful, recoverable workflow when a user makes a mistake or
   loses connection?

If a proposed behavior cannot satisfy these questions, its additional complexity requires an
explicit product decision before it enters the normal workflow.

### Field price visibility

Published prices are hidden from field users by default. The MVP has no after-hours exception.
A later business may explicitly grant a narrowly scoped permission to present—not edit, calculate,
discount, or override—a published fixed-price offering to a customer. That later capability must
be deliberate, audited, and limited to the published offering snapshot; it must not expose costs,
margins, tax internals, or general catalog pricing.

## Financial integrity constraint

This capability handles financially consequential business data. It is not an accounting system,
but price, quote, and approval behavior must be designed and tested with accounting-adjacent care.

- Money is server-authoritative. Clients never decide a total, published price, approval state, or
  price snapshot.
- Monetary values use precise currency storage and a documented rounding policy; floating-point
  arithmetic is prohibited for persisted/calculated money.
- A published price, submitted scope, office quote, approval, and actual-work record retain their
  relevant immutable snapshots. Later catalog/assembly edits cannot rewrite history.
- A manual office price override requires a reason, actor, time, old value, and new value.
- Assembly price treatment is explicit (`summed` or `all-inclusive`); included children are linked
  to their priced parent so they cannot be charged twice.
- Imports are staged, reviewed, and atomically published. They must flag errors, missing/ambiguous
  pricing, duplicates, and material price changes before affecting the live catalog.
- Review UI must make totals, included items, overrides, and deviations from the submitted scope
  obvious before approval.
- Focused automated tests are required for totals, rounding, double-charge prevention, snapshots,
  revisions, import/publish atomicity, authorization/account isolation, and concurrent edits.
- AI, heuristics, or guessed spreadsheet interpretation may identify an import mapping or anomaly,
  but may never set a live price without an authorized human confirmation and publish action.

Keep does not claim to calculate tax, create an invoice, receive payment, or synchronize an
accounting system until each is separately designed, authorized, and proven.

## Mobile reliability

The current native-mobile posture is online-first with blocked offline writes (ADR-403). This
decision does not silently introduce a second offline mutation/sync system. Quote UI must preserve
local draft input on temporary network failures, and any future offline creation/queued submission
must be a deliberate, idempotent mobile-platform decision with its own conflict and recovery rules.

## Explicitly out of scope

- arbitrary Excel formulas, cell dependencies, formula errors, or formula parser compatibility;
- automatic pricing-rule recalculation, margins, markups, labor-rate changes, and consumables
  automation;
- nested/conditional/compatibility-based assemblies, automatic component selection, and
  sophisticated option pricing;
- automatic quote approval, threshold policy, and margin guardrails;
- inventory depletion, purchase orders, vendor stock, or supplier ordering;
- installer payroll/compensation;
- job-level tax calculation, exemption handling, remittance, or tax export;
- invoicing, payments, collection, accounting synchronization, or accounting-system authority;
- customer quote delivery, acceptance, signature, financing, or option-bidding UX;
- customer line-item acceptance/decline/defer dispositions and related follow-up/upsell policy;
- multi-price-book/location/department catalog selection; and
- a customer-specific branch or unguarded implementation path.

## Later evolution, without breaking history

After the MVP has real use evidence, the package may add structured—not spreadsheet—pricing
archetypes: fixed price, target margin, markup/cost-plus, cost-plus assembly, and static bundle.
Any future rule or published-price change applies prospectively. Historical quote and actual-work
snapshots are immutable.

Potential later capabilities include configured bundles, customer option comparisons, tax policy,
multi-price-book scope, T&M, accounting exchange, and offline queued mutations. Each requires its
own workflow, authority, audit, and failure/recovery decision before implementation.

## Follow-up validation questions

Before implementation is authorized, confirm with the contractor:

1. Does the customer-facing quoted price already include all tax they expect to charge, and how is
   that price represented in their accounting system?
2. Is internal Owner/Admin approval required for every quote at launch, and what turnaround/support
   expectation applies when the reviewer is unavailable?
3. Is a field quote required on Day 1, or is reviewed quote creation staged after catalog import and
   actual-material capture prove useful?
4. Which source tabs and columns are authoritative for the first published catalog, and which rows
   are active versus historical/test data?
5. Does the customer need customer-facing quote delivery/acceptance at launch, or only an approved
   internal quote record?
