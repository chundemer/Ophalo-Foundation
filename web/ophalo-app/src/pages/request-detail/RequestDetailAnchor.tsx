import { type RequestDetailLayoutProps } from "./DetailPanels";
import { DetailHeroBadges, DetailHeroName } from "./DetailHero";
import { CustomerContactStrip } from "./CustomerContactStrip";
import { OriginalRequestCard, ServiceLocationPanel, TriagePanel } from "./DetailPanels";
import { TeamSection } from "./TeamSection";
import { TimingPanel } from "./TimingPanel";
import { PrimaryActionSlot } from "./PrimaryActionControl";
import { ClipboardPenLine, MessageSquare, ReceiptText } from "lucide-react";

// Sticky Request Anchor (three-row correction, 2026-08-22): one outer bordered/rounded card with
// a deliberate three-row desktop hierarchy — not a single flattened horizontal strip and not a
// stack of independently card-shaped children.
//   Row 1: reference/status/attention (left, DetailHeroBadges) | the no-attention lifecycle
//          primary action only (right).
//   Row 2: customer identity, full width (DetailHeroName).
//   Divider, then Row 3: three stable context columns — customer contact, service location, and
//          owner/share utilities — each chrome-free inline content inside the one outer card.
//
// Inner content width (GAP-067 completion): the card shares the left-anchored 1000px Work Canvas
// boundary, using the available wide workspace instead of centering a narrow column.
//
// Primary-action slot (Session 0A, 2026-08-25; attention/no-attention split 2026-08-25): the
// Anchor only mounts the shared `PrimaryActionSlot` while `effectiveAttention.level === "none"` —
// while attention is active, `HeroAttentionBanner` is the sole renderer of
// `detail.availableActions.primaryAction`, beside the attention reason it names. During active
// attention the Anchor carries no action at all (RD-058B-2): the demoted "Mark work done" moved
// to the Work Canvas after Actual Work, and channel contact stays in Customer Contact. See
// `PrimaryActionControl.tsx` for the shared, exhaustive target-vocabulary renderer.
interface RequestDetailAnchorProps extends RequestDetailLayoutProps {
  canRecordShareIntent: boolean;
  needsShare: boolean;
  onOpenShareDrawer: () => void;
  onOpenClearAttention: () => void;
  onActivateCustomerUpdateComposer: () => void;
  actualWorkShortcut?: { label: string; onClick: () => void };
  financialReviewShortcut?: { label: string; onClick: () => void };
}

export function RequestDetailAnchor({
  requestId,
  detail,
  onDetailUpdated,
  onContactLaunched,
  onEditLocation,
  onOpenReassignOwner,
  onOpenWatchers,
  onRecordFollowUp,
  onOpenClearAttention,
  onActivateCustomerUpdateComposer,
  actualWorkShortcut,
  financialReviewShortcut,
  canRecordShareIntent,
  needsShare,
  onOpenShareDrawer,
}: RequestDetailAnchorProps) {
  const hasActiveAttention = detail.effectiveAttention.level !== "none";
  const customerResponseOwnsPrimary =
    hasActiveAttention && detail.availableActions.primaryAction?.target === "customer_update_composer";
  const showMessageShortcut = detail.availableActions.canSendBusinessUpdate && !customerResponseOwnsPrimary;

  return (
    <div className="shrink-0 bg-[var(--keep-request-canvas)] px-4 md:px-6 py-3">
      <div className="w-full max-w-[1000px] rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-sm px-4 py-3 md:px-5 md:py-4">
        {/* Row 1: reference/status/attention (left) | no-attention lifecycle primary action
            (right). Contact stays in Customer Contact / a server-routed contact-sheet primary;
            during active attention the primary lives in HeroAttentionBanner and "Mark work done"
            in the Work Canvas — the Anchor carries no competing action here (RD-058B-2). */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <DetailHeroBadges detail={detail} />
          {!hasActiveAttention && (
            <div className="flex shrink-0 items-center gap-2">
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

        {/* Row 2: customer identity, full width */}
        <div className="mt-2">
          <DetailHeroName detail={detail} />
        </div>

        {/* Request shortcuts are navigation accelerators, not duplicate completion controls. They
            take an operator straight to the existing authoritative work surface; the cards below
            retain their own actions. A customer-message Attention Rail already owns its response
            CTA, so it intentionally suppresses the duplicate message shortcut here. */}
        {(actualWorkShortcut || showMessageShortcut || financialReviewShortcut) && (
          <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-[var(--ophalo-border)] pt-3">
            <p className="mr-1 text-[10px] font-bold uppercase tracking-[0.08em] text-[var(--keep-request-eyebrow)]">Request shortcuts</p>
            {actualWorkShortcut && (
              <button type="button" onClick={actualWorkShortcut.onClick} className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--keep-request-primary)] bg-[var(--keep-request-primary)] px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-[var(--keep-request-primary-hover)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-request-primary)] focus-visible:ring-offset-2">
                <ClipboardPenLine className="h-4 w-4" aria-hidden="true" />
                {actualWorkShortcut.label}
              </button>
            )}
            {showMessageShortcut && (
              <button type="button" onClick={onActivateCustomerUpdateComposer} className="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-semibold text-[var(--ophalo-ink)] shadow-sm hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2">
                <MessageSquare className="h-4 w-4 text-[var(--keep-accent)]" aria-hidden="true" />
                Message customer
              </button>
            )}
            {financialReviewShortcut && (
              <button type="button" onClick={financialReviewShortcut.onClick} className="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-semibold text-[var(--ophalo-ink)] shadow-sm hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2">
                <ReceiptText className="h-4 w-4 text-[var(--ophalo-attention)]" aria-hidden="true" />
                {financialReviewShortcut.label}
              </button>
            )}
          </div>
        )}

        {/* Divider, then Row 3: three stable context columns — chrome-free inline content inside
            the one outer Anchor card. */}
        <div className="mt-3 border-t border-[var(--ophalo-border)] pt-3">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <CustomerContactStrip
              phone={detail.customerPhone ?? null}
              email={detail.customerEmail ?? null}
              contactPreference={detail.contactPreference ?? null}
              onContactLaunched={onContactLaunched}
              canRecordShareIntent={canRecordShareIntent}
              needsShare={needsShare}
              onOpenShareDrawer={onOpenShareDrawer}
            />
            <ServiceLocationPanel detail={detail} onEditLocation={onEditLocation} />
            <div className="flex flex-col gap-1.5">
              <TeamSection requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} compact onOpenReassign={onOpenReassignOwner} onOpenWatchers={onOpenWatchers} />
            </div>
          </div>
        </div>

        {/* Row 4: compact Internal Planning row (locked correction, 2026-08-24) — locked order
            Internal priority -> Planned work date -> Set internal follow-up, each a persistently
            labeled, bordered select-style control (one compact row desktop, stacked narrow).
            Reuses TriagePanel/TimingPanel's `strip` mode: same mutation authority, date-only
            formatting, and conflict/error behavior as the full-card variants. */}
        <div className="mt-3 grid grid-cols-1 gap-4 border-t border-[var(--ophalo-border)] pt-3 sm:grid-cols-3">
          <TriagePanel detail={detail} onDetailUpdated={onDetailUpdated} strip />
          <TimingPanel requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} strip />
        </div>

        {/* GAP-067: Customer Need is part of the request anchor, after the planning row. It stays
            mounted independently of attention and remains a neutral factual inset. */}
        <div className="mt-3">
          <OriginalRequestCard detail={detail} />
        </div>
      </div>
    </div>
  );
}
