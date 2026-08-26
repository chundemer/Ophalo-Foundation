import { type RequestDetailLayoutProps } from "./DetailPanels";
import { DetailHeroBadges, DetailHeroName } from "./DetailHero";
import { CustomerContactStrip } from "./CustomerContactStrip";
import { ServiceLocationPanel, TriagePanel } from "./DetailPanels";
import { TeamSection } from "./TeamSection";
import { TimingPanel } from "./TimingPanel";
import { KeepButton } from "../../components/keep/KeepButton";
import { PrimaryActionSlot, MarkWorkDoneSecondarySlot } from "./PrimaryActionControl";

// Sticky Request Anchor (three-row correction, 2026-08-22): one outer bordered/rounded card with
// a deliberate three-row desktop hierarchy — not a single flattened horizontal strip and not a
// stack of independently card-shaped children.
//   Row 1: reference/status/attention (left, DetailHeroBadges) | Log contact + the
//          no-attention lifecycle primary action, plus its authorized secondary Mark work done
//          control (right).
//   Row 2: customer identity, full width (DetailHeroName).
//   Divider, then Row 3: three stable context columns — customer contact, service location, and
//          owner/share utilities — each chrome-free inline content inside the one outer card.
//
// Primary-action slot (Session 0A, 2026-08-25; attention/no-attention split 2026-08-25): the
// Anchor only mounts the shared `PrimaryActionSlot` while `effectiveAttention.level === "none"` —
// while attention is active, `HeroAttentionBanner` is the sole renderer of
// `detail.availableActions.primaryAction`, beside the attention reason it names. The Anchor stays
// utility-focused during active attention: the always-on secondary Contact customer button and
// the demoted `MarkWorkDoneSecondarySlot` remain, but no primary/lifecycle action competes with
// the rail. See `PrimaryActionControl.tsx` for the shared, exhaustive target-vocabulary renderer.
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
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-sm px-4 py-3 md:px-5 md:py-4">
        {/* Row 1: reference/status/attention (left) | Log contact + primary action (right) */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <DetailHeroBadges detail={detail} />
          <div className="flex shrink-0 items-center gap-2">
            {detail.availableActions.canLogExternalContact && (
              <KeepButton
                type="button"
                variant="secondary"
                onClick={() =>
                  onContactLaunched(
                    "outbound",
                    detail.customerPhone ? "phone" : detail.customerEmail ? "email" : "other",
                  )
                }
              >
                Contact customer
              </KeepButton>
            )}
            <MarkWorkDoneSecondarySlot requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
            {!hasActiveAttention && (
              <PrimaryActionSlot
                requestId={requestId}
                detail={detail}
                onDetailUpdated={onDetailUpdated}
                onOpenClearAttention={onOpenClearAttention}
                onRecordFollowUp={onRecordFollowUp}
                onContactLaunched={onContactLaunched}
                onActivateCustomerUpdateComposer={onActivateCustomerUpdateComposer}
              />
            )}
          </div>
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
