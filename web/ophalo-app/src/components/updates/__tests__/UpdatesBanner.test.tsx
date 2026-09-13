import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { UpdatesBanner } from "../UpdatesBanner";
import type { UpdateEntry } from "../../../lib/apiClient";

const WHATS_NEW_ENTRY: UpdateEntry = {
  id: "hl-1",
  published_at: "2026-09-06T00:00:00Z",
  section: "whats_new",
  title: "Scheduling got faster",
  body: "x",
  highlight: true,
};

const KNOWN_ISSUE_ENTRY: UpdateEntry = {
  ...WHATS_NEW_ENTRY,
  id: "hl-2",
  section: "known_issue",
  title: "Photo upload delayed",
  status: "active",
};

describe("UpdatesBanner", () => {
  it("renders the entry title and a Help & Updates CTA in a status region", () => {
    render(<UpdatesBanner entry={WHATS_NEW_ENTRY} moreCount={0} onDismiss={vi.fn()} onViewAll={vi.fn()} />);
    const region = screen.getByRole("status");
    expect(region).toHaveTextContent("Scheduling got faster");
    expect(screen.getByRole("button", { name: "View Help & Updates →" })).toBeInTheDocument();
  });

  it("names the extra count on the CTA only when moreCount > 0, pluralised", () => {
    const onViewAll = vi.fn();
    const { rerender } = render(
      <UpdatesBanner entry={WHATS_NEW_ENTRY} moreCount={0} onDismiss={vi.fn()} onViewAll={onViewAll} />,
    );
    expect(screen.queryByText(/more update/)).toBeNull();

    rerender(<UpdatesBanner entry={WHATS_NEW_ENTRY} moreCount={1} onDismiss={vi.fn()} onViewAll={onViewAll} />);
    expect(screen.getByText("View Help & Updates — 1 more update →")).toBeInTheDocument();

    rerender(<UpdatesBanner entry={WHATS_NEW_ENTRY} moreCount={3} onDismiss={vi.fn()} onViewAll={onViewAll} />);
    expect(screen.getByText("View Help & Updates — 3 more updates →")).toBeInTheDocument();
  });

  it("fires onViewAll and onDismiss from their controls", async () => {
    const user = userEvent.setup();
    const onDismiss = vi.fn();
    const onViewAll = vi.fn();
    render(<UpdatesBanner entry={WHATS_NEW_ENTRY} moreCount={2} onDismiss={onDismiss} onViewAll={onViewAll} />);

    await user.click(screen.getByRole("button", { name: "View Help & Updates — 2 more updates →" }));
    await user.click(screen.getByRole("button", { name: "Dismiss this update" }));
    expect(onViewAll).toHaveBeenCalledTimes(1);
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });

  it("uses the informational teal treatment for a non-known_issue entry, not the amber attention color", () => {
    render(<UpdatesBanner entry={WHATS_NEW_ENTRY} moreCount={0} onDismiss={vi.fn()} onViewAll={vi.fn()} />);
    expect(screen.getByText("New in Help & Updates")).toBeInTheDocument();
    expect(screen.getByRole("status").className).toContain("--keep-accent");
    expect(screen.getByRole("status").className).not.toContain("--ophalo-attention");
  });

  it("keeps the amber attention treatment for an active known_issue entry", () => {
    render(<UpdatesBanner entry={KNOWN_ISSUE_ENTRY} moreCount={0} onDismiss={vi.fn()} onViewAll={vi.fn()} />);
    expect(screen.getByText("Known issue")).toBeInTheDocument();
    expect(screen.getByRole("status").className).toContain("--ophalo-attention");
  });
});
