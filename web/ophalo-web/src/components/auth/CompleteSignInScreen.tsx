"use client";

import { useState } from "react";
import {
  AuthHeading,
  AuthLead,
  AuthField,
  AuthFormError,
  AuthSubmitButton,
  authInputClass,
  authInvalidInputClass,
} from "@/components/auth/AuthShell";

export type ContinuationWorkspace = {
  accountUserId: string;
  businessName: string;
  role: string;
};

/**
 * Redeems a PostAuthContinuation (ADR-497) after /auth/exchange or
 * /accounts/invite/accept returns `requiresContinuation`. The browser already
 * holds the ophalo.continuation cookie from that same-origin response — this
 * only needs credentials: "include" on the /auth/continue fetch, same as the
 * exchange/accept calls that got us here.
 */
export function CompleteSignInScreen({
  requiresName,
  workspaces,
  errorBasePath,
}: {
  requiresName: boolean;
  workspaces: ContinuationWorkspace[] | null;
  errorBasePath: string;
}) {
  const [name, setName] = useState("");
  const [accountUserId, setAccountUserId] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);

    try {
      const res = await fetch(
        `${process.env.NEXT_PUBLIC_API_BASE_URL}/auth/continue`,
        {
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            name: requiresName ? name.trim() : undefined,
            accountUserId: workspaces ? accountUserId || undefined : undefined,
          }),
        },
      );

      if (res.ok) {
        window.location.assign(
          process.env.NEXT_PUBLIC_APP_BASE_URL ?? "http://localhost:5173",
        );
        return;
      }

      if (res.status === 400) {
        const problem = await res.json().catch(() => null);
        setError(
          problem?.detail ?? "Please check your entries and try again.",
        );
        setSubmitting(false);
        return;
      }

      if (res.status === 503) {
        const problem = await res.json().catch(() => null);
        const code = problem?.extensions?.code ?? problem?.code;
        window.location.assign(
          code === "Account.SessionCreationFailed"
            ? `${errorBasePath}?reason=session_creation_failed`
            : `${errorBasePath}?reason=service_unavailable`,
        );
        return;
      }

      // 404 and any other terminal status: the continuation is no longer valid.
      window.location.assign(`${errorBasePath}?reason=invalid`);
    } catch {
      window.location.assign(`${errorBasePath}?reason=service_unavailable`);
    }
  }

  return (
    <>
      <AuthHeading>Just one more step</AuthHeading>
      <AuthLead>
        {requiresName && workspaces
          ? "Tell us your name and choose which workspace to sign in to."
          : requiresName
            ? "Tell us your name to finish signing in."
            : "Choose which workspace to sign in to."}
      </AuthLead>

      <form
        className="mt-6"
        onSubmit={handleSubmit}
        aria-describedby={error ? "auth-form-error" : undefined}
      >
        {error && <AuthFormError>{error}</AuthFormError>}

        {requiresName && (
          <AuthField id="name" label="Your name" required>
            <input
              id="name"
              name="name"
              type="text"
              autoComplete="name"
              required
              disabled={submitting}
              aria-invalid={!!error}
              value={name}
              onChange={(e) => setName(e.target.value)}
              className={authInputClass + (error ? " " + authInvalidInputClass : "")}
            />
          </AuthField>
        )}

        {workspaces && (
          <fieldset className="mb-4">
            <legend className="mb-1.5 block text-sm font-medium text-ophalo-ink">
              Workspace
            </legend>
            <div className="space-y-2">
              {workspaces.map((w) => (
                <label
                  key={w.accountUserId}
                  className="flex cursor-pointer items-center gap-3 rounded-lg border border-ophalo-border bg-ophalo-card px-4 py-3 text-sm text-ophalo-ink has-[:checked]:border-keep-accent has-[:checked]:ring-1 has-[:checked]:ring-keep-accent"
                >
                  <input
                    type="radio"
                    name="accountUserId"
                    value={w.accountUserId}
                    required
                    disabled={submitting}
                    checked={accountUserId === w.accountUserId}
                    onChange={() => setAccountUserId(w.accountUserId)}
                    className="h-4 w-4"
                  />
                  <span className="flex-1">{w.businessName}</span>
                  <span className="text-xs capitalize text-ophalo-muted">{w.role}</span>
                </label>
              ))}
            </div>
          </fieldset>
        )}

        <AuthSubmitButton disabled={submitting}>
          {submitting ? "Continuing…" : "Continue"}
        </AuthSubmitButton>
      </form>
    </>
  );
}
