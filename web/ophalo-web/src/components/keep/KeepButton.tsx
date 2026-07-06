import type { ButtonHTMLAttributes, ReactNode } from "react";

export type KeepButtonVariant = "teal" | "primary";

export function KeepButton({
  variant = "teal",
  className = "",
  disabled,
  children,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: KeepButtonVariant;
  children: ReactNode;
}) {
  const base =
    "inline-flex items-center justify-center rounded-lg px-5 font-semibold transition-colors min-h-[42px] text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

  const enabledTeal = "bg-[var(--keep-accent)] text-white hover:bg-[var(--keep-accent-hover)]";
  const disabledTeal = "border border-[var(--ophalo-border)] bg-[var(--muted)] text-[var(--muted-foreground)]";

  const enabledPrimary = "bg-[var(--ophalo-navy)] text-white hover:opacity-90";
  const disabledPrimary = "border border-[var(--ophalo-border)] bg-[var(--muted)] text-[var(--muted-foreground)]";

  let variantClass: string;
  if (variant === "teal") {
    variantClass = disabled ? disabledTeal : enabledTeal;
  } else {
    variantClass = disabled ? disabledPrimary : enabledPrimary;
  }

  return (
    <button
      disabled={disabled}
      className={`${base} ${variantClass} ${disabled ? "cursor-not-allowed" : "cursor-pointer"} ${className}`}
      {...props}
    >
      {children}
    </button>
  );
}
