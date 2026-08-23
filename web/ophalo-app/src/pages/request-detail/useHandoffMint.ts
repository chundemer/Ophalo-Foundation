import { useCallback, useEffect, useRef, useState } from "react";

interface HandoffMintState {
  handoffUrl: string | null;
  isLoading: boolean;
  error: string | null;
}

const IDLE: HandoffMintState = { handoffUrl: null, isLoading: false, error: null };

// GAP-020 / ADR-448: shared mint/loading/error/retry state machine behind every opaque handoff
// QR (call, SMS). `mint` identity changing re-triggers the effect, so callers wrap it in
// useCallback with whatever deps should force a re-mint.
export function useHandoffMint(
  enabled: boolean,
  mint: () => Promise<{ handoffUrl: string }>,
  errorMessage: string,
) {
  const [state, setState] = useState<HandoffMintState>(IDLE);
  // Guards against a stale mint updating state after unmount, after a newer mint has started, or
  // when overlapping attempts resolve out of order — only the latest generation may write state.
  const generationRef = useRef(0);

  const run = useCallback(async () => {
    const generation = ++generationRef.current;
    setState({ handoffUrl: null, isLoading: true, error: null });
    try {
      const result = await mint();
      if (generationRef.current === generation) {
        setState({ handoffUrl: result.handoffUrl, isLoading: false, error: null });
      }
    } catch {
      if (generationRef.current === generation) {
        setState({ handoffUrl: null, isLoading: false, error: errorMessage });
      }
    }
  }, [mint, errorMessage]);

  useEffect(() => {
    if (enabled) void run();
    else {
      generationRef.current++;
      setState(IDLE);
    }
    return () => {
      generationRef.current++;
    };
  }, [enabled, run]);

  return { ...state, retry: run };
}
