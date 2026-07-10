# OpHalo Foundation Build Plan

## Greenfield Boundaries, Brownfield Behavior

**Status:** Locked build direction — ready for Phase 0 / Phase 1 execution  
**Product context:** OpHalo Foundation with Keep as the first product module  
**Working doctrine:** Build a clean foundation without reinventing proven behavior  

---

## 1. Executive Decision

OpHalo will proceed with a **foundation rebuild using greenfield boundaries and brownfield behavior**.

This is not a pure greenfield rewrite. The current deployed system remains the reference implementation for proven behavior. The new build will create clean architecture boundaries, clean naming, clean documentation, clean migrations, and subscriber-ready foundation primitives while preserving and porting working behavior where it still fits.

### Core Doctrine

```text
Greenfield boundaries.
Brownfield behavior.
Verification gates.
Documented redesign only where the current foundation is missing or structurally wrong.
```

### What this means

We will:

- Create a clean OpHalo solution structure.
- Remove legacy architectural families from the active design: `Continuity`, `Signal`, `Platform` project naming, duplicate abstractions, and two-host deployment assumptions.
- Preserve proven auth, session, public intake, customer page, operator request, SSE, lifecycle, and token behavior unless there is a clear foundation reason to change it.
- Rename and relocate working code into the new structure where appropriate.
- Add missing foundation capabilities intentionally: entitlements, permission keys, device/session readiness, notification preferences, quiet hours, request watch/mute subscriptions, admin diagnostics, audit, activity events, and insight-ready event capture.
- Verify each moved slice against expected behavior before proceeding.

We will not:

- Reimplement working behavior from memory.
- Redesign core flows just for naming cleanliness.
- Build a generic platform/feature-flag/billing engine before Keep is runnable.
- Freeze product learning behind a multi-month rewrite unless explicitly decided.

---

## 2. Why This Build Is Necessary

The repository audit found real structural debt:

- Multiple active architectural families: `OpHalo.*`, `OpHalo.Platform.*`, `OpHalo.Continuity.*`, and transitive `OpHalo.Signal.*` references.
- `Signal.*` is dead at runtime but still live on the build graph through project references.
- Two API hosts exist: one for auth/account/worker and one for Continuity/Keep.
- `ApplicationDbContext` is the real consolidated runtime context, while `OpHaloDbContext` and `ContinuityDbContext` remain as inactive legacy contexts.
- Duplicate abstractions exist, including `IClock`, `ICurrentUser`, and `IEmailSender` in more than one layer.
- Hangfire remains active even though the Platform Work Engine is the intended durable work layer.
- Architecture tests exist only as a stub and enforce no real rules.
- Keep is product-facing, but active backend code is still mostly named Continuity.
- The current system lacks a clean foundation for feature entitlements, user permissions, mobile-ready sessions/devices, request-level notification control, account quiet hours, and subscriber insights.

The build is justified because the current structure makes future mobile, multi-user permissions, notification control, admin support, and subscriber packaging harder than necessary.

The audit also found that much of the working behavior is valuable and should be preserved. Therefore, the objective is not to erase the current product; it is to move proven behavior into the right foundation.

---

## 3. Migration Doctrine

Every moved module must go through this decision ladder.

### 3.1 Preserve

Keep the existing behavior/code if it already works and aligns with the target foundation.

Examples:

- Magic-link sign-in behavior.
- Opaque server-side sessions.
- Prior-code invalidation.
- Cookie create/delete behavior.
- Account lifecycle enforcement.
- Public intake token security.
- Customer page token flow.
- Operator request list behavior.
- SSE broadcaster behavior.
- First-response and attention policies.

### 3.2 Move / Rename

Move code into the new structure and rename legacy language where it improves clarity.

Examples:

- `Continuity` product code becomes `Keep` code.
- `Platform` project concepts become Foundation concepts.
- `ApplicationDbContext` becomes `OpHaloDbContext`.
- `OpHalo.Shared` becomes disciplined `OpHalo.SharedKernel`.

### 3.3 Adapt

Modify only where the new foundation requires it.

Examples:

- Add feature-key entitlement checks.
- Add permission-key checks.
- Add mobile-ready session/device fields.
- Add account quiet hours.
- Add user notification preferences.
- Add request watch/mute subscriptions.
- Add audit events for admin writes.
- Collapse two API hosts into one.

### 3.4 Redesign

Redesign only when the existing implementation is structurally wrong, duplicated, unsafe, untestable, or blocks the target foundation.

Examples:

- Duplicate `IClock`, `ICurrentUser`, and `IEmailSender` abstractions.
- Hangfire dependency for background processing.
- Dead `Signal.*` build references.
- Legacy contexts/migration folders not used at runtime.
- Missing architecture tests.

---

## 4. Locked Architecture Decisions

### 4.1 Solution Name

Use:

```text
OpHalo
```

Do not use:

```text
OpHaloLLC
```

Reason: the solution represents the product/platform system, not the legal entity.

---

### 4.2 Product and Foundation Language

```text
OpHalo Foundation = shared platform capabilities used by products.
Keep = first product module built on the foundation.
Future products = additional modules built on the same foundation.
```

`Continuity` does not survive as the active product/module name in new code. It may remain in historical docs, archived code, or temporary external contracts during staged cutover.

---

### 4.3 Project Structure

Target structure:

```text
ophalo/
  OpHalo.slnx

  src/
    OpHalo.Api/
    OpHalo.Worker/

    OpHalo.Foundation.Core/
    OpHalo.Foundation.Application/
    OpHalo.Foundation.Infrastructure/

    OpHalo.Keep.Core/
    OpHalo.Keep.Application/
    OpHalo.Keep.Infrastructure/

    OpHalo.SharedKernel/

  tests/
    OpHalo.ArchitectureTests/
    OpHalo.UnitTests/
    OpHalo.IntegrationTests/

  web/
    ophalo-app/
    ophalo-web/

  docs/
    architecture/
    build-log/
    decisions/
    deployment/
```

Do not create these in v1:

```text
OpHalo.Foundation.Contracts
OpHalo.Keep.Contracts
OpHalo.Admin
OpHalo.Auth
OpHalo.Signal.*
OpHalo.Continuity.*
OpHalo.Platform.*
```

Rationale:

- Foundation and Keep are real boundaries.
- Keep owns real domain behavior and deserves its own Core/Application/Infrastructure modules.
- Contracts projects are premature until a second host/client integration truly needs them.
- Admin is a shell inside the main app/API, not a separate host.
- Auth is part of Foundation, not a separate project.

---

### 4.4 API Host Strategy

Use one API host:

```text
OpHalo.Api
```

It owns:

- Auth endpoints.
- Account endpoints.
- Keep operator endpoints.
- Keep public intake endpoints.
- Customer page endpoints.
- SSE endpoints.
- Admin diagnostics endpoints.
- Readiness/health endpoints.

Reason: the current two-host setup adds deployment, CORS, cookie, environment-variable, and operational complexity without a strong runtime boundary.

---

### 4.5 Worker Strategy

Use one Foundation work model.

No Hangfire in the target build.

The work processor must support flexible hosting:

```text
Pilot mode: lightweight in-process worker inside OpHalo.Api, if cost/reliability tradeoff is acceptable.
Scale mode: separate OpHalo.Worker deployment using the same work table and handlers.
```

Foundation work engine responsibilities:

- Work item queue.
- Retry policy.
- Dead-letter state.
- Execution log.
- Requeue capability.
- Worker health diagnostics.
- Email/notification delivery.
- Session cleanup.
- Future scheduled work.

Reason: we want one work model without requiring an always-on paid worker service too early.

---

### 4.6 Database Strategy

Use:

```text
One PostgreSQL database.
One primary OpHaloDbContext.
Clean active migrations.
```

Important nuance:

The current repository already has a consolidated runtime context named `ApplicationDbContext`. In the new target, the runtime context should be named `OpHaloDbContext`, but the behavior/schema learning from the existing consolidated baseline should be preserved where useful.

Do not blindly recreate persistence from memory.

Use clean migrations only because there is no production customer data requiring preservation at this stage.

---

### 4.7 Authentication and Sessions

Lock:

```text
Magic links are the entry/recovery mechanism.
Trusted server-side sessions are the authentication mechanism.
No JWT by default.
```

Preserve proven behavior:

- Hashed magic-link/token storage.
- One-time exchange.
- Expiry enforcement.
- Prior unused link invalidation.
- Secure cookie issuance for browser sessions.
- Configured cookie domain for production.
- `/me` current user resolution.
- Logout/session revoke.

Add foundation support for:

- `SessionClientType.Browser`.
- `SessionClientType.MobileApp`.
- `SessionClientType.Admin` if useful.
- `DeviceName`.
- `LastSeenAtUtc`.
- `RevokedAtUtc`.
- Optional future `AccountUserDevice` association.

Native mobile can later use opaque server-side session tokens stored securely on device. Do not introduce JWT just because mobile exists.

---

### 4.8 Account, User, Membership, and Permissions

Foundation owns identity and membership:

- `Account`.
- `User`.
- `AccountUser`.
- Membership status.
- Role.
- Permission map.
- Sessions.
- Invitations.
- Internal/support identity.

Account-level access and user-level access are separate.

Core rule:

```text
Account is entitled.
User is permitted.
Session is trusted.
Action is allowed.
```

V1 roles:

```text
Owner
Admin
Operator
Viewer
```

V1 membership statuses:

```text
Invited
Active
Suspended
Removed
```

Use permission keys instead of scattering role checks throughout product code.

Example permission groups:

```text
account.view
account.manage_settings
account.manage_users
account.manage_billing
account.manage_notifications
account.view_audit

keep.view_requests
keep.create_request_internal
keep.send_update
keep.message_customer
keep.change_status
keep.close_request
keep.add_internal_note
keep.view_insights
keep.manage_keep_settings

internal.view_accounts
internal.view_diagnostics
internal.manage_account_lifecycle
internal.revoke_sessions
internal.rotate_tokens
internal.requeue_work
```

Do not build custom roles or enterprise RBAC in v1.

---

### 4.9 Product-Specific User Settings

Do not create a separate `KeepUser` identity model.

Allowed and expected:

```text
Foundation AccountUser + KeepUserSettings
```

Foundation owns:

- User identity.
- Account membership.
- Role/permission.
- Session.

Keep may own product-specific settings attached to `AccountUser`:

- `KeepUserSettings`.
- Keep notification preferences.
- Default queue view.
- Auto-watch behavior.
- Keep-specific display or workflow preferences.

This pattern allows future products to add their own product user settings without duplicating identity.

---

### 4.10 Account Lifecycle and Commercial State

Foundation owns account access posture.

States:

```text
Lifecycle:
- Active
- Suspended
- Closed

Commercial:
- Trial
- Active
- PastDue
- Expired
- Canceled

Operating mode:
- Standard
- OffSeason

Purpose:
- Business
- Internal
```

Authenticated protected requests check account posture on every request. Computed snapshots may be cached briefly, but authorization must remain server-enforced.

Internal accounts may bypass commercial/trial restrictions only through explicit internal-purpose policy.

---

### 4.11 Entitlements, Features, Plans, and Limits

Foundation owns:

- Plan.
- Entitlements.
- Feature keys.
- Usage limits.
- Feature authorization.

Runtime code checks entitlements, not plan names.

Plans produce entitlements.

Example plans:

```text
Pilot
Starter
Professional
Business
Internal
```

Do not lock final pricing or exact tier matrix yet.

Example feature keys:

```text
keep.enabled
keep.public_intake
keep.customer_page
keep.operator_queue
keep.request_detail
keep.operator_messaging
keep.customer_messaging
keep.internal_notes
keep.close_request
keep.sse_live_updates
keep.email_notifications
keep.browser_push
keep.mobile_push
keep.request_subscriptions
keep.insights

account.user_limit
keep.monthly_request_limit
keep.active_request_limit
```

V1 should support explicit entitlement fields and/or a simple entitlement snapshot. Do not build a generic feature-flag rules engine, Stripe catalog, overage billing, or plan-matrix admin UI yet.

---

### 4.12 Notification Foundation

Notification control is foundation-level product quality, not an afterthought.

Foundation owns:

- Notification channels.
- User notification preferences.
- Account notification policy.
- Account quiet hours.
- Device/destination records.
- Channel availability.
- Delivery suppression rules.

Keep owns:

- Request watch/mute subscription state.
- Keep notification reasons.
- Request-specific routing logic.
- Assignment-related routing later.

Notification routing must consider:

```text
Account entitlement
Account quiet hours
User permission
User notification preferences
Request subscription state
Event type/reason
Actor exclusion
Request visibility
Delivery channel availability
```

Notifications are not just delivery. They are authorization + entitlement + preference + context.

---

### 4.13 Account Quiet Hours

Businesses/accounts must be able to set quiet hours.

Foundation should model:

- Account time zone.
- Quiet hours enabled/disabled.
- Quiet hours start/end.
- Whether critical notifications can bypass quiet hours.

V1 behavior:

- Quiet hours suppress normal notifications.
- Critical notifications may still be delivered if the account allows it.
- User-specific quiet hours are deferred unless easy to add safely.

---

### 4.14 Keep Request Notification Subscriptions

Keep should support request-level notification control.

V1 states:

```text
Watching
Muted
```

Later:

```text
Assigned
```

Rules:

- A user may watch a request without owning it.
- A user may mute a request without disabling all Keep notifications.
- Request mute wins for normal request notifications.
- Do not notify the actor who caused the event.
- Only users who can view a request may subscribe/watch/mute that request.

Needs discussion before web/mobile UI build:

- Tiny-team accounts may not need visible assignment/watch/mute controls.
- Preserve the backend participation/routing foundation, but consider a simplified product surface:
  - 1 active user: hide team-routing controls; everything is implicitly mine/all.
  - 2 active users: consider simplified Mine/All or Assigned-to controls.
  - 3+ active users: expose fuller assignment/watch/mute/unassigned surfaces.
- Decide whether this is automatic by active-user count, configurable as Simple Mode vs Team Mode,
  or both.
- Do not silently expand Operator visibility or bypass assigned/watched/unassigned routing rules as
  a UI simplification; that would affect permissions, notification routing, counts, self-assign
  eligibility, and future mobile push, and needs its own ADR.
- Address after Session 4/5+ when web/mobile product surfaces are being shaped.
- Track as `DEF-052`.

---

### 4.15 Mobile-Ready Device and Activity Model

Foundation should include enough structure for mobile users from the start.

Required:

- Session client type.
- Device name.
- Last seen timestamp.
- Session/device revocation.
- Basic device/activity diagnostics.

Recommended future-ready model:

- `AccountUserDevice`.
- `PushToken` or `NotificationDestination`.
- Device platform: iOS, Android, Web.
- Push token lifecycle: registered, failed, revoked.

Track important activity:

- Sign-in.
- Session created.
- Session revoked.
- Device registered.
- Push token registered/failed.
- User opened Keep.
- User viewed request.
- User sent update.
- User muted/watched request.

Do not build the full native app in the foundation phase.

---

### 4.16 Audit, Activity, and Telemetry

Separate these three concerns:

```text
Audit events = security/support trail.
Product activity events = business/reporting insights.
Telemetry/logs = operational debugging.
```

Foundation owns audit/security events:

- Account lifecycle changes.
- Commercial state changes.
- User invited/removed/suspended.
- Session revoked.
- Public token rotated.
- Entitlement changed.
- Admin/support write action.
- Significant denied/suspicious access attempts.

Keep owns product activity events:

- Request created.
- Customer message sent.
- Business update sent.
- Request closed.
- No longer needed.
- First response recorded.
- Attention raised/cleared.
- Request watched/muted.

Operational telemetry/logging must support diagnostics but should not be confused with business reports.

---

### 4.17 Access-Denied Logging and Owner Notification

Protected access is checked server-side on every relevant request.

Log denied attempts based on severity.

Always audit or security-log:

- Suspended account access attempt.
- Removed/suspended user access attempt.
- Invalid internal/admin access attempt.
- Permission-denied admin write.
- Revoked session use.
- Repeated public token failures.

Do not notify the owner for every normal denial.

Notify owner/admin only for meaningful security/account events, such as:

- New device/session created, if enabled later.
- Suspicious sign-in attempts.
- Removed user repeatedly attempting access.
- Admin/internal action on account.
- Token rotated.
- Account suspended/reactivated.

Avoid creating notification fatigue through security noise.

---

### 4.18 Admin Shell

Admin shell is required for go-live support.

It lives inside:

```text
OpHalo.Api
web/ophalo-app/app/admin
```

No separate Admin host in v1.

V1 admin shell is internal-only and read-only first.

Read-only diagnostics:

- Account search.
- Account detail.
- Users/memberships.
- Sessions/devices.
- Entitlements.
- Lifecycle/commercial state.
- Onboarding state and first-use milestones.
- Account-level usage/adoption summary.
- Keep requests.
- Request events/timeline.
- Notification status.
- Public token status.
- Work queue/dead-letter state.

Write actions require:

```text
Internal guard
Dedicated command
Explicit audit event
Reason/comment where appropriate
```

Potential writes after verification:

- Suspend account.
- Reactivate suspended account.
- Enter off-season.
- Resume from off-season.
- Rotate public intake token.
- Revoke session.
- Requeue dead-lettered work item.

No impersonation in v1.

---

### 4.19 Public Token Security

Foundation defines the public token security posture.

Keep may own specific token types, but token behavior should be standardized:

- High-entropy tokens.
- Hashing where appropriate.
- Expiry where appropriate.
- Rotation.
- Revocation.
- Public guard pattern.
- Rate-limit policies.
- No client-trusted AccountId.

Keep token types:

- Public intake token.
- Customer page token.

Future products, such as QR service history, should reuse the same token posture.

---

### 4.20 Reports and Business Insights

Basic business insights are part of the subscriber value proposition.

Do not build a full analytics platform in v1.

But Keep events must be structured so basic reports can be produced.

Target early insights:

- Request volume by day/week.
- Open vs closed request count.
- Average first response time.
- Overdue first responses.
- Customer message/question count.
- No-longer-needed count.
- Operator activity count.
- Attention events.
- Requests by source later.

Use operational database queries/read models first. Defer warehouse, exports, custom reports, benchmarks, and AI insights.

---

### 4.21 Attachments Posture

Attachments are deferred, but the posture is locked.

Future expected needs:

- Customer photos.
- Technician photos.
- Estimates/PDFs.
- Before/after images.
- QR/service-history assets.

Decision:

- No binary storage in the database.
- Future storage via object storage.
- File access must be account/request scoped.
- Attachment metadata belongs in the database.
- Actual file upload implementation deferred.

---

## 5. Deliberately Deferred

Do not build these in the foundation phase:

- Final pricing matrix.
- Stripe integration.
- Self-service billing portal.
- Usage-based billing/overages.
- Custom roles.
- Enterprise RBAC.
- Multi-location hierarchy.
- Full native app.
- SMS.
- Complex assignment/dispatch.
- Crew/team routing.
- Digest notifications.
- Escalation rules.
- Full analytics warehouse.
- File uploads.
- Integrations.
- Scheduling.
- Customer CRM.
- AI insights.

These may be supported by the foundation later, but they are not part of the initial build.

---

## 6. External Contract Strategy

Some existing names may be external/runtime contracts.

Treat these carefully:

- Public route names.
- Customer page URLs.
- Intake URLs.
- Environment variable names.
- Database table names if any state must be preserved.
- Vercel/Railway deployment settings.

Because there is no production customer data yet, more aggressive cleanup is possible. But any issued links or deployed environment variables must still be handled deliberately.

Staged approach:

```text
Code/module names: rename to Keep during the build.
Public routes/env vars/table names: rename only when cutover strategy is explicit.
Historical docs/ADRs: preserve as historical references.
```

---

## 7. Frontend Cutover Strategy

The frontend must be included in the plan.

Current conceptual split remains:

```text
ophalo-web = marketing/auth/public customer surfaces
ophalo-app = operator/account/admin app
```

Target:

- `ophalo-app` uses the single `OpHalo.Api` base URL.
- `ophalo-web` uses the single `OpHalo.Api` base URL for public intake/customer page/auth flows.
- `CONTINUITY_API_URL` naming is replaced only during a deliberate cutover.
- Keep frontend folders/routes remain `/keep` where product-facing.
- Internal admin shell lives under `ophalo-app/app/admin` and is internal-gated.

Frontend cutover must verify:

- Sign-in.
- Auth exchange.
- Customer page `/r/{pageToken}`.
- Public intake `/q/{publicSlug}/{publicIntakeToken}`.
- Operator request list.
- Request detail.
- SSE connection.
- Admin access denied for non-internal users.

---

## 8. Architecture Rules

Architecture tests must be real before major code movement.

Rules:

- Foundation must not reference Keep.
- Keep may reference Foundation.
- SharedKernel must not contain business concepts.
- Application must not depend on Infrastructure.
- API should depend on Application and composition-root Infrastructure only.
- Core/domain projects must not depend on Application or Infrastructure.
- No active project may reference `Signal.*`.
- No active project may reference `Continuity.*` after the Keep rename phase.
- No active project may reference `Platform.*` after the Foundation fold phase.
- Product-specific settings must attach to `AccountUser`, not duplicate identity.

---

## 9. Phase Plan

Each phase should be small enough to review, test, and merge cleanly.

Every phase ends with:

```text
What behavior was preserved?
What moved?
What was renamed?
What was adapted?
What was redesigned?
Why?
What tests prove it?
What foundation rule now passes?
What risks remain?
```

---

### Phase 0 — Plan Lock and ADR

Create/update:

```text
docs/build-log/000-foundation-build-charter.md
docs/decisions/ADR-000-ophalo-foundation-greenfield-boundaries-brownfield-behavior.md
docs/architecture/decision-index.md
```

Lock:

- Greenfield boundaries, brownfield behavior.
- Target project structure.
- One API host.
- One DbContext.
- No Signal, Continuity, Platform project families in target.
- Preserve auth/session behavior.
- Preserve Keep behavior through parity validation.
- Add missing foundation capabilities deliberately.
- Current deployed system remains reference/fallback until parity.

Exit gate:

- ADR written.
- Decision index updated.
- Build plan accepted.
- No code changed unless documentation-only.

---

### Phase 1 — Skeleton and Architecture Tests

Create or normalize the target solution skeleton.

Add real architecture-test tooling and rules.

Do not move business behavior yet unless necessary for compile.

Exit gate:

- Solution builds.
- Architecture tests compile and run.
- Rules exist for Foundation/Keep/SharedKernel dependency boundaries.
- No Signal references allowed in the target build graph.

---

### Phase 2 — Low-Risk Legacy Exclusion

Remove or exclude from the active target:

- `Signal.*` project references.
- Dead/stub Signal host assumptions.
- Stale generated/bin artifacts.

Document what was removed and why.

Exit gate:

- Build green.
- Tests green.
- No active Signal references.

---

### Phase 3 — Foundation SharedKernel and Duplicate Abstraction Cleanup

Create disciplined SharedKernel.

Allowed:

- Result/Error primitives.
- Tiny domain primitives.
- Guard helpers if truly generic.

Not allowed:

- Account logic.
- CurrentUser.
- Email sending.
- DbContext.
- Entitlements.
- Notifications.
- Keep concepts.

Collapse duplicate abstractions deliberately:

- `IClock`.
- `ICurrentUser`.
- `IEmailSender`.

Exit gate:

- One canonical abstraction for each cross-cutting concern.
- Architecture tests prevent SharedKernel becoming a dump.

---

### Phase 4 — Foundation Account, User, Lifecycle, Entitlements, Permissions

Build or port Foundation identity/account primitives:

- Account.
- User.
- AccountUser.
- Roles.
- Membership status.
- Lifecycle/commercial/operating mode.
- Account purpose.
- Entitlements.
- Feature keys.
- Permission keys.
- Account access policy.
- User authorization policy.

Preserve existing lifecycle semantics where they are already correct.

Add missing permission-key layer and feature-key entitlement layer.

Exit gate:

- Unit tests for account posture.
- Unit tests for role-to-permission mapping.
- Unit tests for entitlement checks.
- Foundation has no Keep references.

---

### Phase 5 — Auth, Magic Links, Sessions, Devices

Move/preserve existing auth behavior into Foundation.

Preserve:

- Hashed magic-link codes.
- One-time exchange.
- Expiry.
- Prior-code invalidation.
- Server-side opaque sessions.
- Cookie create/delete handling.
- `/me`.
- Logout/session revoke.
- No JWT.

Add:

- Session client type.
- Device name.
- Last seen.
- Mobile-ready session/device fields.
- Future push destination relationship if appropriate.

Exit gate:

- Auth parity tests pass.
- Sign-in works.
- Exchange works.
- `/me` works.
- Logout works.
- Suspended/removed user cannot continue access.

---

### Phase 6 — Persistence and OpHaloDbContext

Create the target `OpHaloDbContext`.

Use clean migrations for the active target.

Preserve schema learning from the existing consolidated `ApplicationDbContext` where relevant.

Do not carry forward:

- Dead legacy contexts.
- Old migration folders.
- Multiple migration histories.

Exit gate:

- Fresh database migrates cleanly.
- Foundation auth flow works against the database.
- Tests prove account/session/access persistence.

---

### Phase 7 — Keep Core Vertical Slice 1: Intake to Operator View

Port proven Keep/Continuity behavior into Keep naming and structure.

First slice:

- Public intake token resolution.
- Public request submission.
- Keep request creation.
- Operator request list read.

Preserve behavior; do not redesign the flow unless required by foundation checks.

Add where needed:

- Feature entitlement check.
- User permission check.
- Public guard pattern.
- Rate limit policy.

Exit gate:

- Customer can submit a request.
- Operator can sign in.
- Operator can see request.
- Account entitlements and user permissions are enforced.
- Public endpoint does not trust client AccountId.

---

### Phase 8 — Keep Communication Loop

Port/protect:

- Customer page.
- Business updates.
- Customer messages/questions.
- Timeline.
- Request detail.
- Close request.
- No-longer-needed.
- First-response policy.
- Attention state.

Exit gate:

- Customer/operator communication loop works.
- Timeline parity verified.
- First-response behavior verified.
- Attention behavior verified.
- Entitlement and permission checks applied.

---

### Phase 9 — Notifications, Quiet Hours, Watch/Mute

Build missing notification foundation and Keep routing.

Foundation:

- Account notification policy.
- Account quiet hours.
- User notification preferences.
- Notification channels.
- Device/destination model if needed.

Keep:

- Request watch/mute subscription.
- Notification reasons.
- Actor exclusion.
- Request visibility check.

Exit gate:

- User can globally control Keep notification categories/channels.
- Account can define quiet hours.
- User can watch/mute a request.
- Actor is not notified about their own action.
- Muted request suppresses normal notifications.

---

### Phase 10 — One API Host and Frontend Cutover

Collapse API surface into `OpHalo.Api`.

Move Keep routes, SSE routes, public intake routes, customer page routes, and admin routes into one host.

Update frontend API clients and environment variables deliberately.

Exit gate:

- Auth works from frontend.
- Public intake works.
- Customer page works.
- Operator list/detail works.
- SSE works.
- Readiness endpoint works.
- Old two-host env assumptions removed or compatibility-shimmed intentionally.

---

### Phase 11 — Admin Shell V1

Build internal admin shell.

Read-only first:

- Account search/detail.
- Users.
- Sessions/devices.
- Entitlements.
- Onboarding state and usage/adoption summary.
- Keep requests/events.
- Token status.
- Work queue/dead-letter.

Writes only with audit:

- Suspend/reactivate.
- Off-season/resume.
- Revoke session.
- Rotate token.
- Requeue work.

Internal observability follow-up:

- Founder/internal admins need to know when a business onboards and whether it is using Keep.
- Add account-level usage summaries and important internal event feeds before pilot scale creates
  blind spots.
- Internal mobile alerts may be useful for new-business onboarding, stalled onboarding, unusually
  inactive pilot accounts, delivery/work failures, and other high-signal product-ops events.
- Do not implement broad impersonation by default; internal access must be permission-gated and
  audited.
- Keep internal alerts metadata-light and avoid sending sensitive customer content.
- Decide event subscription transport deliberately; do not copy reference polling/signals by
  default.
- Track as `DEF-057`.

Exit gate:

- Internal-only access.
- Non-internal denied.
- Every admin write audited.
- No impersonation.

---

### Phase 12 — Work Engine and Hangfire Removal

Use Foundation work engine for:

- Email delivery.
- Notification delivery.
- Session cleanup.
- Future scheduled work.

Remove Hangfire from target.

Support:

- In-process worker for pilot cost control.
- Separate `OpHalo.Worker` deployment later.

Exit gate:

- No Hangfire dependency in target.
- Work item retries work.
- Dead-letter works.
- Requeue works.
- Worker diagnostics visible in admin.

---

### Phase 13 — Basic Reports and Insight-Ready Read Models

Add minimal Keep insights/report queries after core events are stable.

Initial reports:

- Request volume.
- Open/closed counts.
- Average first response.
- Overdue first responses.
- Customer activity count.
- Operator activity count.
- No-longer-needed count.

Exit gate:

- Business can see basic activity insights.
- Reports derive from structured product events/timestamps.
- No analytics platform or warehouse introduced.

---

### Phase 14 — Production Readiness and Pilot Cutover

Finalize:

- Production startup guard.
- CORS.
- Cookie domain config.
- Rate limits.
- `/health/ready`.
- Deployment docs.
- Railway/Vercel environment docs.
- Internal support playbook.
- Admin diagnostics checklist.
- Pilot support procedure.

Exit gate:

- Fresh deploy succeeds.
- Ready endpoint passes.
- Auth works.
- Keep loop works.
- Notifications work.
- Admin can diagnose.
- Current deployed behavior parity accepted.

---

## 9.1 Post-Session-8 Pilot Readiness Roadmap

This roadmap reflects the 2026-06-25 pilot readiness discussion. It is provisional sequencing for
the remaining go-live work after Session 8, not a substitute for the per-session ADR/build-log lock.
Before each session starts, hold a short alignment discussion with Christian to confirm scope,
product intent, implementation boundary, and whether the session should split into backend and
client slices.

Sessions 8 and 9 are complete:

- **S8a** — device table and `/me/devices/{appInstallationId}` register/revoke API.
- **S8b** — server-derived personal badge endpoint.
- **S8c** — push abstraction, no-op adapter, payload/display mapping, and candidate/routing
  foundation.
- **S8d** — limited push-worthy mutation hooks.
- **S8e** — ledger and regression gate.

Recommended remaining sessions after Session 9:

1. **Session 9 — Account Classification And Delivery Eligibility** ✓
   Replace `AccountEntitlements.IsPilot` with `Production`/`Pilot`/`Demo`/`InternalTest`
   classification, update `SignupDefaultsSettings`, migrate existing data, and add the delivery
   eligibility gate needed before real APNs/FCM. This also creates the classification foundation
   later reports, billing signals, and demo safety can use.

2. **Session 10 — Brand Guide And UI Foundation**
   Create the V1 product/brand guide before serious PWA/native/customer-page buildout. Lock the
   practical design system inputs: brand voice, customer-facing language rules, typography, color,
   spacing, icons, status/attention visual language, form patterns, empty/loading/error states,
   customer-page trust treatment, and mobile/PWA component conventions. This is not a marketing site
   project; it is the operating guide that keeps the PWA, native app, and customer pages coherent.

3. **Session 11 — Quick Capture And Needs Share Backend Contract**
   Implement the server/API foundation for business-created capture: source/channel enum,
   business-created request command shape, required name/phone/summary/source validation,
   tracker-share state, explicit share-intent mutation/event, and list/detail `Needs Share`
   metadata. Keep this backend contract stable enough for both PWA and native app development.

4. **Session 12 — Account Settings And Onboarding**
   Implement the minimum V1 settings and onboarding completion signals: business display name,
   customer-facing business phone/email, account timezone, intake controls, member-management
   handoff, simple response/status-check policy, and onboarding checklist events. Business
   type/industry presets remain deferred until the separate discussion in `DEF-081`.

5. **Session 13 — PWA App Development: Pilot Workbench**
   Build the actual PWA experience for pilot use. Scope includes Owner/Admin command center,
   onboarding/settings, member management, request list/detail, PWA Quick Capture, tracker sharing
   and `Needs Share`, customer-visible update composer, closeout/feedback review, attention
   explanations, and operator-capable shared-workbench flows where they naturally fit. This is the
   first-class desktop/tablet workbench, not just backend integration.

6. **Session 14 — Public Web Front Door** ✓
   Implemented `ophalo-web` public/auth browser-token surfaces: homepage, About, Pilot, Privacy,
   Terms, `/signin`, `/start`, `/auth/check-email`, `/auth/exchange`, `/auth/exchange/error`,
   `/invite/accept`, `/invite/accept/error`, and `/keep/intake/{token}`.

7. **Session 15 — Pilot Readiness Bug And Gap Closure** ✓
   Closed the active pilot-readiness tracker items from S14/S15, including share-intent/version
   refresh, flattened `ProblemDetails` parsing, Quick Capture navigation, duplicate-email exchange
   copy, post-capture share intent recording, participation controls, production artifact cleanup,
   timezone/polish fixes, and the customer tracker page at `/keep/r/{pageToken}`. `GAP-004`
   browser back/refresh URL routing remains explicitly deferred navigation hardening.

8. **Session 16 — Native Mobile App Foundation**
   Start the native mobile work that has not yet begun. Create the mobile app project, lock the stack
   and build posture, configure bundle identifiers/environments, establish auth/session handling,
   API client conventions, app shell/navigation, secure storage, device registration, badge refresh
   hooks, and deep-link placeholders. This session puts Apple/Google approval work on the critical
   path early instead of waiting until the end of pilot prep.

9. **Session 17 — Native Operator Field App**
   Build the actual phone-first operator workflow: My Work, Available, request detail, native Quick
   Capture, native contact handoff, tracker sharing, customer update action, follow-up/planned-for,
   mark completed, watch/mute, eligible self-assign, badge refresh, refresh/resume behavior, and
   mobile-safe error/empty states. Native may run on tablets, but V1 is optimized for phones;
   tablet/desktop work remains primarily PWA.

10. **Session 18 — Push Delivery And Deep Links**
   After native foundation and field workflows are in place, either implement/test real APNs/FCM
   delivery with Demo/InternalTest suppression and stable deep links, or explicitly clear the pilot
   with the no-op posture and train users that real push is not live.

11. **Session 19 — Store Submission Readiness**
   Prepare Apple/Google approval: app names, icons, screenshots, privacy labels, permission copy,
   signing/profiles, production environment config, demo credentials/account, TestFlight/internal
   testing builds, and review notes. Treat store review lead time as part of the go-live schedule.

12. **Session 20 — Internal Product-Ops And Weekly Value Report**
   Add the founder/internal-only weekly value report endpoint/read service that generates
   copy-pasteable Markdown/text for a given account and reporting period. Existing product-ops
   onboarding events can be reused where appropriate. Do not build Owner/Admin in-app report UI or
   automated email in the first slice.

13. **Session 21 — Pilot Support And Read-Only Founder Support**
   Build authenticated Report Friction, Pilot Updates/Help, and bounded read-only founder/support
   visibility as needed for pilot operations. No anonymous customer OpHalo feedback hook and no
   production impersonation/run-as.

14. **Session 22 — Day-Zero Settings Redesign, Intake Sharing, And Service Location** ✓
   ADR-428: Keep launches in a functional day-zero state. Settings split into Public Link & Profile,
   Response Policy, and Team. Slug-based public intake routing (ADR-429), intake urgency and contact
   preference metadata (ADR-430), business-first public identity (ADR-431), platform email scope
   (ADR-432). Service location exposed on operator detail/list and mobile request detail. Open in
   Maps added to mobile. Build log: `docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`.

15. **Session 23 — Pilot QA And Go-Live Gate**
   Run the end-to-end pilot readiness checklist: onboarding, Quick Capture, public intake, tracker
   sharing, customer page/actions, attention/follow-up/status-check behavior, close/cancel, feedback
   review, Spam/Test, weekly report generation, internal ops signals, notification posture, mobile
   app/store readiness, support runbooks, deployment topology, and known limitations.

Pre-session alignment checklist:

- Confirm the user/persona promise and exact V1 scope.
- Confirm backend/API, PWA, native, docs, and runbook boundaries.
- Confirm what is deliberately deferred and where it is tracked.
- Confirm the file-level gate and whether the session must split.
- Confirm tests/QA evidence expected before the session can be marked complete.

---

## 10. Phase Sizing and Risk Notes

This build is significant. The plan should not hide that.

Rough sizing:

- Phase 0–2: low risk, documentation and skeleton cleanup.
- Phase 3–6: medium risk, foundation/auth/persistence consolidation.
- Phase 7–10: high risk, Keep behavior and host/frontend cutover.
- Phase 11–14: medium risk, admin/work/reports/production hardening.

Highest-risk areas:

- Auth/session behavior regression.
- Public token/customer link regression.
- Request timeline/attention/first-response behavior regression.
- SSE cutover.
- Frontend environment-variable cutover.
- Notification routing complexity.
- Admin write safety.

Risk controls:

- Behavior parity tests.
- Small phases.
- Architecture tests before movement.
- Current deployed system remains fallback until parity.
- Preserve proven code unless foundation direction requires change.
- Each redesign must be documented with reason.

---

## 11. Immediate Next Step

Start with Phase 0.

Create the ADR and decision-index entry before implementation.

Then Phase 1 begins with skeleton and architecture tests, not product behavior movement.

Recommended first coding instruction:

```text
Do not port business behavior yet.
Create the target solution skeleton and real architecture test project.
Add architecture rules for Foundation, Keep, SharedKernel, API, and Signal exclusion.
Make the empty/near-empty solution build and tests pass.
Stop after the skeleton and tests are green.
```

---

## 12. Final Build Principle

```text
Do not drag the old architecture forward unchanged.
Do not rewrite working product behavior for cleanliness.
Move, rename, adapt, and verify.
Redesign only when the existing implementation blocks the foundation.
```

That is the operating principle for the OpHalo Foundation build.
