# Pilot Readiness Decision Questions

**Date:** 2026-06-25  
**Status:** Temporary working document — not authoritative  
**Purpose:** Collect the questions that must be answered before locking the remaining pilot/go-live
build plan. After decisions are made, promote the final outcomes into `docs/decisions/decision-index.md`,
the relevant build log, ADRs, and/or `docs/deferred-topics.md`.

## How To Use This Document

- **Product decision** means the answer defines user promise, positioning, workflow, copy, onboarding,
  operating policy, or pilot behavior.
- **Coding decision** means the answer materially affects data model, API contract, authorization,
  notifications, reporting, client architecture, or implementation order.
- **Both** means the product promise and implementation contract are coupled enough that they should be
  locked together.

Recommended outcomes for each question:

- **Lock now** — required for pilot/go-live or needed before dependent implementation.
- **Defer deliberately** — real topic, but not required for pilot.
- **Reject for V1** — conflicts with the current pilot posture or adds unjustified complexity.

## Locked Outcomes From 2026-06-25 Discussion

This section captures discussion outcomes that are no longer open questions for the remaining pilot
readiness plan. Promote these into ADRs/build logs as implementation slices are scheduled.

### Quick Capture

Quick Capture is pilot-critical and is optimized for the immediately-after-contact moment, not live
note-taking during a customer interaction. Pilot training should teach operators to finish the
customer contact first, then open Keep before the request falls back into memory, paper, or a private
thread.

V1 Quick Capture creates a normal authenticated business-created `KeepRequest`, not a draft and not
the public-intake path. The request starts in `Received`, records the authenticated staff actor,
records a required source/channel, creates the customer page token/link when customer pages are
enabled, and starts with tracker sharing marked needed.

Required Quick Capture fields are customer name, phone number, short request summary, and
source/channel. Phone is required for the pilot service-business workflow because call/text is the
primary contact rail; email is optional secondary contact information. The capture form must support
fast manual entry and paste, and should support native phone-number handoff into prefilled Quick
Capture where feasible. Do not depend on call-log/SMS reading, voice capture, automatic parsing, or
backend SMS/email.

After save, Keep stays in the capture flow and shows confirmation, share/copy actions for the
customer tracker link, `Capture Another`, and `View Request`. Tracker sharing is optional at
creation, but unshared staff-created requests must show a visible `Needs Share` indicator in lists
and detail until staff initiates or records sharing. `Needs Share` clears only from explicit in-app
share-intent actions: native SMS/share handoff, copy tracker link from request share controls, or
manual `Mark Shared`. Clearing means staff initiated or recorded sharing, not that the customer
received or read the link.

Request source/channel uses a predefined enum, not freeform text. V1 values are `Phone`,
`Voicemail`, `Text`, `Email`, `WalkIn`, `Referral`, `PublicIntake`, and `Other`. Public intake sets
`PublicIntake` automatically; staff-created Quick Capture does not manually set `PublicIntake` in
the normal V1 flow. Source/channel is used for reporting and pilot learning and is not
customer-facing by default.

### Weekly Value Reporting And Metrics

Weekly value reporting is pilot-critical. V1 uses a founder/internal-only report endpoint or read
service that generates a copy-pasteable Markdown/text summary for a given account and reporting
period. The founder shares the summary manually during the weekly pilot review cadence — email,
meeting notes, shared doc, or whatever channel fits the relationship. Owner/Admin in-app report UI
and automated email delivery are deferred until the metrics and wording prove useful across pilot
reviews. Direct database queries are not the V1 mechanism; the internal endpoint creates a real read
path that can evolve to an Owner/Admin surface later without a rewrite.

Safe V1 metrics include requests captured, staff-created versus public-intake requests, tracker
links shared/still needing share, first-response timing, customer replies/questions caught,
follow-ups/status checks surfaced, overdue/stale items surfaced, ready-to-close items, closed
requests, negative feedback reviewed, and customer page views/adoption. Reports must avoid
unsupported claims about revenue saved, customers retained, public reviews prevented, SLA
compliance, profitability, labor efficiency, or guaranteed response.

V1 reports use conservative operational metrics, account timezone for business-facing date
boundaries, and direct operational queries/read services. Spam/Test requests and Demo/InternalTest
accounts are excluded from operational/value metrics. Closed and Cancelled rows are included only
where meaningful and are excluded from active queue, stale open-work, and unresolved operational
metrics.

### Internal Product-Ops Visibility

V1 includes lightweight internal product-ops visibility for pilot adoption and risk monitoring.
Durable internal events/digest rows are the source of truth; optional private founder/internal
channel forwarding may be fail-soft. V1 does not require a full analytics platform, warehouse, or
broad founder dashboard before pilot.

Locked V1 signals are account created, onboarding step completed/stalled, first request created,
first staff-created request, first public-intake request, first tracker link shared, first customer
page view, first operator invited, first mobile device registered, first request closed, weekly
inactivity/no new requests, repeated `Needs Share` backlog, and negative feedback awaiting review.

### Account Classification And Demo Safety

Account classification is separate from commercial lifecycle. Classification values are `Pilot`,
`Production`, `Demo`, and `InternalTest`; commercial lifecycle remains separate as trial,
active/paid, expired, cancelled, and similar states. During the founder-led pilot phase, new real
business accounts default to `Pilot`. After pilot, real trial accounts may default to `Production`
while their commercial state remains `Trial`.

The existing `AccountEntitlements.IsPilot` cohort flag is superseded by account classification; do
not keep both as parallel sources of truth. Session 9 migration maps `IsPilot = true` to `Pilot`,
`IsPilot = false` for real accounts to `Production`, and internal-purpose accounts to
`InternalTest`. `SignupDefaultsSettings` now sets classification instead of `IsPilot`.

`Demo` and `InternalTest` accounts are created only through explicit admin/internal action, not
self-signup. Demo/InternalTest accounts are excluded from pilot metrics, impact reports, billing
signals, and production notification delivery by default; Session 9 enforces production push
delivery eligibility at device lookup (`Production`/`Pilot` only). Demo reset requires an explicit
Demo/InternalTest target account, refuses Pilot/Production, preserves users/memberships, clears
notification device tokens, and replaces only demo business/request data. Role demos use separate
real demo users per role; run-as/impersonation is deferred. Rich demo scenario packs/reset UI are
deferred; classification safety comes first.

### Client Surface Scope

PWA is the Owner/Admin command center and full workbench. Native is the operator quick-action
surface. PWA may support operator workflows for desktop/tablet use, but native is primary for field
execution. Native may run on tablets, but V1 native is optimized for phone-based field execution;
tablet/desktop operator work is supported primarily through the PWA unless pilot evidence shows
tablet-native workflows are important.

`My Work` means requests where the signed-in user has an active personal relationship to the work:
assigned to me, watched by me, directly actionable for me, and possibly created by me while still
active if not assigned elsewhere. `Available` means unassigned active requests the operator can pick
up. `All Active` is the broader Owner/Admin view. `Needs Attention` is a cross-cutting signal/filter,
not an ownership bucket.

### Attention Item Copy And Surveillance Posture

Attention item copy uses two tiers. On list rows: attention reason label plus elapsed time or due timing, and optionally a severity/priority visual if already part of ranking. Examples: "Customer asked a question · 3h ago," "Update requested · 45m ago," "Follow-up overdue · 1d," "Status check needed · 5d since update," "Negative feedback · 2d ago," "Ready to close." Do not put "what clears this" copy on list rows. In request detail: full explanation — what happened, why it matters, suggested primary action, whether the action clears attention, whether it is customer-visible or internal, and a relevant recent event/message snippet when safe to include. Example detail copy: "Customer asked a question 3 hours ago. Replying with a customer-visible update will clear this attention item."

Activity history and audit trail show actor names in internal staff history, visible to Owner/Admin and other authorized staff who can view the request. Language is neutral and factual: "Christian logged a phone call," "Maya updated the customer," "Alex marked this complete," "Jordan assigned this to Sam." Named actors prevent duplicate calls and team confusion. Names appear because they explain the request history, not because the product ranks or monitors individuals. History is request-scoped, not surveillance-scoped.

V1 Owner/Admin reporting is account/request-level only. No per-operator performance reporting in V1. Safe report subjects: requests captured, responses sent, follow-ups surfaced, negative feedback reviewed, tracker links shared, open/closed work, overdue/stale work. Forbidden in V1 reporting: per-operator response time, per-operator close rate, per-operator open counts as a scorecard, leaderboard-style metrics, productivity ranking, or slowest-responder views. Operator trust is a prerequisite for field adoption; Keep must feel like shared memory and customer accountability, not employee analytics.

### External-Contact Logging

V1 channel list: Phone, Text, Email, InPerson, Other. "Text" is the user-facing label; the internal enum value may be `Sms` if already established.

Outcome rules by channel:

- **Phone** — outcome required: `Spoke`, `LeftVoicemail`, `NoAnswer`, `WrongNumber`. Optional note.
- **Text** — no outcome required. Log means staff recorded that they texted externally. Optional note.
- **Email** — no outcome required. Log means staff recorded that they emailed externally. Optional note.
- **InPerson** — channel plus optional note. No required outcome in V1.
- **Other** — note required; "Other" without a note is meaningless.

Attention and first-response effects:

- `Spoke` — counts as a business response; may clear business-waiting attention.
- `LeftVoicemail` — counts as a business response; may clear business-waiting attention.
- `NoAnswer` — logs activity; does not clear business-waiting attention.
- `WrongNumber` — logs activity; does not clear attention; should surface a data-quality prompt to correct the customer phone number.
- Text or Email logged — counts as a business response; may clear business-waiting attention. Keep does not prove delivery or read; staff intentionally recording the outreach is sufficient for the log to take effect.

### Owner/Admin Onboarding Completion Checklist

The onboarding checklist follows soft ordering; the UI and playbook should guide sequence without hard-blocking every step unless skipping would create invalid state (e.g., intake link activation before timezone is set).

Recommended order and completion signals:

1. **Business profile and contact** — business display name, customer-facing phone, customer-facing email confirmed (distinct from owner identity fields). Completion: fields saved.
2. **Timezone** — account timezone set. Completion: timezone saved. Must precede intake activation and report generation.
3. **Response and status-check defaults** — first-response target, reply targets, status-check/stale threshold reviewed and accepted or adjusted. Completion: policy saved.
4. **Intake link setup and activation** — public intake link reviewed, enabled. Completion: intake link active.
5. **Member invites and roles** — at least one operator invited and role confirmed, or explicitly acknowledged as owner-only for now. Completion: invite sent or acknowledged.
6. **Native/mobile setup** — at least one operator signs into the native app and registers a device. Completion signal: first mobile device registered event. If the account is PWA-only for this pilot posture, founder/internal marks an explicit "native not used" override. Showing install instructions alone does not count as complete.
7. **First training/test request** — a real or training request entered to verify end-to-end flow. Completion: first request created.
8. **Quick Capture exercise** — operator demonstrates Quick Capture and immediate post-save flow. Completion: founder/onboarding confirms exercise done.
9. **Tracker sharing and customer page review** — operator shares a tracker link and reviews the customer page. Completion: founder/onboarding confirms review done.
10. **Spam/Test classification explained** — owner/admin is shown how to classify junk/test requests. Completion: founder/internal or onboarding UI marks shown. Metric exclusion is automatic; this is a training step only, not an owner confirmation gate.
11. **First weekly review scheduled** — founder-led, recorded in internal pilot notes or runbook. Not an in-app setting or checklist item for the owner. Automated report scheduling is deferred.

### Native V1 Operator Scope

V1 native mobile is the operator quick-action surface. Scope is: My Work, Available, request detail, Quick Capture, contact launch/log, send customer-visible update/message (distinct from editing request fields), Follow Up / Planned For, Mark Completed, Watch (personal, additive), Mute (personal only), and self-assign from Available work where backend policy allows.

Self-assign is available from Available work only; operators cannot assign to others or transfer ownership in native V1. Owner/Admin assignment and dispatch remain PWA/workbench-oriented. "Update customer" means sending a visible message or update to the customer page, not editing core request fields (name, phone, summary, etc.); those surfaces are separate and are not hidden under "Update customer." Limited request-detail correction may exist as its own distinct affordance if proved safe and necessary, but is not default V1 native scope. Negative feedback review is Owner/Admin only, PWA/workbench first, and is absent from native V1 operator scope. Spam/Test classification is Owner/Admin only and is intentionally absent from native operator scope.

### Customer Page Trust And Data Boundary

The customer page is a lightweight service-status page, not a ticket portal. It should answer: do
they have my request, what is the latest, and what can I do now? It must show business name,
plain-language status, latest customer-visible update, available customer actions, fallback business
contact, and safe expired/unavailable language. Tone should be warm, direct, and service-oriented;
avoid internal workflow language such as ticket, case, queue, SLA, assigned operator, or escalation.

Post-close feedback remains one-time. Negative/unresolved feedback raises Owner/Admin review
attention. No automatic public review routing in V1.

Pilot branding minimum is business display name, customer-facing business phone, customer-facing
business email, clear fallback contact, customer-safe page copy, and a trustworthy public URL/path.
Logo/color are optional if cheap. Custom domains, branded public slugs, advanced theme controls,
per-location branding, and rich image/logo management are deferred.

Customer page is append-only/customer-action driven in V1. Customer input creates messages, intents,
feedback, and business attention; it does not directly mutate operational request fields. Staff
enrich/correct request fields from authenticated detail. Customers can message, ask questions,
request updates/timing changes/change/cancel, submit complaints/issues, provide allowed post-close
feedback, and add clarifying details as messages. Customers cannot directly edit name, phone/email,
address, source/channel, status, assignment, follow-up/planned-for, category, internal notes,
business-only metadata, or close/cancel state.

### Customer Page Language And Copy

Customer page status shows both a lifecycle-derived plain-language label and the business-written `CurrentStatusText` when present. If `CurrentStatusText` exists and is customer-safe, it is the primary status/update. If not, the lifecycle label is the fallback. Lifecycle label defaults:

- Received → "We received your request."
- InProgress → "We're working on it."
- PendingCustomer → "We're waiting on your response."
- Resolved → "This looks complete."
- Closed → "This request is closed."
- Cancelled → "This request was cancelled."

`Request` is the default customer-facing noun. Forbidden in all customer-facing copy: ticket, case, queue, SLA, escalation, assigned operator, workflow, priority band, attention, internal note, routed, triage, resolved feedback, and any phrasing that sounds like automated bureaucracy ("your ticket has been routed," "your case is in queue," "escalated to operator"). Prefer plain service language: "We received your request," "Here's the latest update," "Send us a note," "Ask a question," "Request a timing change," "Need to change or cancel?", "Contact [Business Name] directly."

V1 customer pages do not expose internal response targets, countdown timers, "typically responds within X" language, or public SLA text. Internal response policy is an operating target that drives attention and reporting, not a public commitment. Timing copy remains cautious and directs urgent customers to the business fallback contact: "We'll follow up as soon as we can" and "For urgent needs, contact [Business Phone]."

V1 customer action labels and intent behavior:

- **Ask a question** — general customer message; standard business-waiting attention.
- **Add details** — customer sends additional information (SendInfo intent); standard attention.
- **Request an update** — distinct UpdateRequest intent, not a generic message. Creates business-waiting attention. Appears in staff UI as "Customer requested an update." Useful as a pilot-value signal: captures anxious-waiting behavior before it becomes repeat calls or public frustration.
- **Adjust timing** — TimingChange intent; standard attention.
- **Cancel my request** — Cancel intent; requires confirmation step before submitting.
- **Contact me by phone** — replaces "Call me." Creates a phone-contact intent but does not promise a callback window. Button label: "Contact me by phone." Form prompt: "What do you need help with?" Optional helper text: "For urgent needs, call [Business Phone]." Forbidden: "Request a callback," "We'll call you," "Call me now."

### Go-Live And Pilot Operations

Pilot go-live requires each critical loop to work end-to-end at least once for the relevant persona.
The product does not need every deferred feature before pilot, but the pilot cannot rely on founder
heroics to complete core customer/operator/owner/admin workflows.

Minimum end-to-end checklist: account setup/onboarding, business profile/timezone/contact setup,
member invite/activation, Quick Capture, public intake, request list/detail, tracker sharing and
`Needs Share`, customer page, customer messages/actions, attention/follow-up/status-check behavior,
native contact handoff where in scope, close/cancel, post-close feedback, negative feedback review,
Spam/Test classification, weekly value report manual generation, internal product-ops minimum
visibility, notification posture explicitly cleared, and local/deploy/support runbooks.

Notification posture is cleared only when either real APNs/FCM delivery is tested end-to-end on a
real device with deep links and Demo/InternalTest suppression confirmed, or the no-op/test adapter is
explicitly active, real push is not promised, and onboarding/training says push is not live yet.

Pilot blockers include data loss/corrupt request state, authorization/account-boundary bypass,
public token/link leakage, unsafe customer-page data exposure, core flow broken for a required
persona, inability to create/find/update/close real requests, broken Spam/Test exclusion while public
intake is live, or notifications sent to Demo/InternalTest or the wrong account/user. Known
limitations are acceptable when documented if they are deferred features or cosmetic issues that do
not block comprehension/action.

Pilot feedback does not automatically change the build plan. Default response is to record,
classify, and defer. Deferred scope can be pulled forward only with clear evidence that it blocks a
required workflow, materially harms adoption, creates trust/safety risk, repeats across more than one
pilot business or repeatedly within one high-fit business, or is revenue/retention-critical and
aligned with Keep's core promise. Sensitive areas require explicit security/privacy/compliance review
before pull-forward.

### Account Settings And Response Policy

Business settings must be flexible in the model but small in the V1 UI. Settings should be modeled
as extensible account-level operating policy with conceptually separate business profile, operating
time, customer page/intake, response policy, team/access, notifications, and reporting areas. Core
behavioral settings such as timezone and response/status-check thresholds should be typed fields, not
unstructured catch-all metadata.

V1 required account settings are business display name, customer-facing business phone,
customer-facing business email, account timezone, intake link status/controls, member management, and
simple response/status-check policy. Business phone/email are distinct from owner/member identity
fields; onboarding may prefill them from the owner profile, but the user must confirm them as
customer-facing business contact.

V1 response policy is account-level and configurable. Defaults should not be treated as one universal
pilot profile. New public-intake requests have a first-response target measured in minutes, not
hours. Customer-facing copy must not promise a specific SLA unless explicitly supported and intended.
V1 policy fields are new public request first-response target, standard customer reply target,
priority customer reply target, and status-check/stale threshold. Recommended initial defaults are
15 minutes, 4 hours, 1 hour, and 5 calendar days. Full business-hours calculation, per-day schedules,
holiday rules, per-service-type rules, escalation ladders, per-user response expectations, and
customer-visible SLA promises are deferred.

Business type/industry as an onboarding/account-profile setting is deliberately deferred for more
discussion before it is locked.

### Support, Feedback, Closeout, Notifications, And Off-Ramps

V1 founder/support access is read-only first, audited, and bounded. Support may view account
profile/settings, onboarding status, usage/product-ops signals, request list/detail for support
purposes, support/debug context, classification/status, tracker shared/unshared state, and relevant
feedback/review state. Production impersonation/run-as is deferred. Emergency/internal changes must
use explicit admin/internal command paths, require reason text, record audit, and prefer owner/admin
confirmation when practical.

V1 includes authenticated in-app `Report Friction` for Owner/Admin/Operator users and a simple
authenticated Pilot Updates/Help page. Anonymous customer pages do not expose a generic OpHalo
feedback hook in V1.

V1 includes lightweight closeout hygiene but no heavy archive/review workflow. Close/cancel paths
are explicit; close may include an optional final customer-visible message. Warnings before close are
advisory by default for unresolved customer waiting, overdue follow-up/status-check, never-shared
tracker link, unresolved negative feedback, or missing required final context, unless closing would
create invalid or unsafe state.

Push must be rare, routed, and actionable. V1 push candidates are request assigned to me, customer
reply or priority customer intent on an assigned/watched request, overdue follow-up/status-check on
my work, negative feedback requiring Owner/Admin review, and new available request only when the
business explicitly opts into shared queue alerts. Do not push every request update, every public
intake by default, list movement, internal notes, routine report metrics, Demo/InternalTest activity,
or non-actionable events.

If a pilot account stops using Keep or cancels, cancellation/disable is primarily an operational
runbook. Do not hard-delete by default. Authenticated business access may be disabled or limited,
active Keep workflows stop, notifications stop/suppress, and request/customer pages expire according
to policy. The public intake URL remains available for approximately 6 months as a contact handoff
page showing business contact info, but it does not create new Keep requests and must clearly say
online request tracking is not currently active.

### App-Switching And Tool Boundary

Generic locked boundary language for the product, pilot docs, and onboarding materials: *"Keep is where the customer promise lives. Your existing tools remain where labor, schedule, estimate, invoice, and payment work happens."*

During each pilot setup call, the founder customizes this with tool-specific examples for that business: "If a customer asks where their estimate is, update them in Keep. When you're building the estimate, use Jobber/ServiceTitan/QuickBooks/your normal tool. If the job time changes, update the customer promise in Keep; manage the crew schedule in your scheduling tool." These examples are not hardcoded in the product because V1 does not know which tools each pilot business uses.

A short boundary note belongs in the authenticated Pilot Updates/Help page, always findable but not intrusive. Suggested copy: *"Keep helps your team remember and update the customer promise: what the customer asked for, what you told them, what needs follow-up, and whether the loop was closed. Keep does not replace your scheduling, estimating, invoicing, payment, or field-service system."* No repeated in-app instructional banners or feature-tour nags unless pilot confusion proves they are needed.

### Still Deferred From This Discussion

- Business type/industry setting and preset defaults need more discussion before lock.
- Rich demo scenario packs/reset UI.
- Full business-hours math and complex notification schedules.
- Custom domains/branded public slugs.
- Backend SMS/email, proof-of-send, call-log/SMS reading, voice capture, and automatic parsing.
- API compatibility/minimum-client policy before native beta.

## Customer Perspective

| ID | Question | Type | Why It Matters | Suggested Default |
|---|---|---|---|---|
| CUST-001 | What is the minimum customer page promise? | Product decision | Customers will only use the page if it quickly answers whether the business has their request, what changed, and what to do next. | Locked 2026-06-25: lifecycle label + business-written status text when present; see Customer Page Language And Copy section. |
| CUST-002 | What language should the customer page use so it feels helpful rather than like a cold ticket portal? | Product decision | If the page feels bureaucratic, customers may ignore it and keep calling/texting. | Locked 2026-06-25: "request" as default noun; forbidden list locked; see Customer Page Language And Copy section. |
| CUST-003 | What response expectations should customer-facing copy set? | Product decision | A page that invites questions can create frustration if it implies instant response. | Locked 2026-06-25: no public timing language, countdown, or SLA; see Customer Page Language And Copy section. |
| CUST-004 | Which customer actions are V1, and are any too risky to promote? | Both | Customer actions create business attention and notification behavior. | Locked 2026-06-25: see Customer Page Language And Copy section for final action labels and intent behavior. |
| CUST-005 | How should unhappy customers be routed before they leave a public review? | Both | Negative feedback is one of Keep's strongest owner-value moments, but over-automation can backfire. | Locked 2026-06-25: post-close feedback one-time; negative feedback raises Owner/Admin review attention; no automatic public review flow in V1. |
| CUST-006 | Should expired closed/cancelled/test/spam pages be dead ends or repeat-request entry points? | Product decision | Dead ends can waste future demand, but repeat-request routing depends on safe public URL contracts. | Locked 2026-06-25: safe tombstone now; repeat-request routing deferred unless using normal active public intake link. |
| CUST-007 | What customer-visible branding is required for trust at pilot? | Both | A generic page may feel suspicious; too much branding work can delay pilot. | Locked 2026-06-25: business display name, customer-facing phone/email, fallback contact; logo/color optional if cheap; custom domains/slugs deferred. |
| CUST-008 | What customer data can staff edit after Quick Capture, and what should never be customer-editable? | Both | Prevents leakage between public customer page and authenticated staff workbench. | Locked 2026-06-25: staff enrich in authenticated detail; customer page adds messages/intents only, not operational fields. |

## Owner/Admin Perspective

| ID | Question | Type | Why It Matters | Suggested Default |
|---|---|---|---|---|
| OWN-001 | What must the Owner/Admin command center show on day one? | Both | Owners pay when Keep gives control and confidence, not just records. | Locked — established in B5 sessions (ADR-164, ADR-173); see decision index. |
| OWN-002 | Is weekly value reporting pilot-critical? | Product decision | Prevented problems become invisible unless Keep reminds the owner what it caught. | Locked 2026-06-25: yes; start with Markdown/manual sharing; see Weekly Value Reporting section. |
| OWN-003 | What metrics are safe for the weekly value report? | Both | Reports must prove value without overclaiming revenue attribution. | Locked 2026-06-25: see Weekly Value Reporting section. |
| OWN-004 | Should the weekly report be internal-only first or Owner/Admin-facing in-app? | Both | Determines API shape, language, auth boundary, and implementation order. | Locked 2026-06-25: internal founder/admin-only endpoint first; no Owner/Admin in-app report UI at pilot go-live; see Weekly Value Reporting section. |
| OWN-005 | What onboarding completion checklist is required? | Both | Prevents new businesses from being created and then silently failing to activate. | Locked 2026-06-25: see Locked Outcomes section. |
| OWN-006 | Which account settings are required before pilot? | Both | Settings affect reporting dates, stale thresholds, customer trust, and onboarding. | Locked 2026-06-25: business display name, customer-facing business phone/email, account timezone, intake controls, member management, simple response/status-check policy. |
| OWN-007 | How much response-policy configuration should owners get in V1? | Both | Too much configuration delays launch; too little may make reports/attention feel wrong. | Locked 2026-06-25: account-level configurable policy with minute-scale new-public-request target; full business-hours math deferred. |
| OWN-008 | How should owners classify spam/test requests? | Both | Junk/training data must not pollute queues or value reports. | Already locked in Session 7; ensure UI confirmation/copy and report exclusion are carried forward. |
| OWN-009 | What happens when a pilot account stops using Keep or cancels? | Product decision | Avoids ad hoc operational handling during pilot. | Locked 2026-06-25: stop workflows/notifications; public intake URL becomes 6-month contact handoff page, not active intake; retain data by default. |
| OWN-010 | What pilot size and trial posture are we using? | Product decision | Affects support load, reporting, onboarding, and billing decisions. | Locked 2026-06-25: 5-10 businesses, evidence-based pilot length, manual/off-platform billing, Stripe deferred. |
| OWN-011 | What does "pilot success" mean for a business? | Product decision | Prevents building without a learning target. | Locked 2026-06-25: repeated operating habit, real requests captured, owner sees value, reliability holds, willingness to continue/pay. |
| OWN-012 | What closeout/review workflow remains after Session 6? | Both | Owners need to finish work cleanly without another hidden backlog. | Locked 2026-06-25: lightweight closeout hygiene and advisory warnings; heavy archive/review/reopen workflows deferred. |

## Operator Perspective

| ID | Question | Type | Why It Matters | Suggested Default |
|---|---|---|---|---|
| OP-001 | Is Quick Capture pilot-critical? | Both | If work cannot enter Keep in seconds, operators will stay in phone/text memory and Keep will miss real demand. | Locked 2026-06-25: yes; immediately-after-contact workflow, not live note-taking. |
| OP-002 | What are the required Quick Capture fields? | Both | Too many fields kill adoption; too few can create unusable records. | Locked 2026-06-25: customer name, phone, short summary, source/channel. Email/address/category optional after creation. |
| OP-003 | What happens immediately after Quick Capture? | Both | The next action defines whether the customer receives a tracker and whether the operator stays in flow. | Locked 2026-06-25: stay in capture flow with confirmation, share/copy actions, `Capture Another`, and `View Request`; no forced detail redirect. |
| OP-004 | How should native SMS/email handoff work? | Both | Keeps V1 out of backend SMS compliance while making customer tracker sharing fast. | Locked 2026-06-25: native handoff/share only, prefilled tracker-link text where possible, no backend send/proof-of-send; explicit share-intent clears `Needs Share`. |
| OP-005 | What is the first native mobile app scope? | Both | Prevents mobile from becoming a second full admin console. | Locked 2026-06-25: see Locked Outcomes section. |
| OP-006 | What PWA operator scope exists, if any? | Product decision | Some operators may use tablets/desktops; PWA and native should not contradict each other. | Locked 2026-06-25: PWA supports shared-workbench operator workflows; native remains optimized phone field surface. |
| OP-007 | How fast must external-contact logging be? | Both | Source-of-truth breaks if logging a real call/text feels like paperwork. | Locked 2026-06-25: see External-Contact Logging section. |
| OP-008 | Which quick replies are safe for V1? | Both | Quick replies reduce typing but can create broken promises. | Locked 2026-06-25 for now: optional polish only; safe/generic replies; promise-heavy templates excluded unless tied to workflow fields. |
| OP-009 | How should every attention item explain itself? | Both | Operators need to know what clears the alert without guessing. | Locked 2026-06-25: see Attention Item Copy And Surveillance Posture section. |
| OP-010 | How do we avoid a surveillance vibe? | Product decision | Operators will route around an app that feels punitive. | Locked 2026-06-25: see Attention Item Copy And Surveillance Posture section. |
| OP-011 | What notification volume is acceptable for operators? | Both | Push trust is fragile; one noisy week can cause notification disabling. | Already mostly locked; review final native types before real delivery. Push only routed, actionable, immediate work. |
| OP-012 | What app-switching/training guidance is required? | Product decision | Keep coexists with field-service tools; operators need to know when to use which. | Locked 2026-06-25: see App-Switching And Tool Boundary section. |

## Internal Product-Ops Perspective

| ID | Question | Type | Why It Matters | Suggested Default |
|---|---|---|---|---|
| INT-001 | What internal visibility prevents reference-app style blindness? | Both | Founders need to know whether a business onboarded, stalled, or stopped using Keep. | Locked 2026-06-25: see Internal Product-Ops Visibility section. |
| INT-002 | Should internal alerts be direct webhooks, database digest rows, or both? | Coding decision | Direct webhooks are fast but can leak coupling; digest rows are more durable. | Locked 2026-06-25: digest rows first; optional fail-soft forwarder. See Internal Product-Ops Visibility section. |
| INT-003 | Which internal events are V1? | Both | Avoids either under-instrumenting pilot or building an analytics platform. | Locked 2026-06-25: see Internal Product-Ops Visibility section. |
| INT-004 | How do internal ops and business value reports differ? | Product decision | Internal reports can include adoption/health language that should not be shown to customers. | Locked 2026-06-25: separate audiences and copy; shared data is fine, wording and access differ. |
| INT-005 | What account classification is required before demo/report/push delivery? | Both | Protects pilot metrics and prevents demo pushes. | Locked 2026-06-25: see Account Classification And Demo Safety section. |
| INT-006 | What demo reset behavior is safe? | Coding decision | Demo data must be repeatable without threatening real accounts. | Locked 2026-06-25: see Account Classification And Demo Safety section. |
| INT-007 | Do we need demo role switching or real demo users? | Both | Run-as adds security/audit complexity. | Locked 2026-06-25: separate real demo users per role; run-as deferred. |
| INT-008 | What founder/support access is allowed in pilot? | Both | Support needs visibility, but broad impersonation erodes trust. | Locked 2026-06-25: read-only first, audited, bounded; no production impersonation/run-as by default. |
| INT-009 | What pilot feedback loop is required in-app? | Both | Users who cannot easily complain will silently abandon. | Locked 2026-06-25: authenticated business-user Report Friction plus Pilot Updates/Help; no generic OpHalo hook on anonymous customer page. |

## Client Surface And Build-Order Questions

| ID | Question | Type | Why It Matters | Suggested Default |
|---|---|---|---|---|
| CLIENT-001 | What is the exact PWA V1 scope? | Both | Prevents the web app from becoming too broad before pilot. | Locked 2026-06-25: Owner/Admin command center/full workbench; operator workflows where they naturally exist in shared workbench. |
| CLIENT-002 | What is the exact native V1 scope? | Both | Keeps the mobile app fast and operator-centered. | Locked 2026-06-25: operator quick-action surface for phone-based field execution, push, deep links, contact handoff, and fast capture. |
| CLIENT-003 | Are browser/PWA sessions and mobile bearer sessions contract-compatible enough for independent clients? | Coding decision | Mobile beta distribution raises compatibility/rollback needs. | Decide before mobile beta: API compatibility window and minimum-client policy; avoid premature `/v1` unless needed. |
| CLIENT-004 | When do we need real APNs/FCM delivery versus no-op/test adapter? | Coding decision | Real push requires external platform credentials and Demo/InternalTest suppression. | Locked 2026-06-25: go-live must explicitly clear real tested APNs/FCM or no-op posture with users trained that push is not live. |
| CLIENT-005 | What deep-link paths are stable for mobile notifications? | Coding decision | Push payloads depend on route stability. | Locked 2026-06-25: request detail, `My Work`, Owner/Admin feedback review if built; `Available` later if needed. |
| CLIENT-006 | What design/brand review is required before pilot? | Product decision | A working app can still fail if the UX feels confusing, cold, or heavy. | Lock a final UX pass by persona: customer page, owner command center, operator mobile, onboarding, weekly report. |

## Reporting And Metrics Questions

| ID | Question | Type | Why It Matters | Suggested Default |
|---|---|---|---|---|
| REP-001 | Which statuses are excluded from operational and value metrics? | Coding decision | Spam/Test/Demo/Internal data can corrupt pilot conclusions. | Locked 2026-06-25: Spam/Test requests and Demo/InternalTest accounts excluded; Closed/Cancelled included only where meaningful. |
| REP-002 | What date boundary/timezone do reports use? | Both | Weekly reports and owner trust depend on familiar business dates. | Locked 2026-06-25: account timezone for business-facing reports. |
| REP-003 | Do reports query operational tables directly or require read models? | Coding decision | Avoids premature analytics infrastructure. | Locked 2026-06-25: direct operational queries/read services; warehouse/projections deferred. |
| REP-004 | What claims are forbidden in value reports? | Product decision | Avoids overclaiming and trust damage. | Locked 2026-06-25: no unsupported revenue saved, retention, reviews prevented, SLA, profitability/labor efficiency, or guaranteed-response claims. |
| REP-005 | What is the first report delivery mechanism? | Both | Automated email adds compliance/delivery work. | Locked 2026-06-25: Markdown/copy-paste/manual share first; in-app/automated delivery later. |

## Go-Live Readiness Questions

| ID | Question | Type | Why It Matters | Suggested Default |
|---|---|---|---|---|
| LIVE-001 | What is the minimum go-live definition? | Product decision | Prevents the finish line from moving forever. | Locked 2026-06-25: see Go-Live And Pilot Operations section. |
| LIVE-002 | What is the pilot onboarding playbook? | Product decision | White-glove onboarding is part of the product during pilot. | Locked 2026-06-25: see Go-Live And Pilot Operations section. |
| LIVE-003 | What must be tested end-to-end before inviting a real business? | Both | Backend tests are not enough once PWA/native/customer flows exist. | Locked 2026-06-25: see Go-Live And Pilot Operations section. |
| LIVE-004 | What manual operational runbooks are acceptable for pilot? | Product decision | Not everything needs software before pilot, but manual handling must be intentional. | Locked 2026-06-25: see Go-Live And Pilot Operations section. |
| LIVE-005 | What issues block pilot versus become known limitations? | Product decision | Keeps the team honest without requiring perfection. | Locked 2026-06-25: see Go-Live And Pilot Operations section. |
| LIVE-006 | What is the decision process for scope pulled forward by pilot evidence? | Product decision | Avoids reactive build-plan churn. | Locked 2026-06-25: see Go-Live And Pilot Operations section. |

## Initial Priority Recommendations

Before locking the remaining build plan, answer these first:

1. `OP-001` / `OP-002` / `OP-003` — Quick Capture scope and immediate next actions.
2. `OWN-002` / `OWN-003` / `REP-005` — weekly value report posture.
3. `INT-001` / `INT-002` / `INT-003` — internal product-ops visibility.
4. `INT-005` / `INT-006` / `INT-007` — Pilot/Demo/InternalTest classification and demo safety.
5. `CLIENT-001` / `CLIENT-002` — exact PWA and native V1 client scope.
6. `CUST-001` / `CUST-002` / `CUST-005` — customer page trust and unhappy-customer path.
7. `LIVE-001` / `LIVE-002` / `LIVE-003` — go-live definition and pilot playbook.
