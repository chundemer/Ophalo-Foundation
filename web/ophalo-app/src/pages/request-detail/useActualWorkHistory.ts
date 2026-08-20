import { useCallback, useEffect, useState } from "react";
import { api, ApiError, type ActualWorkSubmittedVisitEntry } from "../../lib/apiClient";

/**
 * Batch 5c, build-log/129: standalone read of submitted Actual Work visit history, independent of
 * `useActualWorkCapture` — the capture hook discards `submittedVisits` under `status: "hidden"`
 * (it only serves the active Responsible recorder), but 5a's authorization correction makes
 * submitted history visible to any normally request-visible caller (e.g. a Viewer). This hook
 * fetches the same endpoint on its own so the history card renders for that broader audience.
 */
export type ActualWorkHistoryState =
  | { status: "loading" }
  | { status: "hidden" }
  | { status: "error" }
  | { status: "loaded"; submittedVisits: ActualWorkSubmittedVisitEntry[] };

export function useActualWorkHistory(requestId: string) {
  const [state, setState] = useState<ActualWorkHistoryState>({ status: "loading" });

  const probe = useCallback(async () => {
    setState({ status: "loading" });
    try {
      const result = await api.getActualWorkHistoryForRequest(requestId);
      setState({ status: "loaded", submittedVisits: result.submittedVisits });
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        setState({ status: "hidden" });
        return;
      }
      setState({ status: "error" });
    }
  }, [requestId]);

  useEffect(() => {
    void probe();
  }, [probe]);

  return { state, retry: probe };
}
