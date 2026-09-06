# BL147 — GAP-051 public-web configured business-phone display

**Status:** Complete. One commit. No behavior change to stored/canonical values, API round-trips,
or `tel:`/`sms:` targets — display formatting only.

**Authority:** ADR-444 (normalized ten-digit North American phone), pilot-readiness-bug-tracker
GAP-051. `PhoneDisplayFormatter` (Keep.Core) is the established display-only boundary; the
SMS-handoff message builder already uses it.

## Finding

The PWA Settings → Company screen stores the configured business phone canonical (bare ten
digits, via `normalizeNaPhoneInput`). Every public web surface (`ophalo-web`) rendered that value
verbatim — `Call 5550100199` instead of `Call (555) 010-0199` — because the two public identity
projections returned `KeepBusinessProfile.CustomerFacingPhone` untouched:

- `KeepIntakePersistence.GetPublicIdentityForAccountAsync` → intake info endpoint
  (`GET /keep/public-intake/{token|slug}/info`)
- `EfKeepRequestDetailPersistence.GetRequestByPageTokenAsync` → customer tracker
  (`GET /keep/r/{pageToken}`, active + expired)

Public intake *input* (`IntakeForm.formatPhoneAsYouType`) already tolerated `1`/`+1` and formatted
to ten digits — no change needed there. Both projection points are public-customer-only; neither
feeds an authenticated staff surface.

## Change

Display formatting is applied server-side at the Application-layer projection boundary, keeping a
single implementation (`ophalo-web` has no test runner) and leaving the stored value and API
semantics for canonical use intact. `ophalo-web` treats `phone` as an opaque display string and
strips non-digits when building `tel:`, so no frontend change was required.

- `src/OpHalo.Keep.Core/Domain/PhoneDisplayFormatter.cs` — added
  `FormatConfigured(string?) → string?`: null/blank → `null`; otherwise `Format` (canonical →
  `(XXX) XXX-XXXX`; a non-standard configured value — extension, partial, international — is
  returned trimmed but otherwise untouched).
- `src/OpHalo.Keep.Application/Requests/KeepCustomerPageMapper.cs` — `BuildActiveResult` and
  `BuildExpiredResult` now map `Phone` through `PhoneDisplayFormatter.FormatConfigured`.
- `src/OpHalo.Keep.Application/PublicIntake/CreateKeepPublicIntakeService.cs` —
  `GetInfoByTokenAsync` / `GetInfoBySlugAsync` format `Phone` on the returned
  `KeepPublicIntakeInfo` via a private `WithDisplayPhone` helper.

No Infrastructure change, no migration, no DTO shape change (`phone` stays `string?`).

### Behavior change to note

The `phone` JSON value on `GET /keep/public-intake/{token|slug}/info` and `GET /keep/r/{pageToken}`
changes from raw-stored to display-formatted for canonical numbers. No consumer relies on the
canonical form on these endpoints (verified: display + `tel:`-with-strip only).

## Tests

- `tests/OpHalo.UnitTests/Keep/PhoneDisplayFormatterTests.cs` — **new** (`PhoneDisplayFormatter`
  had no prior coverage): `Format` canonical/`+1`/leading-`1` cases, non-canonical passthrough,
  `FormatConfigured` null/blank → null and canonical/non-canonical.
- `tests/OpHalo.IntegrationTests/Api/KeepCustomerPageTests.cs` — `SeedBusinessIdentityAsync`
  parameterized and defaulted to a canonical ten-digit number; **active** and **expired** tracker
  assertions now expect `(555) 010-0199`; added a non-canonical (`+61 …`) passthrough test.
- `tests/OpHalo.IntegrationTests/Api/KeepIntakeApiTests.cs` — **intake-token** info test updated
  to a canonical stored number expecting `(555) 010-0199`; added an **intake-slug** info test
  asserting the same formatting.

## Verification

`git diff --check` clean. Focused: `PhoneDisplayFormatterTests` + `KeepCustomerPageMapperTests`
40/40; `KeepCustomerPageTests` + `KeepIntakeApiTests` 58/58; `KeepPublicIntakeServiceTests` +
`KeepIntakeSmsHandoff*` 16/16 (integration) and 67/67 (unit); architecture 14/14.

## Tracker

GAP-051 public-web scope complete. Remaining native-parity scope deferred to Session 14
(ADR-236). Phone-and-capture-integrity sequence advances to GAP-025.
