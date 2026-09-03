/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string;
  // Optional at the type level: `src/lib/publicBaseUrl.ts` validates it and `main.tsx`
  // renders a safe configuration-error screen when it is missing or malformed, so no
  // consumer may treat it as guaranteed. Read it only through `getPublicBaseUrl()`.
  readonly VITE_PUBLIC_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
