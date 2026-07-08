import type { ReactNode } from "react";

// Extends the ophalo-web mirror with `danger` and `info` variants needed for
// workbench attention severity mapping.
export type KeepBadgeVariant = "teal" | "attention" | "danger" | "info" | "success" | "default";

const variantClasses: Record<KeepBadgeVariant, string> = {
  teal: "bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]",
  attention: "bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)]",
  danger: "bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]",
  info: "bg-[var(--keep-info-bg)] text-[var(--keep-info)]",
  success: "bg-[var(--ophalo-success-bg)] text-[var(--ophalo-success)]",
  default: "bg-[var(--ophalo-canvas)] text-[var(--ophalo-muted)] border border-[var(--ophalo-border)]",
};

export function KeepBadge({
  variant = "default",
  className = "",
  children,
}: {
  variant?: KeepBadgeVariant;
  className?: string;
  children: ReactNode;
}) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-semibold leading-none ${variantClasses[variant]} ${className}`}
    >
      {children}
    </span>
  );
}
