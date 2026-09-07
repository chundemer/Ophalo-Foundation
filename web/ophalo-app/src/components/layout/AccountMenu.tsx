import { useCallback, useEffect, useId, useRef, useState } from "react";
import { Building2, ChevronDown, Clock, LogOut, Users } from "lucide-react";

// ADR-499: the authenticated shell separates operating workspaces (Requests / Price Book pills)
// from account- and business-level concerns. This menu owns the latter — workspace identity, the
// Owner/Admin Business Settings sections (which no longer have a top-level nav pill), and Sign out.
// It follows the in-repo dropdown pattern established by KeepSplitButton (self-contained open
// state, outside-pointer + Escape close), and adds menu-button keyboard semantics: the trigger
// opens on Enter/Space/ArrowDown, Escape closes and restores focus to the trigger, and
// ArrowUp/ArrowDown/Home/End move between items while open.

export type BusinessSettingsSectionId = "public-profile" | "policy" | "team";

export interface BusinessSettingsSection {
  id: BusinessSettingsSectionId;
  label: string;
}

// Owner/Admin only — callers pass [] for Operator/Viewer, who then see just identity + Sign out.
// Labels are fuller than the in-page tab labels (ADR-499): the menu is the discovery surface.
export function getBusinessSettingsSections(
  role: "owner" | "admin" | "operator" | "viewer" | "unknown" | string,
): BusinessSettingsSection[] {
  if (role !== "owner" && role !== "admin") return [];
  return [
    { id: "public-profile", label: "Company Profile & Public Link" },
    { id: "policy", label: "Response Policy (SLAs)" },
    { id: "team", label: "Team Seats & Permissions" },
  ];
}

const SECTION_ICON: Record<BusinessSettingsSectionId, typeof Building2> = {
  "public-profile": Building2,
  policy: Clock,
  team: Users,
};

interface AccountMenuProps {
  businessName?: string | null;
  userName?: string | null;
  roleLabel: string;
  sections: BusinessSettingsSection[];
  /** True whenever a #/settings route is open — gives the trigger the active affordance the
   *  removed Settings nav pill used to carry. */
  settingsActive: boolean;
  onNavigateSection: (id: BusinessSettingsSectionId) => void;
  onSignOut: () => void;
  isSigningOut: boolean;
}

export function AccountMenu({
  businessName,
  userName,
  roleLabel,
  sections,
  settingsActive,
  onNavigateSection,
  onSignOut,
  isSigningOut,
}: AccountMenuProps) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const itemsRef = useRef<Array<HTMLButtonElement | null>>([]);
  const menuId = useId();

  // The old shell showed `userName · role`, falling back to `businessName · role`, then bare role.
  const identity = userName
    ? `${userName} · ${roleLabel}`
    : businessName
      ? `${businessName} · ${roleLabel}`
      : roleLabel;
  // Only show the business line separately when it would not duplicate the identity line.
  const businessLine = userName && businessName ? businessName : null;

  const close = useCallback((restoreFocus: boolean) => {
    setOpen(false);
    if (restoreFocus) triggerRef.current?.focus();
  }, []);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", onPointerDown);
    return () => document.removeEventListener("mousedown", onPointerDown);
  }, [open]);

  // Move focus to the first item when the menu opens via keyboard or pointer.
  useEffect(() => {
    if (open) itemsRef.current[0]?.focus();
  }, [open]);

  const itemCount = sections.length + 1; // sections + Sign out

  function focusItem(index: number) {
    const clamped = (index + itemCount) % itemCount;
    itemsRef.current[clamped]?.focus();
  }

  function onMenuKeyDown(e: React.KeyboardEvent) {
    const current = itemsRef.current.findIndex((el) => el === document.activeElement);
    if (e.key === "Escape") {
      e.preventDefault();
      close(true);
    } else if (e.key === "ArrowDown") {
      e.preventDefault();
      focusItem(current + 1);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      focusItem(current - 1);
    } else if (e.key === "Home") {
      e.preventDefault();
      focusItem(0);
    } else if (e.key === "End") {
      e.preventDefault();
      focusItem(itemCount - 1);
    } else if (e.key === "Tab") {
      setOpen(false);
    }
  }

  function onTriggerKeyDown(e: React.KeyboardEvent) {
    if (!open && (e.key === "ArrowDown" || e.key === "Enter" || e.key === " ")) {
      e.preventDefault();
      setOpen(true);
    }
  }

  itemsRef.current = [];

  return (
    <div ref={rootRef} className="relative">
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen((v) => !v)}
        onKeyDown={onTriggerKeyDown}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        aria-label={businessName ? `${businessName} — account menu` : "Account menu"}
        className={`flex items-center gap-2 rounded-lg border px-2.5 py-1.5 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
          open || settingsActive
            ? "border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)]"
            : "border-transparent hover:bg-[var(--ophalo-canvas)]"
        }`}
      >
        <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-[var(--keep-accent)]" aria-hidden="true" />
        <span className="flex flex-col leading-tight">
          {businessLine && (
            <span className="text-xs font-semibold text-[var(--ophalo-navy)]">{businessLine}</span>
          )}
          <span className="text-[11px] text-[var(--ophalo-muted)]">{identity}</span>
        </span>
        <ChevronDown className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" aria-hidden="true" />
      </button>

      {open && (
        <div
          id={menuId}
          role="menu"
          aria-label="Account"
          onKeyDown={onMenuKeyDown}
          className="absolute right-0 top-[calc(100%+6px)] z-40 w-72 rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-1.5 shadow-xl"
        >
          <div className="border-b border-[var(--ophalo-border)] px-3 pb-3 pt-2">
            {businessName && (
              <p className="text-sm font-semibold text-[var(--ophalo-navy)]">{businessName}</p>
            )}
            <p className="text-xs text-[var(--ophalo-muted)]">
              {userName ? `${userName} · ${roleLabel}` : roleLabel}
            </p>
          </div>

          {sections.length > 0 && (
            <>
              <p className="px-3 pb-1 pt-2.5 text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--ophalo-muted)]">
                Business Settings
              </p>
              {sections.map((section, i) => {
                const Icon = SECTION_ICON[section.id];
                return (
                  <button
                    key={section.id}
                    ref={(el) => {
                      itemsRef.current[i] = el;
                    }}
                    type="button"
                    role="menuitem"
                    onClick={() => {
                      close(false);
                      onNavigateSection(section.id);
                    }}
                    className="flex w-full items-center gap-2.5 rounded-lg px-3 py-2 text-left text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] focus-visible:outline-none focus-visible:bg-[var(--ophalo-canvas)]"
                  >
                    <Icon className="h-4 w-4 shrink-0 text-[var(--ophalo-muted)]" aria-hidden="true" />
                    {section.label}
                  </button>
                );
              })}
              <div className="my-1.5 h-px bg-[var(--ophalo-border)]" />
            </>
          )}

          <button
            ref={(el) => {
              itemsRef.current[sections.length] = el;
            }}
            type="button"
            role="menuitem"
            onClick={onSignOut}
            disabled={isSigningOut}
            className="flex w-full items-center gap-2.5 rounded-lg px-3 py-2 text-left text-sm font-medium text-[var(--ophalo-accent)] hover:bg-[var(--ophalo-canvas)] disabled:cursor-wait disabled:opacity-60 focus-visible:outline-none focus-visible:bg-[var(--ophalo-canvas)]"
          >
            <LogOut className="h-4 w-4 shrink-0" aria-hidden="true" />
            {isSigningOut ? "Signing out…" : "Sign out"}
          </button>
        </div>
      )}
    </div>
  );
}
