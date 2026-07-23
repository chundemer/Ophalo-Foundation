import { useState, useEffect, useRef, useCallback } from "react";
import { useQuery } from "@tanstack/react-query";
import QRCode from "react-qr-code";
import {
  X,
  Mail,
  MessageSquare,
  Copy,
  Link,
  Check,
  RefreshCw,
  AlertTriangle,
  Share2,
} from "lucide-react";
import { api, type ShareIntentMethod } from "../lib/apiClient";
import { ApiError } from "../lib/apiClient";
import { KeepButton } from "./keep/KeepButton";
import { formatNaPhone } from "./quick-capture/utils";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_BASE =
  "w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm " +
  "text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2.5 " +
  FOCUS_RING;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function firstName(fullName: string): string {
  return fullName.trim().split(/\s+/)[0] ?? fullName;
}

function buildDefaultMessage(
  customerName: string,
  businessName: string,
  customerPageUrl: string
): string {
  return (
    `Hi ${firstName(customerName)} — ${businessName} created a private request page for you:\n` +
    `${customerPageUrl}\n\n` +
    `No account is needed. You can save this link to view updates, ask a question, request a call, or send details anytime.`
  );
}

function cleanPhone(phone: string): string {
  return phone.replace(/\D/g, "");
}

function buildWhatsAppUri(phone: string, message: string): string {
  return `https://wa.me/${cleanPhone(phone)}?text=${encodeURIComponent(message)}`;
}

function buildMailtoUri(email: string, message: string): string {
  const subject = encodeURIComponent(`Your request page`);
  const body = encodeURIComponent(message);
  return `mailto:${email}?subject=${subject}&body=${body}`;
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface ShareLinkModalProps {
  requestId: string;
  onClose: () => void;
  onShared: () => void;
}

// ---------------------------------------------------------------------------
// Main component
// ---------------------------------------------------------------------------

export function ShareLinkModal({ requestId, onClose, onShared }: ShareLinkModalProps) {
  const publicBaseUrl = import.meta.env.VITE_PUBLIC_BASE_URL as string;
  const appBaseUrl = import.meta.env.VITE_APP_BASE_URL as string;

  const { data: detail } = useQuery({
    queryKey: ["request-detail", requestId],
    queryFn: () => api.getRequestDetail(requestId),
    staleTime: 30_000,
  });
  const { data: setup } = useQuery({
    queryKey: ["setup"],
    queryFn: api.getSetup,
    staleTime: 5 * 60_000,
  });

  const customerName = detail?.customerName ?? "";
  const customerPhone = detail?.customerPhone ?? "";
  const customerEmail = detail?.customerEmail ?? null;
  const pageToken = detail?.pageToken ?? "";
  const businessName = setup?.businessName ?? "";
  const customerPageUrl = pageToken ? `${publicBaseUrl}/keep/r/${pageToken}` : "";

  const [messageBody, setMessageBody] = useState("");
  const [handoffUrl, setHandoffUrl] = useState<string | null>(null);
  const [expiresAtUtc, setExpiresAtUtc] = useState<string | null>(null);
  const [isStale, setIsStale] = useState(false);
  const [isCreatingToken, setIsCreatingToken] = useState(false);
  const [tokenError, setTokenError] = useState<string | null>(null);
  const [preparedVia, setPreparedVia] = useState<ShareIntentMethod | null>(null);
  const [preparedLabel, setPreparedLabel] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [copied, setCopied] = useState<"message" | "link" | null>(null);
  const [isExpired, setIsExpired] = useState(false);
  const initialized = useRef(false);

  // Set default message once data is available
  useEffect(() => {
    if (!initialized.current && customerName && businessName && customerPageUrl) {
      setMessageBody(buildDefaultMessage(customerName, businessName, customerPageUrl));
    }
  }, [customerName, businessName, customerPageUrl]);

  const mintToken = useCallback(
    async (body: string) => {
      if (!requestId || !body.trim()) return;
      setIsCreatingToken(true);
      setTokenError(null);
      setIsStale(false);
      setIsExpired(false);
      try {
        const result = await api.createSmsHandoff(requestId, body);
        // Rewrite production host to local app host in dev
        const url = result.handoffUrl.replace(/^https:\/\/app\.ophalo\.com/, appBaseUrl);
        setHandoffUrl(url);
        setExpiresAtUtc(result.expiresAtUtc);
        initialized.current = true;
      } catch {
        setTokenError("Could not create text link. Try again.");
      } finally {
        setIsCreatingToken(false);
      }
    },
    [requestId, appBaseUrl]
  );

  // Mint token once message and phone are ready
  useEffect(() => {
    if (!initialized.current && messageBody && customerPhone) {
      initialized.current = true;
      void mintToken(messageBody);
    }
  }, [messageBody, customerPhone, mintToken]);

  // Expiry polling — check every 30s
  useEffect(() => {
    if (!expiresAtUtc) return;
    const check = () => setIsExpired(Date.now() >= new Date(expiresAtUtc).getTime());
    check();
    const id = setInterval(check, 30_000);
    return () => clearInterval(id);
  }, [expiresAtUtc]);

  // Keyboard dismiss
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  function handleMessageChange(e: React.ChangeEvent<HTMLTextAreaElement>) {
    setMessageBody(e.target.value);
    if (handoffUrl && !isStale) setIsStale(true);
  }

  function setPrepared(method: ShareIntentMethod, label: string) {
    setPreparedVia(method);
    setPreparedLabel(label);
  }

  async function handleCopyMessage() {
    try {
      await navigator.clipboard.writeText(messageBody);
      setCopied("message");
      setTimeout(() => setCopied(null), 2500);
      setPrepared("copy_message", "Message copied.");
    } catch { /* ignore */ }
  }

  async function handleCopyLink() {
    try {
      await navigator.clipboard.writeText(customerPageUrl);
      setCopied("link");
      setTimeout(() => setCopied(null), 2500);
      setPrepared("copy_link", "Link copied.");
    } catch { /* ignore */ }
  }

  async function handleMarkAsShared() {
    setSubmitting(true);
    setSubmitError(null);
    try {
      await api.recordShareIntent(requestId, preparedVia ?? "manual_other");
      onShared();
    } catch (e) {
      setSubmitError(
        e instanceof ApiError ? "Could not record share. Try again." : "Something went wrong."
      );
      setSubmitting(false);
    }
  }

  // ---------------------------------------------------------------------------
  // QR section
  // ---------------------------------------------------------------------------

  function renderQr() {
    if (!customerPhone) return null;

    if (isCreatingToken) {
      return (
        <div className="flex h-[180px] w-full items-center justify-center">
          <RefreshCw className="h-6 w-6 animate-spin text-[var(--ophalo-muted)]" />
        </div>
      );
    }

    if (tokenError) {
      return (
        <div className="flex flex-col items-center gap-3 text-center">
          <p className="text-sm text-[var(--ophalo-danger)]">{tokenError}</p>
          <button
            type="button"
            onClick={() => void mintToken(messageBody)}
            className={`text-sm font-medium text-[var(--keep-accent)] hover:underline ${FOCUS_RING}`}
          >
            Try again
          </button>
        </div>
      );
    }

    if (isExpired) {
      return (
        <div className="flex flex-col items-center gap-3 text-center">
          <p className="text-sm font-medium text-[var(--ophalo-muted)]">Text link expired.</p>
          <KeepButton variant="teal" className="text-sm" onClick={() => void mintToken(messageBody)}>
            Create New Text Link
          </KeepButton>
        </div>
      );
    }

    if (isStale && handoffUrl) {
      return (
        <div className="flex flex-col items-center gap-3">
          <div className="opacity-25 pointer-events-none select-none">
            <QRCode value={handoffUrl} size={160} />
          </div>
          <KeepButton variant="teal" className="text-sm" onClick={() => void mintToken(messageBody)}>
            Update Text Link to Match Edits
          </KeepButton>
        </div>
      );
    }

    if (handoffUrl) {
      return (
        <div className="flex flex-col items-center gap-2">
          <div className="rounded-lg bg-white p-2 shadow-sm">
            <QRCode value={handoffUrl} size={160} />
          </div>
          <p className="text-xs text-[var(--ophalo-muted)] text-center">
            Scan with your phone to open a pre-filled text draft
          </p>
        </div>
      );
    }

    return null;
  }

  const isLoading = !detail || !setup;

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <div className="fixed inset-0 z-50" role="dialog" aria-modal="true" aria-label="Share Request Link">
      {/* Backdrop */}
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />

      {/* Sheet bottom on mobile / centered modal on desktop */}
      <div className="absolute inset-x-0 bottom-0 sm:inset-0 sm:flex sm:items-center sm:justify-center sm:p-4">
        <div
          className="relative bg-[var(--ophalo-card)] rounded-t-2xl sm:rounded-2xl shadow-xl w-full sm:max-w-2xl max-h-[90vh] overflow-y-auto"
          onClick={(e) => e.stopPropagation()}
        >
          {/* Handle on mobile */}
          <div className="sm:hidden flex justify-center pt-3 pb-1">
            <div className="h-1 w-10 rounded-full bg-[var(--ophalo-border)]" />
          </div>

          {/* Header */}
          <div className="flex items-start justify-between px-5 py-4 border-b border-[var(--ophalo-border)]">
            <div className="min-w-0">
              <h2 className="text-base font-semibold text-[var(--ophalo-ink)]">Share Request Link</h2>
              {customerName && (
                <p className="text-sm text-[var(--ophalo-muted)] mt-0.5 truncate">{customerName}</p>
              )}
            </div>
            <button
              type="button"
              onClick={onClose}
              aria-label="Close"
              className={`shrink-0 ml-4 rounded-lg p-1 text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING}`}
            >
              <X className="h-5 w-5" />
            </button>
          </div>

          {/* Loading */}
          {isLoading && (
            <div className="flex items-center justify-center h-48">
              <RefreshCw className="h-6 w-6 animate-spin text-[var(--ophalo-muted)]" />
            </div>
          )}

          {/* Body — two columns on sm+ */}
          {!isLoading && (
            <div className="grid sm:grid-cols-2">
              {/* Left: message + channel actions */}
              <div className="px-5 py-5 flex flex-col gap-4">
                {/* Privacy notice */}
                <p className="text-xs text-[var(--ophalo-muted)] bg-[var(--ophalo-canvas)] rounded-lg px-3 py-2.5 border border-[var(--ophalo-border)]">
                  Anyone with this private link can view the customer request page.
                </p>

                {/* Contact preview */}
                <div className="flex flex-col gap-1 text-sm">
                  <div className="flex items-center gap-1.5 text-[var(--ophalo-ink)]">
                    <MessageSquare className="h-3.5 w-3.5 text-[var(--ophalo-muted)] shrink-0" />
                    <span className="font-medium">{formatNaPhone(customerPhone)}</span>
                  </div>
                  {customerEmail && (
                    <div className="flex items-center gap-1.5 text-[var(--ophalo-muted)]">
                      <Mail className="h-3.5 w-3.5 shrink-0" />
                      <span>{customerEmail}</span>
                    </div>
                  )}
                </div>

                {/* Editable message */}
                <div className="flex flex-col gap-1.5">
                  <label className="text-xs font-semibold text-[var(--ophalo-muted)] uppercase tracking-wide">
                    Message
                  </label>
                  <textarea
                    value={messageBody}
                    onChange={handleMessageChange}
                    rows={6}
                    className={INPUT_BASE}
                    placeholder="Enter share message…"
                  />
                  <p className="text-xs text-[var(--ophalo-muted)]">
                    Editing message stales the Scan to Text QR.
                  </p>
                </div>

                {/* Prepared state */}
                {preparedLabel && (
                  <p className="text-sm font-medium text-[var(--ophalo-success)]">
                    {preparedLabel} After you send it, mark this request as shared.
                  </p>
                )}

                {/* Channel actions */}
                <div className="flex flex-col gap-2">
                  {customerEmail ? (
                    <a
                      href={buildMailtoUri(customerEmail, messageBody)}
                      className="block"
                      onClick={() => setPrepared("email", "Prepared via Email.")}
                    >
                      <ChannelButton icon={<Mail className="h-4 w-4" />} label="Open Email" />
                    </a>
                  ) : (
                    <ChannelButton
                      icon={<Mail className="h-4 w-4" />}
                      label="Open Email"
                      disabled
                      hint="Add customer email to enable email draft."
                    />
                  )}

                  <a
                    href={buildWhatsAppUri(customerPhone, messageBody)}
                    target="_blank"
                    rel="noreferrer"
                    className="block"
                    onClick={() => setPrepared("whatsapp", "Prepared via WhatsApp.")}
                  >
                    <ChannelButton
                      icon={<Share2 className="h-4 w-4" />}
                      label="Open WhatsApp"
                    />
                  </a>

                  <button
                    type="button"
                    onClick={() => void handleCopyMessage()}
                    className="block w-full text-left"
                  >
                    <ChannelButton
                      icon={
                        copied === "message"
                          ? <Check className="h-4 w-4 text-[var(--ophalo-success)]" />
                          : <Copy className="h-4 w-4" />
                      }
                      label={copied === "message" ? "Message Copied!" : "Copy Message"}
                    />
                  </button>

                  <button
                    type="button"
                    onClick={() => void handleCopyLink()}
                    className="block w-full text-left"
                  >
                    <ChannelButton
                      icon={
                        copied === "link"
                          ? <Check className="h-4 w-4 text-[var(--ophalo-success)]" />
                          : <Link className="h-4 w-4" />
                      }
                      label={copied === "link" ? "Link Copied!" : "Copy Link"}
                    />
                  </button>
                </div>
              </div>

              {/* Right: QR + Mark as Shared */}
              <div className="px-5 py-5 flex flex-col gap-5 border-t border-[var(--ophalo-border)] sm:border-t-0 sm:border-l sm:border-[var(--ophalo-border)]">
                <div className="flex flex-col gap-3">
                  <p className="text-xs font-semibold text-[var(--ophalo-muted)] uppercase tracking-wide">
                    Scan to Text
                  </p>
                  <div className="flex justify-center min-h-[180px] items-center">
                    {renderQr()}
                  </div>
                </div>

                <div className="mt-auto flex flex-col gap-2">
                  {submitError && (
                    <p className="flex items-center gap-1.5 text-sm text-[var(--ophalo-danger)]">
                      <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
                      {submitError}
                    </p>
                  )}
                  <KeepButton
                    variant="teal"
                    className="w-full"
                    onClick={() => void handleMarkAsShared()}
                    disabled={submitting}
                  >
                    {submitting ? "Marking as shared…" : "Mark as Shared"}
                  </KeepButton>
                  <p className="text-xs text-center text-[var(--ophalo-muted)]">
                    Records that you shared this request page with the customer.
                  </p>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Channel button sub-component
// ---------------------------------------------------------------------------

function ChannelButton({
  icon,
  label,
  disabled,
  hint,
}: {
  icon: React.ReactNode;
  label: string;
  disabled?: boolean;
  hint?: string;
}) {
  return (
    <div
      className={`flex items-center gap-2.5 rounded-lg border px-3 py-2.5 text-sm font-medium transition-colors ${
        disabled
          ? "border-[var(--ophalo-border)] bg-[var(--muted)] text-[var(--ophalo-muted)] cursor-not-allowed"
          : "border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-[var(--ophalo-ink)] hover:border-[var(--keep-accent)] hover:text-[var(--keep-accent)]"
      }`}
    >
      <span className="shrink-0">{icon}</span>
      <span className="min-w-0">
        <span className="block">{label}</span>
        {hint && (
          <span className="block text-xs font-normal text-[var(--ophalo-muted)]">{hint}</span>
        )}
      </span>
    </div>
  );
}
