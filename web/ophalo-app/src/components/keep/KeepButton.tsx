import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from "react";

export type KeepButtonVariant = "teal" | "primary" | "secondary";

export const KeepButton = forwardRef<
  HTMLButtonElement,
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: KeepButtonVariant; children: ReactNode }
>(function KeepButton({
  variant = "teal",
  className = "",
  disabled,
  children,
  ...props
}, ref) {
  const base =
    "inline-flex items-center justify-center rounded-lg px-5 font-semibold transition-colors min-h-[42px] text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

  const enabledTeal = "bg-[var(--keep-accent)] text-white hover:bg-[var(--keep-accent-hover)]";
  const disabledTeal = "border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] text-[var(--ophalo-muted)]";

  const enabledPrimary = "bg-[var(--ophalo-navy)] text-white hover:opacity-90";
  const disabledPrimary = "border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] text-[var(--ophalo-muted)]";

  const enabledSecondary = "border-2 border-[var(--ophalo-navy)] text-[var(--ophalo-navy)] bg-transparent hover:bg-[var(--ophalo-canvas)]";
  const disabledSecondary = "border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] text-[var(--ophalo-muted)]";

  let variantClass: string;
  if (variant === "teal") {
    variantClass = disabled ? disabledTeal : enabledTeal;
  } else if (variant === "secondary") {
    variantClass = disabled ? disabledSecondary : enabledSecondary;
  } else {
    variantClass = disabled ? disabledPrimary : enabledPrimary;
  }

  return (
    <button
      ref={ref}
      disabled={disabled}
      className={`${base} ${variantClass} ${disabled ? "cursor-not-allowed" : "cursor-pointer"} ${className}`}
      {...props}
    >
      {children}
    </button>
  );
});
