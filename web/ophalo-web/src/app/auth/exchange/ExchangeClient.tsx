"use client";

import { useEffect, useRef, useState } from "react";
import { AuthShell, AuthLead } from "@/components/auth/AuthShell";
import {
  CompleteSignInScreen,
  type ContinuationWorkspace,
} from "@/components/auth/CompleteSignInScreen";

type ContinuationState = {
  requiresName: boolean;
  workspaces: ContinuationWorkspace[] | null;
};

export default function ExchangeClient({ code }: { code: string }) {
  const hasExchanged = useRef(false);
  const [continuation, setContinuation] = useState<ContinuationState | null>(null);

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
        const body = await res.json().catch(() => null) as {
          requiresContinuation?: boolean;
          requiresName?: boolean;
          workspaces?: ContinuationWorkspace[] | null;
        } | null;

        if (body?.requiresContinuation) {
          setContinuation({
            requiresName: !!body.requiresName,
            workspaces: body.workspaces ?? null,
          });
          return;
        }

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
          if (errorCode === "Account.EmailAlreadyInUse") {
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

  if (continuation) {
    return (
      <AuthShell>
        <CompleteSignInScreen
          requiresName={continuation.requiresName}
          workspaces={continuation.workspaces}
          errorBasePath="/auth/exchange/error"
        />
      </AuthShell>
    );
  }

  return (
    <AuthShell>
      <AuthLead>Signing you in&hellip;</AuthLead>
    </AuthShell>
  );
}
