import type { ReactNode, RefObject } from "react";
import { X } from "lucide-react";
import { ResponsiveSheet } from "../../components/keep/ResponsiveSheet";
import { KeepButton } from "../../components/keep/KeepButton";
import { FOCUS_RING } from "./helpers";

interface ActualWorkItemPickerDrawerProps {
  /** Done button / Escape. The backdrop is deliberately inert (`backdropClosable={false}`). */
  onClose: () => void;
  /** Points at the hosted search input so the drawer opens with the cursor ready to type. */
  initialFocus: RefObject<HTMLElement | null>;
  /** BL136 4f-v: the composer-level connection-failure banner rides inside the drawer while it is
   *  open — a failed add must be retryable where it happened, not behind an `aria-modal`. Item-level
   *  errors / expansion notices / nudges stay inside `children`. */
  connectionFailureBanner?: ReactNode;
  /** The hosted `ActualWorkSearchAndAdd` element (kept private to `ActualWorkComposer`). */
  children: ReactNode;
}

/**
 * BL136 4f-v: desktop right-side item-picker drawer for the inline (workspace-route) Actual Work
 * composer. Replaces the inline search-results dropdown — it stays open across multiple adds and
 * overlays the workspace rather than squeezing it into two panes. Presentation-only: it wraps
 * `ResponsiveSheet` (→ `KeepModal`: `aria-modal`, focus trap, Escape, focus restoration) and adds
 * the heading / Done footer. The Request Detail modal composer path does not route through here.
 */
export function ActualWorkItemPickerDrawer({
  onClose,
  initialFocus,
  connectionFailureBanner,
  children,
}: ActualWorkItemPickerDrawerProps) {
  return (
    <ResponsiveSheet
      onClose={onClose}
      labelledBy="actual-work-item-picker-heading"
      backdropClosable={false}
      initialFocus={initialFocus}
      header={
        <div className="flex items-center justify-between gap-2">
          <h2
            id="actual-work-item-picker-heading"
            className="text-base font-semibold text-[var(--ophalo-ink)]"
          >
            Add work &amp; materials
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className={`shrink-0 rounded-lg px-2 py-1 text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
          >
            <X className="h-5 w-5" />
          </button>
        </div>
      }
      footer={
        // Bleed past the sheet's own footer padding to repaint a heavier top divider + upward
        // shadow, lifting the footer clear of the scrolling item list (BL136 4f-vi).
        <div className="-mx-4 -mt-4 flex justify-end border-t border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 pt-4 shadow-[0_-4px_12px_rgba(0,0,0,0.08)] md:-mx-6 md:px-6">
          <KeepButton type="button" variant="teal" onClick={onClose}>
            Done adding
          </KeepButton>
        </div>
      }
    >
      {connectionFailureBanner}
      {children}
    </ResponsiveSheet>
  );
}
