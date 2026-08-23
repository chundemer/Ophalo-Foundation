import type { ReactNode, RefObject } from "react";
import { KeepModal } from "./KeepModal";

type ResponsiveSheetNaming =
  /** Prefer this: point at the sheet's own heading element (usually rendered in `header`). */
  | { labelledBy: string; label?: never }
  /** Fallback for sheets without a visible heading node to point at. */
  | { label: string; labelledBy?: never };

type ResponsiveSheetProps = ResponsiveSheetNaming & {
  onClose: () => void;
  /** Defaults to true, matching KeepModal. */
  backdropClosable?: boolean;
  initialFocus?: RefObject<HTMLElement | null>;
  /** Shrink-wrapped top region (title, close control). Not scrollable. */
  header?: ReactNode;
  /** Shrink-wrapped bottom region (primary/secondary actions). Not scrollable. */
  footer?: ReactNode;
  /** The single scrollable region between header and footer. */
  children: ReactNode;
};

/**
 * Presentation-only responsive sheet: desktop right slide-over, mobile/tablet bottom sheet.
 * Wraps KeepModal for dialog semantics (focus trap, Escape, backdrop, focus restoration) and adds
 * only layout — positioning, sizing, and the header/body/footer scroll structure. Draft state and
 * dirty-close confirmation belong to each workflow that uses this, not to the primitive: those
 * rules differ materially across contact, follow-up, attention, and location (docs/session-log.md
 * step 3).
 *
 * `label` or `labelledBy` is required — an unnamed dialog is not acceptable; prefer `labelledBy`
 * pointing at the heading rendered in `header`.
 */
export function ResponsiveSheet({
  onClose,
  backdropClosable = true,
  label,
  labelledBy,
  initialFocus,
  header,
  footer,
  children,
}: ResponsiveSheetProps) {
  return (
    <KeepModal
      onClose={onClose}
      backdropClosable={backdropClosable}
      label={label}
      labelledBy={labelledBy}
      initialFocus={initialFocus}
      backdropClassName="bg-black/30"
      panelClassName={
        "fixed z-50 inset-x-0 bottom-0 w-full max-h-[85dvh] rounded-t-2xl " +
        "md:inset-x-auto md:left-auto md:bottom-0 md:top-0 md:right-0 md:h-[100dvh] md:max-h-[100dvh] md:w-[480px] md:rounded-none " +
        "bg-[var(--ophalo-card)] shadow-xl flex flex-col"
      }
    >
      {header && (
        <div className="shrink-0 px-4 md:px-6 py-4 border-b border-[var(--ophalo-border)]">
          {header}
        </div>
      )}
      <div className="flex-1 min-h-0 overflow-y-auto px-4 md:px-6 py-4">{children}</div>
      {footer && (
        <div className="shrink-0 px-4 md:px-6 py-4 border-t border-[var(--ophalo-border)]">
          {footer}
        </div>
      )}
    </KeepModal>
  );
}
