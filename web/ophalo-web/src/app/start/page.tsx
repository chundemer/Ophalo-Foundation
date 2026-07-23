"use client";

import { useState, useEffect } from "react";
import { useRouter } from "next/navigation";
import Image from "next/image";
import Link from "next/link";
import {
  AuthShell,
  AuthHeading,
  AuthLead,
  AuthNote,
  AuthField,
  AuthFormError,
  AuthSubmitButton,
  AuthFooterLinks,
  authInputClass,
  authInvalidInputClass,
} from "@/components/auth/AuthShell";
import { SessionRedirectGate } from "@/components/auth/SessionRedirectGate";
import { useSessionRedirect } from "@/lib/useSessionRedirect";

const FALLBACK_TZ_LIST = [
  "America/New_York",
  "America/Chicago",
  "America/Denver",
  "America/Phoenix",
  "America/Los_Angeles",
  "America/Anchorage",
  "Pacific/Honolulu",
  "UTC",
];

function getTimeZoneList(): string[] {
  try {
    const list = (
      Intl as typeof Intl & {
        supportedValuesOf?: (key: string) => string[];
      }
    ).supportedValuesOf?.("timeZone");
    return list && list.length > 0 ? list : FALLBACK_TZ_LIST;
  } catch {
    return FALLBACK_TZ_LIST;
  }
}

const TZ_LIST = getTimeZoneList();

function friendlyTimeZoneLabel(tz: string): string {
  const city = tz.includes("/") ? tz.split("/").pop()!.replace(/_/g, " ") : "";
  for (const style of ["longGeneric", "long"] as const) {
    try {
      const parts = new Intl.DateTimeFormat("en-US", {
        timeZone: tz,
        timeZoneName: style,
      }).formatToParts(new Date());
      const name = parts.find((p) => p.type === "timeZoneName")?.value;
      if (name) return city && city !== name ? `${name} — ${city}` : name;
    } catch {
      // try the next style
    }
  }
  return city || tz;
}

const VALUE_POINTS = [
  "One place to track every service request",
  "Customers get clear updates through a private request page",
  "Dependable follow-through — nothing falls through the cracks",
];

export default function StartPage() {
  const router = useRouter();
  const { sessionCheck, retry: checkSession } = useSessionRedirect();
  const [timeZone, setTimeZone] = useState(TZ_LIST[0]);
  const [detectedTimeZone, setDetectedTimeZone] = useState<string | null>(null);
  const [editingTimeZone, setEditingTimeZone] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pilotFull, setPilotFull] = useState(false);
  const [emailInUse, setEmailInUse] = useState(false);

  useEffect(() => {
    const detected = Intl.DateTimeFormat().resolvedOptions().timeZone;
    if (TZ_LIST.includes(detected)) {
      setTimeZone(detected);
      setDetectedTimeZone(detected);
    }
  }, []);

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setError(null);
    setPilotFull(false);
    setEmailInUse(false);
    setSubmitting(true);

    const data = new FormData(e.currentTarget);
    const name = String(data.get("name") ?? "").trim();
    const businessName = String(data.get("businessName") ?? "").trim();
    const email = String(data.get("email") ?? "").trim();
    const tz = String(data.get("timeZone") ?? "").trim();

    if (!TZ_LIST.includes(tz)) {
      setError("Please select a valid time zone.");
      setSubmitting(false);
      return;
    }

    try {
      const res = await fetch(
        `${process.env.NEXT_PUBLIC_API_BASE_URL}/auth/start`,
        {
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ email, businessName, name, timeZone: tz }),
        },
      );

      if (!res.ok) {
        const problem = await res.json().catch(() => null);
        const code: string | undefined = problem?.code;
        switch (code) {
          case "Account.PilotFull":
            setPilotFull(true);
            break;
          case "Account.EmailAlreadyInUse":
            setEmailInUse(true);
            break;
          case "Validation.EmailRequired":
            setError("Email is required.");
            break;
          case "Validation.BusinessNameRequired":
            setError("Business name is required.");
            break;
          case "Validation.TimeZoneRequired":
          case "Validation.TimeZoneInvalid":
            setError("Please select a valid time zone.");
            break;
          default:
            setError("Something went wrong. Please try again.");
        }
        return;
      }

      router.push("/auth/check-email?flow=start");
    } catch {
      setError("Something went wrong. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  if (pilotFull) {
    return (
      <SessionRedirectGate sessionCheck={sessionCheck} onRetry={checkSession}>
        <AuthShell>
          <AuthHeading>Pilot is full.</AuthHeading>
          <AuthLead>
            {"We've reached our pilot capacity. Email us at "}
            <a href="mailto:pilot@ophalo.com" className="underline underline-offset-2">
              pilot@ophalo.com
            </a>
            {" if you'd like to be notified when we open up."}
          </AuthLead>
        </AuthShell>
      </SessionRedirectGate>
    );
  }

  const emailErrorId = emailInUse ? "email-in-use-error" : undefined;
  const isDetectedValue = detectedTimeZone !== null && timeZone === detectedTimeZone;

  return (
    <SessionRedirectGate sessionCheck={sessionCheck} onRetry={checkSession}>
    <div className="flex min-h-screen items-center justify-center bg-ophalo-canvas px-4 py-8 sm:py-12">
      <div className="w-full max-w-5xl overflow-hidden rounded-2xl border border-ophalo-border bg-ophalo-card shadow-sm md:grid md:grid-cols-5">
        {/* Compact identity/value header — narrow widths only */}
        <div className="flex items-center gap-3 bg-ophalo-navy px-6 py-5 text-white md:hidden">
          <Link
            href="/"
            aria-label="OpHalo home"
            className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-ophalo-card p-1.5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-keep-accent focus-visible:ring-offset-2 focus-visible:ring-offset-ophalo-navy"
          >
            <Image src="/brand/ophalo-mark.svg" alt="" width={32} height={32} />
          </Link>
          <div>
            <p className="text-sm font-semibold tracking-tight">OpHalo Keep</p>
            <p className="text-xs text-white/70">
              The trust and continuity layer for service businesses.
            </p>
          </div>
        </div>

        {/* Identity/value panel — desktop */}
        <div className="hidden flex-col justify-between bg-ophalo-navy px-8 py-10 text-white md:col-span-2 md:flex">
          <div>
            <Link
              href="/"
              aria-label="OpHalo home"
              className="inline-flex h-14 w-14 items-center justify-center rounded-2xl bg-ophalo-card p-2.5 shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-keep-accent focus-visible:ring-offset-2 focus-visible:ring-offset-ophalo-navy"
            >
              <Image src="/brand/ophalo-mark.svg" alt="" width={40} height={40} />
            </Link>
            <p className="mt-5 text-xl font-semibold tracking-tight">OpHalo Keep</p>
            <p className="mt-3 text-sm leading-6 text-white/75">
              Keep gives your service business one place for customer requests,
              with clear updates that keep everyone on the same page.
            </p>
            <ul className="mt-7 space-y-3">
              {VALUE_POINTS.map((point) => (
                <li key={point} className="flex items-start gap-2.5 text-sm leading-6 text-white/90">
                  <span className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full bg-keep-accent" aria-hidden="true" />
                  {point}
                </li>
              ))}
            </ul>
          </div>
        </div>

        {/* Form panel */}
        <div className="px-6 py-8 sm:px-10 sm:py-10 md:col-span-3">
          <AuthHeading>Start your Keep pilot</AuthHeading>
          <AuthLead>
            Tell us about your business and we&apos;ll email you a secure sign-in
            link to finish setting up your account — no separate approval step.
          </AuthLead>
          <AuthNote>No cost during pilot.</AuthNote>

          <form
            className="mt-6"
            onSubmit={handleSubmit}
            aria-describedby={error ? "auth-form-error" : undefined}
          >
            {error && <AuthFormError>{error}</AuthFormError>}
            {emailInUse && (
              <AuthFormError id="email-in-use-error">
                This email already has an account.{" "}
                <Link href="/signin" className="underline underline-offset-2">Sign in instead</Link>
              </AuthFormError>
            )}

            <AuthField id="name" label="Name" required>
              <input
                id="name"
                name="name"
                type="text"
                autoComplete="name"
                required
                disabled={submitting}
                className={authInputClass}
              />
            </AuthField>

            <AuthField id="businessName" label="Business name" required>
              <input
                id="businessName"
                name="businessName"
                type="text"
                autoComplete="organization"
                required
                disabled={submitting}
                className={authInputClass}
              />
            </AuthField>

            <AuthField id="email" label="Work email" required>
              <input
                id="email"
                name="email"
                type="email"
                autoComplete="email"
                required
                disabled={submitting}
                aria-invalid={emailInUse}
                aria-describedby={emailErrorId}
                className={authInputClass + (emailInUse ? " " + authInvalidInputClass : "")}
              />
            </AuthField>

            <div className="mb-4">
              <label htmlFor={editingTimeZone ? "timeZone" : "timeZoneChange"} className="mb-1.5 block text-sm font-medium text-ophalo-ink">
                Time zone <span className="ml-1.5 text-xs font-semibold text-ophalo-danger">* Required</span>
              </label>
              {editingTimeZone ? (
                <select
                  id="timeZone"
                  name="timeZone"
                  value={timeZone}
                  onChange={(e) => {
                    setTimeZone(e.target.value);
                    setEditingTimeZone(false);
                  }}
                  onBlur={() => setEditingTimeZone(false)}
                  required
                  disabled={submitting}
                  autoFocus
                  className={authInputClass}
                >
                  {TZ_LIST.map((tz) => (
                    <option key={tz} value={tz}>
                      {friendlyTimeZoneLabel(tz)}
                    </option>
                  ))}
                </select>
              ) : (
                <div className="flex items-center justify-between gap-3 rounded-lg border border-ophalo-border bg-ophalo-card px-4 py-3">
                  <div>
                    <p className="text-sm text-ophalo-ink">{friendlyTimeZoneLabel(timeZone)}</p>
                    {isDetectedValue && (
                      <p className="mt-0.5 text-xs text-ophalo-muted">Detected from your device</p>
                    )}
                  </div>
                  <button
                    type="button"
                    id="timeZoneChange"
                    onClick={() => setEditingTimeZone(true)}
                    disabled={submitting}
                    className="shrink-0 rounded text-sm font-medium text-keep-accent underline-offset-2 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-keep-accent focus-visible:ring-offset-2 disabled:opacity-50"
                  >
                    Change
                  </button>
                  <input type="hidden" name="timeZone" value={timeZone} />
                </div>
              )}
            </div>

            <AuthSubmitButton disabled={submitting}>
              {submitting ? "Submitting…" : "Start my Keep pilot"}
            </AuthSubmitButton>
          </form>

          <AuthNote>
            Already have an account? <Link href="/signin" className="underline underline-offset-2">Sign in</Link>
          </AuthNote>
          <AuthFooterLinks />
        </div>
      </div>
    </div>
    </SessionRedirectGate>
  );
}
