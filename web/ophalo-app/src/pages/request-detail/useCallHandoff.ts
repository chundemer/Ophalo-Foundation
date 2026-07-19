import { useCallback, useEffect, useState } from "react";
import { api } from "../../lib/apiClient";

interface CallHandoffState {
  handoffUrl: string | null;
  isLoading: boolean;
  error: string | null;
}

const IDLE: CallHandoffState = { handoffUrl: null, isLoading: false, error: null };

// GAP-020 / ADR-448: mints an opaque, short-lived call-handoff URL for the given request rather
// than exposing the raw customer phone number to a QR payload.
export function useCallHandoff(requestId: string, enabled: boolean) {
  const [state, setState] = useState<CallHandoffState>(IDLE);

  const mint = useCallback(async () => {
    setState({ handoffUrl: null, isLoading: true, error: null });
    try {
      const result = await api.createCallHandoff(requestId);
      setState({ handoffUrl: result.handoffUrl, isLoading: false, error: null });
    } catch {
      setState({ handoffUrl: null, isLoading: false, error: "Could not create call link. Try again." });
    }
  }, [requestId]);

  useEffect(() => {
    if (enabled) void mint();
    else setState(IDLE);
  }, [enabled, mint]);

  return { ...state, retry: mint };
}
