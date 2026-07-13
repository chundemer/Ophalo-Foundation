import { useState } from "react";
import { Clock } from "lucide-react";
import { api, ApiError, type KeepRequestDetailResult } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import {
  FOLLOW_UP_REASON_LABELS,
  FOCUS_RING,
  INPUT_CLS,
  STATUS_CONFLICT_MESSAGE,
  formatDateOnly,
} from "./helpers";

interface TimingPanelProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onRecordFollowUp?: () => void;
}

export function TimingPanel({ requestId, detail, onDetailUpdated, onRecordFollowUp }: TimingPanelProps) {
  const { canSetFollowUpOn, canSetPlannedFor } = detail.availableActions;
  const { followUpNoteMaxLength, allowedFollowUpReasons } = detail.validation;

  const [followUpDate, setFollowUpDate] = useState(detail.followUpOnDate ?? "");
  const [followUpReason, setFollowUpReason] = useState(detail.followUpOnReason ?? "");
  const [followUpNote, setFollowUpNote] = useState(detail.followUpOnNote ?? "");
  const [followUpSubmitting, setFollowUpSubmitting] = useState(false);
  const [followUpConflict, setFollowUpConflict] = useState(false);
  const [followUpError, setFollowUpError] = useState<string | null>(null);

  const [plannedDate, setPlannedDate] = useState(detail.plannedForDate ?? "");
  const [plannedSubmitting, setPlannedSubmitting] = useState(false);
  const [plannedConflict, setPlannedConflict] = useState(false);
  const [plannedError, setPlannedError] = useState<string | null>(null);

  if (!canSetFollowUpOn && !canSetPlannedFor) return null;

  const hasFollowUp = !!detail.followUpOnDate;
  const hasPlanned = !!detail.plannedForDate;
  const hasActiveTiming = hasFollowUp || hasPlanned;

  async function handleSetFollowUp(e: React.FormEvent) {
    e.preventDefault();
    if (!followUpDate || !followUpReason || followUpSubmitting || followUpConflict) return;
    setFollowUpSubmitting(true);
    setFollowUpError(null);
    try {
      const updated = await api.setFollowUpOn(
        requestId,
        { date: followUpDate, reason: followUpReason, note: followUpNote.trim() || null },
        detail.version,
      );
      onDetailUpdated(updated);
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setFollowUpConflict(true);
        setFollowUpError(STATUS_CONFLICT_MESSAGE);
      } else {
        setFollowUpError("Could not set follow-up. Try again.");
      }
    } finally {
      setFollowUpSubmitting(false);
    }
  }

  async function handleClearFollowUp() {
    if (followUpSubmitting || followUpConflict) return;
    setFollowUpSubmitting(true);
    setFollowUpError(null);
    try {
      const updated = await api.clearFollowUpOn(requestId, detail.version);
      onDetailUpdated(updated);
      setFollowUpDate("");
      setFollowUpReason("");
      setFollowUpNote("");
      setFollowUpConflict(false);
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setFollowUpConflict(true);
        setFollowUpError(STATUS_CONFLICT_MESSAGE);
      } else {
        setFollowUpError("Could not clear follow-up. Try again.");
      }
    } finally {
      setFollowUpSubmitting(false);
    }
  }

  async function handleSetPlanned(e: React.FormEvent) {
    e.preventDefault();
    if (!plannedDate || plannedSubmitting || plannedConflict) return;
    setPlannedSubmitting(true);
    setPlannedError(null);
    try {
      const updated = await api.setPlannedFor(requestId, { date: plannedDate }, detail.version);
      onDetailUpdated(updated);
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setPlannedConflict(true);
        setPlannedError(STATUS_CONFLICT_MESSAGE);
      } else {
        setPlannedError("Could not set planned date. Try again.");
      }
    } finally {
      setPlannedSubmitting(false);
    }
  }

  async function handleClearPlanned() {
    if (plannedSubmitting || plannedConflict) return;
    setPlannedSubmitting(true);
    setPlannedError(null);
    try {
      const updated = await api.clearPlannedFor(requestId, detail.version);
      onDetailUpdated(updated);
      setPlannedDate("");
      setPlannedConflict(false);
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setPlannedConflict(true);
        setPlannedError(STATUS_CONFLICT_MESSAGE);
      } else {
        setPlannedError("Could not clear planned date. Try again.");
      }
    } finally {
      setPlannedSubmitting(false);
    }
  }

  return (
    <div>
      <p className="px-1 text-[10px] font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] mb-2">
        Follow-up &amp; planned timing
      </p>
      <div
        className={`rounded-xl border bg-[var(--ophalo-card)] px-4 py-3 space-y-4 ${
          hasActiveTiming
            ? "border-[var(--keep-accent)] border-l-4"
            : "border-[var(--ophalo-border)]"
        }`}
      >
        <div className="flex items-start gap-2">
          <div
            className={`mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-full ${
              hasActiveTiming
                ? "bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
                : "bg-[var(--ophalo-canvas)] text-[var(--ophalo-muted)]"
            }`}
            aria-hidden="true"
          >
            <Clock className="h-3.5 w-3.5" />
          </div>
          <p className="text-[11px] leading-5 text-[var(--ophalo-muted)]">
          Timing is internal and does not automatically notify the customer.
          </p>
        </div>

        {canSetFollowUpOn && (
          <div className={hasFollowUp ? "rounded-lg bg-[var(--keep-accent-bg)] px-3 py-2.5" : ""}>
            <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-2">Follow up on</p>
            {hasFollowUp ? (
              <div className="space-y-1">
                <p className="text-sm font-semibold text-[var(--ophalo-ink)]">
                  {formatDateOnly(detail.followUpOnDate!)}
                </p>
                {detail.followUpOnReason && (
                  <p className="text-xs font-medium text-[var(--keep-accent)]">
                    {FOLLOW_UP_REASON_LABELS[detail.followUpOnReason] ?? detail.followUpOnReason}
                  </p>
                )}
                {detail.followUpOnNote && (
                  <p className="text-xs leading-5 text-[var(--ophalo-ink)]">{detail.followUpOnNote}</p>
                )}
                {followUpError && (
                  <p className="text-xs text-[var(--ophalo-danger)]">{followUpError}</p>
                )}
                <div className="flex flex-wrap items-center gap-3 pt-0.5">
                  {onRecordFollowUp && (
                    <button
                      type="button"
                      onClick={onRecordFollowUp}
                      disabled={followUpSubmitting || followUpConflict}
                      className={`text-xs font-semibold text-[var(--keep-accent)] hover:text-[var(--ophalo-ink)] disabled:opacity-50 transition-colors ${FOCUS_RING} rounded`}
                    >
                      Record follow-up
                    </button>
                  )}
                  <button
                    type="button"
                    onClick={() => void handleClearFollowUp()}
                    disabled={followUpSubmitting || followUpConflict}
                    className={`text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] disabled:opacity-50 transition-colors ${FOCUS_RING} rounded`}
                  >
                    Clear follow-up
                  </button>
                </div>
              </div>
            ) : (
              <form onSubmit={(e) => void handleSetFollowUp(e)} className="space-y-2">
                <div>
                  <label htmlFor="follow-up-date" className="text-[11px] text-[var(--ophalo-muted)] block mb-0.5">Date</label>
                  <input
                    id="follow-up-date"
                    type="date"
                    value={followUpDate}
                    onChange={(e) => setFollowUpDate(e.target.value)}
                    disabled={followUpConflict}
                    className={INPUT_CLS}
                  />
                </div>
                <div>
                  <label htmlFor="follow-up-reason" className="text-[11px] text-[var(--ophalo-muted)] block mb-0.5">Reason</label>
                  <select
                    id="follow-up-reason"
                    value={followUpReason}
                    onChange={(e) => setFollowUpReason(e.target.value)}
                    disabled={followUpConflict}
                    className={INPUT_CLS}
                  >
                    <option value="">Select reason…</option>
                    {allowedFollowUpReasons.map((r) => (
                      <option key={r} value={r}>{r}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label htmlFor="follow-up-note" className="text-[11px] text-[var(--ophalo-muted)] block mb-0.5">Note (optional)</label>
                  <input
                    id="follow-up-note"
                    type="text"
                    value={followUpNote}
                    onChange={(e) => setFollowUpNote(e.target.value)}
                    maxLength={followUpNoteMaxLength}
                    disabled={followUpConflict}
                    placeholder="Optional note…"
                    className={INPUT_CLS}
                  />
                </div>
                {followUpError && (
                  <p className="text-xs text-[var(--ophalo-danger)]">{followUpError}</p>
                )}
                <KeepButton
                  type="submit"
                  variant="secondary"
                  disabled={!followUpDate || !followUpReason || followUpSubmitting || followUpConflict}
                  className="w-full"
                >
                  {followUpSubmitting ? "Saving…" : "Set follow-up"}
                </KeepButton>
              </form>
            )}
          </div>
        )}

        {canSetPlannedFor && (
          <div className={hasPlanned ? "rounded-lg bg-[var(--ophalo-canvas)] px-3 py-2.5" : ""}>
            <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-2">Planned for</p>
            <form onSubmit={(e) => void handleSetPlanned(e)} className="space-y-2">
              {hasPlanned && (
                <div className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2">
                  <p className="text-[11px] text-[var(--ophalo-muted)]">Current planned date</p>
                  <p className="mt-1 text-sm font-semibold text-[var(--ophalo-ink)]">
                    {formatDateOnly(detail.plannedForDate!)}
                  </p>
                </div>
              )}
              <div>
                <label htmlFor="planned-date" className="text-[11px] text-[var(--ophalo-muted)] block mb-0.5">
                  {hasPlanned ? "Change planned date" : "Date"}
                </label>
                <input
                  id="planned-date"
                  type="date"
                  value={plannedDate}
                  onChange={(e) => setPlannedDate(e.target.value)}
                  disabled={plannedConflict}
                  className={INPUT_CLS}
                />
              </div>
              {plannedError && (
                <p className="text-xs text-[var(--ophalo-danger)]">{plannedError}</p>
              )}
              <div className="grid gap-2">
                <KeepButton
                  type="submit"
                  variant="secondary"
                  disabled={!plannedDate || plannedSubmitting || plannedConflict}
                  className="w-full"
                >
                  {plannedSubmitting
                    ? "Saving…"
                    : hasPlanned ? "Save planned date" : "Set planned date"}
                </KeepButton>
                {hasPlanned && (
                  <button
                    type="button"
                    onClick={() => void handleClearPlanned()}
                    disabled={plannedSubmitting || plannedConflict}
                    className={`inline-flex min-h-[42px] items-center justify-center rounded-lg border border-[var(--ophalo-danger)] px-4 text-sm font-semibold text-[var(--ophalo-danger)] hover:bg-[var(--ophalo-danger-bg)] disabled:opacity-50 transition-colors ${FOCUS_RING}`}
                  >
                    Remove planned date
                  </button>
                )}
              </div>
            </form>
          </div>
        )}
      </div>
    </div>
  );
}
