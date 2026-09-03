import { useState } from "react";
import {
  BadgeDollarSign,
  ChevronDown,
  ChevronUp,
  CircleDollarSign,
  Mail,
  MessageSquare,
  Phone,
  PhoneCall,
  Share2,
} from "lucide-react";
import { type RequestDetailLayoutProps } from "./DetailPanels";
import { DetailHeroBadges, DetailHeroName } from "./DetailHero";
import { PrimaryActionSlot } from "./PrimaryActionControl";
import { FOCUS_RING } from "./helpers";
import { useCopyFeedback } from "../../hooks/useCopyFeedback";

interface RequestDetailAnchorProps extends RequestDetailLayoutProps {
  canRecordShareIntent: boolean;
  needsShare: boolean;
  onOpenShareDrawer: () => void;
  onOpenClearAttention: () => void;
  onActivateCustomerUpdateComposer: () => void;
  actualWorkShortcut?: { label: string; onClick: () => void };
  financialReviewShortcut?: { label: string; onClick: () => void };
  businessPageUrl?: string | null;
}

const utilityLinkClass = `inline-flex min-h-9 items-center gap-1.5 rounded-md px-2 text-xs font-semibold text-[var(--keep-accent)] hover:bg-[var(--keep-accent-bg)] ${FOCUS_RING}`;

export function RequestDetailAnchor({
  requestId,
  detail,
  onDetailUpdated,
  onContactLaunched,
  onRecordFollowUp,
  onOpenClearAttention,
  onActivateCustomerUpdateComposer,
  actualWorkShortcut,
  financialReviewShortcut,
  canRecordShareIntent,
  needsShare,
  onOpenShareDrawer,
  businessPageUrl,
}: RequestDetailAnchorProps) {
  const [needExpanded, setNeedExpanded] = useState(false);
  const hasActiveAttention = detail.effectiveAttention.level !== "none";
  const defaultContactChannel = detail.customerPhone ? "phone" : detail.customerEmail ? "email" : "other";
  const { copiedId, failedId, copy } = useCopyFeedback();

  async function shareBusinessPage() {
    if (!businessPageUrl) return;
    if (typeof navigator.share === "function") {
      try {
        await navigator.share({ title: detail.businessName, url: businessPageUrl });
        return;
      } catch (error) {
        if (typeof DOMException !== "undefined" && error instanceof DOMException && error.name === "AbortError") return;
      }
    }
    await copy(businessPageUrl, "business-page");
  }

  return (
    <div data-request-detail-anchor className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 shadow-sm md:px-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <DetailHeroBadges detail={detail} />
          <div className="mt-1.5">
            <DetailHeroName detail={detail} />
          </div>
        </div>
        {!hasActiveAttention && (
          <div className="shrink-0">
            <PrimaryActionSlot
              requestId={requestId}
              detail={detail}
              onDetailUpdated={onDetailUpdated}
              onOpenClearAttention={onOpenClearAttention}
              onRecordFollowUp={onRecordFollowUp}
              onContactLaunched={onContactLaunched}
              onActivateCustomerUpdateComposer={onActivateCustomerUpdateComposer}
            />
          </div>
        )}
      </div>

      {detail.description && (
        <div className="mt-2 rounded-lg bg-[var(--keep-request-surface-muted)] px-3 py-2">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <p className="text-[10px] font-bold uppercase tracking-[0.08em] text-[var(--keep-request-eyebrow)]">Customer need</p>
              <p className={`mt-0.5 whitespace-pre-wrap text-sm font-semibold leading-5 text-[var(--ophalo-ink)] ${needExpanded ? "" : "line-clamp-2"}`}>
                {detail.description}
              </p>
            </div>
            {detail.description.length > 150 && (
              <button
                type="button"
                aria-expanded={needExpanded}
                onClick={() => setNeedExpanded((expanded) => !expanded)}
                className={`mt-3 inline-flex shrink-0 items-center gap-1 rounded px-1.5 py-1 text-xs font-semibold text-[var(--keep-accent)] hover:bg-[var(--keep-accent-bg)] ${FOCUS_RING}`}
              >
                {needExpanded ? <ChevronUp className="h-3.5 w-3.5" aria-hidden="true" /> : <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />}
                {needExpanded ? "Collapse" : "Full need"}
              </button>
            )}
          </div>
        </div>
      )}

      <div aria-label="Frequent request actions" className="mt-2 flex max-h-[88px] flex-wrap items-center gap-1.5 overflow-hidden border-t border-[var(--ophalo-border)] pt-2">
        {detail.availableActions.canLogExternalContact && (
          <button
            type="button"
            onClick={() => onContactLaunched("outbound", defaultContactChannel)}
            className={`inline-flex min-h-9 items-center gap-1.5 rounded-lg bg-[var(--keep-request-primary)] px-3 text-xs font-semibold text-white shadow-sm hover:bg-[var(--keep-request-primary-hover)] ${FOCUS_RING}`}
          >
            <PhoneCall className="h-4 w-4" aria-hidden="true" />
            Contact customer
          </button>
        )}

        {(detail.customerPhone || detail.customerEmail) && (
          <div className="inline-flex min-h-9 overflow-hidden rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)]" role="group" aria-label="Contact channels">
            {detail.customerPhone && (
              <>
                <button type="button" onClick={() => onContactLaunched("outbound", "phone")} className={`${utilityLinkClass} rounded-none`}>
                  <Phone className="h-3.5 w-3.5" aria-hidden="true" /> Call
                </button>
                <button type="button" onClick={() => onContactLaunched("outbound", "sms")} className={`${utilityLinkClass} rounded-none border-l border-[var(--ophalo-border)]`}>
                  <MessageSquare className="h-3.5 w-3.5" aria-hidden="true" /> Text
                </button>
              </>
            )}
            {detail.customerEmail && (
              <button type="button" onClick={() => onContactLaunched("outbound", "email")} className={`${utilityLinkClass} rounded-none ${detail.customerPhone ? "border-l border-[var(--ophalo-border)]" : ""}`}>
                <Mail className="h-3.5 w-3.5" aria-hidden="true" /> Email
              </button>
            )}
          </div>
        )}

        {(businessPageUrl || canRecordShareIntent) && (
          <div className="inline-flex min-h-9 overflow-hidden rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)]" role="group" aria-label="Share pages">
            {businessPageUrl && (
              <button type="button" onClick={() => void shareBusinessPage()} className={`${utilityLinkClass} rounded-none`}>
                <Share2 className="h-3.5 w-3.5" aria-hidden="true" />
                {copiedId === "business-page" ? "Business page copied" : failedId === "business-page" ? "Copy failed" : "Business page"}
              </button>
            )}
            {canRecordShareIntent && (
              <button type="button" onClick={onOpenShareDrawer} className={`${utilityLinkClass} rounded-none ${businessPageUrl ? "border-l border-[var(--ophalo-border)]" : ""}`}>
                <Share2 className="h-3.5 w-3.5" aria-hidden="true" />
                Customer request page
                {needsShare && <span className="h-1.5 w-1.5 rounded-full bg-[var(--ophalo-attention)]" aria-label="Not shared" />}
              </button>
            )}
          </div>
        )}

        {actualWorkShortcut && (
          <button type="button" onClick={actualWorkShortcut.onClick} className={`inline-flex min-h-9 items-center gap-1.5 rounded-lg border border-[var(--keep-request-primary)] px-2.5 text-xs font-semibold text-[var(--keep-request-primary)] hover:bg-[var(--keep-accent-bg)] ${FOCUS_RING}`}>
            <BadgeDollarSign className="h-4 w-4" aria-hidden="true" />
            {actualWorkShortcut.label}
          </button>
        )}
        {financialReviewShortcut && (
          <button type="button" onClick={financialReviewShortcut.onClick} className={`inline-flex min-h-9 items-center gap-1.5 rounded-lg px-2.5 text-xs font-semibold text-[var(--keep-request-financial)] hover:bg-slate-100 ${FOCUS_RING}`}>
            <CircleDollarSign className="h-4 w-4" aria-hidden="true" />
            {financialReviewShortcut.label}
          </button>
        )}
      </div>
    </div>
  );
}
