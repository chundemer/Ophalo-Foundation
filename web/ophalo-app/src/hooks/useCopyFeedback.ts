import { useCallback, useEffect, useRef, useState } from "react";

const DEFAULT_RESET_DELAY_MS = 2000;

/**
 * Shared clipboard-copy feedback: tracks which id last succeeded/failed, resets after
 * `resetDelayMs`, and replaces/cleans its own timer on reuse and on unmount (GAP-030).
 * Callers keep their own success copy per id; `failedId` lets them render a non-disruptive
 * failure state without a silently-swallowed clipboard rejection.
 */
export function useCopyFeedback(resetDelayMs: number = DEFAULT_RESET_DELAY_MS) {
  const [copiedId, setCopiedId] = useState<string | null>(null);
  const [failedId, setFailedId] = useState<string | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const isMountedRef = useRef(true);

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, []);

  const copy = useCallback(
    async (text: string, id: string): Promise<boolean> => {
      if (timerRef.current) clearTimeout(timerRef.current);
      let succeeded: boolean;
      try {
        await navigator.clipboard.writeText(text);
        succeeded = true;
      } catch {
        succeeded = false;
      }

      // The clipboard promise may settle after the caller has unmounted (e.g. a
      // navigation away mid-copy); skip all state/timer work in that case.
      if (!isMountedRef.current) return succeeded;

      if (succeeded) {
        setFailedId(null);
        setCopiedId(id);
        timerRef.current = setTimeout(() => {
          if (isMountedRef.current) setCopiedId(null);
        }, resetDelayMs);
      } else {
        setCopiedId(null);
        setFailedId(id);
        timerRef.current = setTimeout(() => {
          if (isMountedRef.current) setFailedId(null);
        }, resetDelayMs);
      }
      return succeeded;
    },
    [resetDelayMs],
  );

  return { copiedId, failedId, copy };
}
