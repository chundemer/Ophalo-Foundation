import { useState, useRef, useEffect } from "react";
import { Phone, X } from "lucide-react";
import { api, ApiError, type KeepRequestDetailResult } from "../../lib/apiClient";
import { ResponsiveSheet } from "../../components/keep/ResponsiveSheet";
import { KeepButton } from "../../components/keep/KeepButton";
import { ExternalContactForm } from "../../components/ExternalContactForm";
import { formatNaPhone } from "../../components/quick-capture/utils";
import { useCopyFeedback } from "../../hooks/useCopyFeedback";
import { getPublicBaseUrl } from "../../lib/publicBaseUrl";
import { FOCUS_RING, STATUS_CONFLICT_MESSAGE } from "./helpers";
import { CallHandoffQr } from "./CallHandoffQr";
import { SmsHandoffQr } from "./SmsHandoffQr";

// Maintainability review item 6: split out of RequestDetail.tsx (see build-log for the page-family
// split). No behavior change — the modal body below is unchanged.

interface LogContactModalProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  initialDirection: string;
  initialChannel: string;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onClose: () => void;
  // GAP-048: called after a tracker-bearing email is explicitly confirmed sent, so the
  // controller can refresh server-truth NeedsShare state (this modal never optimistically
  // clears it itself — recordShareIntent returns no updated detail).
  onShareIntentRecorded: () => void;
}

export function LogContactModal({
  requestId,
  detail,
  initialDirection,
  initialChannel,
  onDetailUpdated,
  onClose,
  onShareIntentRecorded,
}: LogContactModalProps) {
  const [channel, setChannel] = useState(initialChannel);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  const [showDiscardConfirm, setShowDiscardConfirm] = useState(false);
  // GAP-048: opening the mailto: draft is not proof of delivery — only this explicit
  // post-launch confirmation records the "email" share-intent event.
  const [trackerEmailLaunched, setTrackerEmailLaunched] = useState(false);
  const [shareSubmitting, setShareSubmitting] = useState(false);
  const [shareError, setShareError] = useState<string | null>(null);
  const keepEditingRef = useRef<HTMLButtonElement>(null);
  const discardRef = useRef<HTMLButtonElement>(null);
  const previousFocusRef = useRef<Element | null>(null);
  const { copiedId: phoneCopyState, failedId: phoneCopyFailed, copy: copyPhone } = useCopyFeedback();

  function attemptClose() {
    if (dirty) {
      setShowDiscardConfirm(true);
      return;
    }
    onClose();
  }

  useEffect(() => {
    if (!showDiscardConfirm) return;
    previousFocusRef.current = document.activeElement;
    keepEditingRef.current?.focus();

    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        setShowDiscardConfirm(false);
        return;
      }
      if (e.key !== "Tab") return;
      e.preventDefault();
      e.stopPropagation();
      const first = keepEditingRef.current;
      const last = discardRef.current;
      if (!first || !last) return;
      (document.activeElement === first ? last : first).focus();
    }
    document.addEventListener("keydown", onKeyDown, true);
    return () => {
      document.removeEventListener("keydown", onKeyDown, true);
      const prior = previousFocusRef.current;
      if (prior instanceof HTMLElement) prior.focus();
    };
  }, [showDiscardConfirm]);

  const showPhone = channel === "phone" && !!detail.customerPhone;
  const showSms = channel === "sms" && !!detail.customerPhone;
  const showEmail = channel === "email" && !!detail.customerEmail;
  const publicBaseUrl = getPublicBaseUrl();
  const customerPageUrl = detail.pageToken ? `${publicBaseUrl}/keep/r/${detail.pageToken}` : null;
  const directMessage = customerPageUrl
    ? `${detail.businessName}: Regarding your request, please see ${customerPageUrl}`
    : `${detail.businessName}: Regarding your request.`;
  // GAP-048: email may only carry the tracker link when the operator is permitted to record
  // share intent; otherwise it stays a plain email with no private token.
  const canShareTrackerViaEmail = !!customerPageUrl && detail.availableActions.canRecordShareIntent;
  const emailMessage = canShareTrackerViaEmail
    ? directMessage
    : `${detail.businessName}: Regarding your request.`;

  async function confirmTrackerEmailSent() {
    if (shareSubmitting) return;
    setShareSubmitting(true);
    setShareError(null);
    try {
      await api.recordShareIntent(requestId, "email");
      setTrackerEmailLaunched(false);
      onShareIntentRecorded();
    } catch {
      setShareError("Could not record this share. Try again.");
    } finally {
      setShareSubmitting(false);
    }
  }

  function dismissTrackerEmailConfirm() {
    setTrackerEmailLaunched(false);
    setShareError(null);
  }

  async function handleSubmit(body: Parameters<typeof api.logExternalContact>[1]) {
    if (isSubmitting || conflictDisabled) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const updated = await api.logExternalContact(requestId, body, detail.version);
      onDetailUpdated(updated);
      onClose();
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError(STATUS_CONFLICT_MESSAGE);
      } else {
        setError("Could not save contact log. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <ResponsiveSheet
      onClose={attemptClose}
      labelledBy="log-contact-dialog-heading"
      contentInert={showDiscardConfirm}
      header={
        <div className="flex items-center justify-between">
          <h2 id="log-contact-dialog-heading" className="text-base font-semibold text-[var(--ophalo-ink)]">Contact customer</h2>
          <button
            type="button"
            onClick={attemptClose}
            className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] p-1 rounded-md transition-colors ${FOCUS_RING}`}
          >
            <X className="h-4 w-4" />
            <span className="sr-only">Close</span>
          </button>
        </div>
      }
      overlay={
        showDiscardConfirm && (
          <div
            role="alertdialog"
            aria-modal="true"
            aria-label="Discard changes"
            className="absolute inset-0 z-10 flex items-center justify-center bg-black/30 px-6"
          >
            <div className="max-w-xs w-full rounded-lg bg-[var(--ophalo-card)] shadow-xl p-4 flex flex-col gap-3">
              <p className="text-sm text-[var(--ophalo-ink)]">Discard this contact log?</p>
              <div className="flex items-center justify-end gap-3">
                <button
                  ref={keepEditingRef}
                  type="button"
                  onClick={() => setShowDiscardConfirm(false)}
                  className={`text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] rounded ${FOCUS_RING}`}
                >
                  Keep editing
                </button>
                <button
                  ref={discardRef}
                  type="button"
                  onClick={onClose}
                  className={`px-3 py-1.5 rounded-lg text-sm font-medium bg-[var(--ophalo-danger)] text-white hover:opacity-90 ${FOCUS_RING}`}
                >
                  Discard
                </button>
              </div>
            </div>
          </div>
        )
      }
    >
      <p className="text-xs text-[var(--ophalo-muted)] mb-4">
        Use your phone to call or text, then record what actually happened. Opening a call,
        text, or email draft does not update Keep.
      </p>

      {showPhone && (
        <div className="flex flex-col gap-2 mb-4 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2.5">
          <div className="flex flex-wrap items-center gap-3">
            <span className="flex items-center gap-1.5 text-sm font-semibold text-[var(--ophalo-ink)]">
              <Phone className="h-3.5 w-3.5 text-[var(--keep-accent)] shrink-0" />
              {formatNaPhone(detail.customerPhone)}
            </span>
            <div className="flex items-center gap-2 ml-auto">
              <button
                type="button"
                onClick={() => void copyPhone(detail.customerPhone!, "phone")}
                className={`text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING}`}
              >
                {phoneCopyState === "phone" ? "Copied!" : phoneCopyFailed === "phone" ? "Couldn't copy" : "Copy"}
              </button>
              {/* Mobile: direct tel: link (ADR-443) */}
              <span className="md:hidden text-[var(--ophalo-border)]">·</span>
              <a
                href={`tel:${detail.customerPhone}`}
                className={`md:hidden text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING}`}
              >
                Call with phone app
              </a>
            </div>
          </div>
          {/* Desktop: QR handoff instead of direct tel: (ADR-443, GAP-020) */}
          <div className="hidden md:flex flex-col items-center gap-1.5 pt-2 border-t border-[var(--ophalo-border)]">
            <CallHandoffQr requestId={requestId} size={108} caption="Scan to call with your phone" />
          </div>
        </div>
      )}

      {showSms && detail.customerPhone && (
        <div className="flex flex-col gap-2 mb-4 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2.5">
          <div className="flex flex-wrap items-center gap-3">
            <span className="flex items-center gap-1.5 text-sm font-semibold text-[var(--ophalo-ink)]">
              {formatNaPhone(detail.customerPhone)}
            </span>
            <span className="ml-auto text-xs text-[var(--ophalo-muted)]">Text includes the request-page link</span>
          </div>
          <div className="hidden md:flex flex-col items-center gap-1.5 pt-2 border-t border-[var(--ophalo-border)]">
            <SmsHandoffQr requestId={requestId} message={directMessage} />
            <p className="text-xs text-[var(--ophalo-muted)] text-center">Scan to open the text draft on your phone.</p>
          </div>
          <a
            href={`sms:${detail.customerPhone}?&body=${encodeURIComponent(directMessage)}`}
            className={`md:hidden inline-flex items-center justify-center rounded-lg border-2 border-[var(--ophalo-navy)] px-4 py-2 text-sm font-semibold text-[var(--ophalo-navy)] ${FOCUS_RING}`}
          >
            Open text draft
          </a>
        </div>
      )}

      {showEmail && detail.customerEmail && (
        <div className="mb-4">
          <a
            href={`mailto:${detail.customerEmail}?subject=${encodeURIComponent("Regarding your request")}&body=${encodeURIComponent(emailMessage)}`}
            onClick={() => {
              if (canShareTrackerViaEmail) setTrackerEmailLaunched(true);
            }}
            className={`inline-flex w-full items-center justify-center rounded-lg border-2 border-[var(--ophalo-navy)] px-4 py-2 text-sm font-semibold text-[var(--ophalo-navy)] ${FOCUS_RING}`}
          >
            {canShareTrackerViaEmail ? "Open email draft with request link" : "Open email draft"}
          </a>
          {canShareTrackerViaEmail && trackerEmailLaunched && (
            <div className="mt-2 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] p-3">
              <p className="mb-2 text-xs text-[var(--ophalo-muted)]">
                Confirm only after you have actually sent the draft containing the request-page
                link.
              </p>
              {shareError && (
                <p aria-live="polite" className="mb-2 text-xs text-[var(--ophalo-danger)]">
                  {shareError}
                </p>
              )}
              <div className="flex items-center gap-3">
                <KeepButton
                  type="button"
                  variant="teal"
                  disabled={shareSubmitting}
                  onClick={() => void confirmTrackerEmailSent()}
                >
                  {shareSubmitting ? "Confirming…" : "I sent it — confirm"}
                </KeepButton>
                <button
                  type="button"
                  onClick={dismissTrackerEmailConfirm}
                  className={`text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING} rounded`}
                >
                  Not sent
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      <ExternalContactForm
        initialDirection={initialDirection as "outbound" | "inbound"}
        initialChannel={initialChannel}
        maxSummaryLength={detail.validation.externalContactSummaryMaxLength}
        loading={isSubmitting}
        disabled={conflictDisabled}
        error={error}
        onSubmit={(body) => void handleSubmit(body)}
        onCancel={attemptClose}
        onChannelChange={setChannel}
        onDirtyChange={setDirty}
      />
    </ResponsiveSheet>
  );
}
