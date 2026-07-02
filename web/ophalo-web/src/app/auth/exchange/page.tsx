import { redirect } from "next/navigation";
import ExchangeClient from "./ExchangeClient";

export default async function ExchangePage({
  searchParams,
}: {
  searchParams: Promise<{ code?: string }>;
}) {
  const { code } = await searchParams;
  const trimmedCode = code?.trim();

  if (!trimmedCode) {
    redirect("/auth/exchange/error?reason=invalid");
  }

  return <ExchangeClient code={trimmedCode} />;
}
