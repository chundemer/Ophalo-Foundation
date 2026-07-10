import type { Metadata } from "next";
import IntakeForm from "../../intake/[token]/IntakeForm";

export const metadata: Metadata = {
  title: "Submit a Request",
  robots: { index: false, follow: false },
  other: { referrer: "no-referrer" },
};

async function fetchBusinessName(slug: string): Promise<string | null> {
  const apiBase = process.env.NEXT_PUBLIC_API_BASE_URL;
  if (!apiBase) return null;
  try {
    const res = await fetch(
      `${apiBase}/keep/public-intake/slug/${encodeURIComponent(slug)}/info`,
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

export default async function KeepSlugIntakePage({
  params,
}: {
  params: Promise<{ slug: string }>;
}) {
  const { slug } = await params;
  const businessName = await fetchBusinessName(slug);
  return <IntakeForm slug={slug} businessName={businessName} />;
}
