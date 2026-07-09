import type { Metadata } from "next";
import IntakeForm from "../../intake/[token]/IntakeForm";

export const metadata: Metadata = {
  title: "Submit a Request",
  robots: { index: false, follow: false },
  other: { referrer: "no-referrer" },
};

export default async function KeepSlugIntakePage({
  params,
}: {
  params: Promise<{ slug: string }>;
}) {
  const { slug } = await params;
  return <IntakeForm slug={slug} />;
}
