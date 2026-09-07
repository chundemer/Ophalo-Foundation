import { KeepModal } from "../keep/KeepModal";
import type { NavItem } from "../../App";
import type { BusinessSettingsSection, BusinessSettingsSectionId } from "./AccountMenu";

interface MobileNavMenuProps {
  items: NavItem[];
  activeId: NavItem["id"];
  roleLabel: string;
  /** ADR-499: Owner/Admin Business Settings sections, shown as a grouped block below the nav
   *  items. Empty for Operator/Viewer. */
  sections?: BusinessSettingsSection[];
  onNavigate: (id: NavItem["id"]) => void;
  onNavigateSection?: (id: BusinessSettingsSectionId) => void;
  onSignOut: () => void;
  isSigningOut: boolean;
  onClose: () => void;
}

/**
 * Shared mobile overflow nav (Session 2e.4, build-log/112/113). Below md:/768px this is the only
 * navigation surface, so it mirrors the desktop shell: the role/entitlement-filtered nav `items`
 * plus, for Owner/Admin, the same Business Settings sections the desktop AccountMenu groups
 * (ADR-499). It adds no routing or entitlement logic of its own.
 */
export function MobileNavMenu({
  items,
  activeId,
  roleLabel,
  sections = [],
  onNavigate,
  onNavigateSection,
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

        {sections.length > 0 && onNavigateSection && (
          <>
            <p className="px-3 pb-1 pt-4 text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--ophalo-muted)]">
              Business Settings
            </p>
            {sections.map((section) => (
              <button
                key={section.id}
                type="button"
                onClick={() => onNavigateSection(section.id)}
                className={`w-full flex items-center gap-2.5 rounded-md px-3 py-2.5 text-sm text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
                  activeId === "settings"
                    ? "font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)]"
                    : "font-medium text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)]"
                }`}
              >
                <span>{section.label}</span>
              </button>
            ))}
          </>
        )}
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
          className="w-full rounded-md px-3 py-2.5 text-left text-sm font-medium text-[var(--ophalo-accent)] hover:bg-[var(--ophalo-canvas)] disabled:cursor-wait disabled:opacity-60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
        >
          {isSigningOut ? "Signing out…" : "Sign out"}
        </button>
      </div>
    </KeepModal>
  );
}
