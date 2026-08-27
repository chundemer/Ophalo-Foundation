import { useCallback, useEffect, useMemo, useState } from "react";
import {
  api,
  ApiError,
  type ActualWorkFinancialDetailResult,
  type ActualWorkSubmittedVisitEntry,
} from "../../lib/apiClient";

export type ActualWorkFinancialReviewState =
  | { status: "loading" }
  | { status: "hidden" }
  | { status: "error" }
  | { status: "loaded"; visits: ActualWorkFinancialDetailResult[] };

/** Owner/Admin-only financial read and review mutation for the submitted visits on one request.
 * A 403 deliberately degrades to no UI, preserving the price-blind field-work surface. */
export function useActualWorkFinancialReview(submittedVisits: ActualWorkSubmittedVisitEntry[]) {
  const visitIds = useMemo(() => submittedVisits.map((visit) => visit.id), [submittedVisits]);
  const visitKey = visitIds.join(",");
  const [state, setState] = useState<ActualWorkFinancialReviewState>({ status: "loading" });

  const reload = useCallback(async () => {
    if (visitIds.length === 0) {
      setState({ status: "loaded", visits: [] });
      return;
    }
    setState({ status: "loading" });
    try {
      const visits = await Promise.all(visitIds.map((id) => api.getActualWorkFinancialDetail(id)));
      setState({ status: "loaded", visits });
    } catch (error) {
      if (error instanceof ApiError && error.status === 403) {
        setState({ status: "hidden" });
        return;
      }
      setState({ status: "error" });
    }
  // visitKey is the stable value relevant to a history refresh; visitIds is rebuilt each render.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visitKey]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const review = useCallback(async (visit: ActualWorkFinancialDetailResult, reviewNote: string | null) => {
    try {
      await api.reviewActualWork(visit.id, { reviewNote }, visit.concurrencyVersion);
      await reload();
      return { ok: true as const };
    } catch (error) {
      // Both an already-reviewed visit and a stale concurrency version are reconciled from the
      // authoritative read. The caller can then show the up-to-date, read-only visit.
      if (error instanceof ApiError && error.status === 409) await reload();
      return { ok: false as const, conflict: error instanceof ApiError && error.status === 409 };
    }
  }, [reload]);

  return { state, retry: reload, review };
}
