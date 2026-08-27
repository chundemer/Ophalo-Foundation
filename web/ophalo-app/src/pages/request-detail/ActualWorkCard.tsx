import { ClipboardList } from "lucide-react";
import { KeepBadge } from "../../components/keep/KeepBadge";
import { KeepButton } from "../../components/keep/KeepButton";
import { type ActualWorkCaptureState, type ActualWorkRecoveryNotice } from "./useActualWorkCapture";

interface ActualWorkCardProps {
  state: ActualWorkCaptureState;
  onStartCapture: () => void;
  // 1a-ii-b: opens the Owner/Admin recorder-transfer recovery drawer. Only wired for the
  // `owner-recovery` state.
  onReassignRecorder?: () => void;
  // Transient banner after a recorder transfer resolves; shown over whichever post-transfer state
  // the card lands on.
  recoveryNotice?: ActualWorkRecoveryNotice | null;
  onDismissRecoveryNotice?: () => void;
  // bare: no outer card chrome — used when a parent shares one enclosing Work Execution module
  // with ActualWorkHistoryCard (locked exception, 2026-08-22).
  bare?: boolean;
}

/**
 * Batch 5b entry point: "Record completed work". Renders for any caller who holds
 * `RequestsOperate` + `ActualWorkCapture` (GAP-055 — not tied to dispatch assignment); loading/
 * hidden/error states render nothing, same as ProposedScopeCard, so a transient probe failure
 * never blocks the rest of the request-detail cards and a Viewer never sees an action that would
 * fail. When another team member already owns the request's one open Draft, this shows a
 * non-actionable notice instead of an entry point (`held-by-other`) — only that recorder may edit
 * it, and the deliberate transfer workflow is a separate surface.
 */
export function ActualWorkCard({
  state,
  onStartCapture,
  onReassignRecorder,
  recoveryNotice,
  onDismissRecoveryNotice,
  bare = false,
}: ActualWorkCardProps) {
  if (state.status === "loading" || state.status === "hidden" || state.status === "error") {
    return null;
  }

  const wrapperCls = bare
    ? "px-4 py-2"
    : "rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-2";

  // Transient post-transfer banner (1a-ii-b). Rendered above the card content for every non-hidden
  // state, since the re-probe can land on the new recorder's `draft`, `no-draft`, `held-by-other`,
  // or (a failed/again-needed transfer) `owner-recovery`.
  const noticeBanner = recoveryNotice ? (
    <div
      role="status"
      className={`mb-2 flex items-start justify-between gap-2 rounded-lg px-3 py-2 text-xs ${
        recoveryNotice.tone === "success"
          ? "bg-[var(--ophalo-success-bg)] text-[var(--ophalo-success)]"
          : "bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]"
      }`}
    >
      <span className="min-w-0">{recoveryNotice.text}</span>
      {onDismissRecoveryNotice && (
        <button
          type="button"
          onClick={onDismissRecoveryNotice}
          className="shrink-0 font-medium hover:underline"
          aria-label="Dismiss"
        >
          ×
        </button>
      )}
    </div>
  ) : null;

  if (state.status === "held-by-other") {
    const priorVisits =
      state.submittedCount === 0
        ? ""
        : ` ${state.submittedCount} prior visit${state.submittedCount === 1 ? "" : "s"} recorded · locked.`;
    return (
      <div className={wrapperCls}>
        {noticeBanner}
        <div className="min-w-0">
          <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Actual work</p>
          <p className="text-xs text-[var(--ophalo-muted)]">
            Another team member is recording this visit.{priorVisits}
          </p>
        </div>
      </div>
    );
  }

  if (state.status === "owner-recovery") {
    const recorderName = state.draft.recorderDisplayName ?? "Another team member";
    return (
      <div className={wrapperCls}>
        {noticeBanner}
        <div className="flex items-center justify-between gap-3">
          <div className="min-w-0">
            <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Actual work</p>
            <p className="text-xs text-[var(--ophalo-muted)] truncate">
              {recorderName} is recording this visit.
            </p>
          </div>
          <KeepButton
            variant="secondary"
            onClick={onReassignRecorder}
            className="shrink-0 inline-flex items-center gap-1.5"
          >
            <ClipboardList className="h-3.5 w-3.5 shrink-0" />
            Reassign recorder
          </KeepButton>
        </div>
      </div>
    );
  }

  // This is deliberately a one-line execution strip.  Line items live in the drawer so
  // opening a request never turns its canvas into a long visit-history document.

  const isNoDraft = state.status === "no-draft";
  const isDraft = state.status === "draft";
  const hasSavedLines = isDraft && state.draft.lines.length > 0;
  const priorVisits = state.submittedCount === 0
    ? null
    : `${state.submittedCount} prior visit${state.submittedCount === 1 ? "" : "s"} locked.`;
  const summary = isNoDraft
    ? state.submittedCount === 0
      ? "Record the work completed on this visit."
      : `${state.submittedCount} prior visit${state.submittedCount === 1 ? "" : "s"} recorded · locked.`
    : state.draft.lines.length === 0
      ? `Draft visit started — no items added yet.${priorVisits ? ` ${priorVisits}` : ""}`
      : `Draft visit in progress — ${state.draft.lines.length} item${state.draft.lines.length === 1 ? "" : "s"} added.${priorVisits ? ` ${priorVisits}` : ""}`;

  return (
    <div className={wrapperCls}>
      {noticeBanner}
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-1.5">
            <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Actual work</p>
            {isDraft && <KeepBadge variant="attention">Draft — not submitted</KeepBadge>}
          </div>
          <p className="text-xs text-[var(--ophalo-muted)] truncate">{summary}</p>
        </div>
        {/* secondary, not teal — the Anchor owns the one primary-weight action (locked spec);
            this module's CTA must not visually compete with it or the composer's submit button. */}
        <KeepButton
          variant="secondary"
          onClick={onStartCapture}
          className="shrink-0 inline-flex items-center gap-1.5"
        >
          <ClipboardList className="h-3.5 w-3.5 shrink-0" />
          {isDraft ? (hasSavedLines ? "Continue draft" : "Resume draft") : "Add actual work"}
        </KeepButton>
      </div>
    </div>
  );
}
