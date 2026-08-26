const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

interface ConnectionFailureBannerProps {
  message: string;
  onRetry: () => void;
  isRetrying: boolean;
}

/**
 * Slice 5a: the composer-level recovery point for a mutation that failed with a non-`ApiError`
 * (network/transport) rejection — never for a validation (400) or conflict (409) response, which
 * keep their existing local, operation-specific treatment. Owns the consistent field copy so every
 * call site only supplies what failed and how to retry it exactly.
 */
export function ConnectionFailureBanner({ message, onRetry, isRetrying }: ConnectionFailureBannerProps) {
  return (
    <div
      role="alert"
      className="flex items-center justify-between gap-3 rounded-lg border border-[var(--ophalo-danger)] bg-[var(--ophalo-danger-bg)] px-3 py-2 text-sm text-[var(--ophalo-danger)]"
    >
      <span>
        {message} Check your connection and retry.
      </span>
      <button
        type="button"
        disabled={isRetrying}
        onClick={onRetry}
        className={`shrink-0 text-xs font-semibold text-[var(--ophalo-danger)] underline disabled:opacity-60 ${FOCUS_RING}`}
      >
        {isRetrying ? "Retrying…" : "Retry"}
      </button>
    </div>
  );
}
