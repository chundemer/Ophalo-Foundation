/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string;
  // Optional at the type level: `src/lib/publicBaseUrl.ts` validates it and `main.tsx`
  // renders a safe configuration-error screen when it is missing or malformed, so no
  // consumer may treat it as guaranteed. Read it only through `getPublicBaseUrl()`.
  readonly VITE_PUBLIC_BASE_URL?: string;
  // Public browser DSN for Sentry error capture (GAP-039). Optional: absent on local and
  // preview builds, in which case `initSentry()` is a no-op. A production Vercel build
  // fails without it — see `vite.config.ts`.
  readonly VITE_SENTRY_DSN?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

// Compile-time constants injected by `vite.config.ts` from the deployment environment.
// `__SENTRY_RELEASE__` is the commit SHA (`"local"` off a deployment); `__DEPLOY_ENV__` is
// `"production" | "preview" | "development"`.
declare const __SENTRY_RELEASE__: string;
declare const __DEPLOY_ENV__: string;
