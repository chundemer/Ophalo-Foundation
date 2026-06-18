# ADR-236 — Mobile Native App Technology Stack

**Date:** 2026-06-18  
**Status:** Proposed  
**Source:** Product/mobile planning discussion, Phase 5A mobile-ready sessions, Phase 8/9 notification posture

## Context

OpHalo Keep is expected to have three main client surfaces:

- customer public intake and customer request pages;
- web app for Owners/Admins on laptop and iPad;
- native mobile app for operators and mobile admins in the field.

The mobile app's first job is not to duplicate the full web admin console. It should be the field
reaction app: reliable push alerts, request triage, open request detail, claim/assignment where
allowed, call customer, log external contact, post customer update, mark completed/resolved, and
watch/mute.

The product promise makes mobile push strategically important:

```text
Customer submits request -> business is alerted immediately -> someone owns/responds ->
customer has a personal page and does not feel forgotten.
```

PWA/web push is not reliable or consistent enough across all target environments to carry that
promise by itself. Native mobile push via APNs/FCM belongs in the Phase 9 notification/device work.

Existing backend decisions already prepare for mobile:

- one opaque session model for browser and mobile;
- bearer token first for mobile clients;
- `SessionClientType.MobileApp`;
- optional device name and last-seen fields;
- notification routing state is prepared in Keep but actual delivery, device tokens, retries, quiet
  hours, badge counts, and outbox/work-engine integration are deferred to Phase 9.

## Decision

Use **React Native + Expo + TypeScript** for the first native mobile app.

Use Expo for the application framework, routing/build ergonomics, and pilot velocity, but do not
make Expo-hosted push delivery a permanent architectural dependency.

The intended notification posture is:

```text
React Native + Expo app
native device push token captured by the app
OpHalo backend stores NotificationDestination/device-token records
OpHalo backend sends directly to APNs for iOS and FCM for Android
```

Expo Push Service may be used only as a temporary pilot shortcut if a later implementation decision
explicitly accepts the extra vendor hop. The durable architecture should support direct APNs/FCM from
the start.

## Recommended Client Stack

- React Native
- Expo
- TypeScript
- Expo Router
- EAS Build/Submit while useful
- TanStack Query for API/server state
- Small local state store such as Zustand only where needed
- Secure device storage for opaque session tokens
- Native notification registration through `expo-notifications` or React Native Firebase, selected
  during Phase 9 implementation after a small spike

## Recommended Backend Notification Shape

Phase 9 should add a durable device/destination model, likely named `NotificationDestination` or
similar, with fields such as:

- account id;
- account user id;
- platform: iOS, Android, Web if needed later;
- native device push token;
- app installation/session association if useful;
- enabled/revoked timestamps;
- last success/failure timestamps;
- last failure reason;
- badge-count support fields if needed.

Push payloads should be minimal:

```text
requestId
notificationType
accountId, if needed for client routing
```

Do not include customer message bodies, phone numbers, internal notes, feedback comments, or other
sensitive detail in push payloads. The app should open from the notification and refresh trusted
state from the API.

## Rationale

React Native fits OpHalo's product shape:

- The mobile app is workflow-heavy, not a highly custom game-like UI.
- The future web/admin app is already planned around `ophalo-app` / `ophalo-web`; React Native keeps
  the client language and mental model close to the web surface.
- TypeScript can be shared across web and mobile for generated API clients, DTO types, validation
  helpers, and UI/domain vocabulary.
- Expo gives faster pilot iteration, simpler builds, app-store submission help, routing, and access
  to native modules without starting with raw Xcode/Gradle complexity.

Direct APNs/FCM delivery fits Keep's reliability and cost posture:

- Push notifications are part of the core product promise, not optional polish.
- Direct delivery avoids a later migration away from Expo Push Service if scale, privacy, or
  reliability requirements tighten.
- The backend can own delivery attempts, retries, token invalidation, badge counts, quiet hours,
  actor exclusion, mute/watch suppression, and notification audit.
- Native push has no Twilio-style per-message business cost, though it has engineering and platform
  account/setup cost.

## Alternatives Considered

### Flutter

Flutter is technically strong and supports iOS/Android well. It is not the preferred first choice
because it introduces Dart and a separate client ecosystem alongside the C# backend and likely
React/Next web app. It would be reconsidered if the team had stronger Flutter experience or wanted
the mobile app to evolve separately from the web product.

### Pure Native Swift + Kotlin

Pure native maximizes platform control, but doubles client implementation and maintenance work. It
is too much surface area for the first field companion app unless background/device integration later
proves unusually complex.

### PWA First

A responsive web/PWA surface is still useful for the web/admin app and for broad accessibility, but
PWA notifications and install behavior are not consistent enough to carry the urgent request-alert
promise for field operators.

### Expo Push Service As Permanent Transport

Expo Push Service is convenient and currently has no per-notification sending cost, but it adds a
vendor hop between OpHalo and Apple/Google. Because notification reliability is central to Keep, the
durable design should use direct APNs/FCM. Expo's app framework can still be used without committing
to Expo-hosted push transport.

## Open Questions Before Implementation

- Use `expo-notifications` native token capture or React Native Firebase Messaging for the first
  direct APNs/FCM implementation?
- Do we need separate app flavors for pilot/staging/production before the first TestFlight build?
- Should the app be operator-only at first, or allow Owner/Admin fast triage too?
- What badge-count semantics are most useful: total actionable requests, assigned-to-me count, or
  urgent/overdue count?
- Which notification types are in the first mobile slice: new request, assigned to me, customer
  replied, attention overdue, unresolved feedback, or all of these?
- Should notification delivery be backed by the Phase 4 work-engine/outbox model immediately, or a
  narrower Phase 9 delivery table/job first?

## Implementation Timing

Do not build the full native app in the current foundation/Phase 8 work.

Recommended timing:

1. Finish B5 and post-B5 command-center/list/navigation basics.
2. Build the minimal web Owner/Admin control center.
3. During Phase 9, implement notification/device-token foundation.
4. Build the narrow native operator/admin companion alongside direct APNs/FCM push delivery.

