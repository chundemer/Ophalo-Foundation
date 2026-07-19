import type { Metadata } from "next";
import { CallHandoffView } from "./CallHandoffView";
import { KeepPageFooter } from "@/components/keep/KeepPublicShell";

export const metadata: Metadata = {
  title: "Open Call",
  robots: { index: false, follow: false },
  other: { referrer: "no-referrer" },
};

const canvasStyle = { backgroundColor: "var(--ophalo-canvas)" };

type HandoffState =
  | { kind: "valid"; customerPhone: string }
  | { kind: "expired" }
  | { kind: "unavailable" };

async function fetchHandoff(handoffToken: string): Promise<HandoffState> {
  const apiBase = process.env.NEXT_PUBLIC_API_BASE_URL;
  if (!apiBase) return { kind: "unavailable" };

  let res: Response;
  try {
    res = await fetch(
      `${apiBase}/keep/share-call/${encodeURIComponent(handoffToken)}`,
      { cache: "no-store" }
    );
  } catch {
    return { kind: "unavailable" };
  }

  if (res.status === 404) return { kind: "expired" };
  if (!res.ok) return { kind: "unavailable" };

  const data: unknown = await res.json().catch(() => null);
  if (data == null || typeof data !== "object") return { kind: "unavailable" };

  const d = data as Record<string, unknown>;
  const customerPhone = typeof d.customerPhone === "string" ? d.customerPhone : null;

  if (!customerPhone) return { kind: "unavailable" };

  return { kind: "valid", customerPhone };
}

export default async function CallHandoffPage({
  params,
}: {
  params: Promise<{ handoffToken: string }>;
}) {
  const { handoffToken } = await params;
  const state = await fetchHandoff(handoffToken);

  if (state.kind === "expired") {
    return (
      <main className="flex min-h-screen flex-col px-5 py-8" style={canvasStyle}>
        <div className="mx-auto w-full max-w-sm flex-1">
          <h1 className="text-xl font-bold leading-tight tracking-tight text-foreground">
            This call link expired.
          </h1>
          <p className="mt-3 text-sm leading-6 text-muted-foreground">
            Return to OpHalo and create a new call link.
          </p>
        </div>
        <KeepPageFooter className="mt-8" />
      </main>
    );
  }

  if (state.kind === "unavailable") {
    return (
      <main className="flex min-h-screen flex-col px-5 py-8" style={canvasStyle}>
        <div className="mx-auto w-full max-w-sm flex-1">
          <h1 className="text-xl font-bold leading-tight tracking-tight text-foreground">
            This link is not available.
          </h1>
          <p className="mt-3 text-sm leading-6 text-muted-foreground">
            If you received this from OpHalo, the link may have expired or been
            revoked. Return to OpHalo and create a new call link.
          </p>
        </div>
        <KeepPageFooter className="mt-8" />
      </main>
    );
  }

  return <CallHandoffView customerPhone={state.customerPhone} />;
}
