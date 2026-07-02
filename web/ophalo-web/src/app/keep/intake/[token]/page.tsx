import type { Metadata } from "next";
import IntakeForm from "./IntakeForm";

export const metadata: Metadata = {
  title: "Submit a Request",
  robots: { index: false, follow: false },
  other: { referrer: "no-referrer" },
};

export default async function KeepIntakePage({
  params,
}: {
  params: Promise<{ token: string }>;
}) {
  const { token } = await params;
  return <IntakeForm token={token} />;
}
