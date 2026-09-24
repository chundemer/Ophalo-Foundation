import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { ReadyToCloseActivityWarning } from "../DetailHero";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// DEF-063: ready-to-close customer-activity warning — persistent, non-dismissible, Resolved-only,
// server-authoritative (no client-side timestamp comparison). No "safe to close" success state.

function detailWith(
  status: string,
  hasCustomerActivityAfterResolution: boolean,
): KeepRequestDetailResult {
  const base = mockRequestDetails["mock-req-001"];
  return {
    ...base,
    status,
    readyToClose: { hasCustomerActivityAfterResolution },
  };
}

describe("ReadyToCloseActivityWarning", () => {
  it("renders the warning when resolved and the server flag is true", () => {
    render(<ReadyToCloseActivityWarning detail={detailWith("resolved", true)} />);
    expect(screen.getByText(/customer activity since resolution/i)).toBeInTheDocument();
  });

  it("renders nothing when resolved and the server flag is false", () => {
    const { container } = render(<ReadyToCloseActivityWarning detail={detailWith("resolved", false)} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders nothing when the flag is true but the status is not resolved", () => {
    const { container } = render(<ReadyToCloseActivityWarning detail={detailWith("closed", true)} />);
    expect(container).toBeEmptyDOMElement();
  });
});
