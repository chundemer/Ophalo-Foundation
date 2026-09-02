import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { KeepButton } from "../KeepButton";

// GAP-067 Slice 4 added two additive, Request-Detail-scoped fills alongside the existing
// teal/primary/secondary variants. They must map to the locked `--keep-request-*` tokens and
// still fall back to the shared disabled outline treatment.
describe("KeepButton — request-scoped variants (GAP-067 Slice 4)", () => {
  it("request-primary renders the teal customer-resolution fill", () => {
    render(<KeepButton variant="request-primary">Respond</KeepButton>);
    const cls = screen.getByRole("button", { name: "Respond" }).className;
    expect(cls).toContain("bg-[var(--keep-request-primary)]");
    expect(cls).toContain("hover:bg-[var(--keep-request-primary-hover)]");
    expect(cls).toContain("text-white");
  });

  it("request-financial renders the dark-slate internal financial emphasis", () => {
    render(<KeepButton variant="request-financial">Review financials</KeepButton>);
    const cls = screen.getByRole("button", { name: "Review financials" }).className;
    expect(cls).toContain("bg-[var(--keep-request-financial)]");
    expect(cls).toContain("hover:bg-[var(--keep-request-financial-hover)]");
  });

  it("both new variants use the shared disabled outline treatment when disabled", () => {
    const { rerender } = render(
      <KeepButton variant="request-primary" disabled>
        Respond
      </KeepButton>,
    );
    expect(screen.getByRole("button", { name: "Respond" }).className).toContain(
      "border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] text-[var(--ophalo-muted)]",
    );
    rerender(
      <KeepButton variant="request-financial" disabled>
        Review financials
      </KeepButton>,
    );
    expect(screen.getByRole("button", { name: "Review financials" }).className).toContain(
      "border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] text-[var(--ophalo-muted)]",
    );
  });

  it("leaves the existing teal variant on the quiet accent token, distinct from request-primary", () => {
    render(<KeepButton variant="teal">Confirm</KeepButton>);
    const cls = screen.getByRole("button", { name: "Confirm" }).className;
    expect(cls).toContain("bg-[var(--keep-accent)]");
    expect(cls).not.toContain("keep-request-primary");
  });
});
