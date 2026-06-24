# Local PostgreSQL Runbook

Persistent local PostgreSQL is the pre-notification pilot safety baseline. Use this runbook when you
want the API to run against a durable local database instead of the disposable Testcontainers
databases used by integration tests.

This runbook is local/dev only. Do not use the reset commands against Railway, production, pilot, or
any shared database.

## Database

Recommended local database name:

```bash
ophalo_local
```

Create it once with your local PostgreSQL user:

```bash
createdb ophalo_local
```

Connection string shape expected by the API and EF tooling:

```bash
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=ophalo_local;Username=postgres;Password=postgres'
export OPHALO_LOCAL_PSQL_URL='postgresql://postgres:postgres@localhost:5432/ophalo_local'
```

Adjust `Username` and `Password` for your local PostgreSQL install. Keep the database name
`ophalo_local` if you plan to use the guarded reset command below. `ConnectionStrings__DefaultConnection`
is for .NET/Npgsql; `OPHALO_LOCAL_PSQL_URL` is for `psql`.

Optional local runtime values (env var fallback if not using user secrets):

```bash
export App__PublicBaseUrl='http://localhost:5000'
export App__OperatorBaseUrl='http://localhost:5000'
```

## User Secrets Setup

The connection string must be stored in **both** projects — they read secrets independently:

- `src/OpHalo.Keep.Infrastructure` — read by the EF design-time factory for migrations.
- `src/OpHalo.Api` — read by the running API host.

```bash
dotnet user-secrets init --project src/OpHalo.Keep.Infrastructure
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=ophalo_local;Username=<user>;Password=<pass>" \
  --project src/OpHalo.Keep.Infrastructure

dotnet user-secrets init --project src/OpHalo.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=ophalo_local;Username=<user>;Password=<pass>" \
  --project src/OpHalo.Api
dotnet user-secrets set "Keep:RequestListCursorSigningKey" \
  "$(openssl rand -base64 32)" \
  --project src/OpHalo.Api
```

User secrets are only loaded when `ASPNETCORE_ENVIRONMENT=Development`.

## Apply Migrations

Use the Keep design-time factory when applying the full schema. The startup project matters because
Keep EF configurations live outside Foundation.

```bash
dotnet ef database update \
  --project src/OpHalo.Foundation.Infrastructure \
  --startup-project src/OpHalo.Keep.Infrastructure \
  --context OpHaloDbContext
```

The migration history table is `__OpHaloMigrationsHistory`.

## Run The API

```bash
ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project src/OpHalo.Api
```

If the API fails on first database access, confirm `ConnectionStrings__DefaultConnection` is exported
in the same shell that starts the API.

## Minimal Smoke

These checks prove the host starts, the route table is live, and the database-backed public-intake
path can execute without broad seed data.

In one terminal, start the API as shown above. In another terminal:

```bash
curl -i http://localhost:5000/openapi/v1.json
```

Expected: `200 OK` in `Development`.

Then hit public intake with a fake token and a valid-shaped body:

```bash
curl -i \
  -X POST http://localhost:5000/keep/public-intake/token/local-smoke-invalid-token \
  -H 'Content-Type: application/json' \
  -d '{"customerName":"Local Smoke","customerPhone":"555-0100","description":"Local smoke check"}'
```

Expected: a safe public error such as `404 Not Found` for the unknown token. This confirms the
database-backed route executed without requiring demo data or a real intake link. Do not paste real
tokens into terminal history for smoke checks.

For an authenticated operator smoke, use a manually created local account/session only when you need
to exercise protected routes. Broad seed/demo scenario packs are intentionally excluded until the app
has explicit Demo/InternalTest account classification and safe reset rules.

## Guarded Local Reset

Use this only for `ophalo_local`. It drops and recreates the public schema, matching the integration
test reset pattern, then reapplies migrations.

```bash
psql "$OPHALO_LOCAL_PSQL_URL" -v ON_ERROR_STOP=1 <<'SQL'
DO $$
BEGIN
  IF current_database() <> 'ophalo_local' THEN
    RAISE EXCEPTION 'Refusing local reset on database %', current_database();
  END IF;
END
$$;

DROP SCHEMA IF EXISTS public CASCADE;
CREATE SCHEMA public;
GRANT ALL ON SCHEMA public TO public;
SQL

dotnet ef database update \
  --project src/OpHalo.Foundation.Infrastructure \
  --startup-project src/OpHalo.Keep.Infrastructure \
  --context OpHaloDbContext
```

Before resetting, run:

```bash
psql "$OPHALO_LOCAL_PSQL_URL" -c 'select current_database(), current_user, inet_server_addr();'
```

Proceed only when `current_database` is `ophalo_local` and the server address is your local
PostgreSQL instance.

## Public-Intake Abuse Emergency Notes

Before pilot, the founder/internal response to unexpected public-intake abuse is operational, not an
adaptive blocking platform:

- Preserve evidence without copying raw public-intake tokens, customer page tokens, invite tokens,
  auth/session credentials, bearer credentials, or full token URLs into tickets, chat, or logs.
- Capture safe context: account id, request id after lookup, reference code, route template,
  timestamp range, correlation id when available, validation/error reason codes, and coarse source
  context such as trusted client IP metadata when S7b is complete.
- If the intake link itself is compromised, use the existing Owner/Admin replacement flow and notify
  the business that old shared links are stale.
- Once Spam/Test classification exists, classify junk/test requests instead of deleting rows so
  operational queues and impact metrics stay clean while audit history remains.
- If volume threatens pilot operation before adaptive controls exist, temporarily restrict exposure
  of the intake link at the business/channel level and queue S7b/S7d/S7e hardening before broadening
  distribution again.

Still deferred unless pilot evidence pulls it forward: source blocking, adaptive bot challenges,
honeypots, anomaly dashboards, phone verification, broad demo seeds, and production reset tooling.
