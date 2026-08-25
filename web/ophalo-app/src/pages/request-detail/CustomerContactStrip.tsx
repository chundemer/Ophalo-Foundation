import { Phone, Mail, MessageSquare } from "lucide-react";
import { FOCUS_RING } from "./helpers";
import { formatNaPhone } from "../../components/quick-capture/utils";
import { KeepBadge } from "../../components/keep/KeepBadge";
import { contactPreferenceLabel } from "./DetailPanels";
import { ShareLinkAction } from "./DetailHero";

interface CustomerContactStripProps {
  phone: string | null;
  email: string | null;
  // Source-agnostic: shown whenever a real preference is set, regardless of request source
  // (locked 2026-08-25). "no_preference" and unset/unknown values render nothing here — Record
  // details' CustomerSignalPanel keeps its own public-intake-gated "No preference" audit context.
  contactPreference: string | null;
  onContactLaunched: (direction: string, channel: string) => void;
  // Share Link placement (locked 2026-08-25): lives with Call/Text/Email, not the Owner column.
  canRecordShareIntent: boolean;
  needsShare: boolean;
  onOpenShareDrawer: () => void;
}

/**
 * Request Anchor contact context. These are shortcuts into the one Contact customer drawer;
 * they never launch a separate QR/modal workflow or create an activity record on their own.
 */
export function CustomerContactStrip({
  phone,
  email,
  contactPreference,
  onContactLaunched,
  canRecordShareIntent,
  needsShare,
  onOpenShareDrawer,
}: CustomerContactStripProps) {
  if (!phone && !email && !canRecordShareIntent) return null;

  const preferenceLabel = contactPreference !== "no_preference" ? contactPreferenceLabel(contactPreference) : null;

  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] shrink-0">
        Customer contact
      </span>
      {phone && <p className="text-sm text-[var(--ophalo-ink)]">{formatNaPhone(phone)}</p>}
      {email && <p className="text-sm text-[var(--ophalo-ink)] truncate">{email}</p>}
      {preferenceLabel && <KeepBadge variant="teal">{preferenceLabel}</KeepBadge>}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        {phone && (
          <>
            <button
              type="button"
              onClick={() => onContactLaunched("outbound", "phone")}
              className={`inline-flex items-center gap-1.5 text-sm font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
            >
              <Phone className="h-3.5 w-3.5 shrink-0" />
              Call
            </button>
            <button
              type="button"
              onClick={() => onContactLaunched("outbound", "sms")}
              className={`inline-flex items-center gap-1.5 text-sm font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
            >
              <MessageSquare className="h-3.5 w-3.5 shrink-0" />
              Text
            </button>
          </>
        )}
        {email && (
          <button
            type="button"
            onClick={() => onContactLaunched("outbound", "email")}
            className={`inline-flex items-center gap-1.5 text-sm font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
          >
            <Mail className="h-3.5 w-3.5 shrink-0" />
            Email
          </button>
        )}
      </div>
      <ShareLinkAction
        canRecordShareIntent={canRecordShareIntent}
        needsShare={needsShare}
        onOpenShareDrawer={onOpenShareDrawer}
      />
    </div>
  );
}
