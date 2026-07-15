import { useState } from "react";
import { Clock, ChevronDown, ChevronUp } from "lucide-react";
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

  const [expandedEditor, setExpandedEditor] = useState<"followUp" | "planned" | null>(null);

  const [editorFollowUpDate, setEditorFollowUpDate] = useState("");
  const [editorFollowUpReason, setEditorFollowUpReason] = useState("");
  const [editorFollowUpNote, setEditorFollowUpNote] = useState("");
  const [followUpSubmitting, setFollowUpSubmitting] = useState(false);
  const [followUpConflict, setFollowUpConflict] = useState(false);
  const [followUpError, setFollowUpError] = useState<string | null>(null);

  const [editorPlannedDate, setEditorPlannedDate] = useState("");
  const [plannedSubmitting, setPlannedSubmitting] = useState(false);
  const [plannedConflict, setPlannedConflict] = useState(false);
  const [plannedError, setPlannedError] = useState<string | null>(null);

  if (!canSetFollowUpOn && !canSetPlannedFor) return null;

  const hasFollowUp = !!detail.followUpOnDate;
  const hasPlanned = !!detail.plannedForDate;
  const hasActiveTiming = hasFollowUp || hasPlanned;

  function openEditor(which: "followUp" | "planned") {
    if (which === "followUp") {
      setEditorFollowUpDate(detail.followUpOnDate ?? "");
      setEditorFollowUpReason(detail.followUpOnReason ?? "");
      setEditorFollowUpNote(detail.followUpOnNote ?? "");
      setFollowUpError(null);
      setFollowUpConflict(false);
    } else {
      setEditorPlannedDate(detail.plannedForDate ?? "");
      setPlannedError(null);
      setPlannedConflict(false);
    }
    setExpandedEditor(which);
  }

  function closeEditor() {
    setExpandedEditor(null);
  }

  async function handleSetFollowUp(e: React.FormEvent) {
    e.preventDefault();
    if (!editorFollowUpDate || !editorFollowUpReason || followUpSubmitting || followUpConflict) return;
    setFollowUpSubmitting(true);
    setFollowUpError(null);
    try {
      const updated = await api.setFollowUpOn(
        requestId,
        { date: editorFollowUpDate, reason: editorFollowUpReason, note: editorFollowUpNote.trim() || null },
        detail.version,
      );
      onDetailUpdated(updated);
      closeEditor();
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
      closeEditor();
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
    if (!editorPlannedDate || plannedSubmitting || plannedConflict) return;
    setPlannedSubmitting(true);
    setPlannedError(null);
    try {
      const updated = await api.setPlannedFor(requestId, { date: editorPlannedDate }, detail.version);
      onDetailUpdated(updated);
      closeEditor();
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
      closeEditor();
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
        className={`rounded-xl border bg-[var(--ophalo-card)] divide-y divide-[var(--ophalo-border)] ${
          hasActiveTiming ? "border-[var(--keep-accent)] border-l-4" : "border-[var(--ophalo-border)]"
        }`}
      >
        {/* Info row */}
        <div className="flex items-center gap-2 px-4 py-2.5">
          <Clock
            className={`h-3.5 w-3.5 shrink-0 ${hasActiveTiming ? "text-[var(--keep-accent)]" : "text-[var(--ophalo-muted)]"}`}
            aria-hidden="true"
          />
          <p className="text-[11px] leading-5 text-[var(--ophalo-muted)]">
            Internal — does not notify the customer.
          </p>
        </div>

        {/* Follow-up section */}
        {canSetFollowUpOn && (
          <div className="px-4 py-3 space-y-2">
            <button
              type="button"
              aria-expanded={expandedEditor === "followUp"}
              aria-controls="timing-followup-editor"
              onClick={() => expandedEditor === "followUp" ? closeEditor() : openEditor("followUp")}
              className={`w-full flex items-center justify-between gap-2 text-left ${FOCUS_RING} rounded`}
            >
              <div className="min-w-0 flex-1">
                {hasFollowUp ? (
                  <div>
                    <p className="text-sm font-semibold text-[var(--ophalo-ink)]">
                      Follow up: {formatDateOnly(detail.followUpOnDate!)}
                    </p>
                    {detail.followUpOnReason && (
                      <p className="text-xs font-medium text-[var(--keep-accent)]">
                        {FOLLOW_UP_REASON_LABELS[detail.followUpOnReason] ?? detail.followUpOnReason}
                      </p>
                    )}
                    {detail.followUpOnNote && (
                      <p className="text-xs text-[var(--ophalo-muted)] truncate">{detail.followUpOnNote}</p>
                    )}
                  </div>
                ) : (
                  <p className="text-sm text-[var(--ophalo-muted)]">Set follow-up</p>
                )}
              </div>
              <span className="shrink-0 text-[var(--ophalo-muted)]" aria-hidden="true">
                {expandedEditor === "followUp"
                  ? <ChevronUp className="h-4 w-4" />
                  : hasFollowUp
                    ? <span className="text-xs font-semibold">Edit</span>
                    : <ChevronDown className="h-4 w-4" />
                }
              </span>
            </button>

            {/* Inline editor — always in DOM so aria-controls is valid */}
            <div id="timing-followup-editor" hidden={expandedEditor !== "followUp"}>
              {followUpError && expandedEditor === "followUp" && (
                <p className={`mb-2 text-xs ${followUpConflict ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-danger)]"}`}>
                  {followUpError}
                </p>
              )}
              <form onSubmit={(e) => void handleSetFollowUp(e)} className="space-y-2">
                <div>
                  <label htmlFor="follow-up-date" className="text-[11px] text-[var(--ophalo-muted)] block mb-0.5">Date</label>
                  <input
                    id="follow-up-date"
                    type="date"
                    value={editorFollowUpDate}
                    onChange={(e) => setEditorFollowUpDate(e.target.value)}
                    disabled={followUpConflict}
                    className={INPUT_CLS}
                  />
                </div>
                <div>
                  <label htmlFor="follow-up-reason" className="text-[11px] text-[var(--ophalo-muted)] block mb-0.5">Reason</label>
                  <select
                    id="follow-up-reason"
                    value={editorFollowUpReason}
                    onChange={(e) => setEditorFollowUpReason(e.target.value)}
                    disabled={followUpConflict}
                    className={INPUT_CLS}
                  >
                    <option value="">Select reason…</option>
                    {allowedFollowUpReasons.map((r) => (
                      <option key={r} value={r}>{FOLLOW_UP_REASON_LABELS[r] ?? r}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label htmlFor="follow-up-note" className="text-[11px] text-[var(--ophalo-muted)] block mb-0.5">Note (optional)</label>
                  <input
                    id="follow-up-note"
                    type="text"
                    value={editorFollowUpNote}
                    onChange={(e) => setEditorFollowUpNote(e.target.value)}
                    maxLength={followUpNoteMaxLength}
                    disabled={followUpConflict}
                    placeholder="Optional note…"
                    className={INPUT_CLS}
                  />
                </div>
                <div className="flex gap-2">
                  <KeepButton
                    type="submit"
                    variant="secondary"
                    disabled={!editorFollowUpDate || !editorFollowUpReason || followUpSubmitting || followUpConflict}
                    className="flex-1"
                  >
                    {followUpSubmitting ? "Saving…" : hasFollowUp ? "Save follow-up" : "Set follow-up"}
                  </KeepButton>
                  <KeepButton type="button" variant="secondary" onClick={closeEditor}>
                    Cancel
                  </KeepButton>
                </div>
              </form>
            </div>

            {/* Secondary actions — shown when set and editor is closed */}
            {hasFollowUp && expandedEditor !== "followUp" && (
              <div className="flex flex-wrap items-center gap-3">
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
                  className={`text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-danger)] disabled:opacity-50 transition-colors ${FOCUS_RING} rounded`}
                >
                  {followUpSubmitting ? "Clearing…" : "Clear follow-up"}
                </button>
                {followUpError && (
                  <p className={`text-xs w-full ${followUpConflict ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-danger)]"}`}>
                    {followUpError}
                  </p>
                )}
              </div>
            )}
          </div>
        )}

        {/* Planned-for section */}
        {canSetPlannedFor && (
          <div className="px-4 py-3 space-y-2">
            <button
              type="button"
              aria-expanded={expandedEditor === "planned"}
              aria-controls="timing-planned-editor"
              onClick={() => expandedEditor === "planned" ? closeEditor() : openEditor("planned")}
              className={`w-full flex items-center justify-between gap-2 text-left ${FOCUS_RING} rounded`}
            >
              <div className="min-w-0 flex-1">
                {hasPlanned ? (
                  <p className="text-sm font-semibold text-[var(--ophalo-ink)]">
                    Planned: {formatDateOnly(detail.plannedForDate!)}
                  </p>
                ) : (
                  <p className="text-sm text-[var(--ophalo-muted)]">Set planned date</p>
                )}
              </div>
              <span className="shrink-0 text-[var(--ophalo-muted)]" aria-hidden="true">
                {expandedEditor === "planned"
                  ? <ChevronUp className="h-4 w-4" />
                  : hasPlanned
                    ? <span className="text-xs font-semibold">Edit</span>
                    : <ChevronDown className="h-4 w-4" />
                }
              </span>
            </button>

            {/* Inline editor — always in DOM so aria-controls is valid */}
            <div id="timing-planned-editor" hidden={expandedEditor !== "planned"}>
              {plannedError && expandedEditor === "planned" && (
                <p className={`mb-2 text-xs ${plannedConflict ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-danger)]"}`}>
                  {plannedError}
                </p>
              )}
              <form onSubmit={(e) => void handleSetPlanned(e)} className="space-y-2">
                <div>
                  <label htmlFor="planned-date" className="text-[11px] text-[var(--ophalo-muted)] block mb-0.5">
                    {hasPlanned ? "Change date" : "Date"}
                  </label>
                  <input
                    id="planned-date"
                    type="date"
                    value={editorPlannedDate}
                    onChange={(e) => setEditorPlannedDate(e.target.value)}
                    disabled={plannedConflict}
                    className={INPUT_CLS}
                  />
                </div>
                <div className="flex gap-2">
                  <KeepButton
                    type="submit"
                    variant="secondary"
                    disabled={!editorPlannedDate || plannedSubmitting || plannedConflict}
                    className="flex-1"
                  >
                    {plannedSubmitting ? "Saving…" : hasPlanned ? "Save date" : "Set date"}
                  </KeepButton>
                  <KeepButton type="button" variant="secondary" onClick={closeEditor}>
                    Cancel
                  </KeepButton>
                </div>
              </form>
            </div>

            {/* Remove action — shown when set and editor is closed */}
            {hasPlanned && expandedEditor !== "planned" && (
              <div className="flex flex-wrap items-center gap-3">
                <button
                  type="button"
                  onClick={() => void handleClearPlanned()}
                  disabled={plannedSubmitting || plannedConflict}
                  className={`text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-danger)] disabled:opacity-50 transition-colors ${FOCUS_RING} rounded`}
                >
                  {plannedSubmitting ? "Removing…" : "Remove planned date"}
                </button>
                {plannedError && (
                  <p className={`text-xs w-full ${plannedConflict ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-danger)]"}`}>
                    {plannedError}
                  </p>
                )}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
