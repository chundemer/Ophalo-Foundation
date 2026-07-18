import type { ReactNode } from "react";
import Link from "next/link";

// ─── Helpers ─────────────────────────────────────────────────────────────────

export function keepBusinessInitials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "?";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}

// ─── Business identity header ────────────────────────────────────────────────

export function KeepBusinessHeader({
  businessName,
  label,
  description,
  className = "",
}: {
  businessName: string;
  label: string;
  description?: string;
  className?: string;
}) {
  return (
    <div className={`flex items-center gap-3 px-1 ${className}`}>
      <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-[var(--ophalo-navy)] text-sm font-bold tracking-wide text-white">
        {keepBusinessInitials(businessName)}
      </div>
      <div className="min-w-0">
        <p className="truncate text-lg font-bold leading-tight text-foreground">
          {businessName}
        </p>
        <p className="mt-0.5 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
          {label}
        </p>
        {description && (
          <p className="mt-1 text-sm text-muted-foreground">{description}</p>
        )}
      </div>
    </div>
  );
}

// ─── Card shell ──────────────────────────────────────────────────────────────

/**
 * Standard card with optional teal top accent strip.
 * When accentTop is true, children are wrapped in a padded inner div (px-5 py-6).
 * When false, padding (px-5 py-5) is applied to the card itself.
 */
export function KeepCardShell({
  accentTop = false,
  children,
  className = "",
}: {
  accentTop?: boolean;
  children: ReactNode;
  className?: string;
}) {
  if (accentTop) {
    return (
      <div
        className={`overflow-hidden rounded-2xl border border-[var(--ophalo-border)] bg-card shadow-sm ${className}`}
      >
        <div className="h-1 bg-[var(--keep-accent)]" />
        <div className="px-5 py-6">{children}</div>
      </div>
    );
  }
  return (
    <div
      className={`rounded-2xl border border-[var(--ophalo-border)] bg-card px-5 py-5 shadow-sm ${className}`}
    >
      {children}
    </div>
  );
}

// ─── Section header with icon ────────────────────────────────────────────────

export function KeepSectionHeader({
  icon,
  label,
}: {
  icon: ReactNode;
  label: string;
}) {
  return (
    <div className="mb-4 flex items-center gap-2">
      <span className="shrink-0 text-[var(--keep-accent)]" aria-hidden>
        {icon}
      </span>
      <span className="text-sm font-semibold text-foreground">{label}</span>
    </div>
  );
}

// ─── Page footer ─────────────────────────────────────────────────────────────

export function KeepPageFooter({ className = "" }: { className?: string }) {
  return (
    <footer className={`pb-6 pt-4 text-center ${className}`}>
      <img
        src="/brand/ophalo-lockup-color.svg"
        alt="OpHalo"
        className="mx-auto h-6 w-auto opacity-75"
      />
      <p className="mt-2 text-sm font-semibold text-[var(--ophalo-ink)]">
        Keep by OpHalo
      </p>
      <p className="mx-auto mt-1 max-w-md text-sm leading-5 text-[var(--ophalo-muted)]">
        The trust and continuity layer between businesses and customers.
      </p>
      <Link
        href="/privacy"
        className="mt-2 inline-block text-xs font-medium text-[var(--ophalo-muted)] underline-offset-2 hover:underline"
      >
        Privacy policy
      </Link>
    </footer>
  );
}
