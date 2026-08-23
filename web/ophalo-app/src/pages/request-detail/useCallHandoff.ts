import { useCallback } from "react";
import { api } from "../../lib/apiClient";
import { useHandoffMint } from "./useHandoffMint";

// GAP-020 / ADR-448: mints an opaque, short-lived call-handoff URL for the given request rather
// than exposing the raw customer phone number to a QR payload.
export function useCallHandoff(requestId: string, enabled: boolean) {
  const mint = useCallback(() => api.createCallHandoff(requestId), [requestId]);
  return useHandoffMint(enabled, mint, "Could not create call link. Try again.");
}
