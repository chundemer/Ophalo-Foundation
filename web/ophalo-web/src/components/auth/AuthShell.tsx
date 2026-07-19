import type { ReactNode } from "react";
import Image from "next/image";
import Link from "next/link";

// ─── Shared style fragments ─────────────────────────────────────────────────

export const authInputClass =
  "w-full rounded-lg border border-ophalo-border bg-ophalo-card px-4 py-3 text-sm text-ophalo-ink " +
  "placeholder:text-ophalo-muted focus:border-keep-accent focus:ring-1 " +
  "focus:ring-keep-accent focus:outline-none disabled:opacity-50";

export const authInvalidInputClass =
  "border-ophalo-danger ring-1 ring-ophalo-danger focus:border-ophalo-danger focus:ring-ophalo-danger";

const focusRingClass =
  "rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-keep-accent focus-visible:ring-offset-2";

const labelClass = "mb-1.5 block text-sm font-medium text-ophalo-ink";

// ─── Page frame ──────────────────────────────────────────────────────────────

export function AuthShell({
  children,
  maxWidthClassName = "max-w-md",
  bare = false,
}: {
  children: ReactNode;
  maxWidthClassName?: string;
  /**
   * ADR-390: the mobile auth handoff page must be a minimal page with no
   * external links. Suppresses the home-link wrapper and footer links,
   * leaving only static brand imagery and the card content.
   */
  bare?: boolean;
}) {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-ophalo-canvas px-4 py-12 sm:py-16">
      <div className={`w-full ${maxWidthClassName}`}>
        <div className="mb-8 flex justify-center">
          {bare ? (
            <Image
              src="/brand/ophalo-lockup-color.svg"
              alt="OpHalo"
              width={128}
              height={40}
              priority
              className="h-9 w-auto"
            />
          ) : (
            <Link href="/" aria-label="OpHalo home" className={focusRingClass}>
              <Image
                src="/brand/ophalo-lockup-color.svg"
                alt="OpHalo"
                width={128}
                height={40}
                priority
                className="h-9 w-auto"
              />
            </Link>
          )}
        </div>
        <div className="rounded-2xl border border-ophalo-border bg-ophalo-card px-6 py-8 shadow-sm sm:px-8">
          {children}
        </div>
        {!bare && <AuthFooterLinks />}
      </div>
    </div>
  );
}

export function AuthFooterLinks() {
  return (
    <p className="mt-6 text-center text-xs text-ophalo-muted">
      <Link href="/privacy" className={`underline-offset-2 hover:underline ${focusRingClass}`}>
        Privacy policy
      </Link>
      {" · "}
      <Link href="/terms" className={`underline-offset-2 hover:underline ${focusRingClass}`}>
        Terms
      </Link>
      {" · "}
      <a href="mailto:hello@ophalo.com" className={`underline-offset-2 hover:underline ${focusRingClass}`}>
        Contact
      </a>
    </p>
  );
}

// ─── Content primitives ──────────────────────────────────────────────────────

export function AuthHeading({ children }: { children: ReactNode }) {
  return <h1 className="text-2xl font-semibold tracking-tight text-ophalo-navy">{children}</h1>;
}

export function AuthLead({ children }: { children: ReactNode }) {
  return <p className="mt-2 text-sm leading-6 text-ophalo-muted">{children}</p>;
}

export function AuthNote({ children }: { children: ReactNode }) {
  return <p className="mt-4 text-sm text-ophalo-muted">{children}</p>;
}

export function AuthRequiredMark() {
  return <span className="ml-1.5 text-xs font-semibold text-ophalo-danger">* Required</span>;
}

export function AuthFormError({
  id = "auth-form-error",
  children,
}: {
  id?: string;
  children: ReactNode;
}) {
  return (
    <p
      id={id}
      role="alert"
      className="mb-4 rounded-lg bg-ophalo-danger-bg px-3 py-2 text-sm font-medium text-ophalo-danger"
    >
      {children}
    </p>
  );
}

export function AuthField({
  id,
  label,
  required,
  children,
}: {
  id: string;
  label: string;
  required?: boolean;
  children: ReactNode;
}) {
  return (
    <div className="mb-4">
      <label htmlFor={id} className={labelClass}>
        {label} {required && <AuthRequiredMark />}
      </label>
      {children}
    </div>
  );
}

export function AuthSubmitButton({
  children,
  disabled,
}: {
  children: ReactNode;
  disabled?: boolean;
}) {
  return (
    <button
      type="submit"
      disabled={disabled}
      className={`w-full rounded-lg bg-ophalo-navy px-5 py-3 text-sm font-semibold text-white transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50 ${focusRingClass}`}
    >
      {children}
    </button>
  );
}

export function AuthLinkButton({
  href,
  children,
}: {
  href: string;
  children: ReactNode;
}) {
  return (
    <Link
      href={href}
      className={`block w-full rounded-lg bg-ophalo-navy px-5 py-3 text-center text-sm font-semibold text-white transition hover:opacity-90 ${focusRingClass}`}
    >
      {children}
    </Link>
  );
}
