import { useCallback, useState } from "react";
import QRCode from "react-qr-code";
import { RefreshCw } from "lucide-react";
import { api, ApiError, type KeepRequestDetailResult } from "../../lib/apiClient";
import { getPublicBaseUrl } from "../../lib/publicBaseUrl";
import { KeepButton } from "../../components/keep/KeepButton";
import { KeepBadge } from "../../components/keep/KeepBadge";
import { formatEventTime, suggestedNotifyChannel, type NotifyChannel } from "./helpers";
import { formatNaPhone } from "../../components/quick-capture/utils";
import { useHandoffMint } from "./useHandoffMint";

type Channel = NotifyChannel;

interface NotifyCustomerPanelProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  relatedUpdateEventId: string;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onDone: () => void;
  // Set when the composer's auto-prepare (Post & Prepare <channel>) call failed right after
  // posting — surfaces the failure here instead of dropping it, and lets the operator retry
  // the prepare step manually from the selection phase below.
  initialError?: string | null;
}

// GAP-052b / ADR-451: post → prepare → confirm is three distinct, separately attested actions.
// Launching the SMS/email draft (an external app handoff) never itself confirms anything — only
// an explicit "I sent it" click calls ConfirmUpdateNotification. Phase is derived from
// detail.pendingNotification rather than local-only state so a reload/navigate-away-and-return
// truthfully resumes: the durable obligation on the server is the source of truth, not this
// component's memory.
export function NotifyCustomerPanel({
  requestId,
  detail,
  relatedUpdateEventId,
  onDetailUpdated,
  onDone,
  initialError = null,
}: NotifyCustomerPanelProps) {
  const pending =
    detail.pendingNotification?.relatedUpdateEventId === relatedUpdateEventId
      ? detail.pendingNotification
      : null;

  const suggestedChannel: Channel = suggestedNotifyChannel(detail);
  const [selectedChannel, setSelectedChannel] = useState<Channel>(suggestedChannel);
  const [isPreparing, setIsPreparing] = useState(false);
  const [isConfirming, setIsConfirming] = useState(false);
  const [error, setError] = useState<string | null>(initialError);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  // The desktop QR is a handoff tool, not the task itself. Keep it out of the default request
  // canvas until the operator explicitly chooses to send the prepared text from their phone.
  const [smsHandoffOpen, setSmsHandoffOpen] = useState(false);

  const publicBaseUrl = getPublicBaseUrl();
  const customerPageUrl = `${publicBaseUrl}/keep/r/${detail.pageToken}`;
  const messageBody = `${detail.businessName}: You have an update on your request. View it here: ${customerPageUrl}`;

  const activeChannel = pending?.channel === "sms" || pending?.channel === "email"
    ? pending.channel
    : selectedChannel;

  // Mint a fresh desktop SMS QR handoff whenever the prepared phase is entered for the sms
  // channel — Keep stores no draft text, only the pending pointer, so this regenerates each time.
  const mintSmsHandoff = useCallback(
    () => api.createSmsHandoff(requestId, messageBody),
    [requestId, messageBody, pending?.relatedUpdateEventId],
  );
  const {
    handoffUrl: smsHandoffUrl,
    isLoading: isSmsHandoffLoading,
    error: smsHandoffError,
    retry: retrySmsHandoff,
  } = useHandoffMint(
    pending?.channel === "sms" && smsHandoffOpen,
    mintSmsHandoff,
    "Could not create text link. Try again.",
  );

  async function handlePrepare() {
    setIsPreparing(true);
    setError(null);
    try {
      const updated = await api.prepareUpdateNotification(
        requestId,
        { relatedUpdateEventId, channel: activeChannel },
        detail.version,
      );
      onDetailUpdated(updated);
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError("This request changed. Refresh to see the latest state.");
      } else {
        setError("Could not prepare the notification. Try again.");
      }
    } finally {
      setIsPreparing(false);
    }
  }

  async function handleConfirm() {
    if (!pending) return;
    setIsConfirming(true);
    setError(null);
    try {
      const updated = await api.confirmUpdateNotification(
        requestId,
        { relatedUpdateEventId, channel: pending.channel },
        detail.version,
      );
      onDetailUpdated(updated);
      onDone();
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError("This request changed. Refresh to see the latest state.");
      } else if (e instanceof ApiError && e.code === "KeepRequest.NotificationConfirmerMismatch") {
        setError("This was prepared by another teammate. They can confirm it, or you can prepare a new one.");
      } else if (e instanceof ApiError && e.code === "KeepRequest.NotificationNotPrepared") {
        setError("That prepared notification is no longer available. Prepare it again.");
      } else {
        setError("Could not confirm the notification. Try again.");
      }
    } finally {
      setIsConfirming(false);
    }
  }

  const errorBlock = error && (
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

  // --- Prepared phase: launch the external draft, then require explicit confirmation ---
  if (pending) {
    const preparedByCurrentUser = pending.canConfirmAsCurrentUser;
    return (
      <div className="rounded-xl border border-[var(--keep-request-attention-border)] bg-[var(--keep-request-attention-bg)] px-4 py-4">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <p className="text-sm font-semibold text-[var(--ophalo-ink)]">
            {pending.channel === "sms" ? "Text notification prepared" : "Email notification prepared"}
          </p>
          <KeepBadge variant="attention">Prepared {formatEventTime(pending.preparedAtUtc)}</KeepBadge>
        </div>
        {errorBlock}
        {!preparedByCurrentUser ? (
          <>
            <p className="mb-3 text-xs text-[var(--ophalo-muted)]">
              This notification was prepared by another teammate and is waiting on their
              confirmation. Only they can confirm it, or you can prepare a new one.
            </p>
            <KeepButton
              type="button"
              variant="secondary"
              disabled={isPreparing || conflictDisabled}
              onClick={() => void handlePrepare()}
              className="w-full"
            >
              {isPreparing ? "Preparing…" : "Prepare a new one"}
            </KeepButton>
          </>
        ) : (
          <>
            {pending.channel === "sms" ? (
              <div className="mb-4">
                <p className="mb-3 text-xs text-[var(--ophalo-muted)]">
                  The customer-page update is posted. Send the companion text, then confirm it here.
                </p>
                {/* Desktop: an opaque QR handoff is explicitly opened on demand. It never encodes
                    raw phone/message text (ADR-448 pattern). */}
                <div className="hidden md:block">
                  <KeepButton
                    type="button"
                    variant="secondary"
                    aria-expanded={smsHandoffOpen}
                    aria-controls="prepared-sms-handoff"
                    onClick={() => setSmsHandoffOpen((open) => !open)}
                    className="w-full"
                  >
                    {smsHandoffOpen ? "Hide phone handoff" : "Send text from phone"}
                  </KeepButton>
                  {smsHandoffOpen && (
                    <div id="prepared-sms-handoff" className="mt-3 flex flex-col items-center gap-2 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-4">
                      {isSmsHandoffLoading ? (
                        <div
                          className="flex items-center justify-center"
                          style={{ height: 160, width: 160 }}
                          role="status"
                          aria-label="Preparing text link"
                        >
                          <RefreshCw className="h-5 w-5 animate-spin text-[var(--ophalo-muted)]" />
                        </div>
                      ) : smsHandoffUrl ? (
                        <div className="bg-white p-2 rounded-lg">
                          <QRCode value={smsHandoffUrl} size={160} />
                        </div>
                      ) : smsHandoffError ? (
                        <div className="flex flex-col items-center gap-2 text-center">
                          <p className="text-xs text-[var(--ophalo-danger)]">{smsHandoffError}</p>
                          <button
                            type="button"
                            onClick={() => void retrySmsHandoff()}
                            className="text-xs font-medium text-[var(--keep-accent)] hover:underline"
                          >
                            Try again
                          </button>
                        </div>
                      ) : null}
                      <p className="text-xs text-[var(--ophalo-muted)] text-center">
                        Scan with your phone to open the text draft to {formatNaPhone(detail.customerPhone)}.
                      </p>
                    </div>
                  )}
                </div>
                {/* Mobile: direct sms: launch */}
                <a
                  href={`sms:${detail.customerPhone}?&body=${encodeURIComponent(messageBody)}`}
                  className="md:hidden inline-flex w-full items-center justify-center rounded-lg border-2 border-[var(--ophalo-navy)] px-4 py-2 text-sm font-semibold text-[var(--ophalo-navy)]"
                >
                  Open text draft
                </a>
              </div>
            ) : (
              <a
                href={`mailto:${detail.customerEmail}?subject=${encodeURIComponent("Update on your request")}&body=${encodeURIComponent(messageBody)}`}
                className="mb-4 inline-flex w-full items-center justify-center rounded-lg border-2 border-[var(--ophalo-navy)] px-4 py-2 text-sm font-semibold text-[var(--ophalo-navy)]"
              >
                Open email draft
              </a>
            )}
            <p className="mb-3 text-xs text-[var(--ophalo-muted)]">
              Confirm only after you have actually sent the notification.
            </p>
            <KeepButton
              type="button"
              variant="teal"
              disabled={isConfirming || conflictDisabled}
              onClick={() => void handleConfirm()}
              className="w-full"
            >
              {isConfirming ? "Confirming…" : "I sent it — Confirm"}
            </KeepButton>
          </>
        )}
      </div>
    );
  }

  // --- Selection phase: choose a channel before preparing ---
  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-4">
      <p className="mb-3 text-sm font-semibold text-[var(--ophalo-ink)]">
        Notify the customer about this update
      </p>
      {errorBlock}
      <div className="mb-3 flex gap-2" role="radiogroup" aria-label="Notification channel">
        <button
          type="button"
          role="radio"
          aria-checked={selectedChannel === "sms"}
          onClick={() => setSelectedChannel("sms")}
          className={`flex-1 rounded-lg border px-3 py-2 text-sm font-semibold ${
            selectedChannel === "sms"
              ? "border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
              : "border-[var(--ophalo-border)] text-[var(--ophalo-muted)]"
          }`}
        >
          Text message
          {suggestedChannel === "sms" && (
            <span className="ml-1.5 text-[10px] font-normal">(preferred)</span>
          )}
        </button>
        {detail.customerEmail && (
          <button
            type="button"
            role="radio"
            aria-checked={selectedChannel === "email"}
            onClick={() => setSelectedChannel("email")}
            className={`flex-1 rounded-lg border px-3 py-2 text-sm font-semibold ${
              selectedChannel === "email"
                ? "border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
                : "border-[var(--ophalo-border)] text-[var(--ophalo-muted)]"
            }`}
          >
            Email
            {suggestedChannel === "email" && (
              <span className="ml-1.5 text-[10px] font-normal">(preferred)</span>
            )}
          </button>
        )}
      </div>
      <div className="flex gap-2">
        <KeepButton
          type="button"
          variant="secondary"
          disabled={isPreparing}
          onClick={onDone}
          className="flex-1"
        >
          Not now
        </KeepButton>
        <KeepButton
          type="button"
          variant="teal"
          disabled={isPreparing || conflictDisabled}
          onClick={() => void handlePrepare()}
          className="flex-1"
        >
          {isPreparing ? "Preparing…" : "Continue"}
        </KeepButton>
      </div>
    </div>
  );
}
