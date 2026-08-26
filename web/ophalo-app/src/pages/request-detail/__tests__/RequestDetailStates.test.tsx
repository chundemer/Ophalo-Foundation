import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RequestDetailStates } from "../RequestDetailStates";
import { ApiError } from "../../../lib/apiClient";

describe("RequestDetailStates", () => {
  it("renders the loading skeleton and no refetch bar during initial load", () => {
    render(<RequestDetailStates isLoading={true} isError={false} error={null} isFetching={true} onRetry={vi.fn()} />);
    expect(screen.getByLabelText("Loading request details")).toBeInTheDocument();
    expect(screen.queryByLabelText("Refreshing request")).not.toBeInTheDocument();
  });

  it("renders nothing when idle", () => {
    const { container } = render(<RequestDetailStates isLoading={false} isError={false} error={null} isFetching={false} onRetry={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders a refetch bar when fetching stale cached data in the background", () => {
    render(<RequestDetailStates isLoading={false} isError={false} error={null} isFetching={true} onRetry={vi.fn()} />);
    expect(screen.getByLabelText("Refreshing request")).toBeInTheDocument();
  });

  it("renders the error state instead of a refetch bar when isError is true", () => {
    render(<RequestDetailStates isLoading={false} isError={true} error={new ApiError(500, undefined, "boom")} isFetching={true} onRetry={vi.fn()} />);
    expect(screen.queryByLabelText("Refreshing request")).not.toBeInTheDocument();
    expect(screen.getByText("Something went wrong loading this request.")).toBeInTheDocument();
  });
});
