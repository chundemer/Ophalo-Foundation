import { type RequestDetailLayoutProps } from "./DetailPanels";
import { DetailHeroBadges, DetailHeroName } from "./DetailHero";
import { CustomerContactStrip } from "./CustomerContactStrip";
import { ServiceLocationPanel, TriagePanel } from "./DetailPanels";
import { TeamSection } from "./TeamSection";
import { TimingPanel } from "./TimingPanel";
import { PrimaryActionSlot } from "./PrimaryActionControl";

// Sticky Request Anchor (three-row correction, 2026-08-22): one outer bordered/rounded card with
// a deliberate three-row desktop hierarchy — not a single flattened horizontal strip and not a
// stack of independently card-shaped children.
//   Row 1: reference/status/attention (left, DetailHeroBadges) | the no-attention lifecycle
//          primary action only (right).
//   Row 2: customer identity, full width (DetailHeroName).
//   Divider, then Row 3: three stable context columns — customer contact, service location, and
//          owner/share utilities — each chrome-free inline content inside the one outer card.
//
// Inner content width (RD-058B-2): the card is wrapped to `max-w-4xl mx-auto` so its content
// shares one horizontal reading boundary with the Work Canvas, inside the outer page gutter.
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
  canRecordShareIntent,
  needsShare,
  onOpenShareDrawer,
}: RequestDetailAnchorProps) {
  const hasActiveAttention = detail.effectiveAttention.level !== "none";

  return (
    <div className="shrink-0 bg-[var(--ophalo-canvas)] px-4 md:px-6 py-3">
      {/* Inner content aligns to the Work Canvas reading frame (RD-058B-2): one shared
          `max-w-4xl mx-auto` horizontal boundary, inside the outer gutter. */}
      <div className="mx-auto w-full max-w-4xl rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-sm px-4 py-3 md:px-5 md:py-4">
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
      </div>
    </div>
  );
}
