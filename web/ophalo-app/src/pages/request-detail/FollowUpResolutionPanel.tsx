import { useState } from "react";
import { X } from "lucide-react";
import { api, ApiError, type KeepRequestDetailResult } from "../../lib/apiClient";
import type { FollowUpResolutionOutcome, FollowUpCompletionReason } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import {
  COMPLETION_REASON_LABELS,
  FOLLOW_UP_REASON_LABELS,
  INPUT_CLS,
  FOCUS_RING,
  STATUS_CONFLICT_MESSAGE,
  formatDateOnly,
} from "./helpers";

interface FollowUpResolutionPanelProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onClose: () => void;
}

const COMPLETION_REASONS: FollowUpCompletionReason[] = [
  "customer_contacted",
  "work_completed",
  "no_longer_needed",
  "other",
];

function todayIso(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
}

function addDays(isoDate: string, n: number): string {
  const [y, m, d] = isoDate.split("-").map(Number);
  const dt = new Date(y, m - 1, d + n);
  return `${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, "0")}-${String(dt.getDate()).padStart(2, "0")}`;
}

export function FollowUpResolutionPanel({
  requestId,
  detail,
  onDetailUpdated,
  onClose,
}: FollowUpResolutionPanelProps) {
  const { allowedFollowUpReasons } = detail.validation;

  const [outcome, setOutcome] = useState<FollowUpResolutionOutcome | null>(null);
  const [completionReason, setCompletionReason] = useState<FollowUpCompletionReason | "">("");
  const [note, setNote] = useState("");
  const [newDate, setNewDate] = useState(todayIso());
  const [newReason, setNewReason] = useState(detail.followUpOnReason ?? "");
  const [submitting, setSubmitting] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const today = todayIso();
  const tomorrow = addDays(today, 1);
  const nextWeek = addDays(today, 7);

  const currentFollowUpDate = detail.followUpOnDate;
  const currentReason = detail.followUpOnReason
    ? (FOLLOW_UP_REASON_LABELS[detail.followUpOnReason] ?? detail.followUpOnReason)
    : null;

  async function handleSubmit() {
    if (!outcome || submitting || conflict) return;
    if ((outcome === "complete" || outcome === "keep_active") && !completionReason) return;
    if (outcome === "move" && !newDate) return;

    setSubmitting(true);
    setError(null);
    try {
      const updated = await api.resolveFollowUp(
        requestId,
        {
          outcome,
          completionReason: completionReason || null,
          note: note.trim() || null,
          newDate: outcome === "move" ? newDate : null,
          newFollowUpReason: outcome === "move" ? (newReason || null) : null,
        },
        detail.version,
      );
      onDetailUpdated(updated);
      onClose();
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setConflict(true);
        setError(STATUS_CONFLICT_MESSAGE);
      } else {
        setError("Could not record follow-up. Try again.");
      }
    } finally {
      setSubmitting(false);
    }
  }

  const canSubmit =
    !submitting &&
    !conflict &&
    outcome !== null &&
    ((outcome === "complete" || outcome === "keep_active") ? !!completionReason : !!newDate);

  return (
    <div className="fixed inset-0 z-50" role="dialog" aria-modal="true" aria-label="Record follow-up outcome">
      {/* Backdrop */}
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />

      {/* Sheet bottom on mobile / centered modal on desktop */}
      <div className="absolute inset-x-0 bottom-0 sm:inset-0 sm:flex sm:items-center sm:justify-center sm:p-4">
        <div className="relative bg-[var(--ophalo-card)] rounded-t-2xl sm:rounded-2xl shadow-xl w-full sm:max-w-md max-h-[90vh] overflow-y-auto">

          {/* Drag handle — mobile only */}
          <div className="sm:hidden flex justify-center pt-3 pb-1">
            <div className="h-1 w-10 rounded-full bg-[var(--ophalo-border)]" />
          </div>

          {/* Header */}
          <div className="flex items-start justify-between px-5 pt-4 pb-3 border-b border-[var(--ophalo-border)]">
            <div>
              <p className="text-base font-semibold text-[var(--ophalo-ink)]">Record follow-up</p>
              {currentFollowUpDate && (
                <p className="text-xs text-[var(--ophalo-muted)] mt-0.5">
                  {formatDateOnly(currentFollowUpDate)}
                  {currentReason ? ` · ${currentReason}` : ""}
                </p>
              )}
            </div>
            <button
              type="button"
              onClick={onClose}
              className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors rounded-lg p-1 ${FOCUS_RING}`}
              aria-label="Close"
            >
              <X className="h-5 w-5" />
            </button>
          </div>

          {/* Body */}
          <div className="px-5 py-4 space-y-4">
            {conflict && error && (
              <div className="rounded-lg border border-[var(--ophalo-danger)] bg-[var(--ophalo-danger-bg)] px-3 py-2.5 text-xs text-[var(--ophalo-danger)]">
                {error}
              </div>
            )}

            {/* Outcome tap-first buttons */}
            <div>
              <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-2">What happened?</p>
              <div className="grid gap-2">
                <OutcomeButton
                  selected={outcome === "complete"}
                  onClick={() => { setOutcome("complete"); }}
                  label="Mark complete"
                  description="Follow-up is done — closes the promise"
                />
                <OutcomeButton
                  selected={outcome === "move"}
                  onClick={() => { setOutcome("move"); }}
                  label="Move to a new date"
                  description="Still needs follow-up, but on a different day"
                />
                <OutcomeButton
                  selected={outcome === "keep_active"}
                  onClick={() => { setOutcome("keep_active"); }}
                  label="Keep active"
                  description="Checked in but keeping this date as-is"
                />
              </div>
            </div>

            {/* Outcome-specific fields */}
            {(outcome === "complete" || outcome === "keep_active") && (
              <div className="space-y-3">
                <div>
                  <label htmlFor="follow-up-completion-reason" className="text-[11px] text-[var(--ophalo-muted)] block mb-1">
                    Why is this{outcome === "complete" ? " complete" : " still active"}?
                  </label>
                  <div className="grid gap-1.5">
                    {COMPLETION_REASONS.map((r) => (
                      <button
                        key={r}
                        type="button"
                        onClick={() => setCompletionReason(r)}
                        disabled={conflict}
                        className={`text-left rounded-lg border px-3 py-2.5 text-sm transition-colors ${FOCUS_RING} ${
                          completionReason === r
                            ? "border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] text-[var(--ophalo-ink)] font-medium"
                            : "border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-[var(--ophalo-ink)] hover:border-[var(--keep-accent)]"
                        } disabled:opacity-50`}
                      >
                        {COMPLETION_REASON_LABELS[r]}
                        {r === "work_completed" && (
                          <span className="block text-[11px] text-[var(--ophalo-muted)] font-normal mt-0.5">
                            Resolves this follow-up only — does not close the request
                          </span>
                        )}
                      </button>
                    ))}
                  </div>
                </div>
                <div>
                  <label htmlFor="follow-up-note" className="text-[11px] text-[var(--ophalo-muted)] block mb-0.5">Note (optional)</label>
                  <input
                    id="follow-up-note"
                    type="text"
                    value={note}
                    onChange={(e) => setNote(e.target.value)}
                    maxLength={detail.validation.followUpNoteMaxLength}
                    disabled={conflict}
                    placeholder="Optional note…"
                    className={INPUT_CLS}
                  />
                </div>
              </div>
            )}

            {outcome === "move" && (
              <div className="space-y-3">
                <div>
                  <p className="text-[11px] text-[var(--ophalo-muted)] mb-1.5">Quick pick</p>
                  <div className="flex flex-wrap gap-2">
                    {[
                      { label: "Today", value: today },
                      { label: "Tomorrow", value: tomorrow },
                      { label: "Next week", value: nextWeek },
                    ].map(({ label, value }) => (
                      <button
                        key={value}
                        type="button"
                        onClick={() => setNewDate(value)}
                        disabled={conflict}
                        className={`rounded-lg border px-3 py-1.5 text-sm transition-colors ${FOCUS_RING} ${
                          newDate === value
                            ? "border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] text-[var(--ophalo-ink)] font-medium"
                            : "border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-[var(--ophalo-ink)] hover:border-[var(--keep-accent)]"
                        } disabled:opacity-50`}
                      >
                        {label}
                      </button>
                    ))}
                  </div>
                </div>
                <div>
                  <label htmlFor="move-date" className="text-[11px] text-[var(--ophalo-muted)] block mb-0.5">Or choose a date</label>
                  <input
                    id="move-date"
                    type="date"
                    value={newDate}
                    onChange={(e) => setNewDate(e.target.value)}
                    disabled={conflict}
                    className={INPUT_CLS}
                  />
                </div>
                {allowedFollowUpReasons.length > 0 && (
                  <div>
                    <label htmlFor="move-reason" className="text-[11px] text-[var(--ophalo-muted)] block mb-0.5">Reason (optional)</label>
                    <select
                      id="move-reason"
                      value={newReason}
                      onChange={(e) => setNewReason(e.target.value)}
                      disabled={conflict}
                      className={INPUT_CLS}
                    >
                      <option value="">Same reason or none…</option>
                      {allowedFollowUpReasons.map((r) => (
                        <option key={r} value={r}>{FOLLOW_UP_REASON_LABELS[r] ?? r}</option>
                      ))}
                    </select>
                  </div>
                )}
                <div>
                  <label htmlFor="move-note" className="text-[11px] text-[var(--ophalo-muted)] block mb-0.5">Note (optional)</label>
                  <input
                    id="move-note"
                    type="text"
                    value={note}
                    onChange={(e) => setNote(e.target.value)}
                    maxLength={detail.validation.followUpNoteMaxLength}
                    disabled={conflict}
                    placeholder="Optional note…"
                    className={INPUT_CLS}
                  />
                </div>
              </div>
            )}

            {!conflict && error && (
              <p className="text-xs text-[var(--ophalo-danger)]">{error}</p>
            )}
          </div>

          {/* Footer */}
          <div className="px-5 pb-5 pt-1">
            <KeepButton
              type="button"
              variant="primary"
              disabled={!canSubmit}
              onClick={() => void handleSubmit()}
              className="w-full"
            >
              {submitting ? "Saving…" : "Save"}
            </KeepButton>
          </div>
        </div>
      </div>
    </div>
  );
}

interface OutcomeButtonProps {
  selected: boolean;
  onClick: () => void;
  label: string;
  description: string;
}

function OutcomeButton({ selected, onClick, label, description }: OutcomeButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`text-left rounded-xl border px-4 py-3 transition-colors ${FOCUS_RING} ${
        selected
          ? "border-[var(--keep-accent)] bg-[var(--keep-accent-bg)]"
          : "border-[var(--ophalo-border)] bg-[var(--ophalo-card)] hover:border-[var(--keep-accent)]"
      }`}
    >
      <p className={`text-sm font-semibold ${selected ? "text-[var(--ophalo-ink)]" : "text-[var(--ophalo-ink)]"}`}>{label}</p>
      <p className="text-[11px] text-[var(--ophalo-muted)] mt-0.5">{description}</p>
    </button>
  );
}
