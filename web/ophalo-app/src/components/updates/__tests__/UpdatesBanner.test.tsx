import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { UpdatesBanner } from "../UpdatesBanner";
import type { UpdateEntry } from "../../../lib/apiClient";

const ENTRY: UpdateEntry = {
  id: "hl-1",
  published_at: "2026-09-06T00:00:00Z",
  section: "whats_new",
  title: "Scheduling got faster",
  body: "x",
  highlight: true,
};

describe("UpdatesBanner", () => {
  it("renders the entry title in a status region", () => {
    render(<UpdatesBanner entry={ENTRY} moreCount={0} onDismiss={vi.fn()} onViewAll={vi.fn()} />);
    const region = screen.getByRole("status");
    expect(region).toHaveTextContent("Scheduling got faster");
  });

  it("shows a pluralised 'N more updates' action only when moreCount > 0", () => {
    const onViewAll = vi.fn();
    const { rerender } = render(
      <UpdatesBanner entry={ENTRY} moreCount={0} onDismiss={vi.fn()} onViewAll={onViewAll} />,
    );
    expect(screen.queryByText(/more update/)).toBeNull();

    rerender(<UpdatesBanner entry={ENTRY} moreCount={1} onDismiss={vi.fn()} onViewAll={onViewAll} />);
    expect(screen.getByText("1 more update →")).toBeInTheDocument();

    rerender(<UpdatesBanner entry={ENTRY} moreCount={3} onDismiss={vi.fn()} onViewAll={onViewAll} />);
    expect(screen.getByText("3 more updates →")).toBeInTheDocument();
  });

  it("fires onViewAll and onDismiss from their controls", async () => {
    const user = userEvent.setup();
    const onDismiss = vi.fn();
    const onViewAll = vi.fn();
    render(<UpdatesBanner entry={ENTRY} moreCount={2} onDismiss={onDismiss} onViewAll={onViewAll} />);

    await user.click(screen.getByRole("button", { name: "2 more updates →" }));
    await user.click(screen.getByRole("button", { name: "Dismiss this update" }));
    expect(onViewAll).toHaveBeenCalledTimes(1);
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });
});
