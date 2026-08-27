import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ActualWorkReviewCard } from "../ActualWorkReviewCard";

const visit = { id: "visit-1", requestId: "r1", status: "Submitted", outcome: null, completionNote: null, recorderAccountUserId: "tech", submittedAtUtc: "2026-08-27T12:00:00Z", reviewedAtUtc: null, reviewedByAccountUserId: null, reviewNote: null, hasIncompleteFinancialData: true, totalSalesPrice: 450, totalStandardExpectedDirectCost: null, totalMargin: null, concurrencyVersion: "v1", lines: [{ id: "line-1", displayNameSnapshot: "Replacement capacitor", unitOfMeasureSnapshot: null, actualQuantity: 2, note: null, isFinancialDataComplete: false, sellPriceSnapshot: 150, standardExpectedDirectCostSnapshot: null, lineSalesTotal: 300, lineStandardExpectedDirectCostTotal: null, lineMargin: null }] };

describe("ActualWorkReviewCard", () => {
  it("calls review with an optional note and identifies incomplete financial data", async () => {
    const user = userEvent.setup();
    const onReview = vi.fn().mockResolvedValue({ ok: true });
    const onSuccess = vi.fn();
    render(<ActualWorkReviewCard state={{ status: "loaded", visits: [visit] }} onRetry={vi.fn()} onReview={onReview} onReviewSuccess={onSuccess} />);
    expect(screen.getByText(/Missing cost data/)).toBeInTheDocument();
    await user.type(screen.getByLabelText(/Reviewer note/), "Passed margin check");
    await user.click(screen.getByRole("button", { name: /Mark visit reviewed/ }));
    expect(onReview).toHaveBeenCalledWith(visit, "Passed margin check");
    expect(onSuccess).toHaveBeenCalled();
  });

  it("shows the review audit stamp read-only", () => {
    render(<ActualWorkReviewCard state={{ status: "loaded", visits: [{ ...visit, reviewedAtUtc: "2026-08-27T13:00:00Z", reviewedByAccountUserId: "christian", reviewNote: "Passed margin check" }] }} onRetry={vi.fn()} onReview={vi.fn()} onReviewSuccess={vi.fn()} />);
    expect(screen.getByText(/Reviewed .* by christian/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Mark visit reviewed/ })).not.toBeInTheDocument();
  });
});
