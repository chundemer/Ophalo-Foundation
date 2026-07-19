import {
  AuthShell,
  AuthHeading,
  AuthLead,
  AuthLinkButton,
} from "@/components/auth/AuthShell";

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
      <AuthShell>
        <AuthHeading>Pilot is currently full.</AuthHeading>
        <AuthLead>
          We have reached capacity for the current pilot. Return to start to
          check availability or register your interest.
        </AuthLead>
        <div className="mt-6">
          <AuthLinkButton href="/start">Return to start</AuthLinkButton>
        </div>
      </AuthShell>
    );
  }

  if (reason === "session_creation_failed") {
    return (
      <AuthShell>
        <AuthHeading>Account created — sign in to continue.</AuthHeading>
        <AuthLead>
          Your account was created successfully, but we could not complete
          sign-in. Please sign in using the email address you registered with.
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
        <AuthLead>We were unable to complete sign-in. Please try again in a moment.</AuthLead>
        <div className="mt-6">
          <AuthLinkButton href="/signin">Try signing in again</AuthLinkButton>
        </div>
      </AuthShell>
    );
  }

  if (reason === "account_already_exists") {
    return (
      <AuthShell>
        <AuthHeading>An account already exists for that email address.</AuthHeading>
        <AuthLead>
          Use the sign-in page to request a new sign-in link for your existing
          account.
        </AuthLead>
        <div className="mt-6">
          <AuthLinkButton href="/signin">Sign in</AuthLinkButton>
        </div>
      </AuthShell>
    );
  }

  // Default: invalid / expired / missing / unknown reason
  const signinHint = isSigninContext(context);
  return (
    <AuthShell>
      <AuthHeading>This sign-in link is no longer valid.</AuthHeading>
      <AuthLead>
        For your security, sign-in links can only be used once or may expire.
        Please request a new sign-in link to continue.
      </AuthLead>
      <div className="mt-6">
        <AuthLinkButton href={signinHint ? "/signin" : "/start"}>
          {signinHint ? "Sign in" : "Request a new sign-in link"}
        </AuthLinkButton>
      </div>
    </AuthShell>
  );
}
