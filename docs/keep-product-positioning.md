# Keep Product Positioning

**Status:** Current product compass — reviewed 2026-08-21
**Purpose:** Guide product decisions, implementation scope, packaging, and future marketing language.
**Release posture:** The controlled parallel field pilot is the active target; Keep is not yet
pilot-ready. “Available” below means implemented product capability, not a claim of general release.
**Related direction:** Build 101 (contractor asset/workflow discovery), Build 102 (large-account
Request List discussion), and Build 103 (modular capability packages).

## Core Promise

Keep helps service contractors make sure every service need is known, owned, followed through, and
closed with confidence—connected to the customer and service context involved.

The short version:

```text
Know the work. Keep the promise.
```

This is the working **Keep product line**. It expands the earlier customer-only shorthand, “No
customer slips through the cracks,” without discarding its intent. The OpHalo parent tagline remains
`See the gaps. Close them.` The locked public-page/footer motto remains unchanged until a separate
brand/ADR decision changes it.

The more useful product boundary:

```text
Keep does not manage every part of the work. Keep maintains the trusted record and promise around it.
```

Before the job, during the wait, after the service, and even after the close, the question is:

```text
Do we know the service context, the work, and the promise well enough to act with confidence?
```

## What Keep Is

Keep is a continuity and work-record platform for service contractors. It connects a service need
to the people, service location, work record, and communication needed to resolve it responsibly.

The core customer promise remains essential. The future asset direction recognizes that a
contractor cannot reliably keep that promise when the team cannot identify exact equipment,
understand prior work, or retain what was done at the site.

It helps the business answer:

- Did a customer ask for help?
- What service location and request context are involved?
- What work, diagnosis, and material context has been retained?
- Did we acknowledge them quickly?
- Does someone own the follow-up?
- Is the customer waiting on us?
- Are we waiting on the customer?
- Did anyone call, email, text, or speak with them outside the app?
- Did we resolve the issue?
- Did the customer say it was actually resolved?
- Is anything stale, forgotten, or unresolved?

Keep turns scattered work and communication into a visible, retained loop:

```text
service need -> known context -> ownership -> updates/contact -> completion/work record
-> retained history -> feedback/closeout
```

Not every account needs every stage on day one. Keep Core owns the request/continuity loop. The
enabled Price Book capability now supports controlled catalog/assembly data and a price-blind
Actual Work record for the controlled field-pilot workflow. Asset identity, QR labels, B2B property
authorization, customer-facing quotes, and accounting exchange remain staged directions, not
current-product claims.

For pilot, the request must be capturable by the business first. If a customer calls, texts, emails,
leaves a voicemail, walks in, or comes through a referral, Keep should let the business create the
request immediately with the minimum useful details. The public intake link and customer request page
are then optional collaboration and confidence surfaces, not a prerequisite for the work to exist.

That distinction protects the core promise: Keep cannot become a secondary inbox for only the
customers willing to fill out a form. The business hears about the need first; Keep captures it
first; the customer page helps enrich, update, and close the loop afterward.

## Current Product and Capability Direction

Keep remains one product, deployed and operated by OpHalo. It grows through first-party,
server-enforced capability packages rather than customer-specific forks or a third-party runtime
plugin marketplace. Packages below are deliberately marked as available or directional so product
and marketing copy do not confuse a designed capability with a released one.

```text
Keep Core → Asset Operations → Price Book & Materials → B2B Property Workflow
          → Accounting Exchange where the workflow justifies it
```

The packages represent complete customer outcomes, not isolated buttons:

- **Keep Core — available:** a service need is captured, owned, communicated, followed up, and
  closed. It includes business-created and public-intake requests, service-location context,
  customer request pages, external-contact logging, attention/follow-up handling, and feedback
  review.
- **Price Book & Actual Work — enabled-pilot capability:** authorized staff maintain a controlled
  catalog, categories, and offering assemblies. A field user can record factual work/materials on
  a request without seeing prices; submitted visits retain immutable snapshots and enter an
  Owner/Admin review queue. It is not quoting, invoicing, inventory, or accounting export.
- **Asset Operations — directional:** exact equipment identity, opaque QR labels, permitted service
  history, and warranty context require a separate asset-identity and authorization model.
- **B2B Property Workflow — directional:** property/unit, authorization, completion, and billing
  handoff context are not yet a released property-manager workflow.
- **Accounting Exchange — directional:** a reviewed, explicit accounting boundary is planned; Keep
  does not currently replace QuickBooks or create an accounting handoff.

Account entitlement, user permission, and record/state policy remain distinct server-side gates for
every package. A customer cannot access a capability merely because its UI is visible.

### Commercial-Scope Direction

Keep is deliberately avoiding traditional package sprawl. The current capability has controlled
catalog items and office-managed offering assemblies for factual field capture. Reusable scope
recipes, formal commercial documents, and customer-facing quotes are later work, not current pilot
behavior.

Catalog search, primary offerings/assemblies, and explicit factual-completion nudges make repeated
field recording faster without requiring a contractor to configure every job permutation. Field
capture stays price-blind. Customer-facing delivery, acceptance/approval, PDFs, signatures,
multi-option proposals, invoices, and payments remain separately sequenced capabilities.

## Product Surfaces and Intended Users

Keep currently has one authenticated, responsive PWA. It uses the same API, account model, request
state, and server-authoritative authorization rules at desktop and phone sizes; the client never
decides what a user may see or do.

### PWA — staff workbench

The PWA provides the account-wide command-center views for Owners/Admins and the permitted work
views/actions for Operators. It provides:

- all-account work, attention, Available/unassigned, closeout, feedback, and history visibility;
- dispatch, assignment, routing, and workload oversight;
- manual/business request creation;
- fast business-first capture from calls, voicemails, texts, emails, walk-ins, and referrals;
- intake-link, member, account, and operational settings;
- account-wide totals, response posture, and trustworthy operational review;
- enabled-pilot Actual Work capture, retained submitted-visit history, and an Owner/Admin review
  queue; field capture is price-blind while financial review is a distinct office action.

Viewer is a trusted account-wide read-only role on this surface. Viewer supports partners, managers,
consultants, auditors, or others who need oversight without operational or administrative writes.
The product must clearly disclose the breadth of Viewer access when the role is granted.

The responsive PWA adapts contact handoffs and layout to the device: it can hand a desktop user
off to their phone for calling/texting and can launch the permitted external actions on a phone.
Those launches record intent only; contact logging remains an explicit confirmation. A native
mobile app is a separate future track, not a current Keep surface or public promise.

Public intake and customer request pages remain separate anonymous customer surfaces. They are not
PWA or Operator-mobile workspaces and expose only their intentionally limited public contracts.

## What Keep Is Not

Keep should not become a generic full field-service or property-management operating system.

Keep is not trying to replace a customer's entire ServiceTitan, Jobber, Fleetmatics, QuickBooks,
Accela, dispatch-calendar, payment, inventory, payroll, or fleet-management deployment.

Those products help businesses run the work.

Keep protects and retains the operational record and communication promise around the work.

Features should be questioned when they drift into:

- full scheduling/dispatch calendar ownership;
- general estimating/proposal software, invoices, payments, or tax calculation;
- inventory;
- payroll/time tracking;
- route optimization;
- technician productivity management;
- broad CRM/marketing automation.

Bounded capability is allowed where it completes the contractor's record: controlled catalog data,
price-blind Actual Work capture, immutable material/work snapshots, and Owner/Admin review. Future
asset-linked context, customer quotes, and accounting exchange need their own approved workflows.
Keep does not become the source of truth for stock, payment collection, accounting ledgers, fleet
GPS, routing, or a property manager's portfolio system.

## Why Businesses Need It

Many small service businesses already have software. That does not mean they have reliable customer
follow-up.

Common reality:

- requests arrive through phone calls, texts, voicemails, emails, website forms, referrals, and
  memory;
- text threads mix with personal messages and get buried;
- voicemails turn into phone tag;
- one person thinks someone else replied;
- field operators know something happened, but the office does not;
- the customer waits without confidence and calls a competitor;
- after the job, negative feedback is easy to miss or mentally file away.

Keep is valuable when the business already has tools, but still lacks one trusted place for:

- customer request intake;
- current attention state;
- first response tracking;
- customer-visible updates;
- external contact logging;
- responsibility and routing;
- unresolved feedback;
- closeout and history; and
- in enabled pilot accounts, what material or labor was actually recorded on a visit.

Equipment/service history, warranty context, property-manager authorization, and accounting handoff
are future extensions; they should not be used to describe the current pilot as though they exist.

## Differentiation

Keep's wedge is not "manage every part of a service business."

Keep's wedge is:

```text
Every service need becomes a known, owned record—followed through until the promise is closed.
```

Different from full field-service platforms:

- lighter onboarding;
- works beside existing tools;
- customer communication stays connected to the request and retained work record;
- price-blind factual work/material capture can retain what happened without turning Keep into
  inventory or accounting software;
- customer gets a personal request page where the B2C workflow calls for it;
- external calls/texts/emails can be logged without forcing all communication into one channel;
- attention, feedback, and stale work are surfaced as operational promises, not just records;
- the responsive PWA is deliberately refreshed through refetch-after-write, focus/resume sync,
  pull-to-refresh, active polling, and server-derived badges/counts. Native push/deep links are
  future-native-app work, not a current claim.

## Economic Wedge

Keep should not be sold primarily as "better communication."

That sounds helpful, but optional. Many small businesses already believe they communicate well
enough, even when important follow-up still lives in memory, voicemails, personal text threads, and
legal pads.

The stronger wedge is:

```text
Keep protects the revenue, reputation, and operational memory already at risk once service work
begins.
```

Small businesses spend real money and effort to make the phone ring: local SEO, referrals, wrapped
trucks, ads, review reputation, lead services, and years of word of mouth. Keep protects that
investment after the request arrives by making sure the customer is captured, acknowledged, owned,
updated, and reviewed until the loop is closed.

The customer problem is anxiety:

- Did anyone get my request?
- Am I waiting for nothing?
- Should I call someone else?
- Did they forget me?
- If the job is done, do they care whether it was actually resolved?

The owner problem is leakage:

- Did a new lead sit too long?
- Did a tech promise something the office cannot see?
- Did a customer text back and get buried?
- Did an unhappy customer quietly head toward a bad review?
- Are we losing jobs after paying to earn the opportunity?

That makes Keep a defensive, economic product:

```text
Keep helps protect leads, trust, reviews, repeat business, and service continuity without forcing a
contractor to replace every system that runs the business.
```

Use careful revenue language. Keep should not claim it definitely saved a specific job unless the
business confirms that outcome. The honest claim is that Keep surfaces customers and requests at
risk of being forgotten, delayed, or left without clear follow-up.

Sales language to test:

```text
You already spend money to make customers reach out. Keep helps make sure those customers do not
slip away after they contact you.
```

```text
Keep gives your team a trusted record of the work and customer promise that otherwise gets split
across texts, memory, paper, and disconnected tools.
```

```text
Keep does not ask you to replace every system. It gives your team the continuity layer around the
work those systems do not keep connected.
```

## Proving Impact

Keep should eventually help the business see whether the communication loop is improving.

The product should be careful with claims. Keep usually cannot prove that a customer was definitely
going to disappear, that a job was won only because of Keep, or that revenue increased unless the
business records those outcomes.

Better language:

```text
Keep surfaces customers and requests at risk of being forgotten, delayed, or left without clear
follow-up.
```

Early impact measurement should focus on behavior and customer-confidence signals:

- requests captured through the intake link;
- requests captured directly by the business from calls/texts/emails/voicemails/walk-ins/referrals;
- first response time;
- percent of customer-created requests responded to within target;
- requests that became overdue and were later handled;
- stale active requests surfaced for status check;
- customer replies waiting on the business;
- customer-visible updates sent;
- external contacts logged;
- unresolved feedback caught and reviewed;
- completed requests waiting for Owner/Admin closeout;
- submitted Actual Work visits and Owner/Admin review of them, where the capability is enabled;
- repeat customers seen again through Keep when identity matching is reliable enough.

Later business-impact measurement may add optional fields such as:

- estimated job value;
- won/lost/not sure;
- repeat customer;
- referral/source;
- rework/callback relationship;
- customer retained after negative feedback.

The first reporting posture should be an impact summary, not a full analytics platform:

```text
You captured X requests, responded in a median Y minutes, handled Z overdue/stale follow-ups, sent N
customer updates, and reviewed M unresolved feedback items.
```

This is tracked separately because the measurement architecture must be decided deliberately. The
reference app's polling/signal approach should not be copied by default. Keep already has append-only
timeline events plus current-state fields; start from those facts, then decide whether later impact
reporting needs derived read models, background projections, pub/sub, SSE, or a fuller signal engine.

## Who It Is For

Strong fit:

- service contractors with inbound requests and slow or multi-step resolution;
- teams where office/admin staff and field operators share responsibility;
- HVAC and other service businesses where retained request/work context matters;
- contractors serving property managers who need a continuity layer without replacing the property
  manager's formal system;
- businesses where missed follow-up can lose the customer quickly;
- businesses not ready to migrate every workflow to a full field-service platform;
- businesses using several tools but lacking one trustworthy continuity and work record.

Likely early examples:

- HVAC;
- plumbing;
- electrical;
- home repair and contracting;
- property maintenance;
- specialty local service providers.

Weaker fit:

- businesses with no meaningful follow-up loop;
- businesses where every request is completed immediately on first contact;
- businesses already deeply centralized in a full field-service platform and satisfied with its
  communication workflow;
- high-volume support teams that need a traditional ticketing/helpdesk product.

## Tiny-Team Posture

Keep must not feel like heavyweight ticket software for one- and two-person businesses.

The backend may keep assignment, watching, mute, and routing semantics because they protect
permissions, notification routing, and future growth.

The product surface may need to simplify:

- solo: no visible assignment controls; all work is implicitly mine/all;
- two-person: simplified "mine/all", "assigned to", or "send to field" language;
- larger teams: fuller assignment/watch/mute/unassigned controls.

This is tracked separately as `DEF-052` because hiding UI must not silently change Operator
visibility, notification routing, or self-assign rules.

## Product Test For New Features

Before adding a feature, ask:

```text
Does this help the team retain the right service context and what happened, and prevent a customer
or property partner from being forgotten, waiting too long, losing trust, or leaving without the
business knowing?
```

If yes, it may belong in Keep.

If it primarily tries to operate a general ledger, manage stock/procurement, optimize routes, run
payroll, or replace the customer's operating system, it probably belongs outside Keep or at a narrow,
explicit integration boundary.

## Pilot Scope Lock Posture

The pilot product should now be treated as scope-locked.

That does not mean new ideas are bad. It means new ideas are captured, shaped, and deferred unless
they meet a high bar for go-live:

- security, privacy, legal, or data-integrity risk;
- a pilot business cannot complete the core promise loop without it;
- a data-model correction must happen before go-live to avoid a painful migration;
- multiple pilot businesses hit the same blocker in real use.

The locked pilot loop is:

```text
capture/intake -> triage -> ownership -> update/contact -> resolve -> close -> feedback review/history
```

`capture` includes business-created requests. `intake` includes customer-created public form
submissions. Both must enter the same Keep accountability loop.

Pilot support surfaces are also in scope because they protect pilot learning:

- one-tap in-app Report Friction for bugs, confusion, missing needs, and frustrating moments;
- in-app Pilot Updates page for Known Issues, What's New, Coming Soon, and Report Friction.

These are not a helpdesk product, public status page, roadmap portal, CMS, or feature-voting system.
They are lightweight pilot instrumentation so busy businesses can tell us what hurts before the
moment disappears, and so we can tell them what changed without asking them to leave Keep.

Current late-stage ideas are product-valid but should not reopen the existing reliability/pilot scope
by default:

- request Snooze / Waiting Until for parts, booked-out contractors, weather, or third-party delays;
- quick replies with promise-safe reminders;
- sharper attention-action copy and clear-effect UI;
- expired-page repeat-request links;
- end-of-day "open promises" summaries.

Default posture from here:

```text
stabilize and prove Core → validate the bounded Direct Actual Work pilot workflow → productize
reusable capability packages with evidence.
```

## Messaging Drafts

Possible plain-language positioning:

- "Know the work. Keep the promise."
- "Keep gives contractors a trusted record of the work and customer promise."
- "Every service need is known, owned, and followed through."
- "Know what happened on the visit, and keep the work moving."
- "Keep the office, technician, customer, and property partner aligned around the same work."
- "Keep makes sure every customer request is seen, owned, and followed up."
- "Protect the leads you already paid to earn."
- "Stop losing customers after they reach out."
- "A lightweight command center for customer follow-up."
- "For service businesses that already have tools, but still lose customers in the gaps."
- "Give every customer a request page. Give every team a follow-up command center."
- "Stop relying on memory, buried texts, and phone tag to protect customer trust."
- "Keep does not run your whole business. It protects the customer promise."

## Pilot Learning Goals

For early pilots, learn:

- Do customers feel more confident after receiving a personal page?
- Do businesses respond faster to new requests?
- Do admins/operators trust the request list as the place to look?
- Does external-contact logging reduce duplicate work and missed follow-up?
- Does feedback review catch unresolved issues that would otherwise disappear?
- Does the product feel lighter than a field-service platform?
- Does price-blind Actual Work capture reduce re-entry while preserving a trustworthy historical
  record?
- Do catalog search, assemblies, and factual-completion nudges help technicians record complete
  work without exposing prices?
- Where does assignment help, and where does it feel too heavy?
- Do refetch-after-write, focus/resume sync, pull-to-refresh, active polling, and badges make the
  responsive PWA feel fresh enough without SSE/WebSockets?
- Do pilot users actually use Report Friction when something hurts, and does Pilot Updates reduce
  repeated "is this known?" questions?
- Which impact metrics make owners feel the subscription is protecting revenue, reputation, or
  repeat business?

## Internal Product-Ops Visibility

Keep also needs internal visibility for the team building and supporting the product.

This is separate from customer-facing impact reporting. It answers:

- Did a new business onboard?
- Did the owner invite their team?
- Did they create or share the intake link?
- Did their first customer request arrive?
- Are they using the request list/detail/customer page loop?
- Which features are used, ignored, or confusing?
- Has an account gone quiet after setup?
- Are notification, worker, delivery, or integration paths failing?

The reference app felt blind because internal onboarding/usage events were not surfaced well enough.

Future internal tools should include a read-only admin dashboard, account-level usage summaries, and
high-signal internal alerts. Internal mobile alerts may be useful for pilot operations, but they
should be metadata-light, permission-gated, audited, and deliberately designed. Do not copy a
polling/signals approach by default; decide the event/subscription architecture when the admin and
notification slices are planned.

## Pricing Strategy Inputs

Keep pricing should be revisited after pilot usage data exists.

The early strategy should avoid discouraging the behavior Keep wants: businesses should share their
intake link, invite the right team members, and use the system as the trusted follow-up place.

Likely pricing levers:

- active users / team size;
- active operators;
- request volume;
- customer-created vs business-created requests;
- active/open request count;
- notification usage after Phase 9;
- history/search volume;
- feature packaging by plan;
- business impact signals such as response speed, unresolved feedback reviewed, stale follow-ups
  surfaced, repeat customers, and optional estimated job value.

Current pricing instinct:

```text
Charge mainly by business/team size.
Use request volume as plan guardrails or fair-use limits.
Use impact metrics to prove value.
Avoid strict per-request pricing early.
```

Reason:

- Per-request pricing can make businesses hesitant to share the intake link.
- High usage is a success signal, not something the product should punish too early.
- Team-size pricing is easier for small businesses to understand.
- Extra-user pricing should stay low enough that teams invite real users instead of sharing logins.
- SMS, if added later, may need separate limits or pass-through cost because it has real variable
  cost.

Pilot data to collect before final pricing:

- active users;
- active operators;
- requests per month;
- customer-created requests per month;
- average and median first response time;
- response-within-target percentage;
- customer updates per request;
- external contacts per request;
- unresolved feedback count;
- repeat customer signals;
- app open/use frequency by role;
- whether businesses feel the value is closer to a $49-$79, $99, $199, or $299+ monthly product.

Working public pricing hypothesis:

```text
Solo/tiny: $49-$79/month
Small team: $99/month
Team/pro: $199/month
Growth: $299+/month
```

This is not locked. Track final pricing strategy separately after pilots produce real adoption,
volume, and impact data.
