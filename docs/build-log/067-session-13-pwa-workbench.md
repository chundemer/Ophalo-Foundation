# Build Log 067 — Session 13 Verify

**Date:** 2026-06-28  
**Session name:** Session 13 Verify
**Status:** Complete; S13i verify harness and local auth helper delivered; next front-door work moves to `ophalo-web`
**Current ADR after S13 pre-build decisions:** ADR-384

## Session Intent

Session 13 builds `web/ophalo-app`, the authenticated OpHalo Keep workbench, to a ready-for-public
standard. Keep is the first product surface to use the OpHalo web foundation, but the implementation
must establish reusable application patterns rather than a temporary Keep-only trial UI.

The conversion bar matters: a first business customer will only become a subscriber if the workbench
feels reliable, easier than current habits, professional in front of customers, and manageable for
real staff. Session 13 is therefore not "good enough for a trial"; it is the first Keep workbench
experience ready for public use, and it should earn trust, daily use, and subscription conversion.

## Session 13 Ready-For-Public Workbench Scope

Session 13 is an umbrella PWA build broken into bounded coding slices. Each slice must have its own
pre-code gate before implementation: exact endpoints, role/access behavior, UI scope, out-of-scope
items, file-level impact, done gate, and browser verification target.

S13b-S13i are the proposed decomposition for planning and pre-code decisions. They do not have to map
one-to-one to commits or pull requests; a slice may split further if it is too large, or merge with an
adjacent slice when the file-level gate stays small and the user workflow benefits.

Session 13 is complete only when the authenticated Keep PWA supports the core ready-for-public
workbench loop:

- users can enter the app through the authenticated shell and understand what to do next;
- Owner/Admin can complete setup/onboarding and manage account-level work needed for Keep to run;
- staff can capture business-created requests quickly;
- staff can scan active work and understand what needs attention;
- staff can open a request detail, understand history/state, and take the next correct action;
- staff can share/mark tracker access when a staff-created request needs customer visibility;
- staff can send customer-visible updates without leaking internal data or overpromising;
- Owner/Admin can handle closeout/feedback review paths that affect trust and follow-up;
- Operators and Viewers see intentional, permission-correct surfaces;
- no fake data, dead-end placeholder nav, token/session leakage, or prototype-only visual treatment
  remains in the primary workbench.

Global Session 13 ready-for-public quality gates:

- `pnpm typecheck` and `pnpm build` clean for `web/ophalo-app`;
- relevant API tests green when backend behavior is touched;
- browser verification for every completed workflow on desktop-width and mobile-width viewports;
- app uses the active OpHalo/Keep token and component contract, not generic Tailwind defaults as the
  final presentation;
- loading, empty, 401, 402, 403, network, validation, and success states are intentional;
- auth, CORS, localhost cookies, and `return_to` behavior remain aligned with ADR-378;
- docs/runbook updates keep a fresh developer able to run and verify the app locally.

## Proposed Session 13 Coding Slices

### S13a — App Foundation And Authenticated Home

**Status:** Complete.

Delivered:

- standalone `web/ophalo-app` Vite + React + TypeScript scaffold;
- pnpm, Tailwind, Lucide, TanStack Query, self-hosted Source Serif 4 / Inter;
- typed credentialed fetch client with thrown `ApiError`;
- auth guard using `GET /auth/me`;
- onboarding home using `GET /keep/setup/onboarding`;
- Viewer/access-limited and commercial-block handling;
- API CORS and development console email fallback;
- local web setup runbook.

### S13b — Quick Capture UI

**Intent:** Make business-created request capture fast enough to replace the old habit of keeping
new work in texts, notes, memory, or a phone call backlog.

**Status: Complete. Implemented 2026-06-29.**

#### Locked Decisions

**App shell entry**
- Desktop: persistent `+ New Request` button at top of left sidebar.
- Mobile: sticky floating action button (FAB).
- Also linked inside the onboarding home checklist.
- Tapping either entry opens the Phone Lookup Gate first — not Quick Capture directly.

**Phone Lookup Gate (new component)**
- Single phone input field. Contact Picker API icon on mobile browsers that support it. Clipboard
  paste affordance when clipboard contains a phone-shaped string.
- Strip non-digits during typing. Lookup fires when stripped digit count reaches 10, or on explicit
  Enter / "Look Up" button press. Does not poll on every keystroke.
- API: `GET /keep/requests/lookup?phone={normalized_digits}` — net-new endpoint, must be built in
  S13b. Tenant-isolated via server-side session only; no client-supplied account parameters.
  Always returns `200 OK`; null properties on no match (never 404).
- **No match** → advance to Quick Capture with phone pre-filled and locked.
- **Customer match, no active requests** → show customer name + "Create New Request for [Name]".
  Opens Quick Capture with name, email, phone pre-filled and locked.
- **Customer match, with active requests** → show customer name, active request cards (max 3,
  sorted `last_activity_at DESC`), and "Create New Request for [Name]". If more than 3 active
  requests exist, append: "More active work exists in the Command Center." Tapping a card closes
  the modal and navigates to that request's workbench.

**Quick Capture form**
- Desktop: Slide-Over Right Drawer. Mobile: Full-Screen Sheet overlay.
- Source options (human-readable labels → ADR-369 channel values):
  Phone Call, Voicemail, Text Thread, Email, Walk-In, Referral, Other.
  `PublicIntake` is hidden from all manual selection menus.
- Phone field is locked when arriving from the lookup gate.
- Viewer role: disabled button with "Read-only permission" tooltip.
- 402 Past Due: persistent warning header across the drawer layout.
- POST contract: `POST /keep/requests` — `customerName`, `customerPhone`, `customerEmail`,
  `description`, `source`. Response: `201 Created` with `KeepRequestDetailResult`.
- Staff-created requests start with `NeedsShare=true` per ADR-370.
- Successful 201 satisfies the onboarding event tracker automatically via the response payload
  signature. No separate manual `quick-capture-exercise` mark needed.

**S13b implementation note — shell access preflight deferred**

S13b does not disable the shell `+ New Request` button for Viewers and does not precompute the 402
warning before the drawer opens. The current role-neutral shell has no reliable source for those
states: `GET /keep/setup/onboarding` is gated by `Keep.SettingsManage`, so Operators receive 403 even
though they have `Keep.RequestsOperate`, and it cannot reliably distinguish Viewer/read-only state
from settings-access denial. Until role/commercial state is available in session claims or a dedicated
role-neutral access endpoint, the shell entry stays enabled for authenticated users and the drawer
handles backend-denied states from lookup/create responses: Viewer/OffSeason return 403; commercial
block returns 402 when emitted by the request endpoint.

**Post-success behavior (viewport-branched)**
- Desktop/tablet: form clears; drawer stays open; confirmation panel with three actions:
  1. Copy Tracker Link (highlighted — NeedsShare=true on all staff-created requests)
  2. View Request Workbench (closes drawer, navigates to request detail)
  3. Capture Another (resets to a fresh capture form)
- Mobile: auto-navigates directly to Request Detail Workbench (S13d). No confirmation panel.
  Tracker share is deferred to the S13d NeedsShare sticky banner.

**S13d NeedsShare banner (hard entry constraint locked here)**
When a request loads on a mobile viewport with `NeedsShare == true`, a sticky orange action banner
pins to the top of the workbench header: "Customer Tracker Link Not Shared. [Tap to Share]".
Tapping fires the native Web Share API. This is a required layout rule for S13d.

#### Live Contracts

| Contract | Status |
|---|---|
| `GET /keep/requests/lookup?phone={normalized_digits}` | Net-new — must be built in S13b |
| `POST /keep/requests` | Exists |

#### Technical Matrix

| Step | Action | UI | Data Constraint |
|---|---|---|---|
| 1 | Open gate | Modal overlay, single phone field | Paste check + Contact Picker hook |
| 2 | Evaluate input | Non-blocking digit filter | Strip non-digits; fire at count == 10 |
| 3 | API dispatch | GET /keep/requests/lookup | Tenant-isolated via server-side session |
| 4a | No match | Advance to Quick Capture | Pre-fill + lock phone digits |
| 4b | Known customer, no active requests | Pre-populate entity data | Lock fields; offer new request |
| 4c | Known customer, active requests | Render request cards (max 3) | Navigate to workbench on card tap |

S13b out of scope unless explicitly pulled in:

- full request detail buildout;
- request list/command center;
- tracker-share clearing beyond minimal post-capture affordance;
- customer-visible update composer;
- native phone/SMS/contact integration;
- voice/call-log/SMS parsing.

### S13c — Command Center Request List

**Intent:** Give Owner/Admin and Operators a scan-first view of active work and attention without
turning Keep into a generic dashboard.

**Status:** Complete 2026-06-29.

#### Locked Decisions

**Role coverage and navigation**

S13c includes both Owner/Admin command-center views and Operator workbench list views in one
responsive list system. Navigation is role-rendered; the backend remains authoritative for access.

Owner/Admin tabs:

- Default Queue — `GET /keep/requests?view=default`
- Assigned to Me — `GET /keep/requests?view=assigned_to_me`
- Needs Attention — `GET /keep/requests?view=needs_attention`
- Watching — `GET /keep/requests?view=watching`
- Ready to Close — `GET /keep/requests?view=ready_to_close`
- Feedback Review — `GET /keep/requests?view=feedback_review`

Operator tabs:

- My Promises — `GET /keep/requests?view=assigned_to_me`
- Needs Attention — `GET /keep/requests?view=needs_attention`
- Watching — `GET /keep/requests?view=watching`
- Available Work — `GET /keep/requests/available`

`My Promises` and `Assigned to Me` are the same backend view (`assigned_to_me`) with different
role-specific labels. Operators get `Needs Attention` because the backend allows operators to access
`view=needs_attention`; do not hide a capability the server supports.

**Search and filters**

S13c includes text search (`q`) and a simple active-status dropdown. Broad terminal history browsing
(`closed_history`, `cancelled_history`, `all_history`) remains deferred.

Status dropdown values:

- All active statuses (no `status` query param)
- Received (`status=received`)
- Scheduled (`status=scheduled`)
- In Progress (`status=in_progress`)
- Waiting on Customer (`status=pending_customer`)
- Resolved (`status=resolved`)

Closed, Cancelled, Spam, and Test are excluded from the S13c status filter because terminal/history
views are deferred.

**Row rendering**

Rows are navigate-only in S13c. No inline status changes, contact logging, assignment toggles, or
other mutations are added to list rows. Row click/open navigates to the request detail workbench.

Rows with attention render two layers:

- urgency badge: high-contrast, mapped from `attentionReason`;
- action prompt: plain-language next action derived from row/action metadata where available.

Attention reason badge mapping must be exhaustive for every backend slug:

| Badge tone | Attention reasons |
|---|---|
| Red danger | `complaint`, `cancellation_requested`, `unresolved_feedback` |
| Orange overdue/urgent | `first_response_due`, `schedule_change_request`, `timing_change_requested`, `call_requested` |
| Yellow customer waiting | `customer_message`, `update_request`, `change_or_cancel_request` |
| Neutral fallback | any unknown future reason |

Unknown/future attention reasons must render a neutral fallback badge instead of crashing or showing
raw enum text.

`NeedsShare == true` rows show a high-contrast `Unshared tracker link` callout near the reference
code/customer identity. The callout is informational in S13c; clearing share intent belongs to the
tracker-sharing/detail flow.

**Sidebar counts and polling**

The sidebar must read counts from the active list query response's `viewCounts` payload. Do not
instantiate independent TanStack Query polling loops for each tab badge.

Active operational list views poll every 30 seconds on page 1 and refetch on window focus. Polling
pauses when the user is on page 2+ to avoid cursor/page jitter. When polling is paused, the UI shows
a subtle staleness affordance: "Auto-refresh paused while viewing older results" plus a `Refresh`
button that manually refetches the current page.

#### S13c Technical Matrix

| UI label | API contract | Roles | Sort / behavior |
|---|---|---|---|
| Default Queue | `GET /keep/requests?view=default` | Owner, Admin | Attention-first ranking across account-visible active work plus Owner/Admin post-close unresolved feedback |
| Assigned to Me | `GET /keep/requests?view=assigned_to_me` | Owner, Admin | Attention-first ranking within assigned subset |
| My Promises | `GET /keep/requests?view=assigned_to_me` | Operator | Same backend route as Assigned to Me; Operator-specific label |
| Needs Attention | `GET /keep/requests?view=needs_attention` | Owner, Admin, Operator | Attention-first subset; oldest unresolved attention wins within equivalent groups |
| Watching | `GET /keep/requests?view=watching` | Owner, Admin, Operator | Attention-first ranking within watching subset |
| Available Work | `GET /keep/requests/available` | Operator | Dedicated privacy-limited available-work endpoint |
| Ready to Close | `GET /keep/requests?view=ready_to_close` | Owner, Admin | Resolved rows eligible for closeout; Owner/Admin only |
| Feedback Review | `GET /keep/requests?view=feedback_review` | Owner, Admin | Oldest unresolved negative feedback attention first |

#### Empty States

| View | Empty state copy |
|---|---|
| Default Queue | `All customer promises are covered. No active work needs company-wide attention right now.` |
| Assigned to Me / My Promises | `You have no active customer promises assigned to you.` |
| Needs Attention | `Nothing needs attention right now. Customer-facing promises are inside their current follow-up window.` |
| Watching | `You are not watching any active customer promises yet.` |
| Available Work | `Available work is clear. No unassigned customer requests are waiting to be claimed.` |
| Ready to Close | `Nothing is ready to close. Resolved work will appear here when it is ready for owner/admin closeout.` |
| Feedback Review | `No feedback needs review. Negative customer feedback will appear here until it is handled.` |

S13c out of scope unless explicitly pulled in:

- full detail timeline/actions;
- bulk operations;
- charts/reporting;
- SSE/WebSockets;
- broad saved views/report builder;
- terminal history browsing (`closed_history`, `cancelled_history`, `all_history`);
- inline list-row mutations.

### S13d — Request Detail Workbench

**Intent:** Make a single request understandable and actionable: customer context, status, attention,
timeline, contact actions, versioned writes, and safe next actions.

**Status: Pre-work complete. Decisions locked 2026-06-28.**

#### Locked Decisions

**Primary contract**

S13d binds directly to the existing authenticated detail response:

- `GET /keep/requests/{requestId}` with optional `navView`;
- response shape: `KeepRequestDetailResult`;
- timeline source: `detail.events`;
- permissions source: `detail.availableActions`;
- validation limits source: `detail.validation`;
- contact affordance source: `detail.contactActions`;
- tracker share state/source: `detail.needsShare` and `detail.pageToken`;
- tracker share permission source: `detail.availableActions.canRecordShareIntent`;
- concurrency source: `detail.version`.

Do not introduce a separate timeline endpoint, client-side role-policy matrix, or client-invented
request-detail DTO for S13d.

S13d/S13e require extending `AvailableActionsMetadata` with `CanRecordShareIntent` before frontend
implementation. The JSON response should expose `availableActions.canRecordShareIntent`. The frontend
must use that server-computed flag for tracker share controls instead of hand-authored role checks.

**Action scope**

S13d includes the baseline operator workbench mutations:

- status change via `PATCH /keep/requests/{requestId}/status`;
- log external contact via `POST /keep/requests/{requestId}/external-contact`;
- acknowledge attention via `POST /keep/requests/{requestId}/attention/acknowledge`;
- request participation controls:
  - assign/clear responsible;
  - add/remove watchers when server actions allow it;
  - self watch/unwatch;
  - mute/unmute.

Follow Up On and Planned For are explicitly excluded from S13d UI even though backend endpoints exist.
They remain separate timing-context work and should not add noise to the first public detail
workbench.

Customer-visible business update composition remains S13f unless explicitly pulled forward. Closeout,
feedback review, and classification remain S13h/hardening scope unless a later slice explicitly moves
them.

**Timeline treatment**

Render a single unified chronological vertical feed from `detail.events`. Do not group the first
ready-for-public timeline into tabs or separate activity sections.

Timeline visual treatment:

- `visibility="all"` customer-visible events use the strongest card treatment;
- `visibility="internal"` and `visibility="system"` events use a quieter internal treatment;
- event type labels must be mapped from known slugs and tolerate unknown future slugs with a neutral
  fallback;
- internal notes and participation/internal audit details must not be styled as customer-visible
  communications.

**Optimistic concurrency and 409 recovery**

The detail response version is a GUID, not an integer. Every versioned mutation in S13d sends:

`X-Keep-Request-Version: {detail.version}`

On successful mutations that return `KeepRequestDetailResult`, replace the local detail state with the
response body so the UI immediately receives the rotated `version`, updated timeline, and refreshed
`availableActions`.

If a mutation returns `409` with `KeepRequest.RequestChanged`, preserve any open form state, disable
further submit attempts for that form, and show a conflict banner:

`This request has been updated by another team member. Copy your unsaved notes and refresh the workbench to load the latest history.`

The UI may offer an explicit refresh action, but must not silently discard typed modal/drawer input.

All mutation triggers must use a local `isSubmitting` lock that disables submit/action buttons as soon
as the request is dispatched. Duplicate clicks must not launch overlapping writes.

**Contact and external-contact logging**

Native `tel:`, `sms:`, and `mailto:` links are convenience affordances only. Durable Keep state changes
only when the operator saves an external-contact log.

Contact affordance buttons must be built from `detail.contactActions`, not from independent client
inference over raw phone/email fields. When a contact launcher is activated, S13d opens the native
launcher and opens the Log External Contact modal in the workbench.

The Log External Contact modal binds to the existing contract:

- `direction`: `outbound` or `inbound`;
- `channel`: `phone`, `sms`, `email`, `in_person`, or `other`;
- `outcome`: outbound only, optional; values `spoke_with_customer`, `left_voicemail`, `no_answer`,
  `wrong_number`;
- `requiresBusinessFollowUp`: required for inbound, optional for outbound;
- `summary`: optional/required according to server validation and local validation hints.

The modal copy must distinguish launching a phone/SMS/email app from saving a Keep contact record.

**NeedsShare and tracker-link handling**

S13d owns the first operational clearing path for `NeedsShare`.

When a request loads on a mobile viewport with `needsShare == true`, mount the locked S13b sticky
orange banner: `Customer Tracker Link Not Shared. [Tap to Share]`.

Tracker URL construction may use `detail.pageToken`, but the raw token must not be rendered in an
open input or visible text field. Use a local in-memory string for Copy Link and native Web Share
gestures.

After a successful copy/share/manual-mark action, call:

`POST /keep/requests/{requestId}/share-intent`

with one of:

- `copy_link`;
- `native_share`;
- `manual_mark_shared`.

The share-intent endpoint is idempotent and clears `NeedsShare`. S13d should refetch detail or
optimistically clear only the banner/link callout after a successful `204`.

Viewer users cannot record share intent. Viewer detail must remain read-only. The UI should infer this
from `availableActions.canRecordShareIntent == false`, with `403` handling as a defensive fallback.

**Metadata, feedback, and visibility**

Advanced metadata belongs in the header/side rail only when the existing detail response supplies it:

- customer page viewed metadata uses `customerPageLastViewedAtUtc` and
  `customerPageViewedAfterLatestUpdate`;
- customer activity after resolution is derived from `lastCustomerActivityAt` and resolved/terminal
  state when visible in detail;
- unresolved feedback warnings use feedback/attention fields already present on detail.

Feedback comment visibility is enforced by the server. The client must use `feedbackCommentVisible`
and must not attempt to re-create feedback-security policy. Negative unresolved feedback comments are
not returned to Operator/Viewer; resolved/positive feedback may be visible according to the existing
detail contract.

**Viewer and read-only treatment**

The detail page should render for Viewers only when the backend grants row visibility. Viewer UI is
read-only:

- timeline and non-sensitive metadata can render;
- action rail is replaced by a compact read-only state or disabled controls only where useful;
- mutation buttons must be hidden or disabled based on `availableActions`;
- no client-only role check may enable an action the server did not expose.

**Navigation**

S13d does not add a primary next/previous queue navigation UI. Standard browser back/breadcrumb
navigation is enough for the first detail slice.

If a list route passes supported `navView` context, the client may preserve it in detail fetches and
mutations so backend navigation metadata remains available for later work. Complex cursor/list-state
handoff remains deferred.

**Workbench layout and polish**

Desktop uses a high-density two-column workbench:

- left/main: header context and unified timeline;
- right/action rail: status, contact, attention, participation, tracker share controls.

Mobile uses a single-column detail view with sticky NeedsShare behavior, compact action entry points,
and forms in mobile-appropriate sheets.

Empty values must use stable, intentional text such as `No customer email provided`; missing fields
must not collapse or reflow the grid unpredictably.

The right action label must be `Manage Participants` or `Assign / Watch`, not `Adjust Team Roles`,
because account/team roles are a separate settings/member-management concern.

#### Live Contracts

| Contract | Status | S13d use |
|---|---|---|
| `GET /keep/requests/{requestId}` | Exists | Detail source of truth |
| `PATCH /keep/requests/{requestId}/status` | Exists | Status changes |
| `POST /keep/requests/{requestId}/external-contact` | Exists | Durable contact log |
| `POST /keep/requests/{requestId}/attention/acknowledge` | Exists | Attention acknowledgement |
| `POST /keep/requests/{requestId}/share-intent` | Exists | Clear `NeedsShare` after share intent |
| `availableActions.canRecordShareIntent` | Net-new metadata | Server authority for share controls |
| Participation endpoints | Exist | Responsible/watch/mute controls |

#### Technical Matrix

| Concern | Locked behavior |
|---|---|
| Detail DTO | Use `KeepRequestDetailResult`; no parallel client contract |
| Timeline | `detail.events`, unified chronological feed |
| Permissions | `detail.availableActions` controls action rendering |
| Validation | `detail.validation` controls local form limits |
| Share permission | `detail.availableActions.canRecordShareIntent` controls tracker share rendering |
| Versioning | Send GUID `detail.version` in `X-Keep-Request-Version` for versioned mutations |
| Conflict recovery | Preserve form input; show 409 conflict banner; require refresh/retry from latest state |
| Share token safety | Do not render raw `pageToken` in visible/copyable input fields |
| Share clearing | Call `share-intent` after copy/native/manual share gesture |
| Double submit | Per-action/modal `isSubmitting` lock |

S13d out of scope unless explicitly pulled in:

- customer update composer as a polished separate workflow;
- Follow Up On / Planned For UI;
- closeout and feedback-review action flows;
- classification/spam/test action UI;
- member management/settings;
- deep reporting;
- batch closeout.

### S13e — Tracker Sharing And Needs Share

**Intent:** Make staff-created requests customer-visible in a controlled way and remove ambiguity
around whether the tracker still needs to be shared.

**Status: Pre-work complete. Decisions locked 2026-06-28.**

#### Locked Decisions

**Tracker link footprint**

Tracker-link access is intentionally narrow:

- post-capture desktop confirmation panel: `Copy Tracker Link` action, already locked in S13b;
- request detail workbench: `Copy Link`, native share where available, and manual mark shared
  controls in the action rail/customer tracker area;
- list rows: no tracker URL, no page token, and no share action in S13e.

`PageToken` remains a detail-only bearer value. List responses must not grow a page-token field.
S13c list rows may show `NeedsShare`/`Unshared tracker link` status, but opening detail is the
operator path for sharing.

When `NeedsShare == true`, detail emphasizes sharing:

- mobile: sticky S13b banner, `Customer Tracker Link Not Shared. [Tap to Share]`;
- desktop: high-visibility customer tracker action in the right rail.

After `NeedsShare` is cleared, tracker access remains available from detail but is no longer urgent
or bannered.

**Share methods**

The only share-intent methods are the backend values:

- `copy_link`;
- `native_share`;
- `manual_mark_shared`.

UI labels:

- `Copy Link` maps to `copy_link`;
- `Share` or native platform share affordance maps to `native_share` when `navigator.share` is
  available;
- `Mark Shared` maps to `manual_mark_shared`.

Call `POST /keep/requests/{requestId}/share-intent` only after the copy action succeeds, the native
share promise resolves, or the operator explicitly confirms manual mark shared.

If `navigator.share` is unavailable, hide the native share affordance and keep Copy Link plus Manual
Mark Shared.

**Share intent copy**

The micro-copy for manual/recorded sharing should be one sentence:

`Recording this marks that you've initiated sharing the tracker link. It does not confirm the customer received it.`

Do not imply delivery, read receipt, customer identity verification, or successful customer access.

**Permission metadata**

S13e requires a small backend metadata extension before frontend implementation:

- add `CanRecordShareIntent` to `AvailableActionsMetadata`;
- expose it in JSON as `availableActions.canRecordShareIntent`;
- compute it from the same server-side policy used by `ClearShareIntentService`.

Expected policy:

- Owner/Admin: account-wide visible requests;
- Operator: MyWork-visible requests only;
- Viewer: false / blocked;
- OffSeason/read-only/blocked access: false.

The frontend must render tracker share controls from `availableActions.canRecordShareIntent`, not from
hand-authored role checks. A `403` response remains a defensive fallback.

**Idempotency and local state**

`POST /keep/requests/{requestId}/share-intent` is idempotent. If `NeedsShare` is already false, the
server returns success without writing another `ShareIntentRecorded` event.

The UI only needs the standard S13d `isSubmitting` lock. After a successful `204`, clear or refetch
the visible `NeedsShare` affordance without creating a fake timeline entry client-side.

**Workflow layering**

Sharing the tracker link and sending a customer-visible business update are separate workflows:

- share intent: `POST /keep/requests/{requestId}/share-intent`;
- business update: `POST /keep/requests/{requestId}/business-updates`.

Do not couple these actions in the UI. Operators may share verbally, by native SMS, by email client,
or by copy/paste without composing a Keep customer-visible update.

#### Live Contracts

| Contract | Status | S13e use |
|---|---|---|
| `KeepRequestDetailResult.needsShare` | Exists | Urgency/banner state |
| `KeepRequestDetailResult.pageToken` | Exists | Detail-only link construction |
| `POST /keep/requests/{requestId}/share-intent` | Exists | Record share intent / clear `NeedsShare` |
| `AvailableActionsMetadata.canRecordShareIntent` | Net-new | Server authority for share controls |

#### Technical Matrix

| Concern | Locked behavior |
|---|---|
| List rows | Show status only; no token/link/share action |
| Detail after cleared | Tracker link remains accessible but de-emphasized |
| Share methods | `copy_link`, `native_share`, `manual_mark_shared` only |
| Native share | Show only when `navigator.share` exists; call share-intent after promise resolves |
| Copy link | Call share-intent after clipboard copy succeeds |
| Manual mark | Requires explicit operator confirmation |
| Copy | One-sentence initiated-sharing copy; no delivery/read proof claim |
| Permission | Render from `availableActions.canRecordShareIntent` |
| Idempotency | Trust server idempotency; no special client de-dupe beyond `isSubmitting` |
| Business update | Separate workflow and endpoint |

S13e out of scope unless explicitly pulled in:

- backend SMS/email delivery;
- proof-of-delivery/read;
- link rotation/revocation UI;
- identity-bound customer access.

### S13f — Customer Update Composer

**Intent:** Let staff send clear customer-visible updates that preserve trust, clear attention when
appropriate, and avoid promise-breaking copy.

**Status: Pre-work complete. Decisions locked 2026-06-28.**

#### Locked Decisions

**Placement and entry**

S13f is a request-detail-only composer. Do not add list-row customer-update quick actions in V1.

Reasons:

- list rows do not carry full detail action metadata or the current detail version;
- the update text is customer-visible and deserves the surrounding request context;
- adding list-row send behavior would require a detail fetch or a separate write posture that is not
  needed for the pilot.

The primary button label is `Send update`.

**Contract and permission**

S13f uses the existing endpoint:

`POST /keep/requests/{requestId}/business-updates`

Request body:

- `message`;
- optional `setStatus`.

Every submit sends:

`X-Keep-Request-Version: {detail.version}`

Render the composer only when `detail.availableActions.canSendBusinessUpdate == true`. Viewer,
read-only, OffSeason/blocked, terminal, or otherwise disallowed surfaces do not render an enabled
composer.

On successful `200 OK`, the endpoint returns `KeepRequestDetailResult`. The UI must adopt that
returned detail so the timeline, action flags, status, and `version` refresh atomically.

**Optional status update from the composer**

The composer may include an optional status dropdown, but only when:

- `detail.availableActions.canChangeStatus == true`; and
- `detail.availableActions.allowedStatuses` contains at least one S13f-allowed status after filtering.

Status options must come from `detail.availableActions.allowedStatuses`; do not compute transitions in
the browser.

S13f filters out terminal/destructive/admin statuses from the composer even when the backend may allow
them elsewhere:

- exclude `closed`;
- exclude `cancelled`;
- exclude `spam`;
- exclude `test`.

Allowed composer statuses are therefore active/forward workflow states present in
`allowedStatuses`, such as:

- `scheduled`;
- `in_progress`;
- `pending_customer`;
- `resolved`.

Closeout, cancellation, spam/test classification, and other terminal/destructive state changes belong
to dedicated status/closeout/classification flows, not the customer update composer.

Use `detail.validation.messageRequiredForStatuses` only for inline hints and validation copy. Server
validation remains authoritative.

**Manual text only**

No canned replies, promise-like quick replies, template libraries, AI suggestions, or saved response
snippets are included in S13f. Manual text only until DEF-068 / messaging-boundary work is explicitly
decided.

**Message length and validation**

Use `detail.validation.businessUpdateMaxLength` for browser-side validation.

The textarea shows a character count or countdown. It should move into warning styling near the
limit, block submit when over the limit, and never silently truncate text.

On `400`/`422` validation responses, keep the typed message and selected non-terminal status in place
and show inline error feedback.

**Side-effect language**

The composer may show passive micro-copy only:

- `Visible on the customer tracker.`
- If a status is selected: `Status will change to {status label}.`

Do not predict attention clearing, SLA/first-response effects, notification routing, or customer read
state in the UI. The server owns those effects and the returned detail/timeline is the proof after
submission.

**NeedsShare interaction**

`NeedsShare` does not block customer updates.

If `detail.needsShare == true`, show a passive non-blocking reminder near the composer:

`Tracker link not yet shared with customer.`

Do not intercept submit, force sharing, auto-call `share-intent`, or couple the business update to
tracker sharing. ADR-381 keeps sharing and business updates as separate workflows.

**Conflict and data preservation**

On `409 KeepRequest.RequestChanged`, preserve the typed message and selected status in local state,
disable further stale submits for that composer instance, and show:

`This request was updated. Refresh to see the latest state. Your message is saved here.`

The UI may offer a refresh action, but must not wipe the composer state unless the user explicitly
discards or successful send occurs.

On network failure, preserve the same local draft state and allow retry when appropriate.

On success, clear the composer draft after adopting the returned detail.

#### Live Contracts

| Contract | Status | S13f use |
|---|---|---|
| `POST /keep/requests/{requestId}/business-updates` | Exists | Send customer-visible update |
| `BusinessUpdateRequestBody.message` | Exists | Customer-visible text |
| `BusinessUpdateRequestBody.setStatus` | Exists | Optional filtered active status change |
| `X-Keep-Request-Version` | Exists | Optimistic concurrency |
| `KeepRequestDetailResult.version` | Exists | Expected version source |
| `availableActions.canSendBusinessUpdate` | Exists | Composer visibility/enablement |
| `availableActions.canChangeStatus` | Exists | Optional status dropdown gate |
| `availableActions.allowedStatuses` | Exists | Status option source before S13f filtering |
| `validation.businessUpdateMaxLength` | Exists | Local message length limit |
| `validation.messageRequiredForStatuses` | Exists | Inline validation hint source |

#### Technical Matrix

| Concern | Locked behavior |
|---|---|
| Placement | Detail-only composer |
| Button label | `Send update` |
| Permission | Render from `availableActions.canSendBusinessUpdate` |
| Status dropdown | Optional; only from filtered `allowedStatuses` when `canChangeStatus` is true |
| Excluded statuses | `closed`, `cancelled`, `spam`, `test` |
| Text mode | Manual text only; no templates/quick replies |
| Length | Use `validation.businessUpdateMaxLength`; countdown; block over-limit |
| Side-effect copy | Customer-visible + optional status-change copy only |
| NeedsShare | Passive non-blocking reminder; no coupling to share intent |
| 409 recovery | Preserve draft/status; disable stale submit; prompt refresh |
| Success | Adopt returned `KeepRequestDetailResult`, then clear draft |

S13f out of scope unless explicitly pulled in:

- template libraries;
- canned/quick replies;
- AI-generated response suggestions;
- automated SMS/email delivery;
- attachments;
- list-row customer-update quick actions;
- terminal closeout/cancellation/classification via composer;
- customer reply inbox beyond existing customer page behavior.

### S13g — Member Management And Settings Continuation

**Intent:** Give Owner/Admin enough account configuration and team control in-app that Keep can be
used without founder/manual intervention.

**Status: Pre-work complete. Decisions locked 2026-06-28.**

#### Locked Decisions

**Navigation and layout**

Use one Owner/Admin-only `Settings` nav item, not separate top-level `Company` and `Team` nav items.

Inside Settings, use compact sub-sections or nested routes:

- `Company`;
- `Team`;
- `Onboarding`.

Operators and Viewers do not get editable Settings navigation. If they hit a settings URL directly,
show the standard access-limited/403 handling.

**Minimum go-live settings surface**

S13g includes only the self-service controls already supported by the backend:

- Company profile/contact:
  - business name;
  - account timezone;
  - customer-facing phone;
  - customer-facing email;
- response policy:
  - first response target minutes;
  - standard response target minutes;
  - priority response target minutes;
  - status-check threshold days;
- public intake link:
  - view current active status/slug;
  - ensure/create active link;
  - replace link with explicit confirmation;
- Team roster and member mutations;
- Onboarding checklist readout and manual marks.

No billing/plan management, primary-owner transfer, broad account lifecycle controls, or internal
support tooling are added to S13g.

For 1-2 person businesses, Settings should not make Team setup feel mandatory. Company setup remains
prominent; Team is available inside Settings but visually secondary and low-pressure.

**Timezone boundary**

Timezone is part of the existing setup contract (`GET /keep/setup`, `PUT /keep/setup/profile`) and is
therefore editable in S13g.

Implementation rules:

- prefill from `GET /keep/setup`;
- use a deliberate timezone selector/input, not freeform casual text;
- submit timezone together with the profile update body;
- do not create timezone-specific request/workbench behavior in S13g;
- saving profile/contact/timezone records the profile product-ops event, which satisfies both
  `profileAndContactSaved` and `timezoneSaved` in the onboarding checklist.

**Team roster and mutation scope**

Surface the full server-permitted team-management set:

| Action | Contract | UI scope |
|---|---|---|
| Invite team member | `POST /accounts/me/invite` | Owner/Admin; role selector for `admin`, `operator`, `viewer`; no Owner invite |
| List team | `GET /accounts/me/members` | Default roster excludes removed rows |
| Show removed rows | `GET /accounts/me/members?includeRemoved=true` | Optional filter/toggle in Team |
| Resend invite | `POST /accounts/me/members/{accountUserId}/resend-invite` | Pending/removed-invite rows; default email delivery |
| Change role | `PATCH /accounts/me/members/{accountUserId}/role` | Server guards owner/self/primary-owner constraints |
| Suspend | `POST /accounts/me/members/{accountUserId}/suspend` | Active rows; inline confirm |
| Reactivate | `POST /accounts/me/members/{accountUserId}/reactivate` | Suspended or removed-with-user rows when server allows |
| Remove | `DELETE /accounts/me/members/{accountUserId}` | Destructive; explicit confirmation dialog |

Role labels are product labels:

- `Owner`;
- `Admin`;
- `Operator`;
- `Viewer`.

Invite may create only Admin/Operator/Viewer. Role change may present Owner only where appropriate
for Owner callers; the server remains authoritative for owner limit, last owner, primary owner, and
self-mutation protections.

Do not add frontend-only rollout gates that hide server-permitted member controls. The backend remains
the control plane for role, membership, seat-limit, primary-owner, and account-state constraints.

**Seat usage and invite limits**

S13g should add server-authoritative seat metadata to `GET /accounts/me/members` before implementing
the Team UI:

- `occupiedSeats`;
- `maxSeats`;
- `atLimit`;
- `limitApplies`.

Do not compute seat limits by counting visible rows in the browser. Removed rows, invited rows,
suspended rows, plan limits, and future entitlement changes belong to the server.

The Team section shows a compact seat readout when limits apply, for example:

`Team seats: 1 of 2 used`

When `atLimit == true`, disable the primary `Invite team member` button and use a clear disabled
label/reason:

- button label: `Team limit reached`;
- helper: `Your plan includes {maxSeats} team seats. Contact support to add more.`

Reactivate and resend-from-removed controls should also use seat metadata where possible, while still
handling `Member.SeatLimitReached` as the authoritative fallback. All invite/reactivate flows must
continue to map `Invite.SeatLimitReached` and `Member.SeatLimitReached` inline because server state may
change between page load and submit.

**Invite delivery and out-of-app accept flow**

New invites use email delivery through `POST /accounts/me/invite`. Success copy:

`Invite sent to {email}. They'll receive an email link to set up their account.`

Do not explain `ophalo-web`, auth handoff, or redirect mechanics inside the workbench. The admin only
needs to know that the invitee receives an email link.

For resend invite, default to email delivery. Because business email delivery can be blocked, delayed,
or routed to spam, expose manual-share resend (`delivery="manual_share"`) as a deliberate fallback
affordance on pending/removed-invite rows.

Manual-share copy:

`Use this only if the invite email was not received. Anyone with this link can accept the invite.`

Show the raw invite URL only once, immediately after the explicit manual-share action. Never persist
it in visible roster state, and do not log it.

Invite accept implementation remains out of `ophalo-app`; public auth/invite UX belongs to
`ophalo-web`, backed by the existing API accept contract.

**Nomenclature**

Use friendly product language:

- nav/page label: `Settings`;
- section label: `Team`;
- individual noun: `team member`;
- action: `Invite team member`.

Do not surface backend route terminology like `members` in visible copy.

**Commercial and conflict recovery**

Render invite/member-management conflicts inline near the form or row that triggered them, not as
global modals.

Error handling copy:

| Error code | UI guidance |
|---|---|
| `Invite.SeatLimitReached` | `Your plan's team limit has been reached. Contact support to add more seats.` |
| `Member.SeatLimitReached` | `Team seat limit reached. Suspend or remove another team member to free a seat.` |
| `Member.PreviouslyRemoved` + `suggestedAction=reactivate` | `This person was previously on your team. Reactivate them from the team list to restore access.` |
| `Member.PreviouslyRemoved` + `suggestedAction=resend_invite` | `This person was previously invited. Resend their invite to restore access.` |
| `Invite.AlreadyActive` | `This person already has team access.` |
| `Member.CannotModifySelf` | `You cannot change your own team access here.` |
| `Member.PrimaryOwnerProtected` | `The primary owner cannot be changed from this screen.` |
| `Member.CannotModifyOwner` | `Only an Owner can manage another Owner.` |
| `Member.LastOwner` | `At least one active Owner must remain.` |
| `Member.OwnerLimitReached` | `This account can have at most two Owners.` |

**Onboarding logic**

The Onboarding section reads `GET /keep/setup/onboarding` and exposes only the existing manual mark
buttons:

- quick-capture exercise;
- tracker review;
- spam classification explanation.

Do not manually mark `operatorInvited` from the invite UI. The checklist derives
`operatorInvited` from live account state: a non-owner active member must exist. A sent invite alone
does not complete that onboarding item until accepted/activated.

The S13g UI must not block a solo business from using Keep because no non-owner active member exists.
For the first public workbench, present the team/onboarding item as optional or "add later" for solo
businesses. A later backend/onboarding-policy slice may formalize this as a skippable checklist item
or account-size-aware onboarding rule.

#### Live Contracts

| Contract | Status | S13g use |
|---|---|---|
| `GET /keep/setup` | Exists | Company/profile/policy read |
| `PUT /keep/setup/profile` | Exists | Business name, timezone, customer-facing contact |
| `PUT /keep/setup/policy` | Exists | Response policy and status-check threshold |
| `GET /keep/setup/intake` | Exists | Intake link status |
| `POST /keep/setup/intake/ensure` | Exists | Create/ensure intake link |
| `POST /keep/setup/intake/replace` | Exists | Replace intake link |
| `GET /keep/setup/onboarding` | Exists | Checklist readout |
| onboarding mark endpoints | Exist | Manual onboarding checks |
| `POST /accounts/me/invite` | Exists | New team invite |
| `GET /accounts/me/members` | Exists + net-new `seatUsage` metadata | Team roster and invite-limit state |
| member mutation endpoints | Exist | Resend, role, suspend, reactivate, remove |

#### Technical Matrix

| Concern | Locked behavior |
|---|---|
| Navigation | One `Settings` item with Company/Team/Onboarding sections |
| Roles | Owner/Admin only; standard access-limited handling for others |
| Timezone | Editable through profile contract; prefilled; deliberate selector/input |
| Intake link | Status/ensure/replace; replace requires confirmation |
| Team language | Use `Team` / `team member`; hide backend `members` wording |
| Tiny-business posture | Team remains available but secondary; solo usage is not blocked |
| Seat usage | Server-provided `seatUsage`; disable invite at limit; still handle 409 fallback |
| Invite roles | Admin/Operator/Viewer only |
| Role management | Server-authoritative; frontend does not invent extra rollout gates |
| Resend delivery | Email default; manual share secondary recovery for spam/missed email |
| Conflicts | Inline row/form errors with code-specific copy |
| Onboarding invited step | Completed by active non-owner member, but non-blocking/add-later for solo businesses |

S13g out of scope unless explicitly pulled in:

- primary-owner transfer;
- billing/plan management;
- internal admin/support tooling;
- invite accept implementation in `ophalo-app`.

### S13h — Closeout, Feedback Review, And Attention Explanation

**Intent:** Make the accountability loop feel trustworthy: staff understand what requires action,
what action clears it, and how to finish work without losing customer trust.

**Status: Pre-work complete. Decisions locked 2026-06-28.**

#### Locked Decisions

**Surface placement**

S13h extends S13c list and S13d detail. It does not add a new top-level route or standalone closeout
screen.

Touch points:

- S13c request list:
  - Owner/Admin `Ready to Close` view backed by `GET /keep/requests?view=ready_to_close`;
  - Owner/Admin `Feedback Review` view backed by `GET /keep/requests?view=feedback_review`;
- S13d request detail:
  - close action through existing status mutation when server flags allow it;
  - feedback review panel and mark-reviewed action when server flags allow it.

Do not add a separate primary nav item for Closeout or Feedback Review. Counts/badges on the main
request-list nav item are enough when actionable rows exist.

**Close action**

Closing is a detail status action, not a separate composer.

Render a close action only when:

- `detail.availableActions.canClose == true`; and
- `detail.availableActions.allowedStatuses` includes `closed`.

Submit through the existing status mutation:

`PATCH /keep/requests/{requestId}/status`

with:

- `status: "closed"`;
- optional status message only if the status flow requires/allows it;
- `X-Keep-Request-Version: {detail.version}`.

On success, adopt the returned `KeepRequestDetailResult`.

**Feedback review action**

Render the feedback review panel only when the detail contract supports the state:

- `detail.availableActions.canMarkFeedbackReviewed == true` for the action;
- feedback fields indicate submitted negative feedback that still needs review.

UI language:

- headline: `Customer left negative feedback`;
- age badge from `detail.feedbackReviewAgeBucket`:
  - `new` -> `New`;
  - `aging` -> `Aging`;
  - `overdue` -> `Overdue`;
- action button: `Mark reviewed`;
- textarea label: `Internal note (optional)`.

The note is optional because `FeedbackReviewRequestBody.note` is nullable. Use
`detail.validation.feedbackReviewNoteMaxLength` for local textarea validation. Do not require text in
the browser.

Submit through:

`POST /keep/requests/{requestId}/feedback-review`

with:

- optional `note`;
- `X-Keep-Request-Version: {detail.version}`.

On success, adopt the returned `KeepRequestDetailResult`.

**Explaining negative feedback**

For closed requests with negative feedback needing review, show one passive sentence near the
feedback panel:

`This feedback was submitted after the request was closed. Reviewing it does not reopen the request.`

Avoid user-facing labels such as `unresolved feedback`; that is backend/operator taxonomy. The
customer-facing signal is that the customer left negative feedback.

**Ready-to-close and activity language**

Use `Ready to close` as the list/view label and row/detail phrase. Do not invent a separate
translation.

For page-view confidence, use `customerPageViewedAfterLatestUpdate` only for copy such as:

`Customer viewed tracker after your last update.`

Do not use `customerPageViewedAfterLatestUpdate` to claim customer activity after resolution.

For customer activity after resolution, use only fields that truly support the comparison. In the
current detail/list contracts, prefer the existing list warning metadata where available, or derive
carefully from activity timestamps when the request status/context supports it. If the state is
ambiguous, show nothing rather than over-explaining.

**Role scope**

Follow server flags and list authorization:

- Operators may move work to `resolved` when `allowedStatuses` permits it;
- Operators do not see `closed` unless the server includes it, which current policy reserves for
  eligible Owner/Admin closeout;
- Operators do not see feedback-review actions because `canMarkFeedbackReviewed` is false;
- `ready_to_close` and `feedback_review` list views are Owner/Admin-only.

Do not add frontend role-policy logic beyond rendering server-provided views/actions and handling
403/404 responses.

**Navigation**

Do not add dedicated next/previous review navigation in S13h.

The existing detail contract supports `navigation` for `navView=ready_to_close`. Preserve/use that
context where it already exists for ready-to-close flows. Dedicated `next feedback review` or
queue-specific review navigation remains deferred.

#### Live Contracts

| Contract | Status | S13h use |
|---|---|---|
| `GET /keep/requests?view=ready_to_close` | Exists | Owner/Admin closeout list view |
| `GET /keep/requests?view=feedback_review` | Exists | Owner/Admin feedback review list view |
| `PATCH /keep/requests/{requestId}/status` | Exists | Close via status transition |
| `POST /keep/requests/{requestId}/feedback-review` | Exists | Mark negative feedback reviewed |
| `availableActions.canClose` | Exists | Close action gate |
| `availableActions.canMarkFeedbackReviewed` | Exists | Feedback review action gate |
| `availableActions.allowedStatuses` | Exists | Confirm `closed` status availability |
| `validation.feedbackReviewNoteMaxLength` | Exists | Optional note length limit |
| `feedbackReviewAgeBucket` | Exists | `New` / `Aging` / `Overdue` badge |
| `navigation` with `navView=ready_to_close` | Exists | Preserve ready-to-close context |

#### Technical Matrix

| Concern | Locked behavior |
|---|---|
| Route shape | Extend list/detail; no new top-level route |
| Ready-to-close | Owner/Admin list view + detail close action |
| Feedback review | Owner/Admin list view + detail review panel |
| Close gate | `canClose` and `allowedStatuses` includes `closed` |
| Review gate | `canMarkFeedbackReviewed` |
| Review note | Optional; max from `validation.feedbackReviewNoteMaxLength` |
| Negative feedback copy | `Customer left negative feedback`; no `unresolved feedback` UI label |
| Review explanation | Reviewing does not reopen request |
| Activity copy | Page-view confidence and post-resolution activity are separate |
| Navigation | Preserve ready-to-close nav context; defer dedicated review navigation |

S13h out of scope unless explicitly pulled in:

- analytics dashboards;
- public review generation;
- customer-visible feedback replies;
- batch closeout;
- dedicated top-level closeout/feedback-review routes;
- dedicated next-feedback-review navigation.

### S13i — Ready-For-Public Hardening Pass

**Intent:** Convert the completed slices from "functionally present" into a cohesive workbench that
can earn trust and subscription conversion.

#### S13i-0 — Local Workbench Launch Gate

**Status:** Dev-auth helper and runbook update complete; manual authenticated browser smoke deferred
2026-06-30.

The local auth blocker was identified as a browser cookie-store issue: exchanging the magic-link code
with `curl -c cookies.txt` stores `ophalo.sid` in curl's cookie jar, not the browser's. The app at
`localhost:5173` therefore still receives `401` from `GET /auth/me`.

The development-only fix is `web/ophalo-app/public/dev-auth.html`, which posts the raw magic-link
code to `POST /auth/exchange` from the browser with credentials included. This leaves production auth
behavior unchanged and lets the API's `Set-Cookie` response land in the browser session.

Manual verification remains pending:

- run `dotnet run` in `src/OpHalo.Api`;
- run `pnpm dev` in `web/ophalo-app`;
- call `/auth/start` or `/auth/signin`;
- copy the `code=` value from the API console output;
- exchange it at `http://localhost:5173/dev-auth.html`;
- smoke Requests, Request Detail, Quick Capture, Settings, and a mobile viewport.

Decision 2026-06-30: do not let this manual local-auth gate block further foundation work. Keep the
auth-helper/runbook fix, record the pending browser verification, and move next to a development-only
workbench preview path so the team can see and polish the UI while auth/seed-data verification is
handled separately.

#### S13i-1 — Dev/Mock Workbench Preview Path

**Status:** Complete 2026-07-01.

**Approach: mock API adapter swap (no new dependencies)**

`VITE_OPHALO_MOCK_WORKBENCH=true` in `.env.development.local` (git-ignored, not committed). When the
flag is set, `main.tsx`'s async `bootstrap()` dynamically imports `installMockApi()` before React
renders. `installMockApi` replaces every method on the shared exported `api` object in-place. Because
all components import the same `api` reference, they get mock responses automatically with no
component changes. Production `AuthGuard`, session-cookie behavior, and API authorization are
unchanged — `AuthGuard` passes through because the mock `getMe` returns a valid authenticated
`MeResponse`.

**Files delivered:**

- `src/mocks/fixtures.ts` — typed mock DTOs shaped from the real API interfaces. Five requests
  covering the primary display states: in-progress with timeline/participants, unassigned intake with
  `NeedsShare: true` and elevated attention, pending-customer with responsible and watching
  participants, resolved with positive feedback awaiting review, and newly created with empty
  timeline. Setup, members (owner + 2 operators + 1 pending), onboarding checklist, and intake
  fixtures for the Settings screen.
- `src/mocks/mockState.ts` — module-level mutable store (`currentMockRole`, requests array, detail
  map). All write helpers (`addMockRequest`, `updateMockDetail`) keep list summaries and detail
  records in sync so React Query re-fetches show updated state within the session.
- `src/mocks/mockApiClient.ts` — mock for every `api` method. Mutations (status change, log contact,
  acknowledge attention, watch/unwatch/mute, business update, share-intent, feedback review) append
  events, update state, and return the updated detail so real query-cache invalidation paths work
  correctly. `availableActions` are downgraded by role at call time so the Operator and Viewer toggle
  states reflect permission differences.
- `src/mocks/MockWorkbenchOverlay.tsx` — fixed bottom-left chip: `mock  Owner | Admin | Operator |
  Viewer`. Active role is highlighted. Clicking a role calls `setMockRole` + `queryClient.invalidateQueries()`
  so nav items, action rail, and the Viewer `AccessLimited` gate all re-render without a page reload.
- `src/main.tsx` — async `bootstrap()` with conditional dynamic import; mock modules tree-shake out
  of production builds because `import.meta.env.VITE_OPHALO_MOCK_WORKBENCH` is statically false in
  production Vite builds.

**Done gate:**

- `pnpm -C web/ophalo-app typecheck` clean ✓
- `pnpm -C web/ophalo-app build` clean ✓
- Manual browser verification target:
  - mock overlay visible bottom-left with four role buttons
  - 5 requests load; KC-002 shows elevated attention + NeedsShare banner in detail
  - Quick Capture submits → mock request created → appears in list
  - Operator toggle: sidebar loses Settings/Getting Started; detail action rail narrows
  - Viewer toggle: requests page shows AccessLimited surface
  - Settings (Owner): Company, Policy, Team, Intake Link sections render with Apex Home Services data
  - Mobile width: sidebar collapses, FAB visible

**Limitation:** this validates UI shape and in-memory mock mutations only. It does not prove
end-to-end authenticated API behavior. The S13i-0 browser-auth smoke remains a separate pending item.

#### S13i-2 — Verify Closeout

**Status:** Complete 2026-07-01.

Session 13 closes with `ophalo-app` treated as the authenticated Keep workbench and the mock API
treated as a development-only role/UI verification harness, not the primary acceptance path.

Closeout decisions:

- Local Resend configuration is not required for development verification. Without a Resend key, the
  API writes the magic-link URL to the console through `ConsoleEmailSender`; developers copy the
  `code=` value into `http://localhost:5173/dev-auth.html` so the browser receives `ophalo.sid`.
- `VITE_OPHALO_MOCK_WORKBENCH=true` remains useful for fast Owner/Admin/Operator/Viewer role checks,
  especially Viewer and Operator surfaces that are cumbersome to reproduce with real local accounts.
- Regular API/auth mode remains the source of truth for acceptance verification. Set
  `VITE_OPHALO_MOCK_WORKBENCH=false`, run the API and Vite app, exchange the console auth code in
  `dev-auth.html`, and verify real `/auth/me`, onboarding/settings, request list/detail, Quick Capture,
  and mobile layout.
- Owner/Admin are expected to see all operational requests. Viewer is a separate account role, not an
  owner-owned request subset.
- Contact launchers are server-driven from `contactActions`. The mock fixture bug that rendered a
  phone action as a second Email button was corrected by aligning mock data to the backend contract:
  `type: "call" | "email"` with raw phone/email targets.
- User-selectable email-client preference is a valid future settings idea, but V1 keeps native device
  handoff through `mailto:`. A later slice can add default mail app, Gmail web, Outlook web, and copy
  address options if customer usage justifies it.
- The next missing product surface is the public/front-door app. Session 14 should build `ophalo-web`
  for marketing, sign-in/start auth, magic-link exchange, account onboarding entry, and redirect into
  `ophalo-app`.

Session 13 completion gate:

- all selected S13 workflow slices meet their done gates;
- no primary nav item points to an unbuilt placeholder;
- all primary screens use OpHalo/Keep tokens and the Source Serif 4 / Inter type contract;
- `pnpm -C web/ophalo-app typecheck` and `pnpm -C web/ophalo-app build` are green;
- relevant backend tests were not rerun in S13i because backend behavior was not changed;
- regular authenticated browser smoke remains documented through the local runbook and should be
  repeated as part of Session 14 `ophalo-web` auth/onboarding work;
- known limitations are documented in build-log 067 and carried forward instead of hidden in the UI.

## S13a Scope

S13a is the first independently useful web slice:

- scaffold `web/ophalo-app` as a standalone Vite + React + TypeScript app;
- add the authenticated app shell and guarded home route;
- use a hand-written typed fetch client with credentialed API calls;
- call live `GET /auth/me` and `GET /keep/setup/onboarding`;
- render an Owner/Admin home screen with the onboarding checklist as the primary surface;
- render a Viewer access-limited state;
- redirect unauthenticated users to `PublicBaseUrl` auth entry;
- self-host Source Serif 4 and Inter under `public/fonts/`;
- preload the core self-hosted font assets from `index.html`;
- add a minimal PWA manifest, deferring service worker/offline caching.

Out of S13a:

- request list/detail;
- Quick Capture;
- member management;
- tracker sharing;
- customer update composer;
- Operator workbench shell;
- placeholder navigation for unbuilt workflows;
- MSW/test mocking infrastructure unless a tested utility or hook requires it.

## Locked Decisions

- `ophalo-app`: Vite + React + TypeScript.
- `ophalo-web`: future Next.js public/auth/customer surface.
- Package manager: `pnpm`.
- Project shape: standalone app for now; workspace only when a second package makes sharing real.
- Styling: Tailwind CSS with OpHalo/Keep tokens from the active UX design model.
- Typography: Source Serif 4 headings and Inter body/UI, self-hosted from the app origin.
- Components: small Radix/shadcn-style primitive set only as needed by the shell/home.
- Icons: Lucide React.
- Server state: TanStack Query with the ADR-377 freshness model.
- API client: hand-written typed fetch client first; OpenAPI generation can replace it later.
- Auth: browser sessions are HttpOnly cookies; frontend never reads or stores session tokens.
- Local origins: API `http://localhost:5092`, app `http://localhost:5173`, public web
  `http://localhost:3000`.
- Local cookie posture: Development must omit the cookie `Domain` attribute so `ophalo.sid` is a
  host-only localhost cookie; do not configure `.localhost` or `localhost:port` as a cookie domain.
- Local/API credential posture: every app API request uses `credentials: "include"` and API CORS
  allows the explicit app origin, not wildcard origins.
- Base URL vocabulary: `PublicBaseUrl` for public/auth entry, `AppBaseUrl` for authenticated app.
- Home screen: onboarding checklist primary; no fake request summary and no `/keep/requests` call.

## S13a Integration Requirements

### Local Cookies And CORS

Production uses `Auth:CookieDomain=.ophalo.com` so `ophalo.sid` can be shared across
`app.ophalo.com` and `api.ophalo.com`.

Local development must not attempt a shared localhost cookie domain. With `Auth:CookieDomain` empty,
`AuthCookieOptionsFactory` omits the `Domain` attribute and produces a host-only cookie, which modern
browsers accept for the API host. The Vite app must make credentialed requests with
`credentials: "include"`, and API CORS must allow `http://localhost:5173` explicitly with
credentials enabled.

### Return-To Auth Redirects

The unauthenticated app guard must not blind-redirect to the public auth entry. It must append the
current app path/query as a `return_to` value when redirecting to `PublicBaseUrl`.

`ophalo-web` later owns validation and final routing for that value:

- accept only same-app destinations rooted under configured `AppBaseUrl`;
- reject external origins and malformed values;
- after successful exchange/accept, route back to the validated destination or the default app home.

S13a only needs to emit the parameter correctly and document the contract because `ophalo-web` is
out of this slice.

### Fetch Client Error Contract

The typed fetch client must treat non-2xx responses as thrown errors, not `{ data, error }` success
payloads. The thrown error should carry the HTTP status and server-derived problem `code` when
available.

This preserves TanStack Query behavior for retries, error states, error boundaries, and auth/session
invalidations. In particular, a `401` from `/auth/me` must throw so the route guard can clear local
view state and redirect through the auth entry path.

### Self-Hosted Font Loading

`index.html` must preload the primary self-hosted variable font files from `/fonts/...` using
`rel="preload"`, `as="font"`, the correct `type`, and `crossorigin`. CSS remains responsible for the
`@font-face` declarations and `font-display` policy.

## Local Auth Path

S13a must make local authenticated browser testing reliable without weakening production behavior.

Locked implementation direction:

- production email remains provider-backed;
- when email provider secrets are absent in local development, use an explicit developer-only console
  email sender fallback so magic-link URLs can be copied from API logs;
- document `/auth/start` or `/auth/signin` -> magic link -> `/auth/exchange` -> `ophalo.sid`
  cookie -> `localhost:5173` in the local web setup runbook.

## Done Gate

- `pnpm` install succeeds for `web/ophalo-app`;
- `pnpm tsc --noEmit` or equivalent typecheck is clean;
- `pnpm build` succeeds;
- local dev server runs;
- browser screenshot shows the authenticated Owner/Admin home screen;
- docs/runbook local web setup exists with required environment values and auth steps.
