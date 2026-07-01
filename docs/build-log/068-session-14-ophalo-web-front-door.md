# Build Log 068 — Session 14 OpHalo Web Front Door

**Date:** 2026-07-01
**Session name:** Session 14 OpHalo Web Front Door
**Status:** S14c complete; S14d magic-link exchange is the next slice
**Current ADR after S13:** ADR-384

## Session Intent

Session 14 builds `web/ophalo-web` as the public front door for OpHalo. This is foundation work,
not a temporary pilot-only page set. The public web surface must let a real pilot business understand
OpHalo Keep, start or sign in, complete browser-based auth, accept team invites, and land in the
authenticated `ophalo-app` workbench.

`ophalo-web` owns public entry, lightweight public content, and token landing pages. `ophalo-app`
remains the authenticated Keep workbench. `OpHalo.Api` remains the sole authority for auth, session
issuance, account creation, rate limiting, email sending, authorization, and persistence.

The current live/reference website is the content baseline for S14. We are not starting from a blank
marketing page; we are rebuilding the useful parts of the existing public site inside the new
foundation app, with the active UX/brand system and the locked auth/domain topology.

## Source References

- `docs/decisions/ADR-377-web-client-surface-and-technology-stack.md`
- `docs/decisions/ADR-378-pilot-auth-session-and-domain-topology.md`
- `docs/ux-design/ux-design-decisions.md`
- `docs/ux-design/ux-design-model-v1.md`
- `docs/ux-design/keep-review-rubric.md`
- `docs/brand-kit/README.md`
- `docs/brand-kit/BRAND.md`
- `docs/pilot-readiness-decision-questions.md`
- `docs/build-log/067-session-13-pwa-workbench.md`
- Current reference website content on `ophalo.com`, especially `/` and `/about`

## Implementation Checkpoints

### S14a — Complete

**Commit:** `e785ad1 feat: scaffold ophalo-web public front door (S14a)`

Delivered:

- Created standalone `web/ophalo-web` Next.js App Router scaffold.
- Pinned exact dependency versions in `web/ophalo-web/package.json`:
  - Next.js `16.2.9`
  - React / React DOM `19.2.7`
  - Tailwind CSS `4.3.2`
  - TypeScript `6.0.3`
  - Lucide React `1.23.0`
  - checked-in React and Node type packages
- Added Next/Tailwind/TypeScript project config:
  - `next.config.ts`
  - `tsconfig.json`
  - `postcss.config.mjs`
  - `pnpm-workspace.yaml`
- Added local environment contract:
  - `.env.local.example` checked in with local defaults:
    - `NEXT_PUBLIC_API_BASE_URL=http://localhost:5092`
    - `NEXT_PUBLIC_APP_BASE_URL=http://localhost:5173`
  - `.env.local` created locally for immediate dev use and kept gitignored.
- Added `src/app/globals.css` with Tailwind 4 `@import` / `@theme`, OpHalo brand color tokens,
  and the Poppins font variable.
- Added `src/app/layout.tsx` with Poppins via `next/font/google`, canvas background, and ink text.
- Added `src/app/page.tsx` as the minimal placeholder homepage with the OpHalo lockup.
- Copied `docs/brand-kit/logos/ophalo-lockup-color.svg` to
  `web/ophalo-web/public/brand/ophalo-lockup-color.svg`.
- Generated and tracked `next-env.d.ts` for standalone typecheck.
- Updated `src/OpHalo.Api/appsettings.Development.json` CORS allowed origins to include
  `http://localhost:3000`.

Verified:

- `web/ophalo-web` typecheck clean.
- `web/ophalo-web` production build clean.
- Build output produced 2 static routes: `/` and `/_not-found`.
- `git diff --check` clean before commit.

### S14a Follow-Up — UX Token And Font Alignment

**Status:** Complete after `e785ad1`.

Reason: the initial scaffold used the correct brand color values, but it was still too thin for the
active UX contract. Before S14b content work, `ophalo-web` needs the locked CSS token layer and
self-hosted Inter / Source Serif 4 font pipeline that ADR-368 and ADR-379 require.

Follow-up scope:

- Create/import the shared `--ophalo-*` and `--keep-*` CSS token source.
- Keep `web/shared/styles/ophalo-tokens.css` as the canonical token reference, but inline the same
  token definitions in `web/ophalo-web/src/app/globals.css` because Turbopack dev mode cannot
  resolve CSS imports that escape the Next.js project root.
- Replace live Poppins UI usage with Inter body/UI and Source Serif 4 heading availability.
- Keep Poppins reserved to the outlined logo assets.
- Add exact-pinned `@fontsource-variable/inter` and `@fontsource-variable/source-serif-4`.
- Add a `copy-fonts`/`postinstall` step that copies the WOFF2 variable fonts to
  `web/ophalo-web/public/fonts/`.
- Keep all font loading self-hosted from the web origin; no third-party font CDN or
  `next/font/google`.

Verified:

- `pnpm -C web/ophalo-web copy-fonts` copies Inter and Source Serif 4 into
  `web/ophalo-web/public/fonts/`.
- `pnpm -C web/ophalo-web typecheck` clean.
- `pnpm -C web/ophalo-web build` clean.
- `pnpm -C web/ophalo-web dev` no longer hits the Turbopack cross-root CSS import panic once the
  token definitions are inlined in `globals.css`.

## Locked Decisions

### S14-D1 — Session Goal

Session 14 is the public/front-door foundation pass for `ophalo-web`: Keep-led public homepage,
About page, signin, pilot start, magic-link check-email/exchange, invite accept, minimal legal
surface, and redirect handoff into `ophalo-app`.

### S14-D2 — Design And Brand Sources

`ophalo-web` follows `docs/ux-design/` as the active UX/design source of truth and uses approved
assets from `docs/brand-kit/`. Retired brand docs are not authoritative.

`ophalo-web` is the OpHalo parent/public surface, but the homepage should be Keep-led because Keep is
the only live product and the pilot buyer needs to understand the concrete product immediately. The
page may present OpHalo as the company and Keep as the first application, but it must not make the
visitor work through an abstract parent-brand story before reaching the product promise.

### S14-D3 — Technology Stack

Use Next.js + React + TypeScript for `web/ophalo-web`, per ADR-377.

Shared frontend stack:

- Tailwind CSS;
- Lucide React;
- self-hosted OpHalo font and brand assets;
- no third-party font CDN;
- `OpHalo.Api` as the server authority.

S14a should use the latest patched stable Next.js major supported by Vercel and compatible with the
repo's React 19 posture; do not use canary/experimental framework releases for the public front door.
The exact patch version is pinned by `web/ophalo-web/package.json` and its lockfile when the project
is scaffolded.

Use the Next.js App Router. `ophalo-web` owns public routes, metadata, static/public rendering, and
token landing pages, so the App Router's layout/metadata model fits the route set better than the
legacy Pages Router.

### S14-D4 — First Route Set

Session 14 route scope:

- `/` — Keep-led OpHalo public homepage;
- `/about` — real About page based on the current site content;
- `/pilot` — lightweight pilot-fit page, not a required conversion bridge;
- `/signin` — existing-user sign-in;
- `/start` — public pilot owner start flow;
- `/auth/check-email` — post-submit email confirmation;
- `/auth/exchange` — browser magic-link exchange;
- `/invite/accept` — browser invite-token accept landing page;
- public/customer intake page — customer-facing request submission through existing Keep public
  intake backend contracts, required before public pilot launch;
- `/privacy` and `/terms` — minimal real pilot-appropriate pages, not empty placeholders.

Customer tracker, deeper product pages, broad legal/compliance work, and CMS/content systems are
deferred unless explicitly pulled into Session 14 by a later decision.

### S14-D5 — Public Pilot Start Posture

Use open public self-start for the pilot. The primary CTA is `/start`; `/start` calls the existing
`POST /auth/start` endpoint. Durable accounts are created only after magic-link exchange through
`POST /auth/exchange`; unexchanged starts remain temporary auth-code rows.

Production/pilot deployment must configure:

- `SignupDefaults:Classification=Pilot`
- `SignupDefaults:TrialDurationDays=30`
- `SignupDefaults:MaxPilotAccounts=15`

Current committed backend defaults are `Classification=Pilot`, `TrialDurationDays=30`, and
`MaxPilotAccounts=null`. `null` means no cap and is acceptable for local development, not public
pilot launch.

### S14-D6 — Production URL Topology

Production topology is locked per ADR-378:

- `https://ophalo.com` -> `ophalo-web`
- `https://www.ophalo.com` -> `ophalo-web`
- `https://app.ophalo.com` -> `ophalo-app`
- `https://api.ophalo.com` -> `OpHalo.Api`

These origins share eTLD+1 `ophalo.com`, so the production `ophalo.sid` cookie can use
`Domain=.ophalo.com`, `Secure`, `HttpOnly`, `SameSite=Lax`, and `Path=/`.

Resolved during decision review: the temporary concern that bare `https://ophalo.com` might still
serve Shopify-hosted/stale-origin content has been cleared. S14f should still verify final production
DNS/CDN routing after deployment so both `https://ophalo.com` and `https://www.ophalo.com` serve the
S14 `ophalo-web` deployment.

### S14-D7 — Local Dev Ports

Local development ports are established by current API development config:

- `ophalo-web`: `http://localhost:3000`
- `ophalo-app`: `http://localhost:5173`
- `OpHalo.Api`: `http://localhost:5092`

Local development must keep `Auth:CookieDomain` empty so the browser receives a host-only localhost
cookie. Do not configure `.localhost` or `localhost:port` as a cookie domain.

### S14-D8 — CORS Configuration

`OpHalo.Api` CORS allowed origins must include credentialed browser access from:

- `http://localhost:3000`
- `http://localhost:5173`
- `https://ophalo.com`
- `https://www.ophalo.com`
- `https://app.ophalo.com`

`allowCredentials: true` is required. Wildcard origins are invalid for credentialed browser requests.

S14a should update checked-in development config for `localhost:3000` so `ophalo-web` can be tested
locally. S14 verification/runbook work should document the production origins and deployment
checklist.

### S14-D9 — Direct Browser-To-API Auth Calls

`ophalo-web` does not introduce Next.js API routes or Server Actions for S14 auth flows. Sign-in
start, auth exchange, and invite-accept calls are credentialed browser fetches
(`credentials: "include"`) directly to `OpHalo.Api`.

`OpHalo.Api` remains the sole authority for auth, session, account creation, rate limiting, and email
sending. A Next.js proxy layer would add a second sensitive auth path without a security benefit.

Server Components in `ophalo-web` may call `OpHalo.Api` server-side for non-auth public rendering in
a later slice; that is a distinct pattern and does not affect this decision.

### S14-D10 — `/` Homepage Direction

`/` is the Keep-led OpHalo homepage. Use the current reference homepage as the content baseline.

Carry forward:

- headline direction: "Know which customers are waiting on you — before they wonder if you forgot";
- the pain frame around unanswered questions, slipped callbacks, and customers wondering whether
  the business still cares;
- the promise that every active request gets a home and Keep shows who needs a response;
- the "Keep is not a CRM" positioning;
- the "No pipelines / No stages / No heavy setup" contrast;
- the explanation that Keep does not replace existing communication habits, it keeps the work from
  getting lost;
- the before/after contrast of scattered messages versus requests rising to the top;
- realistic product-like visuals, modeled after the Keep workbench/customer-page concepts, but
  rebuilt to match the new foundation app and not copied as a stale mockup.

Change:

- remove or defer the "See it in action" CTA until a real demo/product tour exists;
- make "Try Keep" / "Join the Pilot" the primary conversion path to `/start`;
- use static pilot availability language such as "limited spots available" rather than a live or
  manually precise remaining count;
- align typography, spacing, colors, and assets with the in-repo brand kit and UX system;
- avoid pricing, uptime guarantees, support tiers, roadmap promises, and general-availability copy.

### S14-D11 — `/about` Page Direction

Keep a real `/about` page in S14 because the current About content strengthens trust and explains
the parent brand without burdening the homepage.

Carry forward:

- "Built for the people who do the work";
- the founder/customer-side observation around voicemails, unanswered emails, chasing updates, and
  customers always initiating;
- "Find the gaps and fill them";
- OpHalo as the company and Keep as the first application;
- the belief that software should support the existing workflow, not replace it;
- the grounded founder note from Christian.

Change:

- keep the page concise and secondary to product conversion;
- avoid overpromising future OpHalo apps beyond a careful statement that Keep is the first
  application;
- replace exact remaining-spot copy such as "10 spots open" with static limited-availability copy.

### S14-D12 — `/pilot` Boundary

`/pilot` is not required as a bridge between homepage and start. The homepage already explains the
product and should not force an extra click before `/start`.

S14 implements `/pilot` as a lightweight pilot-fit/expectations page linked from secondary copy. It
should not sit between the homepage and `/start` in the primary CTA path.

`/pilot` should communicate limited/founder-supported access, good-fit businesses, and expectations.
It must not include pricing, uptime guarantees, support tiers, roadmap promises, or
general-availability claims.

### S14-D13 — `/start` Field Set And Time Zone

`/start` collects only:

- owner name;
- work email;
- business name;
- time zone.

Time zone should default from `Intl.DateTimeFormat().resolvedOptions().timeZone` and remain
user-editable through a valid IANA time zone dropdown. Use `Intl.supportedValuesOf("timeZone")` where
available with a reasonable fallback list for browsers that do not support it. The form must not
submit without a valid IANA time zone value.

Additional business profile, contact, policy, team, and intake setup belongs after authentication in
`ophalo-app` account setup.

### S14-D14 — Check Email Route

Use one shared `/auth/check-email` route for both `/signin` and `/start`, with optional
`?flow=start|signin` copy variants. The parameter is UI-only and must never be trusted for auth
logic.

Both `/auth/start` and `/auth/signin` send the same magic-link exchange format:

```text
{PublicBaseUrl}/auth/exchange?code=...
```

### S14-D15 — Magic-Link Exchange Redirect

`/auth/exchange` exchanges the magic-link code in the browser, then redirects to configured
`AppBaseUrl`.

S14 intentionally does **not** support `return_to`. Start/signin magic links do not currently include
`return_to`, and all successful S14 auth entry lands at the app workbench root. This is a deliberate
pilot simplification and a deferral of ADR-378's deep-link resume implementation note. Add validated
return-to support later only when post-auth deep-linking becomes a demonstrated need.

`ophalo-web` does not become the authenticated onboarding workspace.

### S14-D16 — Auth Error-State Copy

Handle these user-facing auth states:

| Route | Error states |
|---|---|
| `/start` | `Account.PilotFull` -> not accepting new registrations; `Account.EmailAlreadyInUse` -> sign in instead; validation errors |
| `/signin` | validation errors only; all account existence behavior stays neutral by backend design |
| `/auth/exchange` | `AuthCode.Expired`, `AuthCode.AlreadyConsumed`, `AuthCode.CannotConsumeInvalidated`, `AuthCode.NotFound` collapse to "link is invalid or expired, request a new one"; `Account.PilotFull` -> pilot full; `Account.SessionCreationFailed` -> could not sign in, try again |
| `/invite/accept` | missing token; `Invite.Expired`; `Invite.InvalidToken`; `Invite.AlreadyActive`; `Invite.SeatLimitReached`; `Account.SessionCreationFailed`; generic forbidden/server failure fallback |

Network/fetch failure is client-only and should show retry-oriented copy such as "Something went
wrong. Please try again."

Do not expose fine-grained magic-link failure distinctions to the user when they are not actionable.

`Account.PilotFull` is a live `/auth/exchange` state, not dead UI. `StartAuthService` checks the cap
before issuing a new-account code, and `ExchangeAuthService` re-checks the cap on the `NewAccount`
path before consuming the code/account graph so a pilot slot taken between start and exchange returns
`Account.PilotFull`. This is covered by `Exchange_PilotCapReachedBetweenStartAndExchange_Returns409AndCodeUnconsumed`.

### S14-D17 — Invite Accept Included

S14 includes `/invite/accept` in `ophalo-web` as a minimal landing page.

The page reads `?token=`, posts `{ token }` to `OpHalo.Api` `POST /accounts/invite/accept` with
`credentials: "include"`, and redirects to configured `AppBaseUrl` on success.

No resend-from-this-page, no identity merge, no numeric code fallback, and no separate existing-user
flow in S14. Invite sending and resend management stay in `ophalo-app`.

### S14-D18 — Invite Link Base URL

S14 moves invite-accept links to the public web origin. Invite emails use:

```text
{PublicBaseUrl}/invite/accept?token=...
```

`ophalo-web` owns `/invite/accept`, posts the token to `OpHalo.Api`, receives the API-set session
cookie, and redirects to `AppBaseUrl`.

`OperatorBaseUrl` is fully retired in S14. Current code uses it only to build initial invite,
resend-invite, and manual-share invite links. Once those links move to `PublicBaseUrl`, there is no
remaining purpose for `OperatorBaseUrl` in the S14 topology.

S14e should remove `OperatorBaseUrl` from `MagicLinkSettings`, checked-in app config, integration
test factory config, and runbooks. Invite-link tests should assert `{PublicBaseUrl}/invite/accept`
instead of `{OperatorBaseUrl}/invite/accept`.

### S14-D19 — Brand Assets

Use `docs/brand-kit/logos/ophalo-lockup-color.svg` as the public header logo across `ophalo-web`.
The header represents the domain-level OpHalo brand.

`docs/brand-kit/logos/ophalo-keep-lockup-color.svg` remains available for Keep-specific product
moments, but it is not the default header mark.

### S14-D20 — Header And Footer

Header contains:

- OpHalo lockup on the left;
- `Sign in` text link on the right;
- `Try Keep` or `Join the Pilot` CTA on the right.

No other primary nav is required for S14. `/about` can be linked from the footer and, if design space
allows, as a restrained secondary nav item.

Footer should include OpHalo LLC identity, contact details if still desired, About, Privacy, and
Terms. Privacy and Terms routes must be minimal real pilot-appropriate pages. Do not ship empty
placeholder legal pages to public pilot traffic.

### S14-D21 — `ophalo-app` Auth Redirect

After S14, unauthenticated `ophalo-app` users should be sent to the public sign-in entry:

```text
{PublicBaseUrl}/signin
```

S14 does not include `return_to`, so the app auth guard should not append unvalidated destination
URLs. Deep-link resume is deferred.

### S14-D22 — Pilot Spots Copy

Use static pilot availability copy such as "limited spots available" or "free during the pilot."
Do not display a dynamic or manually exact remaining spot count in S14.

The actual pilot cap is enforced server-side by `/auth/start` and `/auth/exchange` through
`SignupDefaults:MaxPilotAccounts=15` in production config.

### S14-D23 — Public/Customer Intake Required Before Public Pilot

Public/customer intake is required before public pilot launch and is pulled into Session 14 as a
later slice. It is not part of S14b's static content-shell implementation and should not be described
as live in S14b copy unless the intake slice has landed.

The backend already owns the anonymous public intake authority through existing Keep public-intake
contracts. `ophalo-web` owns the customer-facing browser page that collects the request and submits
to `OpHalo.Api` without introducing a Next.js API proxy.

S14 public copy can explain the intended product loop, but until the public intake page is complete,
it should use staff-created language such as "when a request comes in by call, text, email, or
anywhere else, your team can enter it into Keep" and "you can share the customer page." After S14g,
copy may state that customers can start requests themselves through the public intake link.

## Reference Website Carry-Forward

The current public site already validates much of the product positioning. S14 should preserve the
parts that make the buyer understand Keep quickly while rebuilding the implementation inside the new
foundation app.

### Keep

- Keep the primary headline direction.
- Keep the customer-trust pain frame: customers waiting, wondering, chasing, and slipping through
  scattered communication.
- Keep "Keep is not a CRM" as a central differentiator.
- Keep "no pipelines, no stages, no heavy setup" as a plain-language objection answer.
- Keep the customer-page explanation: each request gets a simple no-login page.
- Keep the "How Keep works" flow, but tune wording to match the actual S13 workbench behavior:
  staff can capture requests manually, share tracker pages, post updates, receive customer replies,
  and see attention rise.

### Change

- Rebuild all visuals using the S13/S14 design system rather than treating current website mockups as
  final product UI.
- Remove undefined CTAs, especially "See it in action," until there is a real destination.
- Replace exact spot counts with static limited-availability copy.
- Make `/start` the main conversion path.
- Keep `/pilot` optional/lightweight so it does not slow the main path.
- Ensure all public copy avoids pricing, support-tier, uptime, roadmap, or general-availability
  promises.
- Shopify/stale-origin concern for bare `ophalo.com` is resolved; keep normal S14f production
  routing verification for `ophalo.com` and `www.ophalo.com`.

Exact homepage, About, Pilot, Privacy, and Terms wording is finalized during S14b. S14a scaffolding
must not block on final copy because the current reference site plus this carry-forward section is
sufficient to establish routes, layout, assets, and build plumbing.

## Session 14 Slices

### S14a — Architecture, Scaffold, Config, And Shared Public Web Foundation

**Status:** Complete. Committed as `e785ad1`.

Intent: create `web/ophalo-web` as a Next.js + React + TypeScript app and establish the public web
foundation.

Delivered scope:

- Next.js project under `web/ophalo-web`;
- Tailwind CSS;
- OpHalo font and brand asset wiring;
- parent OpHalo tokens from the UX/brand contract;
- env contract for `NEXT_PUBLIC_API_BASE_URL` and `NEXT_PUBLIC_APP_BASE_URL`;
- checked-in development CORS update for `http://localhost:3000`;
- baseline typecheck/build.

### S14b — Public Content Shell

**Status:** Complete. Committed as `f902f83`.

Intent: build the credible public OpHalo/Keep content shell from the reference website baseline.

Delivered scope:

- `/` Keep-led homepage;
- `/about`;
- `/pilot` lightweight pilot-fit page;
- `/privacy` and `/terms` routes implemented per S14-D20 posture;
- header/footer/navigation;
- Start Pilot and Sign In CTAs;
- production-ready responsive layout using approved brand assets.

Implemented files:

- `web/ophalo-web/src/app/globals.css`
- `web/ophalo-web/src/app/layout.tsx`
- `web/ophalo-web/src/app/(marketing)/layout.tsx`
- `web/ophalo-web/src/app/(marketing)/page.tsx`
- `web/ophalo-web/src/app/(marketing)/about/page.tsx`
- `web/ophalo-web/src/app/(marketing)/pilot/page.tsx`
- `web/ophalo-web/src/app/(marketing)/privacy/page.tsx`
- `web/ophalo-web/src/app/(marketing)/terms/page.tsx`
- `web/ophalo-web/src/app/(marketing)/marketing.css`
- `web/ophalo-web/src/app/(marketing)/_components/KeepListMockup.tsx`
- `web/ophalo-web/src/components/layout/nav-link.tsx`
- `web/ophalo-web/src/components/layout/site-header.tsx`
- `web/ophalo-web/src/components/layout/site-footer.tsx`
- `web/ophalo-web/public/brand/ophalo-mark.svg`

S14b done gate:

- `/`, `/about`, `/pilot`, `/privacy`, and `/terms` render without auth or API dependency.
- Header/footer links point to the locked S14 routes.
- Primary homepage CTA points to `/start`; sign-in entry points to `/signin`.
- No "See it in action", exact remaining-spot counts, pricing, support-tier, uptime, roadmap, or
  general-availability promises.
- Desktop and mobile responsive checks show no text overlap, broken layout, or clipped CTA labels.
- Font files are present under `web/ophalo-web/public/fonts/` and served from the app origin.
- `globals.css` consumes locked `--ophalo-*` / `--keep-*` tokens; no marketing-local brand hex
  palette or retired teal.
- `pnpm typecheck` and `pnpm build` clean for `web/ophalo-web`.

Out of scope unless pulled in:

- customer tracker;
- public intake implementation details; S14b may mention the future/intended intake path only in
  non-live, non-promissory language until S14g lands;
- CMS/content system;
- broad legal/compliance program.

### S14c — Sign In, Start, And Check Email

**Status:** Complete.

Intent: implement the public owner/user auth entry loop.

Delivered scope:

- `/signin` -> `POST /auth/signin`;
- `/start` -> `POST /auth/start`;
- `/auth/check-email`;
- `Account.PilotFull` handling;
- neutral sign-in copy;
- browser time-zone detection and editable IANA time-zone field;
- local dev support with `ConsoleEmailSender`.

Implemented:

- Added auth-entry routes outside the `(marketing)` route group:
  - `/signin` in `web/ophalo-web/src/app/signin/page.tsx`;
  - `/start` in `web/ophalo-web/src/app/start/page.tsx`;
  - `/auth/check-email` in `web/ophalo-web/src/app/auth/check-email/page.tsx`.
- Kept `/signin` and `/start` as static client pages with focused auth layout through the root
  layout only. They do not inherit `SiteHeader`/`SiteFooter`.
- Kept `/auth/check-email` as a server route that reads `searchParams` for UI-only
  `?flow=start|signin` copy.
- Added direct browser JSON `fetch` calls with `credentials: "include"` to `OpHalo.Api`.
- Added `/start` timezone detection with `Intl.DateTimeFormat().resolvedOptions().timeZone`.
- Used `Intl.supportedValuesOf("timeZone")` when available, with fallback options covering common
  US time zones plus UTC.
- Added auth-form CSS in `web/ophalo-web/src/app/globals.css` under the `auth-*` prefix, matching
  the existing route-family class convention.
- Added a development-only check-email hint for `ConsoleEmailSender`.
- Did not add Next.js API routes, Server Actions, JavaScript cookie handling, or session-token
  storage.

Live API payloads:

```http
POST /auth/signin
```

```json
{ "email": "owner@example.com" }
```

```http
POST /auth/start
```

```json
{
  "email": "owner@example.com",
  "businessName": "Summit Plumbing",
  "name": "Jordan Lee",
  "timeZone": "America/Chicago"
}
```

Endpoint definitions are in `src/OpHalo.Api/Auth/AuthEndpoints.cs`:

- `StartBody(string? Email, string? BusinessName, string? Name, string? TimeZone)`
- `SignInBody(string? Email)`
- both endpoints return `200 OK` on neutral success;
- validation errors use problem responses with codes such as `Validation.EmailRequired`,
  `Validation.BusinessNameRequired`, `Validation.TimeZoneRequired`, and
  `Validation.TimeZoneInvalid`;
- `Account.PilotFull` maps through `ErrorHttpMapper` and must render a clear pilot-full state on
  `/start`;
- `Account.EmailAlreadyInUse` on `/start` should point the user to `/signin`;
- `/signin` must keep account-existence copy neutral because the backend intentionally returns
  neutral `200 OK` for unknown/ineligible emails.

`/start` field rules:

- collect only owner name, work email, business name, and time zone;
- default time zone from `Intl.DateTimeFormat().resolvedOptions().timeZone`;
- keep the field editable through valid IANA time zones;
- prefer `Intl.supportedValuesOf("timeZone")` when available and include a compact fallback list
  covering common US zones plus UTC;
- do not submit unless the selected value is a valid option in the client list. The API remains the
  final validator.

`/auth/check-email` behavior:

- no API call;
- support `?flow=start|signin` as UI-only copy selection;
- optionally accept an email query parameter for display, but do not require it and do not treat it
  as proof of account state;
- include routes back to `/signin` and `/start` for retry/new flow;
- local-dev copy may mention that magic links appear in the API console when `ConsoleEmailSender` is
  active, but keep production-facing copy normal and not implementation-heavy.

File impact:

- `web/ophalo-web/src/app/signin/page.tsx`
- `web/ophalo-web/src/app/start/page.tsx`
- `web/ophalo-web/src/app/auth/check-email/page.tsx`
- `web/ophalo-web/src/app/globals.css`

Verified:

- `/signin` renders, validates email, posts to `POST /auth/signin`, handles validation/network
  failure, and redirects to `/auth/check-email?flow=signin` after neutral success.
- `/start` renders, validates the four locked fields, posts to `POST /auth/start`, handles
  validation/network failure plus `Account.PilotFull` and `Account.EmailAlreadyInUse`, and redirects
  to `/auth/check-email?flow=start` after neutral success.
- `/auth/check-email` renders useful flow-specific copy without requiring auth or calling the API.
- Browser requests include `credentials: "include"` and use `NEXT_PUBLIC_API_BASE_URL`.
- No raw auth code, session token, or invite token is logged, stored, rendered, or added to app state.
- `pnpm --filter ophalo-web typecheck` clean.
- `pnpm --filter ophalo-web build` clean.
- `git diff --check` clean.
- Local smoke, when API is running: submit `/start`, confirm the magic-link URL is written by
  `ConsoleEmailSender`, and confirm the browser lands on `/auth/check-email`.
- Frontend visual verification required restarting the `ophalo-web` dev server so the new global
  auth CSS block was included in the served CSS bundle.

Out of scope unless explicitly pulled in:

- `/auth/exchange` implementation;
- `/invite/accept` implementation;
- backend invite-link URL changes or `OperatorBaseUrl` retirement;
- `ophalo-app` unauthenticated redirect changes;
- public/customer intake;
- production DNS/deployment verification;
- `return_to` / deep-link resume.

### S14d — Magic-Link Exchange And App Handoff

**Status:** Next. Pre-work complete; ready for mechanical implementation preflight and coding.

Intent: replace the dev-only S13 browser exchange helper with the real public web exchange page.

Expected scope:

- `/auth/exchange?code=...`;
- credentialed browser POST to `POST /auth/exchange`;
- API-set `ophalo.sid` cookie handling through normal browser behavior;
- no S14 `return_to`;
- redirect to `AppBaseUrl`;
- bounded expired/invalid/consumed/session-failure states.

Implementation contract:

- Add `/auth/exchange?code=...` outside the `(marketing)` route group.
- Read `code` from the query string in the browser page; do not log or display the raw code.
- POST directly to `OpHalo.Api` with credentialed browser fetch:
  - `POST ${NEXT_PUBLIC_API_BASE_URL}/auth/exchange`
  - body: `{ "code": "...", "clientType": "browser" }`
- Browser exchange returns `200 OK` with no session token body. The API sets `ophalo.sid` as an
  HttpOnly cookie through normal browser response handling.
- Redirect to `NEXT_PUBLIC_APP_BASE_URL` after successful exchange.
- Do not implement `return_to` in S14d.
- Do not add Next.js API routes, Server Actions, JavaScript cookie handling, or session-token
  storage in `ophalo-web`.

Current endpoint definitions are in `src/OpHalo.Api/Auth/AuthEndpoints.cs`:

- `ExchangeBody(string? Code, string? ClientType, string? DeviceName)`
- missing code returns `Validation.CodeRequired`;
- omitted/empty `clientType` maps to browser, and `"browser"` is accepted;
- `"mobile_app"` returns a raw session token in the body and must not be used by `ophalo-web`;
- invalid client types return `Validation.InvalidClientType`;
- browser success sets the auth cookie and returns `200 OK`.

Error-state mapping:

- missing `?code=` or `Validation.CodeRequired` -> show missing/invalid link copy with a CTA to
  `/signin`;
- `AuthCode.Expired`, `AuthCode.AlreadyConsumed`, `AuthCode.CannotConsumeInvalidated`, and
  `AuthCode.NotFound` -> collapse to "link is invalid or expired, request a new one";
- `Account.PilotFull` -> show pilot-full copy;
- `Account.SessionCreationFailed` -> show retry-oriented sign-in failure copy;
- network/fetch failure -> show "Something went wrong. Please try again."

Likely file impact:

- `web/ophalo-web/src/app/auth/exchange/page.tsx`
- optional route-local client component under `web/ophalo-web/src/app/auth/exchange/`
- optional tiny shared auth/problem helper if S14c's inline pattern becomes meaningfully duplicated
- `web/ophalo-web/src/app/globals.css` only if existing `auth-*` styles are insufficient

S14d done gate:

- `/auth/exchange` handles missing code without an API call and without leaking raw token values.
- Valid magic-link code posts to `POST /auth/exchange` with `credentials: "include"` and
  `clientType: "browser"`.
- Successful exchange redirects to `NEXT_PUBLIC_APP_BASE_URL`.
- Invalid/expired/consumed/not-found states collapse to one non-leaky invalid-link state.
- Pilot-full, session-failure, validation, and network states are intentional.
- No raw auth code, session token, invite token, or cookie value is logged, stored, or displayed.
- `pnpm --filter ophalo-web typecheck` clean.
- `pnpm --filter ophalo-web build` clean.
- `git diff --check` clean.

### S14e — Invite Accept And Backend Link Wiring

Intent: make invited operators/admins/viewers able to join a pilot account from their invite email.

Expected scope:

- `/invite/accept?token=...`;
- credentialed browser POST to `POST /accounts/invite/accept`;
- success redirect to `AppBaseUrl`;
- bounded error states;
- backend invite-link generation moves to `{PublicBaseUrl}/invite/accept?token=...`;
- fully retire `OperatorBaseUrl` from settings, config, test factories, and runbooks;
- update invite tests and local setup docs.

### S14f — App Redirect, Verification, Production Config, And Runbook

Intent: prove the public front door works with the existing backend topology.

Expected scope:

- `ophalo-app` unauthenticated redirect points to `{PublicBaseUrl}/signin`;
- `ophalo-web` typecheck/build clean;
- relevant API tests for invite-link URL change and auth/invite accept behavior;
- browser verification:
  - `/`;
  - `/about`;
  - `/pilot` or its redirect/alias;
  - `/signin`;
  - `/start`;
  - `/auth/check-email`;
  - `/auth/exchange`;
  - `/invite/accept`;
  - redirect into `ophalo-app`;
- local runbook updated for three servers: API, `ophalo-web`, and `ophalo-app`;
- production config checklist for public pilot:
  - `PublicBaseUrl`;
  - `AppBaseUrl`;
  - CORS allowed origins;
  - cookie domain;
  - Resend settings;
  - `SignupDefaults:MaxPilotAccounts=15`;
  - DNS/CDN routing verification for both `ophalo.com` and `www.ophalo.com` after deployment.

### S14g — Public/Customer Intake Page

Intent: deliver the customer-facing request submission page required for public pilot.

Expected scope:

- `ophalo-web` public intake route for active business intake links;
- customer-facing request form using active UX/brand system and Keep customer-surface rules;
- browser POST directly to `OpHalo.Api` `POST /keep/public-intake/token/{publicIntakeToken}`;
- success state that gives the customer the created request/customer page path returned by the API
  without exposing raw tokens in logs or UI beyond the expected link destination;
- unavailable state for invalid/revoked/off-season/blocked links using the backend's generic public
  intake unavailable response;
- validation/error states matching existing backend validation without leaking account/token state;
- S14b/S14 marketing copy can then accurately say customers can start requests through a shared
  intake link.

Likely dependencies:

- existing `GET /keep/setup/intake`, `POST /keep/setup/intake/ensure`, and
  `POST /keep/setup/intake/replace` account setup flows in `ophalo-app`;
- existing anonymous public intake endpoint and rate limit policy in `OpHalo.Api`;
- existing token redaction for public intake paths.

Out of scope unless explicitly pulled in:

- new backend public-intake domain behavior;
- customer email delivery beyond existing implemented notification behavior;
- spam/adaptive challenge controls beyond current pre-pilot abuse posture;
- customer tracker redesign beyond linking to the created customer page.

## Quality Gates

- `ophalo-web` typecheck and production build are clean.
- Relevant API tests are green when backend invite-link or config behavior is changed.
- Auth calls use credentialed browser fetches directly to `OpHalo.Api`; no Next.js auth proxy layer.
- `OpHalo.Api` CORS docs/config explicitly cover credentialed requests from `ophalo-web`.
- Public pages use active OpHalo UX/brand sources and approved brand assets.
- Reference website content is carried forward intentionally; stale visuals, undefined CTAs, and exact
  spot counts are removed.
- No token/session leakage in UI, logs, URLs beyond expected one-time magic/invite link query params.
- Magic-link and invite-token pages handle missing, invalid, expired, consumed/already-used, network,
  pilot-full, and session-creation failure states intentionally.
- Browser verification covers desktop and mobile widths.

## Deferred From Session 14 Unless Explicitly Pulled In

- Customer tracker page (`/keep/r/...`);
- resend invite from `/invite/accept`;
- identity merge or account-selection UX;
- validated post-auth `return_to` / deep-link resume;
- native/mobile auth;
- full legal/compliance program beyond minimal public pilot pages;
- deeper product/industry pages;
- analytics/event instrumentation beyond existing backend logs;
- public support/help center;
- demo video/product tour for "See it in action."
