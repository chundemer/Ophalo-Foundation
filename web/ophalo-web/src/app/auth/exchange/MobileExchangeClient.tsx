"use client";

import { useEffect, useRef, useState } from "react";

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
      <div className="auth-page">
        <div className="container">
          <p>Authorizing device&hellip;</p>
        </div>
      </div>
    );
  }

  if (phase === "error") {
    return (
      <div className="auth-page">
        <div className="container">
          <p>This link is invalid, expired, or has already been used.</p>
          <p>Open the app and request a new sign-in link.</p>
        </div>
      </div>
    );
  }

  const deepLink = `ophalo://auth/callback?code=${encodeURIComponent(handoffCode!)}`;

  return (
    <div className="auth-page">
      <div className="container">
        <h1>Device Authorized</h1>
        <p>Tap the button below to open OpHalo Keep.</p>
        <a href={deepLink}>Open Keep Mobile App</a>
        <p>If nothing happens, open the app and sign in again.</p>
      </div>
    </div>
  );
}
