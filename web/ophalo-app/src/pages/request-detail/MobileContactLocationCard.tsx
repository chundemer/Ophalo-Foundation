import { Phone, MessageSquare, MapPin } from "lucide-react";
import { type RequestDetailLayoutProps, LogContactCard, ServiceLocationPanel } from "./DetailPanels";
import { FOCUS_RING } from "./helpers";
import { normalizeNaPhoneInput } from "../../components/quick-capture/utils";

// Mobile-only canvas section (Slice 3, 2026-08-26; locked `pwa-mobile-workflow-spec.md`,
// field-operations decision 2026-08-26). Desktop keeps contact/location solely inside
// `RequestDetailAnchor`/`CustomerContactStrip` — unchanged by this component.
//
// Call/Text are ordinary native `tel:`/`sms:` anchors with no click handler: the browser handles
// the handoff to the phone/messages app directly, and returning to the PWA resumes the existing
// document (unsaved form state and scroll position are preserved because the canvas is never
// unmounted or reloaded). Maps is a normal external anchor (`target="_blank"`, `rel="noopener
// noreferrer"`) for the same reason. None of these create an activity/audit record on their own —
// `LogContactCard` below is the one explicit, separate audit path (gated on
// `canLogExternalContact`, unchanged from its existing modal-opening behavior).

interface MobileContactLocationCardProps extends Pick<RequestDetailLayoutProps, "detail" | "onContactLaunched" | "onEditLocation"> {}

export function MobileContactLocationCard({ detail, onContactLaunched, onEditLocation }: MobileContactLocationCardProps) {
  // normalizeNaPhoneInput strips a leading "1" (bare or from a +1 E.164 value) before slicing to
  // 10 digits — stripToDigits().slice(0, 10) alone would keep that leading 1 as a digit and
  // misdial (e.g. "+1 (555) 555-0102" -> "1555555010" -> "tel:+11555555010").
  const phoneDigits = detail.customerPhone ? normalizeNaPhoneInput(detail.customerPhone) : "";
  const hasPhone = phoneDigits.length === 10;
  const telHref = hasPhone ? `tel:+1${phoneDigits}` : null;
  const smsHref = hasPhone ? `sms:+1${phoneDigits}` : null;

  // Built from independently-present parts and gated on a non-empty result — `hasAddress` alone
  // (line1 or city) can be true while this composed query is empty, e.g. city present without
  // state/line1.
  const mapsQueryParts = [
    detail.serviceAddressLine1,
    detail.serviceAddressLine2,
    detail.serviceCity,
    detail.serviceState,
    detail.serviceZip,
  ].filter((part): part is string => !!part);
  const mapsHref = mapsQueryParts.length > 0
    ? `https://maps.google.com/?q=${encodeURIComponent(mapsQueryParts.join(", "))}`
    : null;

  return (
    <div className="space-y-3">
      {(hasPhone || mapsHref) && (
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 flex flex-wrap items-center gap-x-4 gap-y-2">
          {hasPhone && (
            <a
              href={telHref!}
              className={`inline-flex items-center gap-1.5 text-sm font-semibold text-[var(--keep-accent)] ${FOCUS_RING} rounded`}
            >
              <Phone className="h-4 w-4 shrink-0" />
              Call
            </a>
          )}
          {hasPhone && (
            <a
              href={smsHref!}
              className={`inline-flex items-center gap-1.5 text-sm font-semibold text-[var(--keep-accent)] ${FOCUS_RING} rounded`}
            >
              <MessageSquare className="h-4 w-4 shrink-0" />
              Text
            </a>
          )}
          {mapsHref && (
            <a
              href={mapsHref}
              target="_blank"
              rel="noopener noreferrer"
              className={`inline-flex items-center gap-1.5 text-sm font-semibold text-[var(--keep-accent)] ${FOCUS_RING} rounded`}
            >
              <MapPin className="h-4 w-4 shrink-0" />
              Maps
            </a>
          )}
        </div>
      )}
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3">
        <ServiceLocationPanel detail={detail} onEditLocation={onEditLocation} />
      </div>
      <LogContactCard detail={detail} onContactLaunched={onContactLaunched} />
    </div>
  );
}
