# Local Web Setup — ophalo-app and ophalo-web

This runbook covers running all three services locally: the API, the public front door
(`ophalo-web`), and the authenticated workbench (`ophalo-app`).

## Prerequisites

- Local PostgreSQL with the `ophalo_local` database (see `docs/deployment/local-postgres-runbook.md`)
- `pnpm` ≥ 11 installed globally (`npm i -g pnpm`)
- Node.js ≥ 20 LTS
- .NET 10 SDK

---

## Service topology

| Service | Default local URL | Purpose |
|---------|-------------------|---------|
| `OpHalo.Api` | `http://localhost:5092` | Auth, sessions, data |
| `ophalo-web` | `http://localhost:3000` | Public auth entry, invite accept |
| `ophalo-app` | `http://localhost:5173` | Authenticated workbench |

---

## 1. API setup

### User secrets (one-time)

```bash
cd src/OpHalo.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=ophalo_local;Username=postgres;Password=..."
dotnet user-secrets set "Keep:RequestListCursorSigningKey" "<base64-32-bytes>"
```

Generate the signing key if needed:

```bash
openssl rand -base64 32
```

`appsettings.Development.json` sets `App:PublicBaseUrl=http://localhost:3000` and
`App:AppBaseUrl=http://localhost:5173`, and allows both origins in CORS. No additional
config is needed for standard local dev.

### Email in Development

When no Resend key is configured the API uses `ConsoleEmailSender`, which writes
magic-link and invite-link URLs to the API's stderr — no email delivery needed locally.
Confirm with the API startup log: look for `ConsoleEmailSender`.

To use real email:

```bash
dotnet user-secrets set "Resend:ApiKey" "re_..."
dotnet user-secrets set "Resend:FromAddress" "..."
```

### Run the API

```bash
cd src/OpHalo.Api
dotnet run
```

Confirm: `curl http://localhost:5092/auth/me` → `401`.

---

## 2. ophalo-web setup (one-time)

```bash
cd web/ophalo-web
pnpm install
```

Env defaults are in `.env.local` (or `.env.development`):

```
NEXT_PUBLIC_API_BASE_URL=http://localhost:5092
NEXT_PUBLIC_APP_BASE_URL=http://localhost:5173
```

### Run ophalo-web

```bash
cd web/ophalo-web
pnpm dev
# http://localhost:3000
```

---

## 3. ophalo-app setup (one-time)

```bash
cd web/ophalo-app
pnpm install
```

The `postinstall` script copies fonts automatically. Env defaults are in
`.env.development` (committed):

```
VITE_API_BASE_URL=http://localhost:5092
VITE_PUBLIC_BASE_URL=http://localhost:3000
```

### Run ophalo-app

```bash
cd web/ophalo-app
pnpm dev
# http://localhost:5173
```

---

## Local auth flow

The normal auth path uses `ophalo-web` as the entry point, the same as production.

### Sign in (existing account)

1. Navigate to `http://localhost:3000/signin` and enter your email.
2. The API logs the magic-link URL to stderr:

   ```
   ──── [DEV EMAIL] ────────────────────────────────────────
   URL: http://localhost:3000/auth/exchange?code=<code>
   ─────────────────────────────────────────────────────────
   ```

3. Open that URL in the browser. The exchange page POSTs to the API from the browser so
   the `ophalo.sid` cookie lands in the browser's cookie store, then redirects to
   `http://localhost:5173`.

### New account (start flow)

1. Navigate to `http://localhost:3000/start` and complete the form.
2. Copy the exchange URL from API stderr and open it in the browser (same as above).

### Invite accept

Invite emails contain links of the form:

```
http://localhost:3000/invite/accept?token=<token>
```

Copy the URL from API stderr and open it in the browser. On success the page redirects
to `http://localhost:5173`.

---

## CORS

`appsettings.Development.json` allows `http://localhost:5173` and `http://localhost:3000`
with credentials. For a non-standard port add it to `Cors:AllowedOrigins` in user secrets
or `appsettings.Development.json`.

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
# ophalo-web
cd web/ophalo-web && pnpm typecheck && pnpm build

# ophalo-app
cd web/ophalo-app && pnpm typecheck && pnpm build
```
