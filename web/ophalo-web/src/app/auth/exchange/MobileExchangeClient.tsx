"use client";

import { useEffect, useRef, useState } from "react";
import { AuthShell, AuthHeading, AuthLead, AuthNote } from "@/components/auth/AuthShell";

const deepLinkButtonClass =
  "block w-full rounded-lg bg-ophalo-navy px-5 py-3 text-center text-sm font-semibold text-white " +
  "transition hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 " +
  "focus-visible:ring-keep-accent focus-visible:ring-offset-2";

type Phase = "exchanging" | "ready" | "error";

type HandoffResponse = {
  handoffCode: string;
  expiresAtUtc: string;
};

export default function MobileExchangeClient({ code }: { code: string }) {
  const hasExchanged = useRef(false);
  const [phase, setPhase] = useState<Phase>("exchanging");
  const [handoffCode, setHandoffCode] = useState<string | null>(null);

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
            credentials: "omit",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ code, clientType: "mobile_app" }),
          },
        );
      } catch {
        setPhase("error");
        return;
      }

      if (res.ok) {
        try {
          const body = (await res.json()) as HandoffResponse;
          if (body?.handoffCode) {
            setHandoffCode(body.handoffCode);
            setPhase("ready");
            return;
          }
        } catch {
          // fall through to error
        }
        setPhase("error");
        return;
      }

      setPhase("error");
    }

    exchange();
  }, [code]);

  if (phase === "exchanging") {
    return (
      <AuthShell bare>
        <AuthLead>Authorizing device&hellip;</AuthLead>
      </AuthShell>
    );
  }

  if (phase === "error") {
    return (
      <AuthShell bare>
        <AuthLead>This link is invalid, expired, or has already been used.</AuthLead>
        <AuthNote>Open the app and request a new sign-in link.</AuthNote>
      </AuthShell>
    );
  }

  const deepLink = `ophalo://auth/callback?code=${encodeURIComponent(handoffCode!)}`;

  return (
    <AuthShell bare>
      <AuthHeading>Device Authorized</AuthHeading>
      <AuthLead>Tap the button below to open OpHalo Keep.</AuthLead>
      <div className="mt-6">
        <a href={deepLink} className={deepLinkButtonClass}>
          Open Keep Mobile App
        </a>
      </div>
      <AuthNote>If nothing happens, open the app and sign in again.</AuthNote>
    </AuthShell>
  );
}
