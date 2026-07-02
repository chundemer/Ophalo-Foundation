"use client";

import { useEffect, useRef } from "react";

export default function AcceptClient({ token }: { token: string }) {
  const hasAccepted = useRef(false);

  useEffect(() => {
    if (hasAccepted.current) return;
    hasAccepted.current = true;

    async function accept() {
      let res: Response;
      try {
        res = await fetch(
          `${process.env.NEXT_PUBLIC_API_BASE_URL}/accounts/invite/accept`,
          {
            method: "POST",
            credentials: "include",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ token }),
          },
        );
      } catch {
        window.location.assign("/invite/accept/error?reason=service_unavailable");
        return;
      }

      if (res.ok) {
        window.location.assign(
          process.env.NEXT_PUBLIC_APP_BASE_URL ?? "http://localhost:5173",
        );
        return;
      }

      if (res.status === 422) {
        try {
          const body = (await res.json()) as {
            extensions?: { code?: string };
            code?: string;
          };
          const errorCode = body?.extensions?.code ?? body?.code;
          if (errorCode === "Invite.Expired") {
            window.location.assign("/invite/accept/error?reason=expired");
            return;
          }
        } catch {
          // fall through to invalid
        }
        window.location.assign("/invite/accept/error?reason=invalid");
        return;
      }

      if (res.status === 409) {
        try {
          const body = (await res.json()) as {
            extensions?: { code?: string };
            code?: string;
          };
          const errorCode = body?.extensions?.code ?? body?.code;
          if (errorCode === "Invite.AlreadyActive") {
            window.location.assign("/invite/accept/error?reason=already_active");
            return;
          }
          if (errorCode === "Invite.SeatLimitReached") {
            window.location.assign("/invite/accept/error?reason=seat_limit");
            return;
          }
        } catch {
          // fall through to service_unavailable
        }
      }

      if (res.status === 503) {
        try {
          const body = (await res.json()) as {
            extensions?: { code?: string };
            code?: string;
          };
          const errorCode = body?.extensions?.code ?? body?.code;
          if (errorCode === "Account.SessionCreationFailed") {
            window.location.assign(
              "/invite/accept/error?reason=session_creation_failed",
            );
            return;
          }
        } catch {
          // fall through to service_unavailable
        }
      }

      if (res.status === 403) {
        window.location.assign("/invite/accept/error?reason=invalid");
        return;
      }

      window.location.assign("/invite/accept/error?reason=service_unavailable");
    }

    accept();
  }, [token]);

  return (
    <div className="auth-page">
      <div className="container">
        <p>Accepting your invite&hellip;</p>
      </div>
    </div>
  );
}
