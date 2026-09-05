import { KeepModal } from "../keep/KeepModal";
import type { NavItem } from "../../App";

interface MobileNavMenuProps {
  items: NavItem[];
  activeId: NavItem["id"];
  roleLabel: string;
  onNavigate: (id: NavItem["id"]) => void;
  onSignOut: () => void;
  isSigningOut: boolean;
  onClose: () => void;
}

/**
 * Shared mobile overflow nav (Session 2e.4, build-log/112/113): the only mobile-discoverable
 * path to Owner/Admin-only routes (Getting Started, Settings, and now Price Book — none of
 * these had a persistent mobile entry point before this component). Presents the same
 * role-filtered `items` the desktop sidebar/top nav already compute; adds no routing or
 * entitlement logic of its own.
 */
export function MobileNavMenu({
  items,
  activeId,
  roleLabel,
  onNavigate,
  onSignOut,
  isSigningOut,
  onClose,
}: MobileNavMenuProps) {
  return (
    <KeepModal
      onClose={onClose}
      label="Navigation menu"
      backdropClassName="bg-black/30"
      panelClassName="fixed z-50 inset-x-0 top-0 bg-white shadow-xl flex flex-col max-h-[80vh]"
    >
      <nav className="px-3 py-4 space-y-0.5 overflow-y-auto">
        {items.map((item) => (
          <button
            key={item.id}
            type="button"
            onClick={() => onNavigate(item.id)}
            className={`w-full flex items-center gap-2.5 rounded-md px-3 py-2.5 text-sm text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
              activeId === item.id
                ? "font-semibold bg-[var(--keep-accent-bg)] text-[var(--ophalo-navy)]"
                : "font-medium text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)]"
            }`}
          >
            {item.icon}
            <span>{item.label}</span>
          </button>
        ))}
      </nav>
      {roleLabel && (
        <div className="px-4 py-3 border-t border-[var(--ophalo-border)]">
          <p className="text-xs text-[var(--ophalo-muted)]">{roleLabel}</p>
        </div>
      )}
      <div className="px-3 py-3 border-t border-[var(--ophalo-border)]">
        <button
          type="button"
          onClick={onSignOut}
          disabled={isSigningOut}
          className="w-full rounded-md px-3 py-2.5 text-left text-sm font-medium text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)] disabled:cursor-wait disabled:opacity-60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
        >
          {isSigningOut ? "Signing out…" : "Sign out"}
        </button>
      </div>
    </KeepModal>
  );
}
