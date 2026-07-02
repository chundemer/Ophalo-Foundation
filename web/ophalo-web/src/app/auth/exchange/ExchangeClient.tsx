"use client";

import { useEffect, useRef } from "react";

export default function ExchangeClient({ code }: { code: string }) {
  const hasExchanged = useRef(false);

  useEffect(() => {
    if (hasExchanged.current) return;
    hasExchanged.current = true;

    async function exchange() {
      let res: Response;
      try {
        res = await fetch(
          `${process.env.NEXT_PUBLIC_API_BASE_URL}/auth/exchange`,
          {
            method: "POST",
            credentials: "include",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ code, clientType: "browser" }),
          },
        );
      } catch {
        window.location.assign(
          "/auth/exchange/error?reason=service_unavailable",
        );
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
          const body = (await res.json()) as { entryContext?: string };
          const params = new URLSearchParams({ reason: "invalid" });
          if (body?.entryContext) params.set("context", body.entryContext);
          window.location.assign(`/auth/exchange/error?${params.toString()}`);
          return;
        } catch {
          // fall through to invalid
        }
      }

      if (res.status === 409) {
        try {
          const body = (await res.json()) as {
            extensions?: { code?: string };
            code?: string;
          };
          const errorCode = body?.extensions?.code ?? body?.code;
          if (errorCode === "Account.PilotFull") {
            window.location.assign("/auth/exchange/error?reason=pilot_full");
            return;
          }
          if (errorCode === "Account.NewAccountEmailAlreadyRegistered") {
            window.location.assign(
              "/auth/exchange/error?reason=account_already_exists",
            );
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
              "/auth/exchange/error?reason=session_creation_failed",
            );
            return;
          }
        } catch {
          // fall through to service_unavailable
        }
      }

      if (res.status >= 400 && res.status < 500) {
        window.location.assign("/auth/exchange/error?reason=invalid");
        return;
      }

      window.location.assign(
        "/auth/exchange/error?reason=service_unavailable",
      );
    }

    exchange();
  }, [code]);

  return (
    <div className="auth-page">
      <div className="container">
        <p>Signing you in&hellip;</p>
      </div>
    </div>
  );
}
