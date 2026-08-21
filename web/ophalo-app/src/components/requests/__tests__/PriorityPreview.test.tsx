import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { PriorityPreview } from "../PriorityPreview";
import { mockRequestSummaries } from "../../../mocks/fixtures";
import type { AppliedQueueSnapshot } from "../../../pages/Requests";

function snapshot(overrides: Partial<AppliedQueueSnapshot> = {}): AppliedQueueSnapshot {
  return {
    requests: [],
    isLoading: false,
    isError: false,
    isForbidden: false,
    isFiltered: false,
    isRankedView: true,
    onResetFilters: vi.fn(),
    onRetry: vi.fn(),
    ...overrides,
  };
}

// UI-003: read-only, non-mutating — every branch must be assertable without a route, history,
// or mutation side effect firing.
describe("PriorityPreview", () => {
  it("shows a loading state when the snapshot has not settled yet", () => {
    render(<PriorityPreview snapshot={null} onOpenRequest={vi.fn()} onStartCapture={vi.fn()} />);
    expect(screen.getByText(/Loading priority preview/i)).toBeInTheDocument();
  });

  it("branch: attention — surfaces the top server-ranked attention row with Open request", () => {
    const attentionRow = mockRequestSummaries.find((r) => r.attention.attentionLevel !== "none");
    expect(attentionRow).toBeDefined();
    const onOpenRequest = vi.fn();
    render(
      <PriorityPreview
        snapshot={snapshot({ requests: [attentionRow!] })}
        onOpenRequest={onOpenRequest}
        onStartCapture={vi.fn()}
      />,
    );
    expect(screen.getByText(attentionRow!.customerName)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /open request/i }));
    expect(onOpenRequest).toHaveBeenCalledWith(attentionRow!.id);
  });

  it("branch: no-attention — states the provable fact and previews the next active request", () => {
    // No fixture row carries attentionLevel "none" today — build one so the locked predicate
    // (attentionLevel !== "none") is actually exercised on its true baseline value.
    const noAttentionRow = {
      ...mockRequestSummaries[0],
      attention: { ...mockRequestSummaries[0].attention, attentionLevel: "none", attentionReason: null },
    };
    render(
      <PriorityPreview
        snapshot={snapshot({ requests: [noAttentionRow] })}
        onOpenRequest={vi.fn()}
        onStartCapture={vi.fn()}
      />,
    );
    expect(screen.getByText(/no request in this view currently needs attention/i)).toBeInTheDocument();
    expect(screen.getByText(noAttentionRow.customerName)).toBeInTheDocument();
  });

  it("branch: filtered-empty — offers Reset filters, not New Request", () => {
    const onResetFilters = vi.fn();
    render(
      <PriorityPreview
        snapshot={snapshot({ requests: [], isFiltered: true, onResetFilters })}
        onOpenRequest={vi.fn()}
        onStartCapture={vi.fn()}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: /reset filters/i }));
    expect(onResetFilters).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("button", { name: /new request/i })).not.toBeInTheDocument();
  });

  it("branch: empty — offers an authorized New Request action, not Reset filters", () => {
    const onStartCapture = vi.fn();
    render(
      <PriorityPreview
        snapshot={snapshot({ requests: [], isFiltered: false })}
        onOpenRequest={vi.fn()}
        onStartCapture={onStartCapture}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: /new request/i }));
    expect(onStartCapture).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("button", { name: /reset filters/i })).not.toBeInTheDocument();
  });

  it("shows an error state with retry, not a guessed empty state", () => {
    const onRetry = vi.fn();
    render(
      <PriorityPreview
        snapshot={snapshot({ isError: true, onRetry })}
        onOpenRequest={vi.fn()}
        onStartCapture={vi.fn()}
      />,
    );
    expect(screen.queryByRole("button", { name: /new request/i })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /retry/i }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });
});
