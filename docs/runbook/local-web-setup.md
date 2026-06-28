# Local Web Setup — ophalo-app

This runbook covers running the authenticated OpHalo app locally for S13a development and testing.

## Prerequisites

- Local PostgreSQL with the `ophalo_local` database (see existing API runbook)
- `pnpm` ≥ 11 installed globally (`npm i -g pnpm`)
- Node.js ≥ 20 LTS
- .NET 10 SDK

---

## API setup (one-time)

### 1. User secrets

The API must have a connection string and a cursor signing key. Supply them via user secrets:

```bash
cd src/OpHalo.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=ophalo_local;Username=postgres;Password=..."
dotnet user-secrets set "Keep:RequestListCursorSigningKey" "<base64-32-bytes>"
```

Generate the signing key if needed:

```bash
openssl rand -base64 32
```

### 2. Email in Development

`appsettings.Development.json` sets `App:PublicBaseUrl=http://localhost:3000`. When no Resend key is configured, the API uses `ConsoleEmailSender`, which writes magic-link URLs to stderr — no email delivery needed locally.

To verify the fallback is active, check the API startup logs for `ConsoleEmailSender`. If you prefer real email, set:

```bash
dotnet user-secrets set "Resend:ApiKey" "re_..."
dotnet user-secrets set "Resend:FromAddress" "..."
```

### 3. Run the API

```bash
cd src/OpHalo.Api
dotnet run
# Listens on http://localhost:5092 by default
```

Confirm with: `curl http://localhost:5092/auth/me` → should return 401.

---

## App setup (one-time)

```bash
cd web/ophalo-app
pnpm install   # installs packages and copies fonts to public/fonts/
```

The `postinstall` script runs `scripts/copy-fonts.mjs` automatically, copying:

- `inter-latin-wght-normal.woff2` → `public/fonts/inter-variable.woff2`
- `source-serif-4-latin-wght-normal.woff2` → `public/fonts/source-serif-4-variable.woff2`

Env defaults are in `.env.development` (committed):

```
VITE_API_BASE_URL=http://localhost:5092
VITE_PUBLIC_BASE_URL=http://localhost:3000
```

No overrides needed for standard local dev.

### Run the dev server

```bash
pnpm dev
# App at http://localhost:5173
```

---

## Local auth flow

`ophalo-web` (the public auth surface) does not exist yet. Authenticate locally by calling the API directly:

### 1. Start auth (new account) or sign in (existing account)

For a new account:

```bash
curl -s -X POST http://localhost:5092/auth/start \
  -H "Content-Type: application/json" \
  -d '{"email":"you@example.com","businessName":"Test Co","timeZone":"America/New_York"}'
```

For an existing account:

```bash
curl -s -X POST http://localhost:5092/auth/signin \
  -H "Content-Type: application/json" \
  -d '{"email":"you@example.com"}'
```

### 2. Copy the magic link from API stderr

The API console prints:

```
──── [DEV EMAIL] ────────────────────────────────────────
To:      you@example.com
Subject: Your OpHalo sign-in link
URL:     http://localhost:3000/auth/exchange?code=<rawCode>
─────────────────────────────────────────────────────────
```

### 3. Exchange the code for a session cookie

The exchange endpoint lives on the API, not on `localhost:3000`. Extract the `code` query param and POST:

```bash
curl -s -c cookies.txt -X POST http://localhost:5092/auth/exchange \
  -H "Content-Type: application/json" \
  -d '{"code":"<rawCode>","clientType":"Browser"}'
```

The response sets `ophalo.sid` as a host-only cookie on `localhost:5092`.

### 4. Open the app

Navigate to `http://localhost:5173`. The browser includes `ophalo.sid` on credentialed requests to `localhost:5092`. The `AuthGuard` calls `GET /auth/me` — if the session is valid, the home screen loads.

> **Note on `return_to`:** When the app redirects to `VITE_PUBLIC_BASE_URL/auth/signin?return_to=...`, that URL goes to `localhost:3000` which doesn't exist yet. For S13a dev testing, obtain a fresh session cookie via the curl flow above instead of following the redirect.

---

## CORS

The API allows `http://localhost:5173` with credentials in Development (`appsettings.Development.json`). No additional config needed.

For a new non-standard port, add it to the `Cors:AllowedOrigins` array in your user secrets or `appsettings.Development.json`.

---

## Session parameters (ADR-378)

| Setting | Value |
|---------|-------|
| Absolute expiry | 60 days from creation |
| Inactivity window | 30 days from last request |
| Cookie | `ophalo.sid`, host-only, HttpOnly, SameSite=Lax |
| Cookie domain | empty in Development (host-only localhost) |

---

## Typecheck and build

```bash
pnpm typecheck   # tsc --noEmit, must be clean
pnpm build       # production build to dist/
```
