"use client";

import { useState, useEffect } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";

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

export default function StartPage() {
  const router = useRouter();
  const [timeZone, setTimeZone] = useState(TZ_LIST[0]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pilotFull, setPilotFull] = useState(false);
  const [emailInUse, setEmailInUse] = useState(false);

  useEffect(() => {
    const detected = Intl.DateTimeFormat().resolvedOptions().timeZone;
    if (TZ_LIST.includes(detected)) {
      setTimeZone(detected);
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
      <div className="auth-page">
        <div className="container">
          <h1>Pilot is full.</h1>
          <p>
            {"We've reached our pilot capacity. Email us at "}
            <a href="mailto:pilot@ophalo.com">pilot@ophalo.com</a>
            {" if you'd like to be notified when we open up."}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="auth-page">
      <div className="container">
        <Link href="/" className="auth-back">← Back to OpHalo</Link>
        <h1>Get access to Keep</h1>
        <p>
          Keep is currently in pilot with service businesses. Early users can
          try it on real active jobs and help shape what we build next.
        </p>
        <p className="auth-note">No cost during pilot.</p>

        <form className="auth-form" onSubmit={handleSubmit}>
          <div className="auth-form-field">
            <label htmlFor="name">Name</label>
            <input
              id="name"
              name="name"
              type="text"
              autoComplete="name"
              required
              disabled={submitting}
            />
          </div>
          <div className="auth-form-field">
            <label htmlFor="businessName">Business name</label>
            <input
              id="businessName"
              name="businessName"
              type="text"
              autoComplete="organization"
              required
              disabled={submitting}
            />
          </div>
          <div className="auth-form-field">
            <label htmlFor="email">Work email</label>
            <input
              id="email"
              name="email"
              type="email"
              autoComplete="email"
              required
              disabled={submitting}
            />
          </div>
          <div className="auth-form-field">
            <label htmlFor="timeZone">Time zone</label>
            <select
              id="timeZone"
              name="timeZone"
              value={timeZone}
              onChange={(e) => setTimeZone(e.target.value)}
              required
              disabled={submitting}
            >
              {TZ_LIST.map((tz) => (
                <option key={tz} value={tz}>
                  {tz}
                </option>
              ))}
            </select>
          </div>

          {emailInUse && (
            <p className="auth-error">
              This email already has an account.{" "}
              <Link href="/signin">Sign in instead</Link>
            </p>
          )}
          {error && <p className="auth-error">{error}</p>}

          <button type="submit" className="auth-submit" disabled={submitting}>
            {submitting ? "Submitting…" : "Request access"}
          </button>
        </form>

        <p className="auth-note">
          Already have an account?{" "}
          <Link href="/signin">Sign in</Link>
        </p>
        <p className="auth-note">
          Questions?{" "}
          <a href="mailto:hello@ophalo.com">Talk to us</a>
        </p>
      </div>
    </div>
  );
}
