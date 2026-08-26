import { type RequestDetailLayoutProps } from "./DetailPanels";
import { DetailHeroBadges, DetailHeroName } from "./DetailHero";
import { PrimaryActionSlot } from "./PrimaryActionControl";

// Mobile Request Anchor and action rail (Slice 2, 2026-08-26; locked
// `ux-design/v2/pwa-mobile-workflow-spec.md` §4.1/§4.2). Reuses `RequestDetailAnchor.tsx`'s
// design tokens and `KeepButton` variants directly rather than introducing mobile-only styling —
// only the layout is compact. Scope is deliberately narrow: identity/status/attention only. The
// contact/service-location strip and lower canvas order are Slice 3's job, not this one.
//
// Exclusivity invariant (same rule as desktop's `RequestDetailAnchor`/`HeroAttentionBanner`
// split): exactly one of `MobileActionRail` (no active attention) or `HeroAttentionBanner`
// (active attention, rendered in the scroll canvas) mounts `PrimaryActionSlot` for a given
// request — never both.

interface MobileRequestAnchorProps extends Pick<RequestDetailLayoutProps, "detail"> {}

export function MobileRequestAnchor({ detail }: MobileRequestAnchorProps) {
  return (
    <div className="shrink-0 sticky top-0 z-10 border-b border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-4 pb-2 pt-[max(0.5rem,env(safe-area-inset-top))]">
      <DetailHeroBadges detail={detail} />
      <div className="mt-1">
        <DetailHeroName detail={detail} />
      </div>
    </div>
  );
}

interface MobileActionRailProps extends RequestDetailLayoutProps {
  onOpenClearAttention: () => void;
  onActivateCustomerUpdateComposer: () => void;
  hidden: boolean;
}

export function MobileActionRail({
  requestId,
  detail,
  onDetailUpdated,
  onContactLaunched,
  onRecordFollowUp,
  onOpenClearAttention,
  onActivateCustomerUpdateComposer,
  hidden,
}: MobileActionRailProps) {
  const hasActiveAttention = detail.effectiveAttention.level !== "none";
  if (hasActiveAttention) return null;

  const action = detail.availableActions.primaryAction;
  if (!action) return null;

  return (
    <div
      className={`shrink-0 sticky bottom-0 z-10 border-t border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 pt-2 pb-[max(0.5rem,env(safe-area-inset-bottom))] shadow-[0_-1px_4px_rgba(0,0,0,0.04)] transition-transform ${
        hidden ? "translate-y-full" : "translate-y-0"
      }`}
      aria-hidden={hidden}
      inert={hidden}
    >
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
  );
}
