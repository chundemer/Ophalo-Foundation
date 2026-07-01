import Link from "next/link";

export default async function CheckEmailPage({
  searchParams,
}: {
  searchParams: Promise<{ flow?: string }>;
}) {
  const { flow } = await searchParams;
  const isDev = process.env.NODE_ENV === "development";

  return (
    <div className="auth-page">
      <div className="container">
        <h1>Check your email.</h1>

        {flow === "start" ? (
          <>
            <p>We sent you a sign-in link. Click it to finish setting up Keep.</p>
            <p className="auth-note">The link expires in 24 hours.</p>
          </>
        ) : (
          <p>
            If this email has access to Keep, we&rsquo;ll send you a sign-in
            link. Check your inbox &mdash; if nothing arrives in a few minutes,
            double-check your email or{" "}
            <Link href="/start">get started</Link> if you&rsquo;re new.
          </p>
        )}

        {isDev && (
          <p className="auth-dev-hint">
            Local dev: magic-link URLs are printed to the API console by{" "}
            <code>ConsoleEmailSender</code>.
          </p>
        )}

        <p className="auth-note">
          <Link href="/">Back to OpHalo</Link>
          {" · "}
          <Link href="/signin">Sign in</Link>
          {" · "}
          <Link href="/start">Get started</Link>
        </p>
      </div>
    </div>
  );
}
