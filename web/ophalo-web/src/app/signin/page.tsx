"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import {
  AuthShell,
  AuthHeading,
  AuthLead,
  AuthNote,
  AuthField,
  AuthFormError,
  AuthSubmitButton,
  authInputClass,
  authInvalidInputClass,
} from "@/components/auth/AuthShell";

export default function SignInPage() {
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);

    const data = new FormData(e.currentTarget);
    const email = String(data.get("email") ?? "").trim();

    try {
      const res = await fetch(
        `${process.env.NEXT_PUBLIC_API_BASE_URL}/auth/signin`,
        {
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ email }),
        },
      );

      if (!res.ok) {
        const problem = await res.json().catch(() => null);
        const code: string | undefined = problem?.code;
        if (code === "Validation.EmailRequired") {
          setError("Email is required.");
        } else {
          setError("Something went wrong. Please try again.");
        }
        return;
      }

      router.push("/auth/check-email?flow=signin");
    } catch {
      setError("Something went wrong. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AuthShell>
      <AuthHeading>Sign in to Keep</AuthHeading>
      <AuthLead>Enter your email and we&rsquo;ll send you a sign-in link.</AuthLead>

      <form
        className="mt-6"
        onSubmit={handleSubmit}
        aria-describedby={error ? "auth-form-error" : undefined}
      >
        {error && <AuthFormError>{error}</AuthFormError>}

        <AuthField id="email" label="Email" required>
          <input
            id="email"
            name="email"
            type="email"
            autoComplete="email"
            required
            disabled={submitting}
            aria-invalid={!!error}
            className={authInputClass + (error ? " " + authInvalidInputClass : "")}
          />
        </AuthField>

        <AuthSubmitButton disabled={submitting}>
          {submitting ? "Sending…" : "Send sign-in link"}
        </AuthSubmitButton>
      </form>

      <AuthNote>
        New to Keep? <Link href="/start" className="underline underline-offset-2">Get started</Link>
      </AuthNote>
    </AuthShell>
  );
}
