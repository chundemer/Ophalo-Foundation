import type { Metadata } from "next";
import Link from "next/link";
import {
  AuthShell,
  AuthHeading,
  AuthLead,
  AuthNote,
} from "@/components/auth/AuthShell";

export const metadata: Metadata = {
  title: "Check your email — OpHalo Keep",
};

export default async function CheckEmailPage({
  searchParams,
}: {
  searchParams: Promise<{ flow?: string }>;
}) {
  const { flow } = await searchParams;
  const isDev = process.env.NODE_ENV === "development";

  return (
    <AuthShell>
      <AuthHeading>Check your email.</AuthHeading>

      {flow === "start" ? (
        <>
          <AuthLead>We sent you a sign-in link. Click it to finish setting up Keep.</AuthLead>
          <AuthNote>The link expires in 24 hours.</AuthNote>
        </>
      ) : (
        <AuthLead>
          If this email has access to Keep, we&rsquo;ll send you a sign-in
          link. Check your inbox &mdash; if nothing arrives in a few minutes,
          double-check your email or{" "}
          <Link href="/start" className="underline underline-offset-2">get started</Link> if you&rsquo;re
          new.
        </AuthLead>
      )}

      {isDev && (
        <AuthNote>
          Local dev: magic-link URLs are printed to the API console by{" "}
          <code>ConsoleEmailSender</code>.
        </AuthNote>
      )}

      <AuthNote>
        <Link href="/" className="underline underline-offset-2">Back to OpHalo</Link>
        {" · "}
        <Link href="/signin" className="underline underline-offset-2">Sign in</Link>
        {" · "}
        <Link href="/start" className="underline underline-offset-2">Get started</Link>
      </AuthNote>
    </AuthShell>
  );
}
