# Build Log 002 — Phase 3: SharedKernel & Duplicate Abstraction Cleanup

**Date:** 2026-06-14
**Phase:** 3 — Foundation SharedKernel and Duplicate Abstraction Cleanup (build plan §9)
**Reference repo:** `/Users/christian/application/ophalo` (read-only; aliased at `_reference/`)
**Target repo:** `/Users/christian/saas/ophalo-foundation`

---

## Purpose

Establish a disciplined `OpHalo.SharedKernel` and collapse the duplicate
cross-cutting abstractions found in the reference repo into one canonical
definition each, placed at the correct layer.

---

## What the reference repo had (the debt)

| Abstraction | Duplicate locations in reference repo |
|-------------|----------------------------------------|
| `IClock` | `OpHalo.Shared/Abstractions/IClock.cs` **and** `OpHalo.Application/Abstractions/Infrastructure/IClock.cs` |
| `ICurrentUser` | `OpHalo.Shared/Abstractions/ICurrentUser.cs` **and** `OpHalo.Application/Abstractions/Security/ICurrentUser.cs` (the latter *inherits* the former — a layered duplicate) |
| `IEmailSender` | `OpHalo.Shared/Abstractions/IEmailSender.cs` **and** `OpHalo.Application/Abstractions/Infrastructure/IEmailSender.cs` |

The reference also had proven `Result` / `Error` primitives in
`OpHalo.Shared/Results/` (the latter carried a dead commented-out alternative).

---

## Decisions (placement)

Per build plan §3.3 (SharedKernel **must not** contain CurrentUser or email
sending) and §8 (SharedKernel holds no business concepts):

| Type | Canonical home | Reason |
|------|----------------|--------|
| `Result`, `Result<T>` | `OpHalo.SharedKernel.Results` | Generic, non-business outcome primitive. |
| `Error` | `OpHalo.SharedKernel.Results` | Generic structured error primitive. |
| `IClock` | `OpHalo.SharedKernel.Abstractions` | Time is a generic, non-business cross-cutting concern. |
| `ICurrentUser` | `OpHalo.Foundation.Application.Abstractions.Security` | Identity is a Foundation concern; forbidden in SharedKernel. |
| `IEmailSender` | `OpHalo.Foundation.Application.Abstractions.Messaging` | Email sending is a Foundation concern; forbidden in SharedKernel. |

`ICurrentUser` adopts the richer reference contract (`UserId`, `AccountId`,
`IsAuthenticated`, `IsVerified`) as a single non-inherited interface, eliminating
the cross-layer chain.

---

## Changes

**Preserved (ported verbatim where behavior was already correct):**
- `Result` / `Result<T>` — success/failure invariants, `Map`, `Bind`, `Match`, `TryGetValue`.
- `Error` — `Create` guards, `None`, `Is`, value equality (dead commented block dropped).
- `IClock`, `ICurrentUser`, `IEmailSender` contracts.

**Moved / renamed:**
- Namespaces re-homed from `OpHalo.Shared.*` / `OpHalo.Application.*` to
  `OpHalo.SharedKernel.*` and `OpHalo.Foundation.Application.*`.

**Redesigned:**
- Three duplicated abstractions collapsed to one canonical definition each.
- `IEmailSender` now depends on `OpHalo.SharedKernel.Results.Result`;
  `OpHalo.Foundation.Application` gained an explicit `SharedKernel` reference.

---

## Tests

- **Unit** (`OpHalo.UnitTests/SharedKernel`): 18 tests for `Result`/`Error`
  behavior and invariants.
- **Architecture** (added in this phase): a 7-case theory
  `SharedKernel_must_not_contain_business_typed_names` rejecting type names
  matching `CurrentUser`, `EmailSender`, `DbContext`, `Account`, `Notification`,
  `Entitlement`, `Keep`, complementing the existing dependency-direction rule.

Full suite: **33 passing**, build 0 warnings / 0 errors.

---

## Phase 3 exit gate (§9)

- ✅ One canonical abstraction for each cross-cutting concern (`IClock`,
  `ICurrentUser`, `IEmailSender`).
- ✅ Architecture tests prevent SharedKernel becoming a dump (dependency rule +
  name rule).
- ✅ Build + tests green.

---

## Phase-end summary

- **Preserved:** Result/Error/IClock/ICurrentUser/IEmailSender behavior.
- **Moved:** into SharedKernel and Foundation.Application with clean namespaces.
- **Renamed:** `OpHalo.Shared` → `OpHalo.SharedKernel`; abstraction namespaces re-homed.
- **Adapted:** Foundation.Application references SharedKernel explicitly.
- **Redesigned:** three duplicate abstractions collapsed to one each; the
  `ICurrentUser` cross-layer inheritance chain removed.
- **Why:** duplicate abstractions are §3.4 redesign triggers; placement enforces §3.3/§8.
- **Tests proving it:** 18 SharedKernel unit tests + the SharedKernel discipline arch theory.
- **Foundation rule now passing:** §8 "SharedKernel must not contain business concepts."
- **Risks remaining:** concrete implementations (`SystemClock`, the email provider,
  the HttpContext-backed `ICurrentUser`) are not yet ported — they arrive with the
  auth/persistence phases (5–6). Helpers in the reference `OpHalo.Shared`
  (EmailNormalizer, PhoneNormalizer, token/slug generators, AuthErrors) are
  intentionally deferred to the phases that need them.
