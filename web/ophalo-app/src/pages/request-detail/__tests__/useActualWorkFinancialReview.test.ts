import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { ApiError } from "../../../lib/apiClient";
import { useActualWorkFinancialReview } from "../useActualWorkFinancialReview";

const getDetail = vi.fn();
const review = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return { ...actual, api: { ...actual.api, getActualWorkFinancialDetail: (...args: unknown[]) => getDetail(...args), reviewActualWork: (...args: unknown[]) => review(...args) } };
});

const submitted = [{ id: "visit-1", status: "Submitted", outcome: null, completionNote: null, submittedAtUtc: "2026-08-27T12:00:00Z", lines: [] }];
const detail = { id: "visit-1", requestId: "r1", status: "Submitted", outcome: null, completionNote: null, recorderAccountUserId: "tech", submittedAtUtc: "2026-08-27T12:00:00Z", reviewedAtUtc: null, reviewedByAccountUserId: null, reviewNote: null, hasIncompleteFinancialData: false, totalSalesPrice: 100, totalStandardExpectedDirectCost: 40, totalMargin: 60, lines: [], concurrencyVersion: "version-1" };

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
    await result.current.review(detail, "Passed margin check");
    expect(review).toHaveBeenCalledWith("visit-1", { reviewNote: "Passed margin check" }, "version-1");
    expect(getDetail).toHaveBeenCalledTimes(2);
  });
});
