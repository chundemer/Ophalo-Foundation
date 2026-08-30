import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  api,
  ApiError,
  type ActualWorkFinancialDetailResult,
  type ActualWorkFinancialResolutionBody,
  type ActualWorkSubmittedVisitEntry,
} from "../../lib/apiClient";

export type ActualWorkFinancialReviewState =
  | { status: "loading" }
  | { status: "hidden" }
  | { status: "error" }
  | { status: "loaded"; visits: ActualWorkFinancialDetailResult[] };

/** One outcome family for all three office financial mutations (review, line resolution, no-charge
 * disposition) so a caller can render a truthful notice without inferring intent from a bare flag.
 * `validation-failure` carries the stable error code so a form can focus the first errored field;
 * `reconciled` means the authoritative visit has been re-read (stale version, already reviewed,
 * component already valid, not found); the two `review-blocked-*` variants are review-only. */
export type FinancialReviewOutcome =
  | { kind: "success" }
  | { kind: "validation-failure"; code: string | undefined }
  | { kind: "reconciled"; code: string | undefined }
  | { kind: "review-blocked-incomplete" }
  | { kind: "review-blocked-zero-line" }
  | { kind: "replaced"; successorActualWorkId: string }
  | { kind: "replace-blocked-open-draft" }
  | { kind: "hidden" };

const REVIEW_BLOCKED_INCOMPLETE = "ActualWork.ReviewBlockedIncompleteFinancials";
const REVIEW_BLOCKED_ZERO_LINE = "ActualWork.ReviewBlockedZeroLineDispositionRequired";
const DRAFT_ALREADY_OPEN = "ActualWork.DraftAlreadyOpenForRequest";

/** Owner/Admin-only financial read and review mutation for the submitted visits on one request.
 * A 403 deliberately degrades to no UI, preserving the price-blind field-work surface. Mutations
 * are serialized per visit and expose `mutatingVisitIds` so the card and its inline forms can
 * disable every control for a visit while any mutation or its authoritative reload is in flight. */
export function useActualWorkFinancialReview(submittedVisits: ActualWorkSubmittedVisitEntry[]) {
  const visitIds = useMemo(() => submittedVisits.map((visit) => visit.id), [submittedVisits]);
  const visitKey = visitIds.join(",");
  const [state, setState] = useState<ActualWorkFinancialReviewState>({ status: "loading" });
  const [mutatingVisitIds, setMutatingVisitIds] = useState<ReadonlySet<string>>(new Set());
  // Per-visit promise chain: a second mutation for the same visit waits for the first (and its
  // reload) to settle. The card also disables its controls via mutatingVisitIds; this is the
  // backstop for a programmatic or racing caller.
  const chains = useRef<Map<string, Promise<unknown>>>(new Map());

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

  const mapMutationError = useCallback(async (error: unknown): Promise<FinancialReviewOutcome> => {
    if (!(error instanceof ApiError)) return { kind: "validation-failure", code: undefined };
    if (error.status === 403) {
      setState({ status: "hidden" });
      return { kind: "hidden" };
    }
    if (error.status === 400) return { kind: "validation-failure", code: error.code };
    if (error.status === 409 && error.code === REVIEW_BLOCKED_INCOMPLETE) {
      await reload();
      return { kind: "review-blocked-incomplete" };
    }
    if (error.status === 409 && error.code === REVIEW_BLOCKED_ZERO_LINE) {
      await reload();
      return { kind: "review-blocked-zero-line" };
    }
    if (error.status === 409 || error.status === 404) {
      // Stale version, already reviewed, component already valid, line/visit not found — the
      // authoritative read reconciles the card and clears any stale inline form state.
      await reload();
      return { kind: "reconciled", code: error.code };
    }
    return { kind: "validation-failure", code: error.code };
  }, [reload]);

  const runExclusive = useCallback(
    (visitId: string, op: () => Promise<FinancialReviewOutcome>): Promise<FinancialReviewOutcome> => {
      const prior = chains.current.get(visitId) ?? Promise.resolve();
      const run = prior.catch(() => undefined).then(async () => {
        setMutatingVisitIds((current) => {
          const next = new Set(current);
          next.add(visitId);
          return next;
        });
        try {
          return await op();
        } finally {
          setMutatingVisitIds((current) => {
            const next = new Set(current);
            next.delete(visitId);
            return next;
          });
        }
      });
      chains.current.set(visitId, run);
      return run;
    },
    [],
  );

  const review = useCallback(
    (visit: ActualWorkFinancialDetailResult, reviewNote: string | null) =>
      runExclusive(visit.id, async () => {
        try {
          await api.reviewActualWork(visit.id, { reviewNote }, visit.concurrencyVersion);
          await reload();
          return { kind: "success" as const };
        } catch (error) {
          return mapMutationError(error);
        }
      }),
    [runExclusive, reload, mapMutationError],
  );

  const resolveLine = useCallback(
    (visit: ActualWorkFinancialDetailResult, lineId: string, body: ActualWorkFinancialResolutionBody) =>
      runExclusive(visit.id, async () => {
        try {
          await api.createActualWorkFinancialResolution(visit.id, lineId, body, visit.concurrencyVersion);
          await reload();
          return { kind: "success" as const };
        } catch (error) {
          return mapMutationError(error);
        }
      }),
    [runExclusive, reload, mapMutationError],
  );

  const recordNoChargeDisposition = useCallback(
    (visit: ActualWorkFinancialDetailResult, reason: string) =>
      runExclusive(visit.id, async () => {
        try {
          await api.recordActualWorkFinancialDisposition(
            visit.id,
            { kind: "NoCharge", reason },
            visit.concurrencyVersion,
          );
          await reload();
          return { kind: "success" as const };
        } catch (error) {
          return mapMutationError(error);
        }
      }),
    [runExclusive, reload, mapMutationError],
  );

  // ADR-494 D6 (BL136 4e-iii): Owner/Admin replacement-copy correction. On success the source is
  // superseded and a successor Draft exists — the caller routes to it and refreshes history (which
  // drops the now-superseded source from this hook's input on the next render). An open Draft on the
  // request blocks replacement without changing any financial state, so that 409 is its own outcome
  // rather than a reconcile-and-reload.
  const replace = useCallback(
    (visit: ActualWorkFinancialDetailResult, reason: string) =>
      runExclusive(visit.id, async (): Promise<FinancialReviewOutcome> => {
        try {
          const result = await api.replaceActualWork(visit.id, { reason }, visit.concurrencyVersion);
          return { kind: "replaced", successorActualWorkId: result.successorActualWorkId };
        } catch (error) {
          if (error instanceof ApiError && error.status === 409 && error.code === DRAFT_ALREADY_OPEN) {
            return { kind: "replace-blocked-open-draft" };
          }
          return mapMutationError(error);
        }
      }),
    [runExclusive, mapMutationError],
  );

  const isVisitMutating = useCallback(
    (visitId: string) => mutatingVisitIds.has(visitId),
    [mutatingVisitIds],
  );

  return {
    state,
    retry: reload,
    review,
    resolveLine,
    recordNoChargeDisposition,
    replace,
    mutatingVisitIds,
    isVisitMutating,
  };
}
