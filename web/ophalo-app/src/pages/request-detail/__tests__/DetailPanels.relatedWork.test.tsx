import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RelatedWorkPanel } from "../DetailPanels";

// GAP-050: the panel must stay quiet for single-request customers, must not navigate or
// query without an in-place navigation callback, and rows must invoke that callback.

const mockGetRelatedWork = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    getRelatedWork: (...args: unknown[]) => mockGetRelatedWork(...args),
  },
  ApiError: class ApiError extends Error {
    status: number;
    constructor(status: number, _code: string | undefined, message: string) {
      super(message);
      this.status = status;
    }
  },
}));

function renderWithClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("RelatedWorkPanel (GAP-050)", () => {
  it("renders nothing and issues no query when onNavigate is not provided", async () => {
    const { container } = renderWithClient(<RelatedWorkPanel requestId="req-1" />);
    await Promise.resolve();
    expect(container).toBeEmptyDOMElement();
    expect(mockGetRelatedWork).not.toHaveBeenCalled();
  });

  it("renders nothing for a single-request customer (totalCount 0)", async () => {
    mockGetRelatedWork.mockResolvedValue({ totalCount: 0, items: [] });
    const { container } = renderWithClient(
      <RelatedWorkPanel requestId="req-1" onNavigate={vi.fn()} />,
    );
    await waitFor(() => expect(mockGetRelatedWork).toHaveBeenCalledWith("req-1"));
    await waitFor(() => expect(container).toBeEmptyDOMElement());
  });

  it("shows the accurate total and up to 3 rows with human-readable status labels, capped when more are eligible", async () => {
    mockGetRelatedWork.mockResolvedValue({
      totalCount: 7,
      items: [
        { requestId: "req-2", referenceCode: "R-002", status: "received", lastActivityAtUtc: "2026-07-01T00:00:00Z" },
        { requestId: "req-3", referenceCode: "R-003", status: "in_progress", lastActivityAtUtc: "2026-07-02T00:00:00Z" },
        { requestId: "req-4", referenceCode: "R-004", status: "pending_customer", lastActivityAtUtc: "2026-07-03T00:00:00Z" },
      ],
    });
    renderWithClient(<RelatedWorkPanel requestId="req-1" onNavigate={vi.fn()} />);

    await screen.findByText(/Related work for this customer \(7\)/);
    expect(screen.getByText("R-002")).toBeInTheDocument();
    expect(screen.getByText("Received")).toBeInTheDocument();
    expect(screen.getByText("R-003")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("R-004")).toBeInTheDocument();
    expect(screen.getByText("Pending Customer")).toBeInTheDocument();
    expect(screen.queryByText("in_progress")).not.toBeInTheDocument();
    expect(screen.queryByText("pending_customer")).not.toBeInTheDocument();
  });

  it("navigates in place when a related-work row is clicked", async () => {
    const user = userEvent.setup();
    const onNavigate = vi.fn();
    mockGetRelatedWork.mockResolvedValue({
      totalCount: 1,
      items: [{ requestId: "req-2", referenceCode: "R-002", status: "received", lastActivityAtUtc: "2026-07-01T00:00:00Z" }],
    });
    renderWithClient(<RelatedWorkPanel requestId="req-1" onNavigate={onNavigate} />);

    const row = await screen.findByRole("button", { name: /R-002/ });
    await user.click(row);
    expect(onNavigate).toHaveBeenCalledWith("req-2");
  });

  it("renders nothing while the query is pending, without blocking the detail screen", async () => {
    let resolveQuery: (value: { totalCount: number; items: unknown[] }) => void = () => {};
    mockGetRelatedWork.mockReturnValue(
      new Promise((resolve) => { resolveQuery = resolve; }),
    );
    const { container } = renderWithClient(
      <RelatedWorkPanel requestId="req-1" onNavigate={vi.fn()} />,
    );

    expect(container).toBeEmptyDOMElement();
    resolveQuery({ totalCount: 0, items: [] });
    await waitFor(() => expect(container).toBeEmptyDOMElement());
  });

  it("stays absent and degrades quietly when the query rejects", async () => {
    mockGetRelatedWork.mockRejectedValue(new Error("network error"));
    const { container } = renderWithClient(
      <RelatedWorkPanel requestId="req-1" onNavigate={vi.fn()} />,
    );

    await waitFor(() => expect(mockGetRelatedWork).toHaveBeenCalled());
    await waitFor(() => expect(container).toBeEmptyDOMElement());
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
});
