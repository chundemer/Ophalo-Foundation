import type { ReactNode } from "react";

export type KeepBadgeVariant = "teal" | "attention" | "success" | "default";

const variantClasses: Record<KeepBadgeVariant, string> = {
  teal: "bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]",
  attention: "bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)]",
  success: "bg-[var(--ophalo-success-bg)] text-[var(--ophalo-success)]",
  default: "bg-muted text-muted-foreground",
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
