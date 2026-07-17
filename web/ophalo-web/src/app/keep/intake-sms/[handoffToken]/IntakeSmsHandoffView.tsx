"use client";

import { useEffect, useRef, useState } from "react";
import { KeepButton } from "@/components/keep/KeepButton";
import { KeepPageFooter } from "@/components/keep/KeepPublicShell";

export function IntakeSmsHandoffView({
  customerPhone,
  messageBody,
}: {
  customerPhone: string;
  messageBody: string;
}) {
  const [smsUri, setSmsUri] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const launched = useRef(false);

  useEffect(() => {
    // Compute URI on client to avoid SSR/hydration mismatch on iOS vs Android separator
    const isIos = /iphone|ipad|ipod/i.test(navigator.userAgent.toLowerCase());
    const sep = isIos ? "&" : "?";
    const uri = `sms:${customerPhone}${sep}body=${encodeURIComponent(messageBody)}`;
    setSmsUri(uri);

    if (!launched.current) {
      launched.current = true;
      window.location.href = uri;
    }
  }, [customerPhone, messageBody]);

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(messageBody);
      setCopied(true);
      setTimeout(() => setCopied(false), 2500);
    } catch {
      // clipboard unavailable — silent fail, button stays available
    }
  }

  return (
    <main
      className="flex min-h-screen flex-col px-5 py-8"
      style={{ backgroundColor: "var(--ophalo-canvas)" }}
    >
      <div className="mx-auto w-full max-w-sm flex-1">
        <h1 className="text-xl font-bold leading-tight tracking-tight text-foreground">
          Open or copy your text message to send.
        </h1>
        <p className="mt-2 text-sm leading-5 text-muted-foreground">
          Tap{" "}
          <span className="font-medium text-foreground">Open Text Message</span>{" "}
          to launch your SMS app with the message pre-filled, or copy it to
          paste yourself.
        </p>

        <div
          className="mt-5 rounded-xl border border-[var(--ophalo-border)] bg-card px-4 py-4 text-sm leading-6 text-foreground"
          aria-label="Message preview"
        >
          {messageBody}
        </div>

        <div className="mt-6 flex flex-col gap-3">
          {smsUri ? (
            <a href={smsUri} className="block w-full">
              <KeepButton variant="teal" className="w-full">
                Open Text Message
              </KeepButton>
            </a>
          ) : (
            <KeepButton variant="teal" className="w-full" disabled>
              Open Text Message
            </KeepButton>
          )}

          <KeepButton
            variant="primary"
            className="w-full"
            onClick={handleCopy}
          >
            {copied ? "Copied!" : "Copy Message"}
          </KeepButton>
        </div>
      </div>

      <KeepPageFooter className="mt-8" />
    </main>
  );
}
