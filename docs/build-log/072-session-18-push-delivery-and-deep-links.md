# Build Log 072 — Session 18: Push Delivery And Deep Links

**Started:** 2026-07-06
**Status:** Draft — decisions locked; pre-work/coding-session breakdown pending
**Session name:** S18 push delivery and deep links
**Next free ADR before this log:** ADR-407
**Next free ADR after this log:** ADR-418

---

## Purpose

Session 18 is the push-notification gate between the review-safe native product foundation and store
submission readiness.

S18 does not treat "notifications" as a generic platform project. It decides and prepares the narrow
V1 staff push path:

- real APNs/FCM delivery for store-bound builds, if credentials and device proof clear;
- or an explicit no-push pilot posture, with no accidental product promise that push is live.

The goal is a strong foundation with or without real push:

- mobile remains useful through My Work, Available, badge refresh, active polling, pull-to-refresh,
  and foreground/resume invalidation;
- push, when enabled, is a timely off-screen alert channel only;
- business mutations never depend on push delivery succeeding;
- future outbox/event-worker architecture can reuse the same routing/orchestration contract.

S18 follows the earlier notification foundation:

- Session 8 built device registration, badge count, candidate routing, suppression, and no-op adapter
  foundation.
- Session 9 added account-classification delivery eligibility so Demo/InternalTest accounts are
  suppressed.
- Session 16 registered nullable-token mobile devices and prepared the same row for later token
  upsert.
- Session 17 deferred notification permission, real token capture, and push delivery to this session.

---

## Locked Decisions

### ADR-407 — Push is the V1 off-screen delivery channel; events/pub-sub are not a substitute

Use native push notifications through APNs/FCM as the V1 mechanism for waking staff off-screen.
Events, pub-sub, and outbox workers are internal architecture tools; they do not by themselves wake
an iOS or Android device.

Do not replace push with SSE, WebSockets, generic pub-sub, or a backend event bus for the field
operator alert promise. Keep the existing "fresh, not realtime" posture for foreground app freshness:
server-derived badge count, list refetching, polling, pull-to-refresh, and AppState resume sync.

Reason: the mobile app must remain functional without push, but off-screen field alerts require OS
notification delivery. Internal events may be introduced later for reliability and multiple
consumers, not as a user-facing delivery channel.

### ADR-408 — S18 is a hard gate: Real APNs/FCM or explicit no-push posture

Before pilot/store-bound submission, S18 must end in one of two explicit postures:

1. **Real Push:** direct APNs/FCM delivery is implemented, configured, and tested end to end on real
   devices with native token capture, Demo/InternalTest suppression, payload minimization, badge
   behavior, and deep links verified.
2. **No Push:** the no-op/test adapter remains active, real push is not promised in product copy or
   onboarding, and operator/founder runbooks train users that push is not live yet.

Do not allow an ambiguous middle state where code paths exist but the product, runbooks, or store
readiness assume reliable push without proof.

Recommended execution posture: attempt the direct APNs/FCM path first with a tight pre-work/timebox.
If Apple/Firebase credentials, native builds, or platform delivery proof block the session, close the
gate deliberately under the no-push posture and carry real delivery forward.

### ADR-409 — Durable delivery uses direct APNs/FCM, not Expo Push Service

The durable V1 architecture is:

```text
React Native + Expo app
native APNs/FCM token captured by mobile
OpHalo backend stores AccountUserDevice token state
OpHalo backend sends directly to APNs/FCM through IPushAdapter
```

Expo remains the app framework and build/runtime toolchain. Expo Push Service is not the permanent
transport. It may be considered only as an explicitly documented temporary shortcut in a later
decision if direct delivery is blocked and the extra vendor hop is accepted.

Reason: push is part of the field workflow promise. Direct APNs/FCM keeps token invalidation, badge
counts, actor exclusion, mute/watch suppression, account classification, and delivery posture under
OpHalo control.

### ADR-410 — S18 uses a centralized notification orchestrator

Do not scatter push-dispatch logic across mutation handlers. After a successful database commit, a
mutation path may emit a narrow `NotificationIntent` into a centralized orchestrator.

The orchestrator owns:

- mapping an intent to notification type and deep-link target;
- loading candidate recipients/devices;
- actor exclusion;
- role, participation, mute, OffSeason, stale/ineligible participant, and account-classification
  suppression;
- badge hint calculation where needed;
- adapter-mode dispatch through no-op/test/real providers;
- structured result logging without sensitive payloads or raw tokens.

The mutation handler may know that a notification-worthy thing happened. It must not know who
receives it, which devices are eligible, how payloads are shaped, or whether the active provider is
NoOp, Test, or Real.

Reason: this keeps V1 direct and visible while giving V2 an easy migration path to a durable outbox
or event worker that calls the same orchestration contract.

### ADR-411 — Business mutations never depend on push delivery

Push delivery is post-commit and fail-soft. Request/customer/team mutations must not fail, roll back,
or block because APNs/FCM is unavailable, slow, throttled, or rejects a token.

S18 must keep provider calls outside the domain model and outside transaction success. Provider
errors are observed, classified, logged, and used to update device state when appropriate. The app
recovers through normal badge/list/detail refresh.

Reason: notification delivery is valuable but not authoritative request state. The source of truth
is the API and persisted Keep workflow state.

### ADR-412 — Push provider mode is explicit environment configuration

Add or preserve an explicit provider mode:

```text
NoOp
Test
Real
```

`NoOp` records structural intent/results only and sends nothing. `Test` is allowed for local or
staging proof where platform credentials or sandbox devices are intentionally limited. `Real` sends
through APNs/FCM and must be unavailable unless required credential/config checks pass.

Demo/InternalTest delivery suppression remains mandatory in every mode, including Real. Production
and Pilot accounts are delivery-eligible; Demo and InternalTest are suppressed by default.

### ADR-413 — Push payloads are minimal and non-sensitive

Push payloads may contain only structural routing data:

- `requestId`;
- `notificationType`;
- `accountId` only if needed for safe client routing;
- deep-link path or equivalent route target;
- badge hint where the platform supports it.

Do not include customer message bodies, phone numbers, email addresses, internal notes, feedback
comments, summaries, business-sensitive text, or any data the locked screen should not expose. The
mobile app opens from the notification and refreshes trusted state from the API.

Notification copy should be generic enough for lock-screen exposure and store/privacy review.

### ADR-414 — Invalid native tokens are revoked from platform terminal errors

The real APNs/FCM adapter must classify terminal token failures and disable/revoke affected device
records.

Examples:

- APNs terminal/unregistered responses such as `410 Unregistered`;
- FCM terminal token responses such as `UNREGISTERED`, invalid registration/token, or equivalent
  permanent failure codes.

Do not log raw push tokens. Logs may include device id, account/user ids where appropriate, token
fingerprint/last-four if already part of the existing token-safe pattern, provider, status category,
and failure reason code.

Transient provider/network failures must not revoke devices.

### ADR-415 — Deep links for push use the existing review-safe route posture

S18 push links use the S16/S17 custom-scheme bridge and remain Universal/App Link-compatible for
S19.

Stable targets:

- request detail: `ophalo://keep/requests/{requestId}` or the existing lowercase native route
  equivalent;
- My Work;
- Owner/Admin feedback review only if the built surface and route are verified;
- Available later only if push-worthy routing need is proven.

All deep-linkable authenticated routes must guard auth before rendering or fetching. If the app is
not signed in, the notification opens into the normal sign-in/re-auth flow rather than leaking state.

### ADR-416 — S18 notification permission is explicit and review-safe

Mobile must request notification permission only at an intentional moment connected to the staff
workflow, not as an unexplained first-launch prompt.

Allowed V1 posture:

- explain push as staff request alerts / work updates;
- request permission after sign-in when the user reaches the authenticated workflow or Account
  utility surface;
- allow denial without breaking the app;
- continue registering the installation/session without a token where permission is denied or token
  capture fails.

S18 must not request unrelated protected permissions. No Contacts, Location, SMS, Call Log, Camera,
Microphone, Tracking, or background location permission is introduced by push.

### ADR-417 — Full notification platform remains deferred

S18 does not build the full notification platform:

- no custom preference matrix;
- no quiet-hours/business-hours delivery scheduler;
- no durable notification ledger/outbox/dead-letter table;
- no delivery/open analytics dashboard;
- no email/SMS channel matrix;
- no customer-facing automated SMS/email notifications;
- no delegated notification administration.

These remain deferred until real pilot volume and operational needs justify the extra machinery.
S18 may shape interfaces so an outbox/event-worker can be added later without rewriting routing
policy or mutation workflows.

---

## Draft Pre-Work Pass

This build doc is intentionally draft until the decision/pre-work pass splits S18 into coding
sessions.

Required pre-work before coding slices:

1. Confirm current backend notification types, `IPushAdapter` shape, no-op adapter behavior, device
   model, account-classification suppression, and post-commit hooks.
2. Confirm current mobile device upsert contract, `appInstallationId`, nullable `pushToken`, badge
   hook, deep-link parsing, authenticated route guards, and AppState refresh behavior.
3. Choose the native token-capture approach:
   - `expo-notifications` native device token capture; or
   - React Native Firebase Messaging if Expo token capture cannot meet direct APNs/FCM needs.
4. Choose the backend provider implementation:
   - direct APNs HTTP/2/JWT provider;
   - FCM HTTP v1 provider through focused Google auth/client code;
   - narrow, well-supported libraries only if they reduce risk without creating a broker dependency.
5. Inventory credentials and environments:
   - Apple Developer team, bundle id, APNs key/cert posture, key id/team id;
   - Firebase project, Android package id, service account, sender/project ids;
   - secure config/secrets locations for local, staging, and production;
   - S19 EAS Build implications.
6. Define a real-device proof plan for iOS and Android.
7. Define the fallback no-push runbook language if Real cannot be cleared.

---

## Draft Coding-Session Shape

This is not coding-ready yet. Expected slices after pre-work:

### S18a — Backend notification preflight and orchestrator contract

Goal: confirm current foundation, add/adjust centralized `NotificationOrchestrator` and
`NotificationIntent` contract if the current implementation is too adapter-centric or scattered.

Likely gates:
- no domain-event bus;
- no outbox tables;
- mutation handlers emit intent only after commit;
- candidate/suppression logic remains centralized and tested.

### S18b — Mobile notification permission and native token capture

Goal: add intentional permission request, native token capture, and device upsert using the existing
`appInstallationId` row.

Likely gates:
- permission denial keeps app functional;
- token capture sends raw native APNs/FCM token, not Expo push token, unless an explicit temporary
  Expo Push Service decision is made;
- no unrelated permissions appear.

### S18c — Provider mode, config validation, and test/no-op hardening

Goal: make `NoOp`, `Test`, and `Real` modes explicit; ensure Demo/InternalTest suppression and
token-safe logging are proven before real provider code sends anything.

Likely gates:
- Real fails closed when credentials are absent/invalid;
- logs omit raw tokens and sensitive payloads;
- account-classification suppression tests cover Real-mode candidate filtering.

### S18d — Direct APNs/FCM adapters

Goal: implement real provider(s), classify terminal vs transient failures, and revoke invalid
devices.

Likely gates:
- originating mutations do not fail on provider errors;
- terminal token failures disable only the affected device;
- transient failures do not revoke devices.

### S18e — Deep-link and badge proof

Goal: verify push opens into the intended authenticated native routes and badge/list/detail refresh
returns trusted API state.

Likely gates:
- request detail opens safely when signed in;
- signed-out notification path redirects to sign-in/re-auth without leaking request content;
- badge hint never becomes the source of truth.

### S18f — Gate close: Real or No-Push posture

Goal: record final S18 outcome before S19.

Real close requires:
- iOS and Android real-device proof, or explicit platform-specific limitation documented;
- Demo/InternalTest suppression verified;
- payload/deep-link/badge behavior verified;
- runbooks/config notes updated.

No-Push close requires:
- no-op/test adapter confirmed active;
- product/onboarding/runbook copy says push is not live;
- mobile freshness fallback verified;
- real provider work carried forward.

---

## Hard Boundaries

- No customer SMS/email sending.
- No full notification preference matrix.
- No quiet-hours scheduler.
- No notification outbox/ledger unless a pre-work finding proves real delivery cannot be safe without
  a minimal durability bridge.
- No Expo Push Service as permanent transport.
- No sensitive data in push payloads or logs.
- No push delivery to Demo/InternalTest accounts.
- No business mutation may depend on push success.

---

## Open Pre-Work Questions

- Which library/path best captures raw native APNs/FCM tokens in the current Expo managed setup?
- Do iOS and Android both need to be Real for first pilot, or can S18 explicitly clear one platform
  first while the other remains no-push?
- Which exact mutation paths are push-worthy for V1: new request, assigned to me, customer message,
  customer action/update request, unresolved feedback review, or a smaller first set?
- Should Owner/Admin feedback-review pushes be included in S18 or deferred until mobile has a better
  review queue route?
- Where will production/staging APNs and Firebase credentials live, and who rotates them?
- What exact no-push operator training copy is acceptable if the fallback gate is chosen?
