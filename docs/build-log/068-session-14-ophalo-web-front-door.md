# Build Log 068 — Session 14 OpHalo Web Front Door

**Date:** 2026-07-01
**Session name:** Session 14 OpHalo Web Front Door
**Status:** Pre-implementation decisions locked; implementation not started
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
- `/privacy` and `/terms` — minimal real pilot-appropriate pages, not empty placeholders.

Customer tracker, public intake, deeper product pages, broad legal/compliance work, and CMS/content
systems are deferred unless explicitly pulled into Session 14 by a later decision.

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

Intent: create `web/ophalo-web` as a Next.js + React + TypeScript app and establish the public web
foundation.

Expected scope:

- Next.js project under `web/ophalo-web`;
- Tailwind CSS;
- self-hosted OpHalo fonts/assets;
- parent OpHalo tokens from the UX/brand contract;
- env contract for `NEXT_PUBLIC_API_BASE_URL` and `NEXT_PUBLIC_APP_BASE_URL`;
- checked-in development CORS update for `http://localhost:3000`;
- baseline typecheck/build.

### S14b — Public Content Shell

Intent: build the credible public OpHalo/Keep content shell from the reference website baseline.

Expected scope:

- `/` Keep-led homepage;
- `/about`;
- `/pilot` lightweight pilot-fit page;
- `/privacy` and `/terms` routes implemented per S14-D20 posture;
- header/footer/navigation;
- Start Pilot and Sign In CTAs;
- production-ready responsive layout using approved brand assets.

Out of scope unless pulled in:

- customer tracker;
- public intake;
- CMS/content system;
- broad legal/compliance program.

### S14c — Sign In, Start, And Check Email

Intent: implement the public owner/user auth entry loop.

Expected scope:

- `/signin` -> `POST /auth/signin`;
- `/start` -> `POST /auth/start`;
- `/auth/check-email`;
- `Account.PilotFull` handling;
- neutral sign-in copy;
- browser time-zone detection and editable IANA time-zone field;
- local dev support with `ConsoleEmailSender`.

### S14d — Magic-Link Exchange And App Handoff

Intent: replace the dev-only S13 browser exchange helper with the real public web exchange page.

Expected scope:

- `/auth/exchange?code=...`;
- credentialed browser POST to `POST /auth/exchange`;
- API-set `ophalo.sid` cookie handling through normal browser behavior;
- no S14 `return_to`;
- redirect to `AppBaseUrl`;
- bounded expired/invalid/consumed/session-failure states.

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
- public intake page;
- resend invite from `/invite/accept`;
- identity merge or account-selection UX;
- validated post-auth `return_to` / deep-link resume;
- native/mobile auth;
- full legal/compliance program beyond minimal public pilot pages;
- deeper product/industry pages;
- analytics/event instrumentation beyond existing backend logs;
- public support/help center;
- demo video/product tour for "See it in action."
