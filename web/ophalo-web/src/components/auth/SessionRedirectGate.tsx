"use client";

import type { ReactNode } from "react";
import type { SessionCheck } from "@/lib/useSessionRedirect";

/**
 * Renders the shared checking/redirecting spinner or transient-error retry
 * card while a session check is in flight, and `children` once the caller
 * has confirmed the visitor is unauthenticated. Authenticated redirect is
 * handled by useSessionRedirect itself.
 */
export function SessionRedirectGate({
  sessionCheck,
  onRetry,
  children,
}: {
  sessionCheck: SessionCheck;
  onRetry: () => void;
  children: ReactNode;
}) {
  if (sessionCheck === "checking" || sessionCheck === "authenticated") {
    return (
      <div
        className="flex min-h-screen items-center justify-center bg-ophalo-canvas px-4"
        role="status"
        aria-label="Loading"
      >
        <div className="h-8 w-8 animate-pulse rounded-full bg-ophalo-navy/20" aria-hidden="true" />
      </div>
    );
  }

  if (sessionCheck === "error") {
    return (
      <div className="flex min-h-screen items-center justify-center bg-ophalo-canvas px-4">
        <div className="w-full max-w-sm rounded-2xl border border-ophalo-border bg-ophalo-card px-6 py-8 text-center shadow-sm">
          <p role="alert" className="text-sm text-ophalo-ink">
            We couldn&apos;t check your sign-in status. Please try again.
          </p>
          <button
            type="button"
            onClick={onRetry}
            className="mt-4 w-full rounded-lg bg-ophalo-navy px-5 py-3 text-sm font-semibold text-white transition hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-keep-accent focus-visible:ring-offset-2"
          >
            Try again
          </button>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}
