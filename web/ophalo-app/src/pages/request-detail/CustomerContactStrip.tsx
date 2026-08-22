import { useEffect, useRef, useState } from "react";
import { Phone, Mail, MessageSquare, X } from "lucide-react";
import QRCode from "react-qr-code";
import { FOCUS_RING } from "./helpers";
import { CallHandoffQr } from "./CallHandoffQr";
import { KeepModal } from "../../components/keep/KeepModal";
import { api } from "../../lib/apiClient";
import { formatNaPhone } from "../../components/quick-capture/utils";

interface CustomerContactStripProps {
  requestId: string;
  phone: string | null;
  email: string | null;
  customerName: string;
  pageToken: string | null;
  onContactLaunched: (direction: string, channel: string) => void;
}

export function CustomerContactStrip({
  requestId,
  phone,
  email,
  customerName,
  pageToken,
  onContactLaunched,
}: CustomerContactStripProps) {
  const [callQrOpen, setCallQrOpen] = useState(false);
  const [textQrOpen, setTextQrOpen] = useState(false);

  if (!phone && !email) return null;

  const publicBaseUrl = (import.meta.env.VITE_PUBLIC_BASE_URL as string).replace(/\/$/, "");
  const customerPageUrl = pageToken ? `${publicBaseUrl}/keep/r/${pageToken}` : null;
  const defaultTextMessage = customerPageUrl
    ? `Here is a link to your private request page: ${customerPageUrl}`
    : "";

  // Plain contact shortcut only — the tracker link is shared exclusively through
  // ShareLinkModal's explicit prepare/confirm ceremony (GAP-048).
  const emailHref = email ? `mailto:${email}` : null;

  return (
    <>
      {/* Inline Anchor context item (locked correction, 2026-08-22) — no independent card
          border/padding/background; the Anchor owns the one boundary for the whole strip. */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span className="text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] shrink-0">
          Contact
        </span>
        {phone && (
          <>
            {/* Desktop: QR handoff — no direct tel: on desktop (ADR-443) */}
            <button
              type="button"
              onClick={() => setCallQrOpen(true)}
              className={`hidden md:inline-flex items-center gap-1.5 text-sm font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
            >
              <Phone className="h-3.5 w-3.5 shrink-0" />
              Scan to call
            </button>
            {/* Mobile: direct tel: */}
            <a
              href={`tel:${phone}`}
              onClick={() => onContactLaunched("outbound", "phone")}
              className={`inline-flex md:hidden items-center gap-1.5 text-sm font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
            >
              <Phone className="h-3.5 w-3.5 shrink-0" />
              Call
            </a>
            {/* Desktop: QR handoff — no direct sms: on desktop, matching the call pattern */}
            <button
              type="button"
              onClick={() => setTextQrOpen(true)}
              className={`hidden md:inline-flex items-center gap-1.5 text-sm font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
            >
              <MessageSquare className="h-3.5 w-3.5 shrink-0" />
              Scan to text
            </button>
            {/* Mobile: direct sms: */}
            <a
              href={`sms:${phone}${defaultTextMessage ? `?&body=${encodeURIComponent(defaultTextMessage)}` : ""}`}
              onClick={() => onContactLaunched("outbound", "sms")}
              className={`inline-flex md:hidden items-center gap-1.5 text-sm font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
            >
              <MessageSquare className="h-3.5 w-3.5 shrink-0" />
              Text
            </a>
          </>
        )}
        {email && emailHref && (
          <a
            href={emailHref}
            onClick={() => onContactLaunched("outbound", "email")}
            className={`inline-flex items-center gap-1.5 text-sm font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
          >
            <Mail className="h-3.5 w-3.5 shrink-0" />
            Email
          </a>
        )}
      </div>
      {callQrOpen && phone && (
        <CallQrModal
          requestId={requestId}
          phone={phone}
          customerName={customerName}
          onDone={() => {
            onContactLaunched("outbound", "phone");
            setCallQrOpen(false);
          }}
          onClose={() => setCallQrOpen(false)}
        />
      )}
      {textQrOpen && phone && (
        <TextQrModal
          requestId={requestId}
          phone={phone}
          customerName={customerName}
          defaultMessage={defaultTextMessage}
          onDone={() => {
            onContactLaunched("outbound", "sms");
            setTextQrOpen(false);
          }}
          onClose={() => setTextQrOpen(false)}
        />
      )}
    </>
  );
}

// ---------------------------------------------------------------------------
// QR modal — desktop call handoff
// ---------------------------------------------------------------------------

interface CallQrModalProps {
  requestId: string;
  phone: string;
  customerName: string;
  onDone: () => void;
  onClose: () => void;
}

function CallQrModal({ requestId, phone, customerName, onDone, onClose }: CallQrModalProps) {
  return (
    <KeepModal
      onClose={onClose}
      labelledBy="call-qr-heading"
      overlayClassName="flex items-center justify-center px-4"
      backdropClassName="bg-black/40"
      panelClassName="bg-[var(--ophalo-card)] rounded-xl shadow-xl w-full max-w-xs p-5"
    >
      <div className="flex items-center justify-between mb-1">
        <h2 id="call-qr-heading" className="text-base font-semibold text-[var(--ophalo-ink)]">
          Call {customerName}
        </h2>
        <button
          type="button"
          onClick={onClose}
          className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] p-1 rounded-md transition-colors ${FOCUS_RING}`}
        >
          <X className="h-4 w-4" />
          <span className="sr-only">Close</span>
        </button>
      </div>
      <p className="text-xs text-[var(--ophalo-muted)] mb-4">
        Scan with your phone to call {formatNaPhone(phone)}.
      </p>
      <div className="flex justify-center mb-4">
        <CallHandoffQr requestId={requestId} size={160} />
      </div>
      <button
        type="button"
        onClick={onDone}
        className={`w-full rounded-lg border border-[var(--ophalo-border)] px-4 py-2 text-sm font-semibold text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] transition-colors ${FOCUS_RING}`}
      >
        Done — record this call
      </button>
    </KeepModal>
  );
}

// ---------------------------------------------------------------------------
// QR modal — desktop text handoff
// ---------------------------------------------------------------------------

interface TextQrModalProps {
  requestId: string;
  phone: string;
  customerName: string;
  defaultMessage: string;
  onDone: () => void;
  onClose: () => void;
}

function TextQrModal({ requestId, phone, customerName, defaultMessage, onDone, onClose }: TextQrModalProps) {
  const [messageBody, setMessageBody] = useState(defaultMessage);
  const [handoffUrl, setHandoffUrl] = useState<string | null>(null);
  const [isStale, setIsStale] = useState(false);
  const [isMinting, setIsMinting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Bumped on every edit. A mint captures this at call time; if it no longer matches when the
  // response arrives, the draft changed mid-flight (initial mint racing an edit, or overlapping
  // remints resolving out of order) and the result must be discarded rather than shown as current.
  const messageVersionRef = useRef(0);

  async function mint(body: string) {
    const requestedVersion = messageVersionRef.current;
    setIsMinting(true);
    setError(null);
    try {
      const result = await api.createSmsHandoff(requestId, body);
      if (messageVersionRef.current === requestedVersion) {
        setHandoffUrl(result.handoffUrl);
        setIsStale(false);
      }
    } catch {
      if (messageVersionRef.current === requestedVersion) {
        setError("Could not create text link. Try again.");
      }
    } finally {
      setIsMinting(false);
    }
  }

  useEffect(() => {
    void mint(defaultMessage);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <KeepModal
      onClose={onClose}
      labelledBy="text-qr-heading"
      overlayClassName="flex items-center justify-center px-4"
      backdropClassName="bg-black/40"
      panelClassName="bg-[var(--ophalo-card)] rounded-xl shadow-xl w-full max-w-xs p-5"
    >
      <div className="flex items-center justify-between mb-1">
        <h2 id="text-qr-heading" className="text-base font-semibold text-[var(--ophalo-ink)]">
          Text {customerName}
        </h2>
        <button
          type="button"
          onClick={onClose}
          className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] p-1 rounded-md transition-colors ${FOCUS_RING}`}
        >
          <X className="h-4 w-4" />
          <span className="sr-only">Close</span>
        </button>
      </div>
      <p className="text-xs text-[var(--ophalo-muted)] mb-2">
        Scan with your phone to open a text draft to {formatNaPhone(phone)}.
      </p>
      <textarea
        value={messageBody}
        onChange={(e) => {
          messageVersionRef.current += 1;
          setMessageBody(e.target.value);
          setIsStale(true);
        }}
        rows={3}
        className={`w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-sm text-[var(--ophalo-ink)] mb-3 ${FOCUS_RING}`}
      />
      {isStale && (
        <button
          type="button"
          onClick={() => void mint(messageBody)}
          disabled={isMinting}
          className={`w-full mb-3 rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-xs font-semibold text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] transition-colors ${FOCUS_RING}`}
        >
          {isMinting ? "Updating…" : "Update QR for this message"}
        </button>
      )}
      <div className="flex justify-center mb-4">
        {isMinting && !handoffUrl ? (
          <div
            className="flex items-center justify-center"
            style={{ height: 160, width: 160 }}
            role="status"
            aria-label="Preparing text link"
          />
        ) : error ? (
          <p className="text-xs text-[var(--ophalo-danger)]">{error}</p>
        ) : handoffUrl && !isStale ? (
          <div className="bg-white p-2 rounded-lg">
            <QRCode value={handoffUrl} size={160} />
          </div>
        ) : (
          <p className="text-xs text-[var(--ophalo-muted)]">Edit above, then update the QR.</p>
        )}
      </div>
      <button
        type="button"
        onClick={onDone}
        className={`w-full rounded-lg border border-[var(--ophalo-border)] px-4 py-2 text-sm font-semibold text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] transition-colors ${FOCUS_RING}`}
      >
        Done — record this text
      </button>
    </KeepModal>
  );
}
