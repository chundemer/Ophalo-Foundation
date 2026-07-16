import { useState, useRef, useEffect } from "react";
import { Phone, Mail, X } from "lucide-react";
import QRCode from "react-qr-code";
import { FOCUS_RING } from "./helpers";

interface CustomerContactStripProps {
  phone: string | null;
  email: string | null;
  customerName: string;
  pageToken: string | null;
  onContactLaunched: (direction: string, channel: string) => void;
}

export function CustomerContactStrip({
  phone,
  email,
  customerName,
  pageToken,
  onContactLaunched,
}: CustomerContactStripProps) {
  const [callQrOpen, setCallQrOpen] = useState(false);

  if (!phone && !email) return null;

  const publicBaseUrl = (import.meta.env.VITE_PUBLIC_BASE_URL as string).replace(/\/$/, "");
  const customerPageUrl = pageToken ? `${publicBaseUrl}/keep/r/${pageToken}` : null;

  const emailHref = email
    ? (() => {
        const subject = encodeURIComponent("Your request page link");
        const body = customerPageUrl
          ? encodeURIComponent(
              `Here is a link to your private request page:\n\n${customerPageUrl}`
            )
          : "";
        return `mailto:${email}?subject=${subject}${body ? `&body=${body}` : ""}`;
      })()
    : null;

  return (
    <>
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 flex flex-wrap items-center gap-x-4 gap-y-2">
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
          phone={phone}
          customerName={customerName}
          onDone={() => {
            onContactLaunched("outbound", "phone");
            setCallQrOpen(false);
          }}
          onClose={() => setCallQrOpen(false)}
        />
      )}
    </>
  );
}

// ---------------------------------------------------------------------------
// QR modal — desktop call handoff
// ---------------------------------------------------------------------------

interface CallQrModalProps {
  phone: string;
  customerName: string;
  onDone: () => void;
  onClose: () => void;
}

function CallQrModal({ phone, customerName, onDone, onClose }: CallQrModalProps) {
  const dialogRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    dialogRef.current?.focus();
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onClose();
      }
    };
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      onClick={onClose}
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="call-qr-heading"
        tabIndex={-1}
        className="bg-[var(--ophalo-card)] rounded-xl shadow-xl w-full max-w-xs p-5 focus:outline-none"
        onClick={(e) => e.stopPropagation()}
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
          Scan with your phone to call {phone}.
        </p>
        <div className="flex justify-center mb-4 p-3 bg-white rounded-lg">
          <QRCode value={`tel:${phone}`} size={160} />
        </div>
        <button
          type="button"
          onClick={onDone}
          className={`w-full rounded-lg border border-[var(--ophalo-border)] px-4 py-2 text-sm font-semibold text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] transition-colors ${FOCUS_RING}`}
        >
          Done — record this call
        </button>
      </div>
    </div>
  );
}
