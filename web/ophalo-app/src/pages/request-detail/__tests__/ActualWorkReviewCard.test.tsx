import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ActualWorkReviewCard } from "../ActualWorkReviewCard";

const visit = { id: "visit-1", requestId: "r1", status: "Submitted", outcome: null, completionNote: null, recorderAccountUserId: "tech", submittedAtUtc: "2026-08-27T12:00:00Z", reviewedAtUtc: null, reviewedByAccountUserId: null, reviewedByDisplayName: null, reviewNote: null, hasIncompleteFinancialData: true, totalSalesPrice: 450, totalStandardExpectedDirectCost: null, totalMargin: null, concurrencyVersion: "v1", lines: [{ id: "line-1", displayNameSnapshot: "Replacement capacitor", unitOfMeasureSnapshot: null, actualQuantity: 2, note: null, isFinancialDataComplete: false, sellPriceSnapshot: 150, standardExpectedDirectCostSnapshot: null, lineSalesTotal: 300, lineStandardExpectedDirectCostTotal: null, lineMargin: null }] };

describe("ActualWorkReviewCard", () => {
  it("calls review with an optional note and flags incomplete financial data without implying an estimate", async () => {
    const user = userEvent.setup();
    const onReview = vi.fn().mockResolvedValue({ ok: true });
    const onSuccess = vi.fn();
    render(<ActualWorkReviewCard state={{ status: "loaded", visits: [visit] }} onRetry={vi.fn()} onReview={onReview} onReviewSuccess={onSuccess} />);
    expect(screen.getByText(/totals and margin are unavailable/)).toBeInTheDocument();
    await user.type(screen.getByLabelText(/Reviewer note/), "Passed margin check");
    await user.click(screen.getByRole("button", { name: /Mark visit reviewed/ }));
    expect(onReview).toHaveBeenCalledWith(visit, "Passed margin check");
    expect(onSuccess).toHaveBeenCalled();
  });

  it("shows the resolved reviewer name read-only, never the account-user id", () => {
    render(<ActualWorkReviewCard state={{ status: "loaded", visits: [{ ...visit, reviewedAtUtc: "2026-08-27T13:00:00Z", reviewedByAccountUserId: "acct-user-guid", reviewedByDisplayName: "Christian Hundemer", reviewNote: "Passed margin check" }] }} onRetry={vi.fn()} onReview={vi.fn()} onReviewSuccess={vi.fn()} />);
    expect(screen.getByText(/Reviewed .* by Christian Hundemer/)).toBeInTheDocument();
    expect(screen.queryByText(/acct-user-guid/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Mark visit reviewed/ })).not.toBeInTheDocument();
  });

  it("renders outcome and completion note for a zero-line diagnostic visit", () => {
    render(<ActualWorkReviewCard state={{ status: "loaded", visits: [{ ...visit, hasIncompleteFinancialData: false, totalSalesPrice: 0, totalStandardExpectedDirectCost: 0, totalMargin: 0, outcome: "DiagnosticOnly", completionNote: "Compressor sized; quote to follow.", lines: [] }] }} onRetry={vi.fn()} onReview={vi.fn()} onReviewSuccess={vi.fn()} />);
    expect(screen.getByText(/Diagnostic only/)).toBeInTheDocument();
    expect(screen.getByText(/Compressor sized/)).toBeInTheDocument();
    expect(screen.getByText(/No work lines were recorded/)).toBeInTheDocument();
  });

  it("surfaces a conflict notice when review reconciliation reports a stale/already-reviewed visit", async () => {
    const user = userEvent.setup();
    const onReview = vi.fn().mockResolvedValue({ ok: false, conflict: true });
    render(<ActualWorkReviewCard state={{ status: "loaded", visits: [visit] }} onRetry={vi.fn()} onReview={onReview} onReviewSuccess={vi.fn()} />);
    await user.click(screen.getByRole("button", { name: /Mark visit reviewed/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/already reviewed or changed/);
  });
});
