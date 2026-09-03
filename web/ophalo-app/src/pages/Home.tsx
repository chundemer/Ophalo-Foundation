import { useQuery } from "@tanstack/react-query";
import { api, type AccountRole } from "../lib/apiClient";
import { getPublicBaseUrl } from "../lib/publicBaseUrl";
import { useCopyFeedback } from "../hooks/useCopyFeedback";
import { KeepButton } from "../components/keep/KeepButton";
import { AccessLimited } from "./AccessLimited";

interface HomeProps {
  onStartCapture: () => void;
  role: AccountRole;
  onNavigateSettings: (section?: "public-profile" | "policy" | "team") => void;
  onNavigateRequests: () => void;
}

function OperatorHome() {
  return (
    <div className="keep-settings-frame pt-6 pb-10 sm:pt-8">
      <div className="max-w-xl">
        <h1 className="keep-page-title tracking-tight">Your workspace</h1>
        <p className="mt-1.5 keep-page-subtitle">
          Head to Requests to view and manage customer requests.
        </p>
      </div>
    </div>
  );
}

const OPTION_ROW_CLASS =
  "w-full text-left rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3.5 py-3 transition-colors hover:bg-[var(--keep-accent-bg)]/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]";

function OwnerHome({ onStartCapture, onNavigateSettings }: Omit<HomeProps, "role" | "onNavigateRequests">) {
  const { data: intake, isLoading } = useQuery({
    queryKey: ["intake"],
    queryFn: api.getIntake,
    staleTime: 2 * 60 * 1000,
  });
  const { copiedId, failedId, copy } = useCopyFeedback();

  const linkUrl =
    intake?.hasActiveLink && intake.publicSlug
      ? `${getPublicBaseUrl()}/keep/s/${intake.publicSlug}`
      : null;

  return (
    <div className="keep-settings-frame pt-6 pb-10 sm:pt-8">
      <div className="space-y-6">
        <div>
          <h1 className="keep-page-title tracking-tight">Getting started</h1>
          <p className="mt-1.5 keep-page-subtitle">
            Keep is ready — here's your setup at a glance.
          </p>
        </div>

        {/* Readiness panel: the one elevated surface and Keep-teal moment. It states a
            fact (the link exists by default, ADR-428) — not a claim about delivery or
            verification. Verification, not a checklist: no steps, score, or completion. */}
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-5 shadow-md sm:p-6">
          <div className="flex items-center gap-2">
            <span className="h-2 w-2 shrink-0 rounded-full bg-[var(--keep-accent)]" aria-hidden="true" />
            <h2 className="keep-row-title">Your business is live on Keep</h2>
          </div>
          <p className="mt-1 text-sm text-[var(--ophalo-muted)]">
            Customers can send you requests through your public link. Nothing else is required to start.
          </p>

          <div className="mt-4 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] p-3">
            <p className="mb-1 text-xs text-[var(--ophalo-muted)]">Public request link</p>
            {isLoading ? (
              <div
                className="h-5 w-64 max-w-full animate-pulse rounded bg-[var(--ophalo-border)]"
                aria-label="Loading your public request link"
                role="status"
              />
            ) : linkUrl ? (
              <>
                <p className="mb-2 break-all font-mono text-sm text-[var(--ophalo-ink)]">{linkUrl}</p>
                <div className="flex gap-2">
                  <button
                    type="button"
                    onClick={() => void copy(linkUrl, "gs-link")}
                    className="rounded-lg bg-[var(--ophalo-navy)] px-3 py-1.5 text-xs font-medium text-white hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
                  >
                    {copiedId === "gs-link"
                      ? "Copied!"
                      : failedId === "gs-link"
                        ? "Couldn't copy"
                        : "Copy link"}
                  </button>
                  <a
                    href={linkUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-xs font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
                  >
                    Open ↗
                  </a>
                </div>
              </>
            ) : (
              <p className="text-sm text-[var(--ophalo-ink)]">
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
          </div>
        </div>

        {/* Optional adjustments — visibly subordinate to the readiness panel. Same
            deep-link targets as before; Keep stays usable without any of them. */}
        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)]">
            Optional — adjust when you want to
          </p>
          <div className="grid gap-3 sm:grid-cols-3">
            <button type="button" onClick={() => onNavigateSettings("public-profile")} className={OPTION_ROW_CLASS}>
              <p className="text-sm font-medium text-[var(--ophalo-ink)]">Business profile</p>
              <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">Name, phone, email, and logo customers see.</p>
            </button>
            <button type="button" onClick={() => onNavigateSettings("policy")} className={OPTION_ROW_CLASS}>
              <p className="text-sm font-medium text-[var(--ophalo-ink)]">Response targets</p>
              <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">Defaults work well for most service businesses.</p>
            </button>
            <button type="button" onClick={() => onNavigateSettings("team")} className={OPTION_ROW_CLASS}>
              <p className="text-sm font-medium text-[var(--ophalo-ink)]">Invite teammates</p>
              <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">Solo works great — add people whenever you need them.</p>
            </button>
          </div>
        </div>

        <div>
          <KeepButton variant="primary" onClick={onStartCapture}>
            Add your first customer request
          </KeepButton>
        </div>
      </div>
    </div>
  );
}

export function Home({ onStartCapture, role, onNavigateSettings, onNavigateRequests: _ }: HomeProps) {
  if (role === "viewer") return <AccessLimited />;
  if (role === "operator") return <OperatorHome />;
  if (role === "unknown") return null;
  return (
    <OwnerHome
      onStartCapture={onStartCapture}
      onNavigateSettings={onNavigateSettings}
    />
  );
}
