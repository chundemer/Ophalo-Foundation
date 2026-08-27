import { useMemo, useState, type FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import { KeepModal } from "../../components/keep/KeepModal";
import { api, type ActualWorkHistoryResult } from "../../lib/apiClient";
import type { ActualWorkTransferOutcome } from "./useActualWorkCapture";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const REASON_MAX = 500;

interface ActualWorkRecoveryDrawerProps {
  /** The retained read-only Draft (Owner/Admin non-recorder view): id + version for the exact-version
   *  submit, plus the current recorder to exclude from the candidate list. */
  draft: NonNullable<ActualWorkHistoryResult["openDraft"]>;
  onClose: () => void;
  onTransfer: (
    newRecorderAccountUserId: string,
    newRecorderDisplayName: string,
    reason: string,
  ) => Promise<ActualWorkTransferOutcome>;
}

/**
 * 1a-ii-b: Owner/Admin-only recorder-transfer recovery for a stranded Actual Work Draft (GAP-055).
 * Picks one eligible recorder (the current recorder is excluded — a no-op transfer would still write
 * an immutable audit event), requires a reason, and submits against the exact retained Draft version.
 * `ineligible` (a stale/racing candidate list) and transient failures keep the drawer open for a
 * retry; a settled outcome (`transferred`/`stale`) closes it and the card shows the result.
 */
export function ActualWorkRecoveryDrawer({ draft, onClose, onTransfer }: ActualWorkRecoveryDrawerProps) {
  const [selectedId, setSelectedId] = useState("");
  const [reason, setReason] = useState("");
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const candidatesQuery = useQuery({
    queryKey: ["actual-work-recorder-candidates"],
    queryFn: () => api.getActualWorkRecorderCandidates(),
  });

  const candidates = useMemo(
    () => (candidatesQuery.data?.candidates ?? []).filter((c) => c.accountUserId !== draft.recorderAccountUserId),
    [candidatesQuery.data, draft.recorderAccountUserId],
  );

  const currentRecorderName = draft.recorderDisplayName ?? "the current recorder";
  const noEligibleCandidates = candidatesQuery.isSuccess && candidates.length === 0;

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (submitting) return;
    setFormError(null);
    if (!selectedId) {
      setFormError("Choose a team member to record this visit.");
      return;
    }
    if (!reason.trim()) {
      setFormError("A reason is required to reassign the recorder.");
      return;
    }
    const picked = candidates.find((c) => c.accountUserId === selectedId);
    if (!picked) {
      setFormError("That team member is no longer available. Refresh and try again.");
      void candidatesQuery.refetch();
      return;
    }

    setSubmitting(true);
    const outcome = await onTransfer(picked.accountUserId, picked.displayName, reason.trim());
    setSubmitting(false);

    if (outcome === "transferred" || outcome === "stale") {
      onClose();
      return;
    }
    if (outcome === "ineligible") {
      setFormError("That team member can't be assigned as the recorder. Pick someone else.");
      void candidatesQuery.refetch();
      return;
    }
    setFormError("Couldn't reassign the recorder. Check your connection and try again.");
  }

  return (
    <KeepModal
      onClose={onClose}
      label="Reassign recorder"
      backdropClassName="bg-black/30"
      panelClassName="fixed z-50 top-0 right-0 h-[100dvh] max-h-[100dvh] w-full sm:w-[460px] bg-[var(--ophalo-card)] shadow-xl flex flex-col"
    >
      <form onSubmit={handleSubmit} className="h-full min-h-0 flex flex-col">
        <div className="shrink-0 px-4 sm:px-6 py-4 border-b border-[var(--ophalo-border)] flex items-center justify-between">
          <h2 className="font-serif text-lg font-semibold text-[var(--ophalo-ink)]">Reassign recorder</h2>
          <button
            type="button"
            onClick={onClose}
            className={`rounded-lg px-2 py-1 text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
            aria-label="Close"
          >
            ×
          </button>
        </div>

        <div className="flex-1 min-h-0 overflow-y-auto px-4 sm:px-6 py-4 space-y-4">
          <p className="text-sm text-[var(--ophalo-muted)]">
            {currentRecorderName} is recording this visit. Reassigning transfers the open draft to another
            team member. This is recorded on the request&rsquo;s history and can&rsquo;t be undone.
          </p>

          {formError && (
            <div className="rounded-lg bg-[var(--ophalo-danger-bg)] px-3 py-2 text-sm text-[var(--ophalo-danger)]">
              {formError}
            </div>
          )}

          <div>
            <label htmlFor="recorder-candidate" className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">
              New recorder
            </label>
            {candidatesQuery.isLoading ? (
              <p className="text-sm text-[var(--ophalo-muted)]">Loading eligible team members…</p>
            ) : candidatesQuery.isError ? (
              <p className="text-sm text-[var(--ophalo-danger)]">
                Couldn&rsquo;t load eligible team members.{" "}
                <button
                  type="button"
                  onClick={() => void candidatesQuery.refetch()}
                  className={`font-medium text-[var(--keep-accent)] hover:underline ${FOCUS_RING}`}
                >
                  Retry
                </button>
              </p>
            ) : noEligibleCandidates ? (
              <p className="text-sm text-[var(--ophalo-muted)]">
                No other team member is eligible to record this visit.
              </p>
            ) : (
              <select
                id="recorder-candidate"
                value={selectedId}
                onChange={(e) => setSelectedId(e.target.value)}
                className={`w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2 text-sm text-[var(--ophalo-ink)] ${FOCUS_RING}`}
              >
                <option value="">Select a team member…</option>
                {candidates.map((c) => (
                  <option key={c.accountUserId} value={c.accountUserId}>
                    {c.displayName} · {c.role}
                  </option>
                ))}
              </select>
            )}
          </div>

          <div>
            <label htmlFor="recorder-reason" className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">
              Reason
            </label>
            <textarea
              id="recorder-reason"
              value={reason}
              maxLength={REASON_MAX}
              onChange={(e) => setReason(e.target.value)}
              rows={3}
              placeholder="Why is this draft being reassigned?"
              className={`w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2 text-sm text-[var(--ophalo-ink)] ${FOCUS_RING}`}
            />
          </div>
        </div>

        <div className="shrink-0 px-4 sm:px-6 py-4 border-t border-[var(--ophalo-border)] flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className={`rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={submitting || noEligibleCandidates || !candidatesQuery.isSuccess}
            className={`rounded-lg bg-[var(--keep-accent)] px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-60 ${FOCUS_RING}`}
          >
            {submitting ? "Reassigning…" : "Reassign recorder"}
          </button>
        </div>
      </form>
    </KeepModal>
  );
}
