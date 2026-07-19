import {
  AuthShell,
  AuthHeading,
  AuthLead,
  AuthLinkButton,
} from "@/components/auth/AuthShell";

export default async function InviteAcceptErrorPage({
  searchParams,
}: {
  searchParams: Promise<{ reason?: string }>;
}) {
  const { reason } = await searchParams;

  if (reason === "missing_token") {
    return (
      <AuthShell>
        <AuthHeading>Invalid invite link.</AuthHeading>
        <AuthLead>
          This invite link is missing required information. Please use the
          link from your invitation email or contact your account owner for a
          new invite.
        </AuthLead>
        <div className="mt-6">
          <AuthLinkButton href="/signin">Sign in</AuthLinkButton>
        </div>
      </AuthShell>
    );
  }

  if (reason === "expired") {
    return (
      <AuthShell>
        <AuthHeading>This invite link has expired.</AuthHeading>
        <AuthLead>
          Invite links expire after a limited time. Please contact your
          account owner to request a new invite.
        </AuthLead>
        <div className="mt-6">
          <AuthLinkButton href="/signin">Sign in</AuthLinkButton>
        </div>
      </AuthShell>
    );
  }

  if (reason === "already_active") {
    return (
      <AuthShell>
        <AuthHeading>You are already a member.</AuthHeading>
        <AuthLead>
          This invite has already been accepted. Sign in to access your
          account.
        </AuthLead>
        <div className="mt-6">
          <AuthLinkButton href="/signin">Sign in</AuthLinkButton>
        </div>
      </AuthShell>
    );
  }

  if (reason === "seat_limit") {
    return (
      <AuthShell>
        <AuthHeading>Team limit reached.</AuthHeading>
        <AuthLead>
          This account has reached its member limit. Please contact your
          account owner for assistance.
        </AuthLead>
        <div className="mt-6">
          <AuthLinkButton href="/">Return home</AuthLinkButton>
        </div>
      </AuthShell>
    );
  }

  if (reason === "session_creation_failed") {
    return (
      <AuthShell>
        <AuthHeading>Invite accepted — sign in to continue.</AuthHeading>
        <AuthLead>
          Your invite was accepted successfully, but we could not complete
          sign-in automatically. Please sign in to access your account.
        </AuthLead>
        <div className="mt-6">
          <AuthLinkButton href="/signin">Sign in</AuthLinkButton>
        </div>
      </AuthShell>
    );
  }

  if (reason === "service_unavailable") {
    return (
      <AuthShell>
        <AuthHeading>Something went wrong.</AuthHeading>
        <AuthLead>
          We were unable to accept your invite. Please try again in a moment
          or contact support if the problem continues.
        </AuthLead>
        <div className="mt-6">
          <AuthLinkButton href="/signin">Try signing in</AuthLinkButton>
        </div>
      </AuthShell>
    );
  }

  // Default: invalid / already-used / unknown reason
  return (
    <AuthShell>
      <AuthHeading>This invite link is no longer valid.</AuthHeading>
      <AuthLead>
        The invite link may have already been used or is no longer active.
        Please contact your account owner for a new invite.
      </AuthLead>
      <div className="mt-6">
        <AuthLinkButton href="/signin">Sign in</AuthLinkButton>
      </div>
    </AuthShell>
  );
}
