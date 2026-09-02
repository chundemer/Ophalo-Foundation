import { useEffect, useRef, useState, type ReactNode } from "react";
import { ChevronDown } from "lucide-react";

export interface KeepSplitButtonOption {
  key: string;
  label: ReactNode;
  onSelect: () => void;
  disabled?: boolean;
}

interface KeepSplitButtonProps {
  label: ReactNode;
  onClick: () => void;
  options: KeepSplitButtonOption[];
  disabled?: boolean;
  className?: string;
  variant?: "teal" | "request-primary";
}

// Split button: primary action on the left, a caret-triggered menu of alternate actions on the
// right. Defaults to the quiet teal accent; `request-primary` (GAP-067 Slice 4) is the additive
// Request-Detail customer-resolution fill (`--keep-request-primary`), used only by the
// customer-update composer submit. Both reuse existing KeepButton design tokens.
export function KeepSplitButton({ label, onClick, options, disabled, className = "", variant = "teal" }: KeepSplitButtonProps) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
    }
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  const enabledCls =
    variant === "request-primary"
      ? "bg-[var(--keep-request-primary)] text-white hover:bg-[var(--keep-request-primary-hover)]"
      : "bg-[var(--keep-accent)] text-white hover:bg-[var(--keep-accent-hover)]";
  const disabledCls = "border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] text-[var(--ophalo-muted)]";
  const focusRing =
    "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

  return (
    <div ref={rootRef} className={`relative inline-flex ${className}`}>
      <button
        type="button"
        onClick={onClick}
        disabled={disabled}
        className={`inline-flex flex-1 items-center justify-center rounded-l-lg px-5 min-h-[42px] text-sm font-semibold transition-colors ${focusRing} ${
          disabled ? disabledCls : enabledCls
        } ${disabled ? "cursor-not-allowed" : "cursor-pointer"}`}
      >
        {label}
      </button>
      <button
        type="button"
        aria-label="More notify options"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
        disabled={disabled}
        className={`inline-flex items-center justify-center rounded-r-lg border-l border-white/25 px-2 min-h-[42px] transition-colors ${focusRing} ${
          disabled ? disabledCls : enabledCls
        } ${disabled ? "cursor-not-allowed" : "cursor-pointer"}`}
      >
        <ChevronDown className="h-4 w-4" />
      </button>
      {open && (
        <div
          role="menu"
          className="absolute right-0 top-[calc(100%+4px)] z-10 min-w-[220px] rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] py-1 shadow-lg"
        >
          {options.map((opt) => (
            <button
              key={opt.key}
              type="button"
              role="menuitem"
              disabled={opt.disabled}
              onClick={() => {
                setOpen(false);
                opt.onSelect();
              }}
              className="block w-full px-3 py-2 text-left text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:cursor-not-allowed disabled:text-[var(--ophalo-muted)]"
            >
              {opt.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
