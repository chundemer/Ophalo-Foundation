import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RequestsWorkspaceHeader } from "../RequestsWorkspaceHeader";

// UI-001 post-Step-4 density refinement (build-log 134 §1, locked 2026-08-21): pane mode swaps
// the full-page H1/subtitle for a compact "Request Queue" label. Undefined/false must render the
// exact full-page H1/subtitle unchanged.

function baseProps(overrides: Partial<React.ComponentProps<typeof RequestsWorkspaceHeader>> = {}) {
  return {
    showOnboardingBanner: false,
    setup: undefined,
    onNavigateSettings: vi.fn(),
    onStartCapture: vi.fn(),
    pageTitle: "Requests for Apex Home Services",
    pageSubtitle: "Requests with customer promises needing attention now.",
    ...overrides,
  };
}

describe("RequestsWorkspaceHeader pane mode (UI-001 post-Step-4 density refinement)", () => {
  it("pane mode: renders the compact label, not the H1/subtitle", () => {
    render(<RequestsWorkspaceHeader {...baseProps()} paneMode />);

    expect(screen.getByText("Request Queue")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { level: 1 })).not.toBeInTheDocument();
    expect(screen.queryByText("Requests for Apex Home Services")).not.toBeInTheDocument();
  });

  it("full-page/narrow mode: keeps the H1 and subtitle unchanged when paneMode is false/omitted", () => {
    render(<RequestsWorkspaceHeader {...baseProps()} />);

    expect(screen.getByRole("heading", { level: 1, name: "Requests for Apex Home Services" })).toBeInTheDocument();
    expect(screen.getByText("Requests with customer promises needing attention now.")).toBeInTheDocument();
    expect(screen.queryByText("Request Queue")).not.toBeInTheDocument();
  });
});
