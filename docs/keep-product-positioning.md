# Keep Product Positioning

**Status:** Working product compass
**Purpose:** Guide product decisions, implementation scope, and future marketing language.

## Core Promise

Keep helps small service businesses make sure customer requests are visible, owned, followed up, and
closed with confidence.

The short version:

```text
No customer slips through the cracks.
```

The more useful product boundary:

```text
Keep does not manage the work. Keep manages the promise.
```

Before the job, during the wait, after the service, and even after the close, the question is:

```text
Is the customer still confident someone has them?
```

## What Keep Is

Keep is a customer communication and follow-up command center for small service businesses.

It helps the business answer:

- Did a customer ask for help?
- Did we acknowledge them quickly?
- Does someone own the follow-up?
- Is the customer waiting on us?
- Are we waiting on the customer?
- Did anyone call, email, text, or speak with them outside the app?
- Did we resolve the issue?
- Did the customer say it was actually resolved?
- Is anything stale, forgotten, or unresolved?

Keep turns scattered communication into a visible loop:

```text
request -> acknowledgement -> ownership -> updates -> resolution -> feedback -> closeout
```

## Product Surfaces and Intended Users

Keep has two authenticated work surfaces with deliberately different jobs. They use the same API,
account model, request state, and server-authoritative authorization rules; the client surface never
decides what a user may see or do.

### PWA — Owner/Admin command center

The PWA is primarily for Owners and Admins managing the business-wide customer promise. It provides:

- all-account work, attention, Available/unassigned, closeout, feedback, and history visibility;
- dispatch, assignment, routing, and workload oversight;
- manual/business request creation;
- intake-link, member, account, and operational settings;
- account-wide totals, response posture, and trustworthy operational review.

Viewer is a trusted account-wide read-only role on this surface. Viewer supports partners, managers,
consultants, auditors, or others who need oversight without operational or administrative writes.
The product must clearly disclose the breadth of Viewer access when the role is granted.

### Native mobile — Operator field workspace

The native mobile app is primarily for Operators working on the road. It should stay narrow, fast,
and interruption-tolerant. It provides:

- My Work for requests where the Operator is Responsible or Watching;
- a prominent privacy-limited Available surface for discovering and explicitly claiming/watching
  eligible unassigned work;
- customer contact actions, updates, internal notes, status changes, attention handling, and
  participation controls allowed for that Operator;
- native phone, email, and Messages launchers plus explicit contact logging;
- resume synchronization, badges, push, and deep links for urgent actionable work.

Launching an external phone or messaging app records local intent, not a completed communication.
When the Operator returns, Keep should request confirmation without blocking unrelated work or
writing a false server-side audit event.

The surface split is:

```text
PWA    -> manage the whole business and close the loop
Mobile -> handle the work in front of the Operator
```

This is a product and UX boundary, not a security shortcut. Owners/Admins and Operators remain
subject to the same server-side account, row-visibility, and action policies regardless of client.

Public intake and customer request pages remain separate anonymous customer surfaces. They are not
PWA or Operator-mobile workspaces and expose only their intentionally limited public contracts.

## What Keep Is Not

Keep should not become a full field-service operating system.

Keep is not trying to replace ServiceTitan, Jobber, Housecall Pro, QuickBooks, dispatch calendars,
estimating tools, payment systems, inventory systems, payroll, or fleet management.

Those products help businesses run the work.

Keep protects the communication promise around the work.

Features should be questioned when they drift into:

- full scheduling/dispatch calendar ownership;
- estimates and proposals;
- invoices and payments;
- inventory;
- payroll/time tracking;
- route optimization;
- technician productivity management;
- broad CRM/marketing automation.

Some lightweight context from those areas may be useful later, but Keep should not make them its
center of gravity.

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
- closeout and history.

## Differentiation

Keep's wedge is not "manage every part of a service business."

Keep's wedge is:

```text
Every customer request becomes trackable, visible, and followed up until the loop is closed.
```

Different from full field-service platforms:

- lighter onboarding;
- works beside existing tools;
- customer gets a personal request page immediately;
- communication status is the main object, not invoices or jobs;
- external calls/texts/emails can be logged without forcing all communication into one channel;
- attention, feedback, and stale work are surfaced as operational promises, not just records.
- V1 should feel fresh through refetch-after-write, focus/resume sync, pull-to-refresh, active
  polling, server-derived badges/counts, and native push/deep links, not through SSE/WebSockets.

## Economic Wedge

Keep should not be sold primarily as "better communication."

That sounds helpful, but optional. Many small businesses already believe they communicate well
enough, even when important follow-up still lives in memory, voicemails, personal text threads, and
legal pads.

The stronger wedge is:

```text
Keep protects the revenue and reputation already at risk after a customer reaches out.
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
Keep helps protect leads, trust, reviews, repeat business, and follow-up discipline without
replacing the software that runs the job.
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
Keep works beside your existing field-service tools to protect the follow-up loop they still leave
in texts, voicemails, memory, and phone tag.
```

```text
Keep does not replace Jobber, ServiceTitan, QuickBooks, or your calendar. It protects the customer
promise around them.
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
- first response time;
- percent of customer-created requests responded to within target;
- requests that became overdue and were later handled;
- stale active requests surfaced for status check;
- customer replies waiting on the business;
- customer-visible updates sent;
- external contacts logged;
- unresolved feedback caught and reviewed;
- completed requests waiting for Owner/Admin closeout;
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

- small service businesses with inbound requests and slow or multi-step resolution;
- teams where office/admin staff and field operators share responsibility;
- businesses where missed follow-up can lose the customer quickly;
- businesses not ready to migrate to a full field-service platform;
- businesses using several tools but lacking one communication command center.

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
Does this help prevent a customer from being forgotten, waiting too long, losing trust, or leaving
without the business knowing?
```

If yes, it may belong in Keep.

If it primarily helps run labor, collect money, manage inventory, optimize schedules, or replace the
business's operating system, it probably belongs later, outside Keep, or as a lightweight integration
point.

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
intake -> triage -> ownership -> update/contact -> resolve -> close -> feedback review/history
```

Pilot support surfaces are also in scope because they protect pilot learning:

- one-tap in-app Report Friction for bugs, confusion, missing needs, and frustrating moments;
- in-app Pilot Updates page for Known Issues, What's New, Coming Soon, and Report Friction.

These are not a helpdesk product, public status page, roadmap portal, CMS, or feature-voting system.
They are lightweight pilot instrumentation so busy businesses can tell us what hurts before the
moment disappears, and so we can tell them what changed without asking them to leave Keep.

Current late-stage ideas are product-valid but should not reopen the pilot scope by default:

- request Snooze / Waiting Until for parts, booked-out contractors, weather, or third-party delays;
- quick replies with promise-safe reminders;
- sharper attention-action copy and clear-effect UI;
- expired-page repeat-request links;
- end-of-day "open promises" summaries.

Default posture from here:

```text
build, deploy, test, onboard pilots, measure, then reopen scope with evidence.
```

## Messaging Drafts

Possible plain-language positioning:

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
- Where does assignment help, and where does it feel too heavy?
- Which V1 notification types are urgent enough for native push?
- Do refetch-after-write, focus/resume sync, pull-to-refresh, active polling, badges, and push make
  the UI feel fresh enough without SSE/WebSockets?
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
