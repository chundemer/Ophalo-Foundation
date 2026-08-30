import { useCallback } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type ActualWorkSubmittedVisitEntry } from "../../lib/apiClient";
import { useActualWorkCapture } from "./useActualWorkCapture";
import { useActualWorkHistory } from "./useActualWorkHistory";
import { useActualWorkFinancialReview } from "./useActualWorkFinancialReview";

/**
 * BL136 4f-i (D7): composition hook for the dedicated Actual Work Ticket Workspace route. It owns
 * one instance each of the existing capture and history hooks for the request, plus the request
 * detail read the workspace header needs (customer / reference / status), and a lookup for the
 * read-only submitted-visit view. The editable path is still `ActualWorkComposer` driven by
 * `useActualWorkCapture`, unchanged and price-blind.
 *
 * BL136 4f-ii: when `canReviewActualWork` is set (Owner/Admin) and `reviewVisitId` names the
 * submitted visit currently on the read-only view, it also owns one `useActualWorkFinancialReview`
 * instance scoped to that single visit — the capability-gated office region reuses the existing
 * `ActualWorkReviewCard` against it. A superseded source is inert for financial review (its detail
 * read returns 409), so it is excluded even though `submittedVisit` stays unfiltered for lineage.
 */
export function useActualWorkWorkspace(
  requestId: string,
  currentAccountUserId?: string,
  canReviewActualWork = false,
  reviewVisitId?: string,
) {
  const capture = useActualWorkCapture(requestId, currentAccountUserId);
  const history = useActualWorkHistory(requestId);
  const financialReview = useActualWorkFinancialReview(
    canReviewActualWork && reviewVisitId != null && history.state.status === "loaded"
      ? history.state.submittedVisits.filter((visit) => visit.id === reviewVisitId && !visit.superseded)
      : [],
  );
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

  return { capture, history, requestQuery, submittedVisit, financialReview };
}
