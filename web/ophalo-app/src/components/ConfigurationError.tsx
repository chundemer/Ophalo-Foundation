/**
 * Shown by `main.tsx` when required client configuration (currently `VITE_PUBLIC_BASE_URL`)
 * is missing or malformed. Deliberately shows no configuration values, environment names,
 * or diagnostic detail. Reloading cannot fix a build-time configuration error, so no
 * recovery action is offered.
 */
export function ConfigurationError() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-[var(--ophalo-canvas)] px-4">
      <div className="max-w-sm w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-6 text-center">
        <p className="text-base font-semibold text-[var(--ophalo-ink)] mb-2">
          This app isn’t configured correctly
        </p>
        <p className="text-sm text-[var(--ophalo-muted)]">
          It can’t start right now. Please contact your administrator.
        </p>
      </div>
    </div>
  );
}
