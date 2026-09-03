/// <reference types="vitest" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { sentryVitePlugin } from "@sentry/vite-plugin";
import { resolveDeployment } from "./scripts/resolveDeployment";

// --- Deployment identity (GAP-039, ADR-495) -------------------------------------------------
// The PWA release and environment come from the deploy platform, never from
// `import.meta.env.PROD` — a Vercel *preview* is also a production `vite build`. See
// `scripts/resolveDeployment.ts` for the classification rules and fail-closed behavior.
const deployment = resolveDeployment(process.env);

export default defineConfig({
  plugins: [
    react(),
    // Build-only. Uploads release-matched source maps to Sentry, then physically deletes
    // every `.map` from `dist/` so they are never served in the Vercel artifact. The auth
    // token is a build secret. No `errorHandler`: a failed upload fails the build.
    ...(deployment.sentryUploadEnabled
      ? [
          sentryVitePlugin({
            org: deployment.sentryUpload.org,
            project: deployment.sentryUpload.project,
            authToken: deployment.sentryUpload.authToken,
            telemetry: false,
            release: { name: deployment.release },
            sourcemaps: { filesToDeleteAfterUpload: ["./dist/**/*.map"] },
          }),
        ]
      : []),
  ],
  build: {
    // "hidden" emits maps for upload but writes no `//# sourceMappingURL=` comment into the
    // shipped JS. Only generate them when the plugin is present to consume and delete them.
    sourcemap: deployment.sentryUploadEnabled ? "hidden" : false,
  },
  define: {
    __SENTRY_RELEASE__: JSON.stringify(deployment.release),
    __DEPLOY_ENV__: JSON.stringify(deployment.deployEnv),
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    globals: true,
  },
});
