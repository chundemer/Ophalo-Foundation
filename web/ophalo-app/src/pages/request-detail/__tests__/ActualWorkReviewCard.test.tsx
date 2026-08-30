import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ActualWorkReviewCard } from "../ActualWorkReviewCard";

const line = { id: "line-1", displayNameSnapshot: "Replacement capacitor", unitOfMeasureSnapshot: null, actualQuantity: 2, note: null, performedByAccountUserId: "tech", performerDisplayName: "Dana Tech", isFinancialDataComplete: false, sellPriceSnapshot: 150, standardExpectedDirectCostSnapshot: null, lineSalesTotal: 300, lineStandardExpectedDirectCostTotal: null, lineMargin: null, sellPriceResolved: false, resolvedSellPrice: null, resolvedSellPriceBasis: null, directCostResolved: false, resolvedStandardExpectedDirectCost: null, resolvedStandardExpectedDirectCostBasis: null };
const blocker = { lineId: "line-1", displayNameSnapshot: "Replacement capacitor", sellPriceMissing: false, standardExpectedDirectCostMissing: true };
const visit = { id: "visit-1", requestId: "r1", status: "Submitted", outcome: null, completionNote: null, recorderAccountUserId: "tech", submittedAtUtc: "2026-08-27T12:00:00Z", reviewedAtUtc: null, reviewedByAccountUserId: null, reviewedByDisplayName: null, reviewNote: null, hasIncompleteFinancialData: true, totalSalesPrice: 450, totalStandardExpectedDirectCost: null, totalMargin: null, concurrencyVersion: "v1", hasNoChargeDisposition: false, blockers: [blocker], lines: [line] };

const noop = () => Promise.resolve({ kind: "success" as const });
const baseProps = { onRetry: vi.fn(), onReview: noop, onResolveLine: noop, onRecordNoChargeDisposition: noop, isVisitMutating: () => false, onReviewSuccess: vi.fn() };

describe("ActualWorkReviewCard", () => {
  it("calls review with an optional note and flags incomplete financial data without implying an estimate", async () => {
    const user = userEvent.setup();
    const onReview = vi.fn().mockResolvedValue({ kind: "success" });
    const onReviewSuccess = vi.fn();
    render(<ActualWorkReviewCard {...baseProps} state={{ status: "loaded", visits: [{ ...visit, blockers: [] }] }} onReview={onReview} onReviewSuccess={onReviewSuccess} />);
    expect(screen.getByText(/totals and margin are unavailable/)).toBeInTheDocument();
    await user.type(screen.getByLabelText(/Reviewer note/), "Passed margin check");
    await user.click(screen.getByRole("button", { name: /Mark visit reviewed/ }));
    expect(onReview).toHaveBeenCalledWith({ ...visit, blockers: [] }, "Passed margin check");
    expect(onReviewSuccess).toHaveBeenCalled();
  });

  it("shows the resolved reviewer name read-only, never the account-user id, and renders no resolution form", () => {
    render(<ActualWorkReviewCard {...baseProps} state={{ status: "loaded", visits: [{ ...visit, reviewedAtUtc: "2026-08-27T13:00:00Z", reviewedByAccountUserId: "acct-user-guid", reviewedByDisplayName: "Christian Hundemer", reviewNote: "Passed margin check" }] }} />);
    expect(screen.getByText(/Reviewed .* by Christian Hundemer/)).toBeInTheDocument();
    expect(screen.queryByText(/acct-user-guid/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Mark visit reviewed/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/Resolve missing/)).not.toBeInTheDocument();
  });

  it("renders a resolution form for each still-incomplete line on an unreviewed visit", () => {
    render(<ActualWorkReviewCard {...baseProps} state={{ status: "loaded", visits: [visit] }} />);
    expect(screen.getByText(/Resolve missing cost · Replacement capacitor/)).toBeInTheDocument();
  });

  it("renders the no-charge form only for an unreviewed zero-line visit with no disposition", () => {
    const zero = { ...visit, hasIncompleteFinancialData: false, blockers: [], lines: [], totalSalesPrice: 0, totalStandardExpectedDirectCost: 0, totalMargin: 0 };
    const { rerender } = render(<ActualWorkReviewCard {...baseProps} state={{ status: "loaded", visits: [zero] }} />);
    expect(screen.getByText(/Record this visit as no charge/)).toBeInTheDocument();

    rerender(<ActualWorkReviewCard {...baseProps} state={{ status: "loaded", visits: [{ ...zero, hasNoChargeDisposition: true }] }} />);
    expect(screen.queryByText(/Record this visit as no charge/)).not.toBeInTheDocument();
    expect(screen.getByText(/Recorded as no charge/)).toBeInTheDocument();

    rerender(<ActualWorkReviewCard {...baseProps} state={{ status: "loaded", visits: [{ ...zero, reviewedAtUtc: "2026-08-27T13:00:00Z" }] }} />);
    expect(screen.queryByText(/Record this visit as no charge/)).not.toBeInTheDocument();
  });

  it("distinguishes the two review-blocked outcomes in the notice", async () => {
    const user = userEvent.setup();
    render(<ActualWorkReviewCard {...baseProps} state={{ status: "loaded", visits: [{ ...visit, blockers: [] }] }} onReview={() => Promise.resolve({ kind: "review-blocked-incomplete" as const })} />);
    await user.click(screen.getByRole("button", { name: /Mark visit reviewed/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/missing pricing or cost on every line/);
  });

  it("shows the zero-line-disposition blocked notice", async () => {
    const user = userEvent.setup();
    render(<ActualWorkReviewCard {...baseProps} state={{ status: "loaded", visits: [{ ...visit, blockers: [] }] }} onReview={() => Promise.resolve({ kind: "review-blocked-zero-line" as const })} />);
    await user.click(screen.getByRole("button", { name: /Mark visit reviewed/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/Record this visit as no charge/);
  });

  it("surfaces a conflict notice when review reconciliation reports a stale/already-reviewed visit", async () => {
    const user = userEvent.setup();
    render(<ActualWorkReviewCard {...baseProps} state={{ status: "loaded", visits: [{ ...visit, blockers: [] }] }} onReview={() => Promise.resolve({ kind: "reconciled" as const, code: "ActualWork.AlreadyReviewed" })} />);
    await user.click(screen.getByRole("button", { name: /Mark visit reviewed/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/already reviewed or changed/);
  });

  it("disables the review button while the visit is mutating", () => {
    render(<ActualWorkReviewCard {...baseProps} state={{ status: "loaded", visits: [{ ...visit, blockers: [] }] }} isVisitMutating={() => true} />);
    expect(screen.getByRole("button", { name: /Working…/ })).toBeDisabled();
  });
});
