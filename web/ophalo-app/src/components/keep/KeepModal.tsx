import { useEffect, useRef, type MouseEvent, type ReactNode } from "react";

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

function getFocusable(panel: HTMLElement): HTMLElement[] {
  return Array.from(panel.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR));
}

function isFocusable(el: Element | null): el is HTMLElement {
  if (!el || !(el instanceof HTMLElement)) return false;
  if (!el.isConnected) return false;
  if ("disabled" in el && (el as unknown as { disabled?: boolean }).disabled) return false;
  return true;
}

interface KeepModalProps {
  onClose: () => void;
  /** Defaults to true — matches both current call sites' existing behavior. GAP-032's
   *  dirty-close confirmation is a separate, later decision and is not added here. */
  backdropClosable?: boolean;
  label?: string;
  labelledBy?: string;
  /** Layout classes for the outer click-to-close container (e.g. flex centering). */
  overlayClassName?: string;
  panelClassName?: string;
  /** Visual classes for the decorative dim layer only (e.g. background color). */
  backdropClassName?: string;
  children: ReactNode;
}

/**
 * Shared modal primitive (GAP-024/GAP-032 scope): dialog semantics, initial focus, a Tab/
 * Shift+Tab focus trap confined to the panel, focus restoration to the pre-open trigger on
 * unmount, and Escape-to-close. Visual chrome (position, size, backdrop) is caller-supplied
 * via className props so different modals can keep their own layout.
 */
export function KeepModal({
  onClose,
  backdropClosable = true,
  label,
  labelledBy,
  overlayClassName = "",
  panelClassName = "",
  backdropClassName = "",
  children,
}: KeepModalProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const previousFocusRef = useRef<Element | null>(null);

  useEffect(() => {
    previousFocusRef.current = document.activeElement;
    const panel = panelRef.current;
    const focusable = panel ? getFocusable(panel) : [];
    (focusable[0] ?? panel)?.focus();

    return () => {
      const prior = previousFocusRef.current;
      if (isFocusable(prior)) prior.focus();
    };
  }, []);

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.preventDefault();
        onClose();
        return;
      }
      if (e.key !== "Tab") return;
      const panel = panelRef.current;
      if (!panel) return;
      const focusable = getFocusable(panel);
      if (focusable.length === 0) {
        // Nothing to trap Tab between — keep focus pinned to the panel itself.
        e.preventDefault();
        panel.focus();
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      } else if (!panel.contains(document.activeElement)) {
        e.preventDefault();
        first.focus();
      }
    }
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  function handleBackdropClick() {
    if (backdropClosable) onClose();
  }

  function stopPropagation(e: MouseEvent) {
    e.stopPropagation();
  }

  return (
    <div
      className={`fixed inset-0 z-40 ${overlayClassName}`}
      onClick={handleBackdropClick}
    >
      {/* Decorative dim layer only — must stay a sibling of the dialog, never an
          ancestor, or aria-hidden hides the whole dialog from the accessibility tree. */}
      <div className={`absolute inset-0 ${backdropClassName}`} aria-hidden="true" />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label={label}
        aria-labelledby={labelledBy}
        tabIndex={-1}
        className={`focus:outline-none ${panelClassName}`}
        onClick={stopPropagation}
      >
        {children}
      </div>
    </div>
  );
}
