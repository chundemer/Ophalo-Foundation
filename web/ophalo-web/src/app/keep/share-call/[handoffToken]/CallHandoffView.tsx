"use client";

import { useEffect, useRef, useState } from "react";
import { KeepButton } from "@/components/keep/KeepButton";
import { KeepPageFooter } from "@/components/keep/KeepPublicShell";

export function CallHandoffView({ customerPhone }: { customerPhone: string }) {
  const [copied, setCopied] = useState(false);
  const launched = useRef(false);
  const telUri = `tel:${customerPhone.replace(/[^\d+]/g, "")}`;

  useEffect(() => {
    if (!launched.current) {
      launched.current = true;
      window.location.href = telUri;
    }
  }, [telUri]);

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(customerPhone);
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
          Tap to call the customer.
        </h1>
        <p className="mt-2 text-sm leading-5 text-muted-foreground">
          Tap <span className="font-medium text-foreground">Call Customer</span>{" "}
          to launch your phone&apos;s dialer with the number ready, or copy it to
          dial yourself.
        </p>

        <div
          className="mt-5 rounded-xl border border-[var(--ophalo-border)] bg-card px-4 py-4 text-sm leading-6 text-foreground"
          aria-label="Customer phone number"
        >
          {customerPhone}
        </div>

        <div className="mt-6 flex flex-col gap-3">
          <a href={telUri} className="block w-full">
            <KeepButton variant="teal" className="w-full">
              Call Customer
            </KeepButton>
          </a>

          <KeepButton
            variant="primary"
            className="w-full"
            onClick={handleCopy}
          >
            {copied ? "Copied!" : "Copy Number"}
          </KeepButton>
        </div>
      </div>

      <KeepPageFooter className="mt-8" />
    </main>
  );
}
