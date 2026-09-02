import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { OriginalRequestCard } from "../DetailPanels";
import { mockRequestDetails } from "../../../mocks/fixtures";

// GAP-067 Slice 3: Customer Need is a neutral, visibly bordered inset on the muted request
// surface — it must never borrow the amber attention treatment (visual spec §Component-rule-1).
describe("OriginalRequestCard — Customer Need surface (GAP-067 Slice 3)", () => {
  it("renders on the muted request surface with a standard border, never an attention panel", () => {
    render(<OriginalRequestCard detail={mockRequestDetails["mock-req-001"]} />);

    const label = screen.getByText("Customer need");
    const card = label.parentElement as HTMLElement;

    expect(card.className).toContain("bg-[var(--keep-request-surface-muted)]");
    expect(card.className).toContain("border-[var(--ophalo-border)]");
    expect(card.className).not.toContain("attention");
  });
});
