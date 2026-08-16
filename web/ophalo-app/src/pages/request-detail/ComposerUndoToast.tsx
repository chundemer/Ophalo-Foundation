import { useEffect } from "react";
import { useMutation } from "@tanstack/react-query";
import { api, ApiError } from "../../lib/apiClient";

const UNDO_WINDOW_MS = 5000;

export interface PendingUndo {
  lineId: string;
  version: string;
  label: string;
}

interface ComposerUndoToastProps {
  proposedScopeId: string;
  pendingUndo: PendingUndo;
  onRestored: () => void;
  onConflict: (message?: string) => void;
  onExpire: () => void;
}

/**
 * Session 5D, build-log/120: the five-second Undo for a confirmed line removal (ADR-485). Restore
 * always uses the version the delete response itself returned, captured by the caller at removal
 * time — never the scope's current version, which may have moved on for unrelated reasons (another
 * edit, a Quick action) by the time Undo is pressed. The server's `RemovedAtUtc` check remains the
 * sole expiration authority; this client timer only controls when the toast itself disappears.
 */
export function ComposerUndoToast({ proposedScopeId, pendingUndo, onRestored, onConflict, onExpire }: ComposerUndoToastProps) {
  useEffect(() => {
    const handle = setTimeout(onExpire, UNDO_WINDOW_MS);
    return () => clearTimeout(handle);
  }, [pendingUndo, onExpire]);

  const restoreMutation = useMutation({
    mutationFn: () => api.restoreProposedScopeLine(proposedScopeId, pendingUndo.lineId, pendingUndo.version),
    onSuccess: onRestored,
    onError: (err) => {
      if (
        err instanceof ApiError &&
        (err.code === "ProposedScope.RestoreExpired" || err.code === "ProposedScope.RestoreLineAlreadyExists")
      ) {
        onConflict("This item can no longer be undone — refreshed with the latest scope.");
        return;
      }
      onConflict();
    },
  });

  return (
    <div
      role="status"
      aria-live="polite"
      className="flex items-center justify-between gap-2 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-sm text-[var(--ophalo-ink)]"
    >
      <span className="min-w-0 truncate">Removed "{pendingUndo.label}".</span>
      <button
        type="button"
        disabled={restoreMutation.isPending}
        onClick={() => restoreMutation.mutate()}
        className="shrink-0 text-xs font-medium text-[var(--keep-accent)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
      >
        Undo
      </button>
    </div>
  );
}
