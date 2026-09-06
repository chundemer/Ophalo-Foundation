# BL146 — GAP-016 / GAP-021 phone validation closure

**Status:** Complete. Truthfulness/closure cleanup only — no behavior change. One commit.

**Authority:** ADR-444 (launch phone validation is normalized 10-digit North American),
ADR-236 (native mobile app deferred to Session 14), pilot-readiness-bug-tracker GAP-016 / GAP-021.

## Finding

Discovery confirmed the ADR-444 normalized ten-digit North American path is already implemented
across every client path that exists in this repo:

- **Backend.** `PhoneNormalizer` (strip non-ASCII digits, drop a leading `1` on 11-digit input,
  canonical = exactly 10; `IsValidLength` == 10) drives `KeepCustomer.Create` and
  `LookupKeepRequestByPhoneService`. `KeepRequestErrors` and the lookup service already report
  "exactly 10 digits".
- **Web Quick Capture (`ophalo-app`).** `normalizeNaPhoneInput` (`quick-capture/utils.ts`) drops a
  leading `1` and caps at ten digits before the UI gate, lookup, and return-to-draft path;
  `LookupGate` and `HandoffPanel` gate lookup/submit on ten digits and show "Please enter a
  10-digit number"; `CaptureForm` provides the draft-preserving **Change** action that re-runs
  duplicate lookup. Covered by `phoneFormat`, `LookupGate`, `HandoffPanel`, and
  `draft-preservation` tests.
- **Public intake (`ophalo-web`).** `IntakeForm` slices a leading `1` from 11-digit input.
- **Native.** No `ophalo-native` project exists yet; native parity is deferred to Session 14
  (ADR-236). This is GAP-051's remaining scope, not GAP-016's.

The only remaining discrepancy was stale text that still described a "7–15 digit" / E.164 bound
the shared validator no longer enforces.

## Change

- `src/OpHalo.Keep.Core/Entities/KeepCustomer.cs` — `Create` exception message
  `"Phone must contain 7–15 digits after normalization…"` → `"…exactly 10 digits after
  normalization; got {n}."`
- `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepCustomerConfiguration.cs` —
  comment only: the canonical form is exactly 10 digits (ADR-444); the `HasMaxLength(15)` column
  bound is retained headroom for a future international slice and shrinking it would require a
  migration.
- `tests/OpHalo.UnitTests/Keep/KeepCustomerTests.cs` — renamed
  `Create_throws_when_phone_has_too_many_digits` →
  `Create_throws_when_phone_is_not_ten_digits_after_normalization`; corrected the stale
  "15-digit maximum" comment; added `Assert.Contains("exactly 10 digits", ex.Message)` to the
  too-few-digits theory.

Not touched: the `isPhoneShaped` (7–15) detection heuristic — it gates "does this text look like
a phone at all", not validity. No migration (column length unchanged).

## Verification

`git diff --check` clean. `dotnet test tests/OpHalo.UnitTests` filtered to `KeepCustomerTests` —
24/24 passed.

## Tracker

GAP-016 → Resolved. GAP-021 → Resolved. Phone-and-capture-integrity sequence advances to GAP-051
(native parity / public-web audit), then GAP-025.
