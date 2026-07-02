import Link from "next/link";

export default async function InviteAcceptErrorPage({
  searchParams,
}: {
  searchParams: Promise<{ reason?: string }>;
}) {
  const { reason } = await searchParams;

  if (reason === "missing_token") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>Invalid invite link.</h1>
          <p>
            This invite link is missing required information. Please use the
            link from your invitation email or contact your account owner for a
            new invite.
          </p>
          <Link href="/signin" className="auth-submit">
            Sign in
          </Link>
        </div>
      </div>
    );
  }

  if (reason === "expired") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>This invite link has expired.</h1>
          <p>
            Invite links expire after a limited time. Please contact your
            account owner to request a new invite.
          </p>
          <Link href="/signin" className="auth-submit">
            Sign in
          </Link>
        </div>
      </div>
    );
  }

  if (reason === "already_active") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>You are already a member.</h1>
          <p>
            This invite has already been accepted. Sign in to access your
            account.
          </p>
          <Link href="/signin" className="auth-submit">
            Sign in
          </Link>
        </div>
      </div>
    );
  }

  if (reason === "seat_limit") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>Team limit reached.</h1>
          <p>
            This account has reached its member limit. Please contact your
            account owner for assistance.
          </p>
          <Link href="/" className="auth-submit">
            Return home
          </Link>
        </div>
      </div>
    );
  }

  if (reason === "session_creation_failed") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>Invite accepted — sign in to continue.</h1>
          <p>
            Your invite was accepted successfully, but we could not complete
            sign-in automatically. Please sign in to access your account.
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
          <p>
            We were unable to accept your invite. Please try again in a moment
            or contact support if the problem continues.
          </p>
          <Link href="/signin" className="auth-submit">
            Try signing in
          </Link>
        </div>
      </div>
    );
  }

  // Default: invalid / already-used / unknown reason
  return (
    <div className="auth-page">
      <div className="container">
        <h1>This invite link is no longer valid.</h1>
        <p>
          The invite link may have already been used or is no longer active.
          Please contact your account owner for a new invite.
        </p>
        <Link href="/signin" className="auth-submit">
          Sign in
        </Link>
      </div>
    </div>
  );
}
