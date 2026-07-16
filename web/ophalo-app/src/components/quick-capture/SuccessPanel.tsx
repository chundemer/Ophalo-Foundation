import { useState, useEffect, useRef, useCallback } from "react";
import { useQuery } from "@tanstack/react-query";
import QRCode from "react-qr-code";
import { ExternalLink, RefreshCw, Mail, MessageSquare, Check } from "lucide-react";
import { api, type ShareIntentMethod } from "../../lib/apiClient";

function firstName(fullName: string): string {
  return fullName.trim().split(/\s+/)[0] ?? fullName;
}

function buildMessage(customerName: string, businessName: string, customerPageUrl: string): string {
  return (
    `Hi ${firstName(customerName)} — ${businessName} created a private request page for you:\n` +
    `${customerPageUrl}\n\n` +
    `No account is needed. You can save this link to view updates, ask a question, request a call, or send details anytime.`
  );
}

interface SuccessPanelProps {
  requestId: string;
  referenceCode: string;
  pageToken: string;
  publicBaseUrl: string;
  customerPhone: string;
  customerEmail: string | null;
  customerName: string;
  onCaptureAnother: () => void;
  onViewRequest: () => void;
}

export function SuccessPanel({
  requestId,
  referenceCode,
  pageToken,
  publicBaseUrl,
  customerPhone,
  customerEmail,
  customerName,
  onCaptureAnother,
  onViewRequest,
}: SuccessPanelProps) {
  const appBaseUrl = import.meta.env.VITE_APP_BASE_URL as string;
  const customerPageUrl = `${publicBaseUrl.replace(/\/$/, "")}/keep/r/${pageToken}`;

  const { data: setup } = useQuery({
    queryKey: ["setup"],
    queryFn: api.getSetup,
    staleTime: 5 * 60_000,
  });

  const businessName = setup?.businessName ?? "";
  const message = customerName && businessName
    ? buildMessage(customerName, businessName, customerPageUrl)
    : customerPageUrl;

  // SMS handoff token state (drives desktop QR)
  const [handoffUrl, setHandoffUrl] = useState<string | null>(null);
  const [expiresAtUtc, setExpiresAtUtc] = useState<string | null>(null);
  const [isCreatingToken, setIsCreatingToken] = useState(false);
  const [tokenError, setTokenError] = useState<string | null>(null);
  const [isExpired, setIsExpired] = useState(false);

  // Prepared-handoff and mark-as-shared state
  const [preparedVia, setPreparedVia] = useState<ShareIntentMethod | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [marked, setMarked] = useState(false);

  const mintToken = useCallback(async () => {
    if (!businessName) return;
    setIsCreatingToken(true);
    setTokenError(null);
    setIsExpired(false);
    try {
      const msg = buildMessage(customerName, businessName, customerPageUrl);
      const result = await api.createSmsHandoff(requestId, msg);
      const url = result.handoffUrl.replace(/^https:\/\/app\.ophalo\.com/, appBaseUrl);
      setHandoffUrl(url);
      setExpiresAtUtc(result.expiresAtUtc);
    } catch {
      setTokenError("Could not create text link. Try again.");
    } finally {
      setIsCreatingToken(false);
    }
  }, [requestId, customerName, businessName, customerPageUrl, appBaseUrl]);

  // Mint once when businessName resolves
  const mintedRef = useRef(false);
  useEffect(() => {
    if (mintedRef.current || !businessName) return;
    mintedRef.current = true;
    void mintToken();
  }, [businessName, mintToken]);

  // Expiry polling
  useEffect(() => {
    if (!expiresAtUtc) return;
    const check = () => setIsExpired(Date.now() >= new Date(expiresAtUtc).getTime());
    check();
    const id = setInterval(check, 30_000);
    return () => clearInterval(id);
  }, [expiresAtUtc]);

  async function handleMarkAsShared() {
    if (!preparedVia) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      await api.recordShareIntent(requestId, preparedVia);
      setMarked(true);
    } catch {
      setSubmitError("Could not record share. Try again.");
      setSubmitting(false);
    }
  }

  const smsHref = `sms:${customerPhone}?body=${encodeURIComponent(message)}`;
  const mailtoHref = customerEmail
    ? `mailto:${customerEmail}?subject=${encodeURIComponent("Your request page link")}&body=${encodeURIComponent(message)}`
    : `mailto:?subject=${encodeURIComponent("Your request page link")}&body=${encodeURIComponent(message)}`;

  function renderQr() {
    if (isCreatingToken || (!handoffUrl && !tokenError)) {
      return (
        <div className="flex h-[160px] w-full items-center justify-center">
          <RefreshCw className="h-5 w-5 animate-spin text-slate-400" />
        </div>
      );
    }
    if (tokenError) {
      return (
        <div className="flex flex-col items-center gap-2 text-center">
          <p className="text-sm text-red-600">{tokenError}</p>
          <button
            type="button"
            onClick={() => void mintToken()}
            className="text-sm font-medium text-[var(--keep-accent)] hover:underline"
          >
            Try again
          </button>
        </div>
      );
    }
    if (isExpired) {
      return (
        <div className="flex flex-col items-center gap-2 text-center">
          <p className="text-sm text-slate-500">Text link expired.</p>
          <button
            type="button"
            onClick={() => void mintToken()}
            className="text-sm font-medium text-[var(--keep-accent)] hover:underline"
          >
            Refresh text link
          </button>
        </div>
      );
    }
    if (handoffUrl) {
      return (
        <div className="flex flex-col items-center gap-3">
          <div className="rounded-lg bg-white p-2 shadow-sm">
            <QRCode value={handoffUrl} size={140} />
          </div>
          <p className="text-xs text-slate-500 text-center">
            Scan with your phone to send a pre-addressed text
          </p>
          <button
            type="button"
            onClick={() => setPreparedVia("sms_qr")}
            className="w-full flex items-center justify-center gap-2 rounded-md border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
          >
            <MessageSquare className="h-4 w-4" />
            Done — I sent the text
          </button>
        </div>
      );
    }
    return null;
  }

  return (
    <div className="flex flex-col gap-4">
      {/* Success banner */}
      <div className="rounded-md bg-emerald-50 border border-emerald-200 px-4 py-3">
        <p className="text-sm font-medium text-emerald-800">Request captured</p>
        <p className="text-xs text-emerald-600 font-mono mt-0.5">{referenceCode}</p>
      </div>

      <p className="text-xs text-amber-700 font-medium">
        Share the customer page so they can follow progress.
      </p>

      {/* Desktop: QR + Email */}
      <div className="hidden md:flex flex-col gap-3">
        {renderQr()}
        <a
          href={mailtoHref}
          onClick={() => setPreparedVia("email")}
          className="w-full flex items-center justify-center gap-2 rounded-md border border-slate-300 px-4 py-2.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          <Mail className="h-4 w-4" />
          Email customer page link
        </a>
      </div>

      {/* Mobile: Text + Email */}
      <div className="flex md:hidden flex-col gap-2">
        <a
          href={smsHref}
          onClick={() => setPreparedVia("sms_qr")}
          className="w-full flex items-center justify-center gap-2 rounded-md bg-slate-900 px-4 py-2.5 text-sm font-medium text-white hover:bg-slate-700"
        >
          <MessageSquare className="h-4 w-4" />
          Text customer page link
        </a>
        <a
          href={mailtoHref}
          onClick={() => setPreparedVia("email")}
          className="w-full flex items-center justify-center gap-2 rounded-md border border-slate-300 px-4 py-2.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          <Mail className="h-4 w-4" />
          Email customer page link
        </a>
      </div>

      {/* Mark as Shared — appears after a handoff action is prepared */}
      {preparedVia && !marked && (
        <div className="flex flex-col gap-2">
          {submitError && (
            <p className="text-sm text-red-600">{submitError}</p>
          )}
          <button
            type="button"
            onClick={() => void handleMarkAsShared()}
            disabled={submitting}
            className="w-full flex items-center justify-center gap-2 rounded-md bg-teal-600 px-4 py-2.5 text-sm font-medium text-white hover:bg-teal-700 disabled:opacity-50"
          >
            {submitting ? "Marking…" : "Mark as Shared"}
          </button>
          <p className="text-xs text-center text-slate-500">
            Records that you shared this request page with the customer.
          </p>
        </div>
      )}
      {marked && (
        <p className="flex items-center gap-1.5 text-sm font-medium text-emerald-700">
          <Check className="h-4 w-4" />
          Marked as shared.
        </p>
      )}

      {/* Navigation */}
      <div className="flex flex-col gap-2 pt-2 border-t border-slate-100">
        <button
          type="button"
          onClick={onViewRequest}
          className="w-full flex items-center justify-center gap-2 rounded-md border border-slate-300 px-4 py-2.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          <ExternalLink className="h-4 w-4" />
          View Request Workbench
        </button>
        <button
          type="button"
          onClick={onCaptureAnother}
          className="w-full flex items-center justify-center gap-2 rounded-md border border-slate-300 px-4 py-2.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          <RefreshCw className="h-4 w-4" />
          Capture Another
        </button>
      </div>
    </div>
  );
}
