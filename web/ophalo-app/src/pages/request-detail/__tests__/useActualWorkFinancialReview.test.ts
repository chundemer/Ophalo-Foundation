import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { ApiError } from "../../../lib/apiClient";
import { useActualWorkFinancialReview } from "../useActualWorkFinancialReview";

const getDetail = vi.fn();
const review = vi.fn();
const resolution = vi.fn();
const disposition = vi.fn();
const replace = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getActualWorkFinancialDetail: (...args: unknown[]) => getDetail(...args),
      reviewActualWork: (...args: unknown[]) => review(...args),
      createActualWorkFinancialResolution: (...args: unknown[]) => resolution(...args),
      recordActualWorkFinancialDisposition: (...args: unknown[]) => disposition(...args),
      replaceActualWork: (...args: unknown[]) => replace(...args),
    },
  };
});

const submitted = [{ id: "visit-1", status: "Submitted", outcome: null, completionNote: null, submittedAtUtc: "2026-08-27T12:00:00Z", lines: [] }];
const detail = { id: "visit-1", requestId: "r1", status: "Submitted", outcome: null, completionNote: null, recorderAccountUserId: "tech", submittedAtUtc: "2026-08-27T12:00:00Z", reviewedAtUtc: null, reviewedByAccountUserId: null, reviewedByDisplayName: null, reviewNote: null, hasIncompleteFinancialData: false, totalSalesPrice: 100, totalStandardExpectedDirectCost: 40, totalMargin: 60, lines: [], concurrencyVersion: "version-1", hasNoChargeDisposition: false, blockers: [] };

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const visitArg = detail as any;

beforeEach(() => vi.clearAllMocks());

describe("useActualWorkFinancialReview", () => {
  it("quietly hides the financial surface when the backend denies access", async () => {
    getDetail.mockRejectedValueOnce(new ApiError(403, "Forbidden", "forbidden"));
    const { result } = renderHook(() => useActualWorkFinancialReview(submitted));
    await waitFor(() => expect(result.current.state).toEqual({ status: "hidden" }));
  });

  it("submits the exact detail concurrency version and refreshes the authoritative visit", async () => {
    getDetail.mockResolvedValue(detail);
    review.mockResolvedValue({ concurrencyVersion: "version-2" });
    const { result } = renderHook(() => useActualWorkFinancialReview(submitted));
    await waitFor(() => expect(result.current.state).toMatchObject({ status: "loaded", visits: [detail] }));
    const outcome = await result.current.review(visitArg, "Passed margin check");
    expect(outcome).toEqual({ kind: "success" });
    expect(review).toHaveBeenCalledWith("visit-1", { reviewNote: "Passed margin check" }, "version-1");
    expect(getDetail).toHaveBeenCalledTimes(2);
  });

  it("reconciles a 409 (stale version / already reviewed) by re-reading the authoritative visit", async () => {
    const reviewed = { ...detail, reviewedAtUtc: "2026-08-27T13:00:00Z", reviewedByAccountUserId: "other", reviewedByDisplayName: "Dana Owner", reviewNote: "Already handled" };
    getDetail.mockResolvedValueOnce(detail).mockResolvedValueOnce(reviewed);
    review.mockRejectedValueOnce(new ApiError(409, "ActualWork.AlreadyReviewed", "conflict"));
    const { result } = renderHook(() => useActualWorkFinancialReview(submitted));
    await waitFor(() => expect(result.current.state).toMatchObject({ status: "loaded", visits: [detail] }));

    const outcome = await result.current.review(visitArg, null);

    expect(outcome).toEqual({ kind: "reconciled", code: "ActualWork.AlreadyReviewed" });
    expect(getDetail).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(result.current.state).toMatchObject({ status: "loaded", visits: [reviewed] }));
  });

  it("maps the two hard review-gate codes to distinct blocked outcomes and reloads", async () => {
    getDetail.mockResolvedValue(detail);
    review
      .mockRejectedValueOnce(new ApiError(409, "ActualWork.ReviewBlockedIncompleteFinancials", "conflict"))
      .mockRejectedValueOnce(new ApiError(409, "ActualWork.ReviewBlockedZeroLineDispositionRequired", "conflict"));
    const { result } = renderHook(() => useActualWorkFinancialReview(submitted));
    await waitFor(() => expect(result.current.state).toMatchObject({ status: "loaded" }));

    expect(await result.current.review(visitArg, null)).toEqual({ kind: "review-blocked-incomplete" });
    expect(await result.current.review(visitArg, null)).toEqual({ kind: "review-blocked-zero-line" });
    expect(getDetail).toHaveBeenCalledTimes(3);
  });

  it("reports a 400 as a validation failure carrying the stable code, without re-reading", async () => {
    getDetail.mockResolvedValue(detail);
    resolution.mockRejectedValueOnce(new ApiError(400, "ActualWork.FinancialResolutionReasonRequired", "bad request"));
    const { result } = renderHook(() => useActualWorkFinancialReview(submitted));
    await waitFor(() => expect(result.current.state).toMatchObject({ status: "loaded" }));

    const outcome = await result.current.resolveLine(visitArg, "line-1", { resolvedUnitSellPrice: 10, resolvedUnitStandardExpectedDirectCost: null, basis: "OwnerSetPrice", reason: "" });

    expect(outcome).toEqual({ kind: "validation-failure", code: "ActualWork.FinancialResolutionReasonRequired" });
    expect(resolution).toHaveBeenCalledWith("visit-1", "line-1", { resolvedUnitSellPrice: 10, resolvedUnitStandardExpectedDirectCost: null, basis: "OwnerSetPrice", reason: "" }, "version-1");
    expect(getDetail).toHaveBeenCalledTimes(1);
  });

  it("records a no-charge disposition and reloads the authoritative visit", async () => {
    getDetail.mockResolvedValue(detail);
    disposition.mockResolvedValue({ concurrencyVersion: "version-2" });
    const { result } = renderHook(() => useActualWorkFinancialReview(submitted));
    await waitFor(() => expect(result.current.state).toMatchObject({ status: "loaded" }));

    const outcome = await result.current.recordNoChargeDisposition(visitArg, "Warranty callback");

    expect(outcome).toEqual({ kind: "success" });
    expect(disposition).toHaveBeenCalledWith("visit-1", { kind: "NoCharge", reason: "Warranty callback" }, "version-1");
    expect(getDetail).toHaveBeenCalledTimes(2);
  });

  it("returns the successor id on a replacement and sends the exact detail version", async () => {
    getDetail.mockResolvedValue(detail);
    replace.mockResolvedValue({ successorActualWorkId: "successor-9" });
    const { result } = renderHook(() => useActualWorkFinancialReview(submitted));
    await waitFor(() => expect(result.current.state).toMatchObject({ status: "loaded" }));

    const outcome = await result.current.replace(visitArg, "wrong part");

    expect(outcome).toEqual({ kind: "replaced", successorActualWorkId: "successor-9" });
    expect(replace).toHaveBeenCalledWith("visit-1", { reason: "wrong part" }, "version-1");
    // no reload — the caller refreshes history, which drops the superseded source
    expect(getDetail).toHaveBeenCalledTimes(1);
  });

  it("maps replace conflicts: open-draft to its own outcome, concurrency/already-superseded to reconcile", async () => {
    getDetail.mockResolvedValue(detail);
    replace
      .mockRejectedValueOnce(new ApiError(409, "ActualWork.DraftAlreadyOpenForRequest", "conflict"))
      .mockRejectedValueOnce(new ApiError(409, "ActualWork.AlreadySuperseded", "conflict"))
      .mockRejectedValueOnce(new ApiError(403, "Forbidden", "forbidden"));
    const { result } = renderHook(() => useActualWorkFinancialReview(submitted));
    await waitFor(() => expect(result.current.state).toMatchObject({ status: "loaded" }));

    expect(await result.current.replace(visitArg, "r")).toEqual({ kind: "replace-blocked-open-draft" });
    expect(await result.current.replace(visitArg, "r")).toEqual({ kind: "reconciled", code: "ActualWork.AlreadySuperseded" });
    expect(await result.current.replace(visitArg, "r")).toEqual({ kind: "hidden" });
    await waitFor(() => expect(result.current.state).toEqual({ status: "hidden" }));
  });

  it("flags the visit as mutating for the duration of a mutation and its reload", async () => {
    getDetail.mockResolvedValue(detail);
    let release: (value: unknown) => void = () => {};
    review.mockImplementation(() => new Promise((resolve) => { release = resolve; }));
    const { result } = renderHook(() => useActualWorkFinancialReview(submitted));
    await waitFor(() => expect(result.current.state).toMatchObject({ status: "loaded" }));

    let done = false;
    void result.current.review(visitArg, null).then(() => { done = true; });
    await waitFor(() => expect(result.current.isVisitMutating("visit-1")).toBe(true));
    expect(result.current.mutatingVisitIds.has("visit-1")).toBe(true);

    release({ concurrencyVersion: "version-2" });
    await waitFor(() => expect(done).toBe(true));
    await waitFor(() => expect(result.current.isVisitMutating("visit-1")).toBe(false));
  });
});
