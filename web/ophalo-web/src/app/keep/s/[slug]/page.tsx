import type { Metadata } from "next";
import IntakeForm from "../../intake/[token]/IntakeForm";

const baseMetadata: Metadata = {
  robots: { index: false, follow: false },
  other: { referrer: "no-referrer" },
};

interface PublicIntakeIdentity {
  businessName: string;
  logoUrl: string | null;
  websiteUrl: string | null;
  phone: string | null;
}

async function fetchIdentity(slug: string): Promise<PublicIntakeIdentity | null> {
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
    const d = data as Record<string, unknown>;
    const name = d.businessName;
    if (typeof name !== "string" || name.length === 0) return null;
    return {
      businessName: name,
      logoUrl: typeof d.logoUrl === "string" ? d.logoUrl : null,
      websiteUrl: typeof d.websiteUrl === "string" ? d.websiteUrl : null,
      phone: typeof d.phone === "string" ? d.phone : null,
    };
  } catch {
    return null;
  }
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ slug: string }>;
}): Promise<Metadata> {
  const { slug } = await params;
  const identity = await fetchIdentity(slug);
  return {
    ...baseMetadata,
    title: identity ? `${identity.businessName} — Submit a Request` : "Submit a Request",
  };
}

export default async function KeepSlugIntakePage({
  params,
}: {
  params: Promise<{ slug: string }>;
}) {
  const { slug } = await params;
  const identity = await fetchIdentity(slug);
  return (
    <IntakeForm
      slug={slug}
      businessName={identity?.businessName ?? null}
      logoUrl={identity?.logoUrl ?? null}
      websiteUrl={identity?.websiteUrl ?? null}
      phone={identity?.phone ?? null}
    />
  );
}
