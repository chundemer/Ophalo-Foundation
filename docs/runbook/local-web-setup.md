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

`ophalo-web` (the public auth surface) does not exist yet. Authenticate locally by calling the API
directly. The exchange step **must happen from the browser** — a curl-only exchange sets the cookie
in curl's jar, not the browser's, so the app at `localhost:5173` would still see 401.

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

### 2. Copy the raw code from API stderr

The API console prints:

```
──── [DEV EMAIL] ────────────────────────────────────────
To:      you@example.com
Subject: Your OpHalo sign-in link
URL:     http://localhost:3000/auth/exchange?code=<rawCode>
─────────────────────────────────────────────────────────
```

Copy the `<rawCode>` value from the `code=` query parameter in that URL.

### 3. Exchange the code in the browser (browser auth helper)

With the Vite dev server running, navigate to:

```
http://localhost:5173/dev-auth.html
```

Paste the raw code into the form and click **Exchange & Open App**. The page POSTs to the API
from the browser so the `ophalo.sid` cookie is stored in the browser's cookie store (not just curl's
jar), then redirects to `http://localhost:5173`.

> **Why this page exists:** `ophalo.sid` is `HttpOnly` and `SameSite=Lax`. `curl -c cookies.txt`
> writes the cookie to a local file — the browser never sees it. The dev auth helper makes the same
> `POST /auth/exchange` call from within the browser so the response's `Set-Cookie` header lands in
> the browser's own session.

### 4. Verify the app loads

The `AuthGuard` calls `GET /auth/me`. If the session cookie is present and valid, the home screen
or requests list loads. Owner/Admin accounts see the full nav; Viewer accounts see the access-limited
state.

> **Note on `return_to`:** When the app redirects to `VITE_PUBLIC_BASE_URL/auth/signin?return_to=...`,
> the URL targets `localhost:3000` which doesn't exist yet. To re-authenticate, use
> `http://localhost:5173/dev-auth.html` with a fresh code from the sign-in flow above.

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
