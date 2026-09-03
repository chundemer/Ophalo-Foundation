import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RequestDetailActivity } from "../RequestDetailActivity";
import { mockRequestDetails } from "../../../mocks/fixtures";

describe("RequestDetailActivity — sidebar summary", () => {
  const events = Array.from({ length: 4 }, (_, index) => ({
    ...mockRequestDetails["mock-req-001"].events[0],
    id: `event-${index}`,
  }));

  it("keeps the sidebar to the latest three entries and opens the complete history in a sheet", async () => {
    const user = userEvent.setup();
    render(
      <RequestDetailActivity
        timelineFilter="all"
        onTimelineFilterChange={vi.fn()}
        displayedEvents={events}
      />,
    );

    expect(screen.getByText("4 entries")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "View all 4 activity entries" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "View all 4 activity entries" }));
    expect(screen.getByRole("dialog", { name: "Activity history" })).toBeInTheDocument();
  });
});
