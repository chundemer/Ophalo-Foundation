import { useState, useRef, useEffect, useCallback } from "react";
import { api, ApiError, type KeepRequestDetailResult } from "../../lib/apiClient";
import { KeepBadge } from "../../components/keep/KeepBadge";
import { KeepButton } from "../../components/keep/KeepButton";
import {
  type HighlightLevel,
  highlightBorderCls,
  highlightBgCls,
  highlightBoxShadow,
  RecommendedActionBadge,
} from "./highlights";
import { INPUT_CLS, statusLabel } from "./helpers";

// ---------------------------------------------------------------------------
// Work Done card
// ---------------------------------------------------------------------------

const WORK_DONE_CONFIRM_TIMEOUT_MS = 8000;

interface WorkDoneCardProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}

export function WorkDoneCard({ requestId, detail, onDetailUpdated }: WorkDoneCardProps) {
  const baseEligible =
    detail.availableActions.canChangeStatus &&
    detail.availableActions.allowedStatuses.includes("resolved") &&
    detail.status !== "resolved";
  const hasAttention = detail.attentionLevel !== "none";
  const isReceived = detail.status === "received";

  // Three visual branches:
  // normal:    no attention, not Received  → teal, "Primary action" badge
  // received:  no attention, Received      → secondary, no badge
  // attention: has attention               → secondary, "attention remains" copy
  const isNormalPath = baseEligible && !hasAttention && !isReceived;
  const isReceivedPath = baseEligible && !hasAttention && isReceived;
  const isDemotedPath = baseEligible && hasAttention;

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);

  const confirmBtnRef = useRef<HTMLButtonElement>(null);
  const triggerBtnRef = useRef<HTMLButtonElement>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const clearTimer = useCallback(() => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const exitConfirming = useCallback((returnFocus: boolean) => {
    clearTimer();
    setConfirming(false);
    if (returnFocus) {
      triggerBtnRef.current?.focus();
    }
  }, [clearTimer]);

  useEffect(() => {
    if (!confirming) return;
    confirmBtnRef.current?.focus();
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        exitConfirming(true);
      }
    };
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [confirming, exitConfirming]);

  useEffect(() => {
    return () => clearTimer();
  }, [clearTimer]);

  if (!isNormalPath && !isReceivedPath && !isDemotedPath) return null;

  function enterConfirming() {
    setConfirming(true);
    clearTimer();
    timerRef.current = setTimeout(() => {
      setConfirming(false);
      timerRef.current = null;
    }, WORK_DONE_CONFIRM_TIMEOUT_MS);
  }

  async function handleMarkDone() {
    if (isSubmitting || conflictDisabled) return;
    clearTimer();
    setConfirming(false);
    setIsSubmitting(true);
    setError(null);
    try {
      const updated = await api.patchRequestStatus(
        requestId,
        { status: "resolved" },
        detail.version,
      );
      onDetailUpdated(updated);
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError("This request was updated. Refresh to see the latest state.");
      } else {
        setError("Could not mark work done. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  function renderError() {
    if (!error) return null;
    return (
      <div
        aria-live="polite"
        className={`mb-3 rounded-lg p-3 text-xs ${
          conflictDisabled
            ? "bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)]"
            : "bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]"
        }`}
      >
        {error}
      </div>
    );
  }

  function renderConfirmation() {
    return (
      <div className="flex items-center gap-2">
        <KeepButton
          ref={confirmBtnRef}
          type="button"
          variant="teal"
          disabled={isSubmitting}
          onClick={() => void handleMarkDone()}
          className="flex-1"
        >
          {isSubmitting ? "Saving…" : "Confirm"}
        </KeepButton>
        <KeepButton
          type="button"
          variant="secondary"
          disabled={isSubmitting}
          onClick={() => exitConfirming(true)}
          className="flex-1"
        >
          Cancel
        </KeepButton>
      </div>
    );
  }

  if (isNormalPath) {
    return (
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-5">
        <div className="mb-3 flex items-start justify-between gap-3">
          <div>
            <p className="text-base font-semibold text-[var(--ophalo-ink)]">Work completed</p>
            <p className="mt-1 text-xs leading-5 text-[var(--ophalo-muted)]">
              Mark this when the work has been performed. Owner/Admin can close the request after review.
            </p>
          </div>
          <KeepBadge variant="success">Primary action</KeepBadge>
        </div>
        {renderError()}
        {confirming ? (
          <>
            <p className="mb-2 text-sm font-medium text-[var(--ophalo-ink)]">Confirm work is done?</p>
            {renderConfirmation()}
          </>
        ) : (
          <KeepButton
            ref={triggerBtnRef}
            type="button"
            variant="teal"
            disabled={isSubmitting || conflictDisabled}
            onClick={enterConfirming}
            className="w-full"
          >
            Mark work done
          </KeepButton>
        )}
      </div>
    );
  }

  if (isReceivedPath) {
    return (
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4 opacity-80">
        <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">Mark work done</p>
        <p className="text-xs leading-5 text-[var(--ophalo-muted)] mb-3">
          This request hasn't been acted on yet. Mark work done only when the work has actually been performed.
        </p>
        {renderError()}
        {confirming ? (
          <>
            <p className="mb-2 text-sm font-medium text-[var(--ophalo-ink)]">Confirm work is done?</p>
            {renderConfirmation()}
          </>
        ) : (
          <KeepButton
            ref={triggerBtnRef}
            type="button"
            variant="secondary"
            disabled={isSubmitting || conflictDisabled}
            onClick={enterConfirming}
            className="w-full"
          >
            Mark work done
          </KeepButton>
        )}
      </div>
    );
  }

  // Demoted path: attention is active; make this secondary and explicit
  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4 opacity-80">
      <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">Mark work done, attention remains</p>
      <p className="text-xs leading-5 text-[var(--ophalo-muted)] mb-3">
        This records that the work was performed, but this request still needs attention before it can be closed.
      </p>
      {renderError()}
      {confirming ? (
        <>
          <p className="mb-2 text-sm font-medium text-[var(--ophalo-ink)]">Confirm work is done?</p>
          {renderConfirmation()}
        </>
      ) : (
        <KeepButton
          ref={triggerBtnRef}
          type="button"
          variant="secondary"
          disabled={isSubmitting || conflictDisabled}
          onClick={enterConfirming}
          className="w-full"
        >
          Mark work done, attention remains
        </KeepButton>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Close Request (Owner/Admin closeout)
// ---------------------------------------------------------------------------

interface CloseRequestCardProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}

export function CloseRequestCard({ requestId, detail, onDetailUpdated }: CloseRequestCardProps) {
  const canClose =
    detail.availableActions.canClose &&
    detail.availableActions.allowedStatuses.includes("closed") &&
    detail.status === "resolved" &&
    detail.attentionLevel === "none";
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!canClose) return null;

  async function handleClose() {
    if (isSubmitting || conflictDisabled) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const updated = await api.patchRequestStatus(
        requestId,
        { status: "closed" },
        detail.version,
      );
      onDetailUpdated(updated);
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError("This request was updated. Refresh to see the latest state.");
      } else {
        setError("Could not close request. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-5">
      <div className="mb-3 flex items-start justify-between gap-3">
        <div>
          <p className="text-base font-semibold text-[var(--ophalo-ink)]">Ready to close</p>
          <p className="mt-1 text-xs leading-5 text-[var(--ophalo-muted)]">
            Close this request when the business is done managing it. The customer can still leave one-time feedback from their request page.
          </p>
        </div>
        <KeepBadge variant="success">Owner/Admin</KeepBadge>
      </div>
      {error && (
        <div
          aria-live="polite"
          className={`mb-3 rounded-lg p-3 text-xs ${
            conflictDisabled
              ? "bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)]"
              : "bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]"
          }`}
        >
          {error}
        </div>
      )}
      <KeepButton
        type="button"
        variant="teal"
        disabled={isSubmitting || conflictDisabled}
        onClick={() => void handleClose()}
        className="w-full"
      >
        {isSubmitting ? "Closing…" : "Close request"}
      </KeepButton>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Send customer update (primary communication action)
// ---------------------------------------------------------------------------

const BUSINESS_UPDATE_EXCLUDED_STATUSES = new Set(["closed", "cancelled", "spam", "test"]);
const BUSINESS_UPDATE_CONFLICT_MESSAGE =
  "This request was updated. Refresh to see the latest state. Your message is saved here.";

interface BusinessUpdateSectionProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  draft: string;
  onDraftChange: (v: string) => void;
  draftStatus: string;
  onDraftStatusChange: (v: string) => void;
  highlight?: HighlightLevel;
  composerMode?: boolean;
}

export function BusinessUpdateSection({
  requestId,
  detail,
  onDetailUpdated,
  draft,
  onDraftChange,
  draftStatus,
  onDraftStatusChange,
  highlight,
  composerMode = false,
}: BusinessUpdateSectionProps) {
  const { canSendBusinessUpdate, canChangeStatus, allowedStatuses } = detail.availableActions;
  const maxLength = detail.validation.businessUpdateMaxLength;

  const message = draft;
  const setMessage = onDraftChange;
  const selectedStatus = draftStatus;
  const setSelectedStatus = onDraftStatusChange;
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!canSendBusinessUpdate) return null;

  const composerStatuses = allowedStatuses;
  const showStatusDropdown = canChangeStatus && composerStatuses.length > 0;
  const remaining = maxLength - message.length;
  const overLimit = remaining < 0;
  const hasMessage = message.trim().length > 0;
  const hasStatus = selectedStatus !== "";
  const statusRequiresMessage = detail.validation.messageRequiredForStatuses.includes(selectedStatus);
  const canSubmit =
    (hasMessage || hasStatus) &&
    !overLimit &&
    !isSubmitting &&
    !conflictDisabled &&
    (!statusRequiresMessage || hasMessage);
  const submitLabel =
    hasMessage && hasStatus
      ? "Send update & change status"
      : hasStatus
        ? "Update status"
        : "Send update";

  async function doSubmit() {
    if (!canSubmit) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const statusUsesStatusEndpoint =
        selectedStatus !== "" && BUSINESS_UPDATE_EXCLUDED_STATUSES.has(selectedStatus);
      const updated =
        selectedStatus && (!hasMessage || statusUsesStatusEndpoint)
          ? await api.patchRequestStatus(
              requestId,
              hasMessage
                ? { status: selectedStatus, message: message.trim() }
                : { status: selectedStatus },
              detail.version,
            )
          : await api.postBusinessUpdate(
              requestId,
              selectedStatus
                ? { message: message.trim(), setStatus: selectedStatus }
                : { message: message.trim() },
              detail.version,
            );
      onDetailUpdated(updated);
      setMessage("");
      setSelectedStatus("");
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError(BUSINESS_UPDATE_CONFLICT_MESSAGE);
      } else {
        setError(hasMessage ? "Could not send update. Try again." : "Could not update status. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    await doSubmit();
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if ((e.metaKey || e.ctrlKey) && e.key === "Enter") {
      e.preventDefault();
      void doSubmit();
    }
  }

  const sharedErrorBlock = error && (
    <div
      aria-live="polite"
      className={`mb-3 rounded-lg p-3 text-xs ${
        conflictDisabled
          ? "bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)]"
          : "bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]"
      }`}
    >
      {error}
    </div>
  );

  const sharedForm = (
    <form onSubmit={(e) => void handleSubmit(e)} className="space-y-3">
      <div>
        <label htmlFor="business-update-message" className="sr-only">
          Customer update message
        </label>
        <textarea
          id="business-update-message"
          value={message}
          onChange={(e) => {
            setMessage(e.target.value);
            if (error && !conflictDisabled) setError(null);
          }}
          onKeyDown={handleKeyDown}
          disabled={conflictDisabled}
          placeholder="Write an update for the customer…"
          rows={4}
          className={`${INPUT_CLS} resize-none ${
            overLimit
              ? "border-[var(--ophalo-danger)] focus:ring-[var(--ophalo-danger)] focus:border-[var(--ophalo-danger)]"
              : ""
          }`}
        />
        <p
          className={`mt-1 text-xs text-right ${
            overLimit
              ? "text-[var(--ophalo-danger)] font-medium"
              : remaining <= 50
                ? "text-[var(--ophalo-attention)]"
                : "text-[var(--ophalo-muted)]"
          }`}
        >
          {overLimit ? `${Math.abs(remaining)} over limit` : `${remaining} remaining`}
        </p>
      </div>
      {showStatusDropdown && (
        <div>
          <label htmlFor="update-status-select" className="sr-only">
            Also change status (optional)
          </label>
          <select
            id="update-status-select"
            value={selectedStatus}
            onChange={(e) => setSelectedStatus(e.target.value)}
            disabled={conflictDisabled}
            className={INPUT_CLS}
          >
            <option value="">No status change</option>
            {composerStatuses.map((s) => (
              <option key={s} value={s}>{statusLabel(s)}</option>
            ))}
          </select>
        </div>
      )}
      <p className="text-xs text-[var(--ophalo-muted)]">
        {hasMessage ? "Visible on the customer page." : "No customer message will be sent."}
        {selectedStatus && ` Status will change to ${statusLabel(selectedStatus)}.`}
        {statusRequiresMessage && !hasMessage && " Add a message for this status."}
      </p>
      <KeepButton type="submit" variant="teal" disabled={!canSubmit} className="w-full">
        {isSubmitting ? "Saving…" : submitLabel}
      </KeepButton>
    </form>
  );

  if (composerMode) {
    return (
      <div>
        <p className="text-xs font-medium text-[var(--ophalo-teal)] mb-3">Visible to customer</p>
        {detail.needsShare && (
          <p className="mb-3 text-xs text-[var(--ophalo-attention)]">
            Customer page not yet shared — the customer won't see this until you share it.
          </p>
        )}
        {sharedErrorBlock}
        {sharedForm}
      </div>
    );
  }

  return (
    <div
      id="customer-update"
      className={`rounded-xl border px-5 py-5 scroll-mt-4 transition-[border-color,background-color,box-shadow] ${highlightBorderCls(highlight)} ${highlightBgCls()}`}
      style={{ boxShadow: highlightBoxShadow(highlight) }}
    >
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <p className="text-base font-semibold text-[var(--ophalo-ink)]">Send customer update</p>
        <RecommendedActionBadge level={highlight} />
      </div>
      {detail.needsShare && (
        <p className="mb-3 text-xs text-[var(--ophalo-attention)]">
          Customer page not yet shared — the customer won't see this until you share it.
        </p>
      )}
      {sharedErrorBlock}
      {sharedForm}
    </div>
  );
}
