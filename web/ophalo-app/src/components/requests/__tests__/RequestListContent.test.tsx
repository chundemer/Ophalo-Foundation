import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RequestListContent } from "../RequestListContent";

function baseProps(overrides: Partial<React.ComponentProps<typeof RequestListContent>> = {}) {
  return {
    listRegionRef: { current: null },
    pageHeadingRef: { current: null },
    contextLabel: "Open work",
    heading: {
      headingText: "Showing 1–2",
      isLoading: false,
      isFetching: false,
      isError: false,
      isForbidden: false,
      emptyState: { heading: "No requests", detail: "Nothing to show." },
      onClearFilters: vi.fn(),
    },
    rows: {
      itemCount: 0,
      isAvailableTab: false,
      availableRequests: [],
      onAvailableSelect: vi.fn(),
      requests: [],
      isDefaultTab: false,
      needsAttentionRows: [],
      openWorkRows: [],
      renderRow: vi.fn(),
    },
    pager: {
      hasMore: false,
      isOnFirstPage: true,
      onPrevPage: vi.fn(),
      onNextPage: vi.fn(),
    },
    ...overrides,
  };
}

describe("RequestListContent", () => {
  it("renders a refetch bar when fetching stale cached rows in the background", () => {
    render(<RequestListContent {...baseProps({ heading: { ...baseProps().heading, isFetching: true } })} />);
    expect(screen.getByLabelText("Refreshing requests")).toBeInTheDocument();
  });

  it("does not render the refetch bar during initial load", () => {
    render(<RequestListContent {...baseProps({ heading: { ...baseProps().heading, isLoading: true, isFetching: true } })} />);
    expect(screen.queryByLabelText("Refreshing requests")).not.toBeInTheDocument();
  });

  it("does not render the refetch bar in an error state", () => {
    render(<RequestListContent {...baseProps({ heading: { ...baseProps().heading, isError: true, isFetching: true } })} />);
    expect(screen.queryByLabelText("Refreshing requests")).not.toBeInTheDocument();
  });

  it("does not render the refetch bar when idle", () => {
    render(<RequestListContent {...baseProps()} />);
    expect(screen.queryByLabelText("Refreshing requests")).not.toBeInTheDocument();
  });
});
