import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { TodayPromiseBanner } from "../DetailHero";
import { mockRequestDetails } from "../../../mocks/fixtures";

describe("TodayPromiseBanner", () => {
  it("does not duplicate the overdue follow-up attention guidance and its Resolve follow-up CTA", () => {
    const onRecordFollowUp = vi.fn();
    render(
      <TodayPromiseBanner
        detail={{
          ...mockRequestDetails["mock-req-001"],
          followUpOnDate: "2026-07-12",
          followUpOnReason: "other",
          effectiveAttention: {
            level: "overdue",
            reason: "follow_up_due",
            dueAtUtc: null,
            dueOnDate: "2026-07-12",
            guidanceKey: "resolve_follow_up",
          },
        }}
        onRecordFollowUp={onRecordFollowUp}
      />,
    );

    expect(screen.queryByText(/Overdue follow-up/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Record follow-up" })).not.toBeInTheDocument();
  });
});
