import type { Metadata } from "next";
import IntakeForm from "./IntakeForm";

export const metadata: Metadata = {
  title: "Submit a Request",
  robots: { index: false, follow: false },
  other: { referrer: "no-referrer" },
};

async function fetchBusinessName(token: string): Promise<string | null> {
  const apiBase = process.env.NEXT_PUBLIC_API_BASE_URL;
  if (!apiBase) return null;
  try {
    const res = await fetch(
      `${apiBase}/keep/public-intake/token/${encodeURIComponent(token)}/info`,
      { cache: "no-store" }
    );
    if (!res.ok) return null;
    const data: unknown = await res.json().catch(() => null);
    if (data == null || typeof data !== "object") return null;
    const name = (data as Record<string, unknown>).businessName;
    return typeof name === "string" && name.length > 0 ? name : null;
  } catch {
    return null;
  }
}

export default async function KeepIntakePage({
  params,
}: {
  params: Promise<{ token: string }>;
}) {
  const { token } = await params;
  const businessName = await fetchBusinessName(token);
  return <IntakeForm token={token} businessName={businessName} />;
}
