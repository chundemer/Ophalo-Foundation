import { ApiError } from "../../lib/apiClient";
import { FOCUS_RING } from "./helpers";

interface RequestDetailStatesProps {
  isLoading: boolean;
  isError: boolean;
  error: unknown;
  isFetching: boolean;
  onRetry: () => void;
}

function RequestDetailSkeleton() {
  const pulse = "animate-pulse motion-reduce:animate-none rounded bg-[var(--ophalo-canvas)]";
  return (
    <div aria-busy="true" aria-label="Loading request details" className="flex flex-1 min-h-0 overflow-hidden md:grid md:[grid-template-columns:minmax(0,7fr)_minmax(320px,3fr)]">
      <div className="flex-1 md:flex-none overflow-y-auto px-4 md:px-6 py-5 space-y-4">
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-5"><div className="flex gap-2 mb-3"><div className={`h-5 w-16 ${pulse}`} /><div className={`h-5 w-24 ${pulse}`} /></div><div className={`h-8 w-56 ${pulse}`} /></div>
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4 space-y-3"><div className="flex gap-2"><div className={`h-8 w-36 ${pulse}`} /><div className={`h-8 w-28 ${pulse}`} /></div><div className={`h-24 w-full ${pulse}`} /></div>
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4 space-y-3"><div className={`h-4 w-20 ${pulse}`} /><div className={`h-3 w-48 ${pulse}`} /></div>
      </div>
      <div className="hidden md:flex md:flex-col border-l border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-5 gap-4"><div className={`h-24 w-full ${pulse}`} /><div className={`h-16 w-full ${pulse}`} /><div className={`h-16 w-full ${pulse}`} /></div>
    </div>
  );
}

export function RequestDetailStates({ isLoading, isError, error, isFetching, onRetry }: RequestDetailStatesProps) {
  if (isLoading) return <RequestDetailSkeleton />;
  if (!isError) return null;
  const inaccessible = error instanceof ApiError && (error.status === 403 || error.status === 404);
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-3 px-4">
      <span className="text-[var(--ophalo-muted)] text-sm text-center">
        {error instanceof ApiError && error.status === 403 ? "You don't have access to this request." : error instanceof ApiError && error.status === 404 ? "Request not found." : "Something went wrong loading this request."}
      </span>
      {!inaccessible && <button type="button" onClick={onRetry} disabled={isFetching} className={`px-4 py-2 text-sm font-semibold rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${FOCUS_RING}`}>{isFetching ? "Retrying…" : "Retry"}</button>}
    </div>
  );
}
