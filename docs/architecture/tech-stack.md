# OpHalo Technology Stack

**Last reviewed:** 2026-08-09  
**Purpose:** A concise reference for the technologies that currently make up the OpHalo product and
its pilot deployment path.

## Product surfaces

| Surface | Technologies | Role |
| --- | --- | --- |
| Authenticated workbench (`web/ophalo-app`) | React 19, TypeScript, Vite 6, TanStack React Query, Tailwind CSS 3, Lucide | Owner/Admin and staff workspace. |
| Public web and authentication entry (`web/ophalo-web`) | Next.js 16, React 19, TypeScript, Tailwind CSS 4, Lucide | Public pages, account entry, sign-in, magic-link exchange, and invite acceptance. |
| Native mobile app (`mobile/ophalo-mobile`) | Expo 57, React Native 0.86, Expo Router, TypeScript, TanStack React Query, Expo Secure Store | Narrow field/operator workflow. |

## Backend and data

- **API:** ASP.NET Core on .NET 10, serving REST-style JSON endpoints and OpenAPI metadata.
- **Architecture:** One API host and a modular layered monolith. `Foundation` and `Keep` are split
  into Core, Application, and Infrastructure projects; Entity Framework Core is confined to the
  Infrastructure boundary.
- **Database:** One PostgreSQL database, accessed with EF Core 10 and the Npgsql provider.
  Migrations, snake_case naming, and a single `OpHaloDbContext` are the persistence baseline.
- **Authentication:** Magic-link entry and recovery with trusted, opaque server-side sessions. The
  product does not use JWTs.
- **Email:** Resend in production; a console sender emits development magic/invite links locally.

## Storage, deployment, and operations

- **Object storage:** Private Cloudflare R2 through the application-owned
  `IBusinessDocumentStorage` seam, implemented with the AWS S3 SDK. The seam exists today; its
  first production use will be the separately preflighted equipment/work image-storage slice.
- **Hosting:** Vercel hosts the two web clients. Railway runs the containerized .NET API and its
  production runtime/database configuration.
- **Containerization:** The API is published from a .NET 10 SDK Docker build image and runs on the
  .NET 10 ASP.NET runtime image.
- **Diagnostics:** Railway and Vercel logs are the deployment-log sources. Errors-only Sentry is
  the selected pilot browser/API error-capture path; it is intentionally configured without session
  replay, performance tracing, or broad telemetry.

## Quality and developer tooling

- **Frontend tests:** Vitest, React Testing Library, and Testing Library user-event.
- **Backend tests:** xUnit, ASP.NET Core integration testing, Testcontainers PostgreSQL, and
  NetArchTest architecture rules.
- **Tooling:** pnpm, Node.js, TypeScript, the .NET SDK, Docker, and PostgreSQL for local
  development.

## Key constraints

- The production document bucket is private: no database blobs, public object URLs, or local-disk
  production fallback.
- The API is the authorization and tenancy authority; clients do not enforce access as the source
  of truth.
- The pilot favors a small operating footprint: Vercel, Railway, and the errors-only Sentry tier;
  no persistent staging environment or paid observability add-ons before demonstrated need.

## Source records

- `web/ophalo-app/package.json`
- `web/ophalo-web/package.json`
- `mobile/ophalo-mobile/package.json`
- `src/OpHalo.Api/OpHalo.Api.csproj`
- `src/OpHalo.Foundation.Infrastructure/OpHalo.Foundation.Infrastructure.csproj`
- `Dockerfile`
- `docs/decisions/ADR-236-mobile-native-app-technology-stack.md`
- `docs/decisions/ADR-377-web-client-surface-and-technology-stack.md`
