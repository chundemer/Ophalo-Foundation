import { type RequestDetailLayoutProps } from "./DetailPanels";
import { DetailHero, CustomerPageHeroActions } from "./DetailHero";
import { CustomerContactStrip } from "./CustomerContactStrip";
import { ServiceLocationPanel } from "./DetailPanels";
import { TeamSection } from "./TeamSection";
import { WorkDoneCard, CloseRequestCard } from "./BusinessSection";
import { FOCUS_RING } from "./helpers";

// Sticky Request Anchor (locked correction, 2026-08-22): one outer card with a real two-row
// grid, not a stack of card-shaped children.
//   Row 1: reference/status/attention/name (left, DetailHero) | one compact primary action (right).
//   Row 2: contact, service location, owner, Log contact, and share — all inline context items
//          with no independent border/padding/background of their own.
// Target footprint: a compact ~160px header, not a tall mostly-empty one.
//
// Primary-action slot: only two already-authorized, mutually-exclusive-by-status derivations are
// shown here — ADR-434 "Mark work done" (WorkDoneCard) and the locked spec's own Close-request-
// as-Anchor-primary rule for canClose (CloseRequestCard) — both rendered via their `compact` prop
// so at most one small, normal-height button appears, never full card chrome. Both self-guard on
// their own eligibility, so at most one is ever visible (never both at once). No other
// client-side action-choice/recommendation logic — the backend does not yet expose a
// server-authored PrimaryAction field (API preflight gap 5), so every other case renders no
// primary rather than inventing one.
interface RequestDetailAnchorProps extends RequestDetailLayoutProps {
  canRecordShareIntent: boolean;
  needsShare: boolean;
  onOpenShareDrawer: () => void;
}

export function RequestDetailAnchor({
  requestId,
  detail,
  onDetailUpdated,
  onContactLaunched,
  onEditLocation,
  canRecordShareIntent,
  needsShare,
  onOpenShareDrawer,
}: RequestDetailAnchorProps) {
  return (
    <div className="shrink-0 border-b border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-4 md:px-6 py-2.5">
      {/* Row 1 */}
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0 flex-1">
          <DetailHero detail={detail} />
        </div>
        <div className="shrink-0">
          <WorkDoneCard requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} compact />
          <CloseRequestCard requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} compact />
        </div>
      </div>
      {/* Row 2 — inline context items, no independent card chrome */}
      <div className="mt-1.5 flex flex-wrap items-center gap-x-5 gap-y-1.5">
        <CustomerContactStrip
          requestId={requestId}
          phone={detail.customerPhone ?? null}
          email={detail.customerEmail ?? null}
          customerName={detail.customerName}
          pageToken={detail.pageToken ?? null}
          onContactLaunched={onContactLaunched}
        />
        <ServiceLocationPanel detail={detail} onEditLocation={onEditLocation} />
        <TeamSection requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} compact />
        {detail.availableActions.canLogExternalContact && (
          <button
            type="button"
            onClick={() =>
              onContactLaunched(
                "outbound",
                detail.customerPhone ? "phone" : detail.customerEmail ? "email" : "other",
              )
            }
            className={`text-xs font-semibold text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING} rounded`}
          >
            Log contact
          </button>
        )}
        {detail.pageToken && (
          <CustomerPageHeroActions
            pageToken={detail.pageToken}
            canRecordShareIntent={canRecordShareIntent}
            needsShare={needsShare}
            onOpenShareDrawer={onOpenShareDrawer}
          />
        )}
      </div>
    </div>
  );
}
