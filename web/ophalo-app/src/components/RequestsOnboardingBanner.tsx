import { CheckCircle2, Circle } from "lucide-react";
import { KeepButton } from "./keep/KeepButton";
import type { KeepBusinessSetupResult } from "../lib/apiClient";

interface RequestsOnboardingBannerProps {
  setup: KeepBusinessSetupResult;
  onNavigateSettings: (section?: "public-profile" | "policy" | "team") => void;
  onStartCapture: () => void;
  // Pane-mode's request-list column is a fixed 360px — the default layout below relies on
  // `sm:` (640px) breakpoints to go full-width, so at 360px it never resolves and the banner
  // renders squeezed/glitched. `compact` is a purpose-built stack-only layout for that column;
  // it never relies on `sm:` breakpoints. Undefined/false keeps today's full-width banner.
  compact?: boolean;
}

export function RequestsOnboardingBanner({
  setup,
  onNavigateSettings,
  onStartCapture,
  compact = false,
}: RequestsOnboardingBannerProps) {
  const requestPageReady = setup.businessInfoComplete && setup.createIntakePageComplete;
  const firstRequestReady = setup.addFirstRequestComplete;

  const steps = [
    {
      key: "request-page",
      label: "Set up your public request page",
      done: requestPageReady,
      onClick: () => onNavigateSettings("public-profile"),
    },
    {
      key: "first-request",
      label: "Add your first customer request",
      done: firstRequestReady,
      onClick: onStartCapture,
    },
    {
      key: "team",
      label: "Invite teammates when you're ready",
      done: setup.buildTeamComplete,
      onClick: () => onNavigateSettings("team"),
      optional: true,
    },
  ];

  const primaryCta = !requestPageReady
    ? { label: "Set up request page", onClick: () => onNavigateSettings("public-profile") }
    : !firstRequestReady
      ? { label: "Add your first request", onClick: onStartCapture }
      : { label: "Invite your team", onClick: () => onNavigateSettings("team") };

  if (compact) {
    return (
      <div
        role="region"
        aria-label="Onboarding"
        className="rounded-lg border border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] px-3 py-3"
      >
        <h2 className="text-xs font-semibold text-[var(--ophalo-navy)]">
          Set up your request page
        </h2>
        <KeepButton
          variant="primary"
          onClick={primaryCta.onClick}
          className="mt-2 w-full px-3 text-xs"
        >
          {primaryCta.label}
        </KeepButton>
        <ul className="mt-2.5 flex flex-col gap-1.5">
          {steps.map((step) => (
            <li key={step.key}>
              <button
                type="button"
                onClick={step.onClick}
                className="inline-flex w-full items-center gap-1.5 rounded-md text-left text-xs font-medium text-[var(--ophalo-navy)] hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
              >
                {step.done ? (
                  <CheckCircle2 className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-success)]" />
                ) : (
                  <Circle className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" />
                )}
                <span className={step.done ? "text-[var(--ophalo-muted)] line-through" : ""}>
                  {step.label}
                </span>
                {step.optional && !step.done && (
                  <span className="text-[var(--ophalo-muted)] font-normal">(optional)</span>
                )}
              </button>
            </li>
          ))}
        </ul>
      </div>
    );
  }

  return (
    <div
      role="region"
      aria-label="Onboarding"
      className="rounded-xl border border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] px-4 py-4 sm:px-5 sm:py-5"
    >
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h2 className="text-sm font-semibold text-[var(--ophalo-navy)]">
            Set up your customer request page
          </h2>
          <p className="mt-0.5 text-sm text-[var(--ophalo-muted)]">
            Give customers a clear place to start a request and keep work from slipping through.
          </p>
        </div>
        <KeepButton
          variant="primary"
          onClick={primaryCta.onClick}
          className="shrink-0"
        >
          {primaryCta.label}
        </KeepButton>
      </div>

      <ul className="mt-3 flex flex-col gap-1.5 sm:flex-row sm:flex-wrap sm:gap-x-5 sm:gap-y-1.5">
        {steps.map((step) => (
          <li key={step.key}>
            <button
              type="button"
              onClick={step.onClick}
              className="inline-flex items-center gap-1.5 rounded-md text-xs font-medium text-[var(--ophalo-navy)] hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
            >
              {step.done ? (
                <CheckCircle2 className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-success)]" />
              ) : (
                <Circle className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" />
              )}
              <span className={step.done ? "text-[var(--ophalo-muted)] line-through" : ""}>
                {step.label}
              </span>
              {step.optional && !step.done && (
                <span className="text-[var(--ophalo-muted)] font-normal">(optional)</span>
              )}
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
