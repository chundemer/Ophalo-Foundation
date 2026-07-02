import Link from "next/link";

const VALID_CONTEXTS = ["existing_member", "recovery", "new_account"] as const;
type ValidContext = (typeof VALID_CONTEXTS)[number];

function normalizeContext(raw: string | undefined): ValidContext | null {
  if (VALID_CONTEXTS.includes(raw as ValidContext)) return raw as ValidContext;
  return null;
}

function isSigninContext(context: ValidContext | null): boolean {
  return context === "existing_member" || context === "recovery";
}

export default async function ExchangeErrorPage({
  searchParams,
}: {
  searchParams: Promise<{ reason?: string; context?: string }>;
}) {
  const { reason, context: rawContext } = await searchParams;
  const context = normalizeContext(rawContext);

  if (reason === "pilot_full") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>Pilot is currently full.</h1>
          <p>
            We have reached capacity for the current pilot. Return to start to
            check availability or register your interest.
          </p>
          <Link href="/start" className="auth-submit">
            Return to start
          </Link>
        </div>
      </div>
    );
  }

  if (reason === "session_creation_failed") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>Account created — sign in to continue.</h1>
          <p>
            Your account was created successfully, but we could not complete
            sign-in. Please sign in using the email address you registered with.
          </p>
          <Link href="/signin" className="auth-submit">
            Sign in
          </Link>
        </div>
      </div>
    );
  }

  if (reason === "service_unavailable") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>Something went wrong.</h1>
          <p>We were unable to complete sign-in. Please try again in a moment.</p>
          <Link href="/signin" className="auth-submit">
            Try signing in again
          </Link>
        </div>
      </div>
    );
  }

  if (reason === "account_already_exists") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>An account already exists for that email address.</h1>
          <p>
            Use the sign-in page to request a new sign-in link for your existing
            account.
          </p>
          <Link href="/signin" className="auth-submit">
            Sign in
          </Link>
        </div>
      </div>
    );
  }

  // Default: invalid / expired / missing / unknown reason
  const signinHint = isSigninContext(context);
  return (
    <div className="auth-page">
      <div className="container">
        <h1>This sign-in link is no longer valid.</h1>
        <p>
          For your security, sign-in links can only be used once or may expire.
          Please request a new sign-in link to continue.
        </p>
        <Link href={signinHint ? "/signin" : "/start"} className="auth-submit">
          {signinHint ? "Sign in" : "Request a new sign-in link"}
        </Link>
      </div>
    </div>
  );
}
