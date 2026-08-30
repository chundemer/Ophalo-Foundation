import { useCallback } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type ActualWorkSubmittedVisitEntry } from "../../lib/apiClient";
import { useActualWorkCapture } from "./useActualWorkCapture";
import { useActualWorkHistory } from "./useActualWorkHistory";

/**
 * BL136 4f-i (D7): composition hook for the dedicated Actual Work Ticket Workspace route. It owns
 * one instance each of the existing capture and history hooks for the request, plus the request
 * detail read the workspace header needs (customer / reference / status), and a lookup for the
 * read-only submitted-visit view. It adds no mutation surface of its own — the editable path is
 * still `ActualWorkComposer` driven by `useActualWorkCapture`, unchanged and price-blind.
 */
export function useActualWorkWorkspace(requestId: string, currentAccountUserId?: string) {
  const capture = useActualWorkCapture(requestId, currentAccountUserId);
  const history = useActualWorkHistory(requestId);
  // Same query key as RequestDetail's own read, so navigating between the two reuses the cache.
  const requestQuery = useQuery({
    queryKey: ["request-detail", requestId],
    queryFn: () => api.getRequestDetail(requestId),
  });

  const submittedVisit = useCallback(
    (visitId: string): ActualWorkSubmittedVisitEntry | null =>
      history.state.status === "loaded"
        ? history.state.submittedVisits.find((v) => v.id === visitId) ?? null
        : null,
    [history.state],
  );

  return { capture, history, requestQuery, submittedVisit };
}
