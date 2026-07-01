"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";

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
    <div className="auth-page">
      <div className="container">
        <Link href="/" className="auth-back">← Back to OpHalo</Link>
        <h1>Sign in to Keep</h1>
        <p>Enter your email and we&rsquo;ll send you a sign-in link.</p>

        <form className="auth-form" onSubmit={handleSubmit}>
          <div className="auth-form-field">
            <label htmlFor="email">Email</label>
            <input
              id="email"
              name="email"
              type="email"
              autoComplete="email"
              required
              disabled={submitting}
            />
          </div>

          {error && <p className="auth-error">{error}</p>}

          <button type="submit" className="auth-submit" disabled={submitting}>
            {submitting ? "Sending…" : "Send sign-in link"}
          </button>
        </form>

        <p className="auth-note">
          New to Keep?{" "}
          <Link href="/start">Get started</Link>
        </p>
      </div>
    </div>
  );
}
