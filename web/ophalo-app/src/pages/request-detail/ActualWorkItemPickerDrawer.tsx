import type { ReactNode, RefObject } from "react";
import { ResponsiveSheet } from "../../components/keep/ResponsiveSheet";
import { KeepButton } from "../../components/keep/KeepButton";

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
        <h2
          id="actual-work-item-picker-heading"
          className="text-base font-semibold text-[var(--ophalo-ink)]"
        >
          Add work &amp; materials
        </h2>
      }
      footer={
        <div className="flex justify-end">
          <KeepButton type="button" variant="teal" onClick={onClose}>
            Done
          </KeepButton>
        </div>
      }
    >
      {connectionFailureBanner}
      {children}
    </ResponsiveSheet>
  );
}
