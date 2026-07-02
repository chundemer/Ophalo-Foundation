import { redirect } from "next/navigation";
import AcceptClient from "./AcceptClient";

export default async function InviteAcceptPage({
  searchParams,
}: {
  searchParams: Promise<{ token?: string }>;
}) {
  const { token } = await searchParams;
  const trimmedToken = token?.trim();

  if (!trimmedToken) {
    redirect("/invite/accept/error?reason=missing_token");
  }

  return <AcceptClient token={trimmedToken} />;
}
