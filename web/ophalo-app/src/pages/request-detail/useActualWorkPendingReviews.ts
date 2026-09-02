import { useCallback, useEffect, useState } from "react";
import {
  api,
  ApiError,
  type ActualWorkRequestPendingReviewEntry,
} from "../../lib/apiClient";

/**
 * BL138 Slice 1B-client: request-scoped read for the Owner/Admin "Pending financial reviews (N)"
 * card on Request Detail. Mirrors `useActualWorkHistory` — local `useState` + `useEffect(reload)`,
 * its own `reload()` (there is no shared React Query key to hang this on; `RequestDetailContent`
 * coordinates the cross-hook refresh). `enabled` is `canReviewActualWork === true`; a 403 still
 * degrades to `hidden` as a backstop so the price-blind field surface is preserved.
 *
 * The submitted / unreviewed / non-superseded predicate and the three-value `reviewStatus` are
 * server-authoritative — this hook never re-derives either.
 */
export type ActualWorkPendingReviewsState =
  | { status: "loading" }
  | { status: "hidden" }
  | { status: "error" }
  | { status: "loaded"; count: number; items: ActualWorkRequestPendingReviewEntry[] };

export function useActualWorkPendingReviews(requestId: string, enabled: boolean) {
  const [state, setState] = useState<ActualWorkPendingReviewsState>(
    enabled ? { status: "loading" } : { status: "hidden" },
  );

  const reload = useCallback(async () => {
    if (!enabled) {
      setState({ status: "hidden" });
      return;
    }
    setState({ status: "loading" });
    try {
      const result = await api.getActualWorkPendingReviewsForRequest(requestId);
      setState({ status: "loaded", count: result.count, items: result.items });
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        setState({ status: "hidden" });
        return;
      }
      setState({ status: "error" });
    }
  }, [requestId, enabled]);

  useEffect(() => {
    void reload();
  }, [reload]);

  return { state, reload };
}
