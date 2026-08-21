import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RequestDetailHeader } from "../RequestDetailHeader";

// Step 5: pane-mode Request Detail suppresses Back (the Queue pane is already the navigation
// context, so a second Back control is redundant/ambiguous) while keeping identity and Prev/Next.

describe("RequestDetailHeader — showBack (Step 5 pane mode)", () => {
  it("renders Back by default (one-pane / narrow-fallback identity, unchanged)", () => {
    render(<RequestDetailHeader onBack={() => {}} referenceCode="REQ-1" />);
    expect(screen.getByRole("button", { name: /requests/i })).toBeInTheDocument();
  });

  it("hides Back when showBack is false, but keeps reference code and Prev/Next", () => {
    const onNavigate = vi.fn();
    render(
      <RequestDetailHeader
        onBack={() => {}}
        showBack={false}
        referenceCode="REQ-1"
        prevId="prev-1"
        nextId="next-1"
        onNavigate={onNavigate}
      />,
    );
    expect(screen.queryByRole("button", { name: /requests/i })).not.toBeInTheDocument();
    expect(screen.getByText("REQ-1")).toBeInTheDocument();
    screen.getByRole("button", { name: "Next request" }).click();
    expect(onNavigate).toHaveBeenCalledWith("next-1");
  });
});
