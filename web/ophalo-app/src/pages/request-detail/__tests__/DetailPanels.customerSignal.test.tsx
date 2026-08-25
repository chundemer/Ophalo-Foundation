import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CustomerSignalPanel } from "../DetailPanels";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// Record details keeps its own public-intake-gated "No preference" audit context even though
// the header (CustomerContactStrip) now omits it — locked 2026-08-25 surface-specific visibility.
describe("CustomerSignalPanel — contact preference wording", () => {
  it("still renders 'No preference' for a public_intake request", () => {
    const detail: KeepRequestDetailResult = {
      ...mockRequestDetails["mock-req-002"],
      source: "public_intake",
      contactPreference: "no_preference",
    };
    render(<CustomerSignalPanel detail={detail} />);
    expect(screen.getByText("No preference")).toBeInTheDocument();
  });

  it("renders nothing for a non-public_intake source with no urgency set", () => {
    const detail: KeepRequestDetailResult = {
      ...mockRequestDetails["mock-req-001"],
      source: "phone",
      contactPreference: "no_preference",
      intakeUrgency: "",
    };
    const { container } = render(<CustomerSignalPanel detail={detail} />);
    expect(container).toBeEmptyDOMElement();
  });
});
