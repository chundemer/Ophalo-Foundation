import { useEffect, useRef, useState } from "react";
import { CalendarDays, Check, Clock, ChevronDown, ChevronUp } from "lucide-react";
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
  // bare: renders as two compact grid-item tiles (no outer card chrome, shared label, or info
  // row) so a parent can align them with TriagePanel's priority tile in one shared
  // Communication & Planning planning row (locked exception, 2026-08-22).
  bare?: boolean;
  // strip: each tile is one labeled, bordered select-style control (persistent label above,
  // no helper copy) with the editor form in a dropdown popover, for the Anchor's compact
  // Internal Planning row (locked correction, 2026-08-24).
  strip?: boolean;
}

export function TimingPanel({ requestId, detail, onDetailUpdated, onRecordFollowUp, bare = false, strip = false }: TimingPanelProps) {
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

  const hasFollowUp = !!detail.followUpOnDate;
  const hasPlanned = !!detail.plannedForDate;
  const hasActiveTiming = hasFollowUp || hasPlanned;

  // strip: an existing planned/follow-up value is operational planning data, not an editing
  // affordance — it must stay visible as a read-only label even when the viewer lacks the
  // mutation permission (locked correction, 2026-08-24). The card variant is unaffected: it only
  // ever shows an editable tile when the viewer is authorized to edit it.
  const showFollowUp = canSetFollowUpOn || (strip && hasFollowUp);
  const showPlanned = canSetPlannedFor || (strip && hasPlanned);

  if (!showFollowUp && !showPlanned) return null;

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

  // Refs for keyboard recovery (RD-059A): first editor field is focused on open; Escape and
  // Cancel close the editor and return focus to the disclosure trigger.
  const followUpTriggerRef = useRef<HTMLButtonElement>(null);
  const plannedTriggerRef = useRef<HTMLButtonElement>(null);
  const followUpDateRef = useRef<HTMLInputElement>(null);
  const plannedDateRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (expandedEditor === "followUp") followUpDateRef.current?.focus();
    else if (expandedEditor === "planned") plannedDateRef.current?.focus();
  }, [expandedEditor]);

  function closeEditor() {
    const which = expandedEditor;
    setExpandedEditor(null);
    if (which === "followUp") followUpTriggerRef.current?.focus();
    else if (which === "planned") plannedTriggerRef.current?.focus();
  }

  function handleEditorKeyDown(e: React.KeyboardEvent) {
    if (e.key === "Escape") {
      e.preventDefault();
      e.stopPropagation();
      closeEditor();
    }
  }

  async function handleSetFollowUp(e: React.FormEvent) {
    e.preventDefault();
    const isOtherReason = editorFollowUpReason === "other";
    if (!editorFollowUpDate || !editorFollowUpReason || (isOtherReason && editorFollowUpNote.trim().length === 0) || followUpSubmitting || followUpConflict) return;
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
      } else if (err instanceof ApiError && err.code === "KeepRequest.FollowUpOnNoteRequired") {
        setFollowUpError('A note is required when the reason is "Other".');
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

  const followUpForm = (
    <form onSubmit={(e) => void handleSetFollowUp(e)} className="space-y-2">
      <div>
        <label htmlFor="follow-up-date" className="text-xs text-[var(--ophalo-muted)] block mb-0.5">Date</label>
        <input
          ref={followUpDateRef}
          id="follow-up-date"
          type="date"
          value={editorFollowUpDate}
          onChange={(e) => setEditorFollowUpDate(e.target.value)}
          disabled={followUpConflict}
          className={INPUT_CLS}
        />
      </div>
      <div>
        <label htmlFor="follow-up-reason" className="text-xs text-[var(--ophalo-muted)] block mb-0.5">Reason</label>
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
        <label htmlFor="follow-up-note" className="text-xs text-[var(--ophalo-muted)] block mb-0.5">
          {editorFollowUpReason === "other" ? "Note (required)" : "Note (optional)"}
        </label>
        <input
          id="follow-up-note"
          type="text"
          value={editorFollowUpNote}
          onChange={(e) => setEditorFollowUpNote(e.target.value)}
          maxLength={followUpNoteMaxLength}
          disabled={followUpConflict}
          placeholder={editorFollowUpReason === "other" ? "Describe the follow-up reason…" : "Optional note…"}
          className={INPUT_CLS}
        />
      </div>
      <div className="flex gap-2">
        <KeepButton
          type="submit"
          variant="secondary"
          disabled={!editorFollowUpDate || !editorFollowUpReason || (editorFollowUpReason === "other" && editorFollowUpNote.trim().length === 0) || followUpSubmitting || followUpConflict}
          className="flex-1"
        >
          {followUpSubmitting ? "Saving…" : hasFollowUp ? "Save follow-up" : "Set follow-up"}
        </KeepButton>
        <KeepButton type="button" variant="secondary" onClick={closeEditor}>
          Cancel
        </KeepButton>
      </div>
    </form>
  );

  const followUpClearAction = hasFollowUp && (
    <button
      type="button"
      onClick={() => void handleClearFollowUp()}
      disabled={followUpSubmitting || followUpConflict}
      className={`text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-danger)] disabled:opacity-50 transition-colors ${FOCUS_RING} rounded`}
    >
      {followUpSubmitting ? "Clearing…" : "Clear follow-up"}
    </button>
  );

  const followUpTile = showFollowUp && (
    strip ? (
      <div className="flex flex-col gap-1 min-w-0">
        {/* No htmlFor/id association to the trigger button: a <label for> on a button replaces
            its accessible name entirely, which would hide the current value from screen
            readers. The label stays a persistent visual heading; the button carries its own
            aria-label with the value. */}
        <label className="text-[10px] font-bold uppercase tracking-[0.08em] text-[var(--keep-request-eyebrow)]">
          Internal follow-up (optional)
        </label>
        {canSetFollowUpOn ? (
          <div className="relative">
            <button
              ref={followUpTriggerRef}
              type="button"
              aria-expanded={expandedEditor === "followUp"}
              aria-controls="timing-followup-editor"
              aria-label={`Internal follow-up (optional): ${hasFollowUp ? formatDateOnly(detail.followUpOnDate!) : "not set"}`}
              onClick={() => expandedEditor === "followUp" ? closeEditor() : openEditor("followUp")}
              className={`w-full flex items-center justify-between gap-2 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2 text-left text-sm text-[var(--ophalo-ink)] transition-colors hover:border-[var(--keep-accent)] ${FOCUS_RING}`}
            >
              <span className="flex items-center gap-1.5 truncate" aria-hidden="true">
                {!hasFollowUp && <CalendarDays className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" />}
                <span className="truncate">{hasFollowUp ? formatDateOnly(detail.followUpOnDate!) : "Set follow-up date"}</span>
              </span>
              {expandedEditor === "followUp" ? <ChevronUp className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" /> : <ChevronDown className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" />}
            </button>
            <div
              id="timing-followup-editor"
              hidden={expandedEditor !== "followUp"}
              onKeyDown={handleEditorKeyDown}
              className="absolute z-20 mt-1 w-72 space-y-2 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-3 shadow-lg"
            >
              {followUpError && expandedEditor === "followUp" && (
                <p role="alert" className={`text-xs ${followUpConflict ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-danger)]"}`}>
                  {followUpError}
                </p>
              )}
              {followUpForm}
              {followUpClearAction && <div className="pt-1">{followUpClearAction}</div>}
            </div>
          </div>
        ) : (
          // Read-only: unauthorized to edit, but an existing value is operational planning data
          // and must stay visible, not be hidden because it can't be mutated here.
          <div className="flex flex-col gap-0.5">
            <span className="text-sm text-[var(--ophalo-ink)]">{formatDateOnly(detail.followUpOnDate!)}</span>
            <span className="text-xs text-[var(--ophalo-muted)]">Read only</span>
          </div>
        )}
      </div>
    ) : (
          <div className={bare ? "space-y-2" : "px-4 py-3 space-y-2"}>
            <p className="text-xs text-[var(--ophalo-muted)]">Your internal reminder to check back on this request.</p>
            <button
              ref={followUpTriggerRef}
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
                  <p className="flex items-center gap-1.5 text-sm text-[var(--ophalo-ink)]">
                    <CalendarDays className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" aria-hidden="true" />
                    Set follow-up date
                  </p>
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
            <div id="timing-followup-editor" hidden={expandedEditor !== "followUp"} onKeyDown={handleEditorKeyDown}>
              {followUpError && expandedEditor === "followUp" && (
                <p role="alert" className={`mb-2 text-xs ${followUpConflict ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-danger)]"}`}>
                  {followUpError}
                </p>
              )}
              {followUpForm}
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
                {followUpClearAction}
                {followUpError && (
                  <p role="alert" className={`text-xs w-full ${followUpConflict ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-danger)]"}`}>
                    {followUpError}
                  </p>
                )}
              </div>
            )}
          </div>
    )
  );

  const plannedForm = (
    <form onSubmit={(e) => void handleSetPlanned(e)} className="space-y-2">
      <div>
        <label htmlFor="planned-date" className="text-xs text-[var(--ophalo-muted)] block mb-0.5">
          {hasPlanned ? "Change date" : "Date"}
        </label>
        <input
          ref={plannedDateRef}
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
  );

  const plannedClearAction = hasPlanned && (
    <button
      type="button"
      onClick={() => void handleClearPlanned()}
      disabled={plannedSubmitting || plannedConflict}
      className={`text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-danger)] disabled:opacity-50 transition-colors ${FOCUS_RING} rounded`}
    >
      {plannedSubmitting ? "Removing…" : "Remove planned date"}
    </button>
  );

  const plannedTile = showPlanned && (
    strip ? (
      <div className="flex flex-col gap-1 min-w-0">
        {/* No htmlFor/id association to the trigger button: a <label for> on a button replaces
            its accessible name entirely, which would hide the current value from screen
            readers. The label stays a persistent visual heading; the button carries its own
            aria-label with the value. */}
        <label className="text-[10px] font-bold uppercase tracking-[0.08em] text-[var(--keep-request-eyebrow)]">
          Planned work date
        </label>
        {canSetPlannedFor ? (
          <div className="relative">
            <button
              ref={plannedTriggerRef}
              type="button"
              aria-expanded={expandedEditor === "planned"}
              aria-controls="timing-planned-editor"
              aria-label={`Planned work date: ${hasPlanned ? formatDateOnly(detail.plannedForDate!) : "not set"}`}
              onClick={() => expandedEditor === "planned" ? closeEditor() : openEditor("planned")}
              className={`w-full flex items-center justify-between gap-2 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2 text-left text-sm text-[var(--ophalo-ink)] transition-colors hover:border-[var(--keep-accent)] ${FOCUS_RING}`}
            >
              <span className="flex items-center gap-1.5 truncate" aria-hidden="true">
                {hasPlanned
                  ? <Check className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" />
                  : <CalendarDays className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" />}
                <span className="truncate">{hasPlanned ? formatDateOnly(detail.plannedForDate!) : "Set planned date"}</span>
              </span>
              {expandedEditor === "planned" ? <ChevronUp className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" /> : <ChevronDown className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" />}
            </button>
            <div
              id="timing-planned-editor"
              hidden={expandedEditor !== "planned"}
              onKeyDown={handleEditorKeyDown}
              className="absolute z-20 mt-1 w-64 space-y-2 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-3 shadow-lg"
            >
              {plannedError && expandedEditor === "planned" && (
                <p role="alert" className={`text-xs ${plannedConflict ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-danger)]"}`}>
                  {plannedError}
                </p>
              )}
              {plannedForm}
              {plannedClearAction && <div className="pt-1">{plannedClearAction}</div>}
            </div>
          </div>
        ) : (
          <div className="flex flex-col gap-0.5">
            <span className="flex items-center gap-1.5 text-sm text-[var(--ophalo-ink)]">
              {hasPlanned && <Check className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" aria-hidden="true" />}
              {formatDateOnly(detail.plannedForDate!)}
            </span>
            <span className="text-xs text-[var(--ophalo-muted)]">Read only</span>
          </div>
        )}
      </div>
    ) : (
          <div className={bare ? "space-y-2" : "px-4 py-3 space-y-2"}>
            <p className="text-xs text-[var(--ophalo-muted)]">When work is scheduled to be performed.</p>
            <button
              ref={plannedTriggerRef}
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
                  <p className="flex items-center gap-1.5 text-sm text-[var(--ophalo-ink)]">
                    <CalendarDays className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" aria-hidden="true" />
                    Set planned date
                  </p>
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
            <div id="timing-planned-editor" hidden={expandedEditor !== "planned"} onKeyDown={handleEditorKeyDown}>
              {plannedError && expandedEditor === "planned" && (
                <p role="alert" className={`mb-2 text-xs ${plannedConflict ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-danger)]"}`}>
                  {plannedError}
                </p>
              )}
              {plannedForm}
            </div>

            {/* Remove action — shown when set and editor is closed */}
            {hasPlanned && expandedEditor !== "planned" && (
              <div className="flex flex-wrap items-center gap-3">
                {plannedClearAction}
                {plannedError && (
                  <p role="alert" className={`text-xs w-full ${plannedConflict ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-danger)]"}`}>
                    {plannedError}
                  </p>
                )}
              </div>
            )}
          </div>
    )
  );

  if (strip || bare) {
    return (
      <>
        {plannedTile}
        {followUpTile}
      </>
    );
  }

  return (
    <div>
      <p className="px-1 text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] mb-2">
        Follow-up &amp; planned timing
      </p>
      <div
        className={`divide-y divide-[var(--ophalo-border)] rounded-xl border bg-[var(--ophalo-card)] ${
          hasActiveTiming ? "border-[var(--keep-accent)] border-l-4" : "border-[var(--ophalo-border)]"
        }`}
      >
        {/* Info row */}
        <div className="flex items-center gap-2 px-4 py-2.5">
          <Clock
            className={`h-3.5 w-3.5 shrink-0 ${hasActiveTiming ? "text-[var(--keep-accent)]" : "text-[var(--ophalo-muted)]"}`}
            aria-hidden="true"
          />
          <p className="text-xs leading-5 text-[var(--ophalo-muted)]">
            Internal — does not notify the customer.
          </p>
        </div>
        {followUpTile}
        {plannedTile}
      </div>
    </div>
  );
}
