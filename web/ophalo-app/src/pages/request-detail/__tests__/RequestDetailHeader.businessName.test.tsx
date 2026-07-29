import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RequestDetailHeader } from "../RequestDetailHeader";

// GAP-042: businessName is optional workspace-shell context sourced from ["me"] by the caller —
// the header itself only renders what it's given, staying quiet until the caller's query resolves.

describe("RequestDetailHeader — businessName (GAP-042)", () => {
  it("renders nothing extra when businessName is undefined (query not yet resolved)", () => {
    render(<RequestDetailHeader onBack={() => {}} referenceCode="REQ-1" />);
    expect(screen.queryByText(/·/)).not.toBeInTheDocument();
  });

  it("renders nothing extra when businessName is null (no workspace identity)", () => {
    render(<RequestDetailHeader onBack={() => {}} referenceCode="REQ-1" businessName={null} />);
    expect(screen.queryByText(/·/)).not.toBeInTheDocument();
  });

  it("renders the business name as truncating chrome next to the reference code", () => {
    render(<RequestDetailHeader onBack={() => {}} referenceCode="REQ-1" businessName="Acme Plumbing" />);
    const span = screen.getByTitle("Acme Plumbing");
    expect(span).toHaveClass("truncate");
    expect(screen.getByText("REQ-1")).toBeInTheDocument();
  });

  it("keeps Prev/Next usable alongside a long business name", () => {
    const onNavigate = vi.fn();
    render(
      <RequestDetailHeader
        onBack={() => {}}
        referenceCode="REQ-1"
        businessName="A Very Long Business Name That Would Otherwise Push Navigation Off Screen LLC"
        prevId="prev-1"
        nextId="next-1"
        onNavigate={onNavigate}
      />,
    );
    const nextButton = screen.getByRole("button", { name: "Next request" });
    nextButton.click();
    expect(onNavigate).toHaveBeenCalledWith("next-1");
  });
});
