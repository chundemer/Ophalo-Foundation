# ADR-236 — Mobile Native App Technology Stack

**Date:** 2026-06-18  
**Status:** Locked (promoted in S16a / ADR-385)
**Source:** Product/mobile planning discussion, Phase 5A mobile-ready sessions, Phase 8 notification posture, Keep V1 lock (ADR-288..292)

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
promise by itself. Basic native mobile push via APNs/FCM belongs in the V1 pre-go-live
notification/device foundation. The heavier notification platform remains later.

Existing backend decisions already prepare for mobile:

- one opaque session model for browser and mobile;
- bearer token first for mobile clients;
- `SessionClientType.MobileApp`;
- optional device name and last-seen fields;
- notification routing state is prepared in Keep; V1 should add basic device-token registration,
  minimal push/deep links, and server-derived badge counts;
- retries, quiet hours, custom preferences, notification rows/outbox/dead-letter, delivery
  analytics, and work-engine integration remain deferred to the fuller notification platform.

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
  during the V1 notification/device implementation after a small spike

## Recommended Backend Notification Shape

The V1 notification/device slice should add a small durable device model, likely named
`AccountUserDevice`, `NotificationDestination`, or similar, with fields such as:

- account id;
- account user id;
- platform: iOS, Android, Web if needed later;
- native device push token;
- app installation/session association if useful;
- enabled/revoked timestamps;
- last success/failure timestamps;
- last failure reason;
- badge-count support fields only if derived counts need cached platform handoff state.

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
- The backend can own token invalidation, badge counts, actor exclusion, mute/watch suppression, and
  notification routing. Retries, quiet hours, delivery analytics, and notification audit can deepen
  later.
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
- What V1 badge-count semantics are most useful: total actionable requests, assigned-to-me count,
  urgent/overdue count, or view-count backed badges?
- Which notification types are in the first V1 mobile slice: new request, assigned to me, customer
  replied, attention overdue, unresolved feedback, or a narrower launch set?
- Should V1 use a simple fail-soft dispatcher first, with the Phase 4 work-engine/outbox model
  deferred until notification volume/requirements justify it?

## Implementation Timing

Do not build the full native app in the current foundation/Phase 8 work.

Recommended timing:

1. Finish B5 and post-B5 command-center/list/navigation basics.
2. Build the minimal web Owner/Admin control center.
3. Before go-live, implement the V1 notification/device foundation: device table, derived badges,
   minimal push/deep links, and fail-soft delivery.
4. Build the narrow native operator/admin companion alongside direct APNs/FCM push delivery.
