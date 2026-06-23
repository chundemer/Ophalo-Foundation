# Build Log 059 — G8b: Token-Safe Application Logging Proof

**Gap:** GAP-013
**Status:** Complete
**Baseline entering:** 1243 tests (659 unit · 14 arch · 570 integration)

---

## What Was Done

### Problem

Public bearer tokens (`publicIntakeToken`, `pageToken`) appear as raw route segments in URLs.
ASP.NET Core `Microsoft.AspNetCore.Hosting.Diagnostics` can log request start/finish paths at
Information level. If that category were ever enabled, raw tokens would appear in application logs.

### Changes

**`src/OpHalo.Api/Helpers/PublicTokenPathRedactor.cs`** — new pure static helper.

Maps known public bearer-token path prefixes to redacted shapes:
- `/keep/public-intake/token/{token}` → `/keep/public-intake/token/[redacted]`
- `/continuity/public-intake/token/{token}` → `/continuity/public-intake/token/[redacted]`
- `/keep/r/{token}` → `/keep/r/[redacted]`
- `/keep/r/{token}/{action}` → `/keep/r/[redacted]/{action}`
- All other paths returned unchanged.

**`src/OpHalo.Api/Program.cs`** — one added line:

```csharp
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
```

`appsettings.json` already suppresses `Microsoft.AspNetCore: Warning` (which covers this category),
but the code-level filter makes the intent explicit and durable against config-only changes.
No `UseHttpLogging`, Serilog, or other logging infrastructure was added.

**`tests/OpHalo.UnitTests/PublicTokenPathRedactorTests.cs`** — 18 unit tests.

Covers all four path families, all seven customer-write action suffixes (via `[Theory]`), case
insensitivity, unrelated paths (unchanged), null, and empty input. No new project references
needed (UnitTests already references OpHalo.Api).

**`tests/OpHalo.IntegrationTests/Api/TokenSafeLoggingTests.cs`** — factory + 4 integration proofs.

`TokenSafeLoggingWebFactory` uses `"Testing"` environment (HTTPS redirect and rate limiting
disabled) and adds `CapturingLoggerProvider` to the logging pipeline. The provider captures all
log messages that pass the application's configured category filters.

The four proofs hit the four required routes with obviously unique sentinel tokens
(`g8b_*_SHOULD_NOT_APPEAR_*`) and assert no captured log message contains the raw sentinel:

1. `POST /keep/public-intake/token/{publicIntakeSentinel}` — unknown token, returns error
2. `POST /continuity/public-intake/token/{legacyIntakeSentinel}` — unregistered route, returns 404
3. `GET /keep/r/{pageTokenSentinel}` — unknown token, returns error
4. `POST /keep/r/{pageWriteSentinel}/message` — unknown token, returns error

All four pass: no raw token appears in captured application logs.

---

## Edge / Deployment Residual Risk

### Cloudflare and Railway access logs

Cloudflare and Railway operate edge access logs at the infrastructure layer. These are outside
application control and cannot be proven by repository tests.

**Required deployment posture:**

- **Cloudflare:** Enable "Log Scrubbing" for path-bearing fields, or configure a Transform Rule to
  mask the `/keep/r/*` and `/keep/public-intake/token/*` path segments in Logpush datasets before
  export. If Workers are added later, confirm no Worker logs raw request URLs at Info level.
- **Railway:** Disable HTTP access logging for the service, or configure the Railway log drain to
  exclude access-log entries, or ensure access logs are not exported to any durable log store.
  If Railway's managed TLS termination logs path-level access, treat token-bearing paths as
  residual accepted risk until a Railway-level filter or the `pageToken` rotation strategy in a
  future security slice can bound the blast radius.

**Accepted residual risk (pilot scope):** Token-bearing paths may appear in Cloudflare/Railway
access logs at the infrastructure layer. Mitigation is deployment configuration, not code.
Database reads of `KeepRequest.PageToken` are a separate risk already documented under G8c
(Option 3: accepted retrievable storage for pilot). Application-controlled logs/traces are proven
clean by this G8b slice.

---

## Verification

```
dotnet test tests/OpHalo.UnitTests/OpHalo.UnitTests.csproj --no-restore --filter PublicTokenPathRedactor
# Passed: 18

dotnet test tests/OpHalo.IntegrationTests/OpHalo.IntegrationTests.csproj --no-restore --filter TokenSafeLogging
# Passed: 4

git diff --check
# clean
```

**Final baseline:** 1265 tests (677 unit · 14 arch · 574 integration; +22)
