import { useState, useRef, useEffect, useCallback } from "react";
import { api, ApiError, type KeepRequestDetailResult } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import { ConnectionFailureBanner } from "./ConnectionFailureBanner";
import { announcePolite } from "../../lib/liveAnnouncer";

// Shared, exhaustive renderer over the closed server `target` vocabulary (Session 0A,
// 2026-08-25). Exactly one of `RequestDetailAnchor` (no active attention) or
// `HeroAttentionBanner` (active attention) mounts this for a given request — never both — so the
// same server-authored `detail.availableActions.primaryAction` is never rendered/labeled twice by
// independent logic. The client performs no attention, lifecycle, work-state, or closeout
// precedence derivation; an unrecognized target/key combination fails safely with factual
// "unavailable" feedback rather than falling back to capability-flag inference.

export interface PrimaryActionSlotProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onOpenClearAttention: () => void;
  onRecordFollowUp: () => void;
  onContactLaunched: (direction: string, channel: string) => void;
  onActivateCustomerUpdateComposer: () => void;
}

export function PrimaryActionSlot({
  requestId,
  detail,
  onDetailUpdated,
  onOpenClearAttention,
  onRecordFollowUp,
  onContactLaunched,
  onActivateCustomerUpdateComposer,
}: PrimaryActionSlotProps) {
  const action = detail.availableActions.primaryAction;
  if (!action) return null;

  switch (action.target) {
    case "attention_sheet":
      if (action.key !== "acknowledge_attention") return <PrimaryActionUnavailable />;
      return (
        <KeepButton type="button" variant="teal" onClick={onOpenClearAttention}>
          {action.label}
        </KeepButton>
      );

    case "follow_up_sheet":
      if (action.key !== "resolve_follow_up") return <PrimaryActionUnavailable />;
      return (
        <KeepButton type="button" variant="teal" onClick={onRecordFollowUp}>
          {action.label}
        </KeepButton>
      );

    case "customer_update_composer":
      if (action.key !== "respond_to_customer") return <PrimaryActionUnavailable />;
      return (
        <KeepButton type="button" variant="teal" onClick={onActivateCustomerUpdateComposer}>
          {action.label}
        </KeepButton>
      );

    case "contact_sheet": {
      if (action.key !== "log_external_contact") return <PrimaryActionUnavailable />;
      const channel = detail.customerPhone ? "phone" : detail.customerEmail ? "email" : "other";
      return (
        <KeepButton type="button" variant="teal" onClick={() => onContactLaunched("outbound", channel)}>
          {action.label}
        </KeepButton>
      );
    }

    case "mutation": {
      if (action.key !== "mark_work_done" && action.key !== "close_request") return <PrimaryActionUnavailable />;
      const targetStatus = action.key === "mark_work_done" ? "resolved" : "closed";
      return (
        <PrimaryMutationButton
          requestId={requestId}
          detail={detail}
          onDetailUpdated={onDetailUpdated}
          label={action.label}
          targetStatus={targetStatus}
          confirmationCopy={action.confirmationCopy}
        />
      );
    }

    default:
      // Unknown/malformed server target — fail safely rather than guess or fall back to
      // capability-flag inference.
      return <PrimaryActionUnavailable />;
  }
}

export function PrimaryActionUnavailable() {
  return (
    <span role="status" className="text-xs text-[var(--ophalo-muted)]">
      Primary action unavailable
    </span>
  );
}

// ---------------------------------------------------------------------------
// Mark work done — server-authored secondary control (Session 0A). Anchor-only: its own
// null-check already gates it to the (attention-active) case where the backend populates it, so
// it never needs to move alongside the primary slot's attention/no-attention mount split.
// ---------------------------------------------------------------------------

export function MarkWorkDoneSecondarySlot({
  requestId,
  detail,
  onDetailUpdated,
}: {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}) {
  const secondary = detail.availableActions.markWorkDoneSecondary;
  if (!secondary) return null;

  switch (secondary.consequence) {
    case "attention_remains":
      return (
        <PrimaryMutationButton
          requestId={requestId}
          detail={detail}
          onDetailUpdated={onDetailUpdated}
          label={secondary.label}
          targetStatus="resolved"
          confirmationCopy={null}
          variant="secondary"
          accessibleSuffix="attention remains"
        />
      );
    default:
      return <PrimaryActionUnavailable />;
  }
}

// ---------------------------------------------------------------------------
// Shared confirm-then-submit control for the two mutation-target primary/secondary actions
// ---------------------------------------------------------------------------

const CONFIRM_TIMEOUT_MS = 8000;

interface PrimaryMutationButtonProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  label: string;
  targetStatus: "resolved" | "closed";
  confirmationCopy: string | null;
  variant?: "primary" | "secondary";
  accessibleSuffix?: string;
}

// Always confirms locally before submitting (click -> inline Confirm/Cancel -> Confirm), for both
// mark_work_done and close_request — this predates and is independent of the server's
// PrimaryActionMetadata.RequiresConfirmation flag, which only governs whether server-authored
// confirmationCopy is mandatory (close_request today). Do not gate the confirm step on that flag.
function PrimaryMutationButton({
  requestId,
  detail,
  onDetailUpdated,
  label,
  targetStatus,
  confirmationCopy,
  variant = "primary",
  accessibleSuffix,
}: PrimaryMutationButtonProps) {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [connectionFailure, setConnectionFailure] = useState<{
    message: string;
    snapshot: { requestId: string; targetStatus: "resolved" | "closed"; version: string };
  } | null>(null);

  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const confirmBtnRef = useRef<HTMLButtonElement>(null);
  const triggerBtnRef = useRef<HTMLButtonElement>(null);

  const clearTimer = useCallback(() => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const exitConfirming = useCallback(
    (returnFocus: boolean) => {
      clearTimer();
      setConfirming(false);
      if (returnFocus) triggerBtnRef.current?.focus();
    },
    [clearTimer],
  );

  useEffect(() => {
    if (!confirming) return;
    confirmBtnRef.current?.focus();
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        exitConfirming(true);
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [confirming, exitConfirming]);

  useEffect(() => () => clearTimer(), [clearTimer]);

  async function submit(retrySnapshot?: { requestId: string; targetStatus: "resolved" | "closed"; version: string }) {
    if (isSubmitting || conflictDisabled) return;
    // Snapshot at the original attempt (not read live from props at retry time) so a Retry
    // replays the exact request that failed, even if the parent re-renders with a newer
    // `detail.version` before the operator presses Retry — same rule as 5a's
    // ActualWorkComposer retries.
    const snapshot = retrySnapshot ?? { requestId, targetStatus, version: detail.version };
    const isRetry = retrySnapshot !== undefined;
    clearTimer();
    setConfirming(false);
    setIsSubmitting(true);
    setError(null);
    setConnectionFailure(null);
    try {
      const updated = await api.patchRequestStatus(snapshot.requestId, { status: snapshot.targetStatus }, snapshot.version);
      // `onDetailUpdated` can make the server-authored primary action disappear (and this
      // component with it) in the same commit — announce via the root-mounted live region
      // (`liveAnnouncer.ts`) rather than local state, which would never reach the DOM.
      if (isRetry) announcePolite("Retry succeeded.");
      onDetailUpdated(updated);
    } catch (e) {
      if (e instanceof ApiError) {
        if (e.status === 409) {
          setConflictDisabled(true);
          setError("This request was updated. Refresh to see the latest state.");
        } else {
          setError(snapshot.targetStatus === "resolved" ? "Could not mark work done. Try again." : "Could not close request. Try again.");
        }
      } else {
        setConnectionFailure({
          message: snapshot.targetStatus === "resolved" ? "Couldn't mark work done." : "Couldn't close request.",
          snapshot,
        });
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  function handleRetry() {
    if (!connectionFailure) return;
    void submit(connectionFailure.snapshot);
  }

  function handleClick() {
    if (isSubmitting || conflictDisabled) return;
    // Every mutation-target action always confirms locally before submitting — this predates
    // and is independent of the server's `requiresConfirmation` flag, which only controls
    // whether server-authored confirmation copy is mandatory (close_request today). Removing
    // this step for mark_work_done was a regression (2026-08-25) against the app's existing,
    // out-of-scope-to-change confirm-before-mutate convention.
    setConfirming(true);
    clearTimer();
    timerRef.current = setTimeout(() => setConfirming(false), CONFIRM_TIMEOUT_MS);
  }

  // Visible text always carries the consequence — never hide it in an aria-label-only suffix.
  // The demoted secondary (Mark work done, attention remains) must read its own consequence
  // before the user acts, not just be discoverable to screen readers.
  const visibleLabel = accessibleSuffix ? `${label}, ${accessibleSuffix}` : label;
  // Server-authored copy (close_request) always wins; mark_work_done carries no server copy, so
  // fall back to the app's existing local prompt for that mutation rather than showing nothing.
  const confirmPrompt = confirmationCopy ?? (targetStatus === "resolved" ? "Confirm work is done?" : null);

  return (
    <div className="flex flex-col gap-1">
      {connectionFailure && (
        <ConnectionFailureBanner message={connectionFailure.message} onRetry={handleRetry} isRetrying={isSubmitting} />
      )}
      {error && (
        <p
          aria-live="polite"
          className={`text-xs ${conflictDisabled ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-danger)]"}`}
        >
          {error}
        </p>
      )}
      {confirming ? (
        <div className="flex items-center gap-2">
          {confirmPrompt && <span className="text-xs text-[var(--ophalo-ink)]">{confirmPrompt}</span>}
          <KeepButton ref={confirmBtnRef} type="button" variant="teal" disabled={isSubmitting} onClick={() => void submit()}>
            {isSubmitting ? "Working…" : "Confirm"}
          </KeepButton>
          <KeepButton type="button" variant="secondary" onClick={() => exitConfirming(true)}>
            Cancel
          </KeepButton>
        </div>
      ) : variant === "primary" ? (
        <KeepButton
          ref={triggerBtnRef}
          type="button"
          variant="teal"
          disabled={isSubmitting || conflictDisabled}
          onClick={handleClick}
        >
          {isSubmitting ? "Working…" : visibleLabel}
        </KeepButton>
      ) : (
        // Demoted secondary (locked desktop-polish decision, 2026-08-24): a quiet text-style
        // trigger, not an equal-weight outline button competing with Contact customer — and its
        // full visible text always states the consequence plainly before the user acts.
        <button
          ref={triggerBtnRef}
          type="button"
          disabled={isSubmitting || conflictDisabled}
          onClick={handleClick}
          className="px-2 text-left text-sm font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] disabled:opacity-50 disabled:cursor-not-allowed transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 rounded"
        >
          {isSubmitting ? "Working…" : visibleLabel}
        </button>
      )}
    </div>
  );
}
