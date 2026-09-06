import { useQuery } from "@tanstack/react-query";
import { api } from "../../lib/apiClient";
import { getPublicBaseUrl } from "../../lib/publicBaseUrl";
import { KeepButton } from "../keep/KeepButton";

interface RequestsEmptyStatePanelProps {
  onNavigateSettings: (section?: "public-profile" | "policy" | "team") => void;
  onStartCapture: () => void;
  // Pane-mode's request-list column is a fixed 360px — mirrors RequestsOnboardingBanner's
  // former `compact` prop so the panel never relies on `sm:` breakpoints there.
  compact?: boolean;
}

// BL142 Session 3: replaces the removed Getting Started page and RequestsOnboardingBanner
// checklist. States a fact (the link exists by default, ADR-428) — no steps, score, or
// completion — and offers the two real ways a first request comes in.
export function RequestsEmptyStatePanel({
  onNavigateSettings,
  onStartCapture,
  compact = false,
}: RequestsEmptyStatePanelProps) {
  const { data: intake, isLoading } = useQuery({
    queryKey: ["intake"],
    queryFn: api.getIntake,
    staleTime: 2 * 60 * 1000,
  });

  const linkUrl =
    intake?.hasActiveLink && intake.publicSlug
      ? `${getPublicBaseUrl()}/keep/s/${intake.publicSlug}`
      : null;

  const heading = isLoading
    ? "Checking your public request link"
    : linkUrl
      ? "Your public request link is live"
      : "Your public request link is being set up";

  return (
    <div
      role="region"
      aria-label="Get your first request"
      className={
        compact
          ? "rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-3"
          : "rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-4 sm:px-5 sm:py-5"
      }
    >
      <h2 className={compact ? "text-xs font-semibold text-[var(--ophalo-navy)]" : "text-sm font-semibold text-[var(--ophalo-navy)]"}>
        {heading}
      </h2>
      {isLoading ? (
        <div
          className="mt-2 h-4 w-48 max-w-full animate-pulse rounded bg-[var(--ophalo-border)]"
          aria-label="Loading your public request link"
          role="status"
        />
      ) : linkUrl ? (
        <p className={compact ? "mt-1 text-xs text-[var(--ophalo-muted)]" : "mt-0.5 text-sm text-[var(--ophalo-muted)]"}>
          Customers can start a request there any time.
        </p>
      ) : (
        <p className={compact ? "mt-1 text-xs text-[var(--ophalo-ink)]" : "mt-0.5 text-sm text-[var(--ophalo-ink)]"}>
          Your link is being set up.{" "}
          <button
            type="button"
            onClick={() => onNavigateSettings("public-profile")}
            className="font-medium text-[var(--keep-accent)] underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
          >
            Check in Settings
          </button>
        </p>
      )}

      <div className={compact ? "mt-2.5 flex flex-col gap-1.5" : "mt-3 flex flex-wrap gap-2"}>
        {linkUrl && (
          <a
            href={linkUrl}
            target="_blank"
            rel="noreferrer"
            className={
              compact
                ? "inline-flex w-full items-center justify-center rounded-md border border-[var(--ophalo-border)] px-3 py-1.5 text-xs font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
                : "inline-flex items-center rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
            }
          >
            Open customer view
          </a>
        )}
        <KeepButton
          variant="primary"
          onClick={onStartCapture}
          className={compact ? "w-full px-3 text-xs" : "shrink-0"}
        >
          Add your first request
        </KeepButton>
      </div>
    </div>
  );
}
