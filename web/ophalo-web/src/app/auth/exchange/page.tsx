import { redirect } from "next/navigation";
import ExchangeClient from "./ExchangeClient";
import MobileExchangeClient from "./MobileExchangeClient";

export default async function ExchangePage({
  searchParams,
}: {
  searchParams: Promise<{ code?: string; from?: string }>;
}) {
  const { code, from } = await searchParams;
  const trimmedCode = code?.trim();

  if (!trimmedCode) {
    redirect("/auth/exchange/error?reason=invalid");
  }

  if (from === "mobile") {
    return <MobileExchangeClient code={trimmedCode} />;
  }

  return <ExchangeClient code={trimmedCode} />;
}
