import { describe, expect, it, vi, afterEach } from "vitest";
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

  // GAP-092 1b: business-timezone day-boundary cues, pinned system clock so "today" is
  // deterministic regardless of when the suite runs.
  describe("business-timezone day-boundary cues", () => {
    afterEach(() => {
      vi.useRealTimers();
    });

    it("shows the today banner when the follow-up date matches the business-zone calendar day", () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date("2026-07-15T12:00:00Z"));

      render(
        <TodayPromiseBanner
          detail={{
            ...mockRequestDetails["mock-req-001"],
            followUpOnDate: "2026-07-15",
            followUpOnReason: "other",
            effectiveAttention: { level: "none", reason: null, dueAtUtc: null, dueOnDate: null, guidanceKey: null },
          }}
          timeZone="America/New_York"
        />,
      );

      expect(screen.getByText(/Follow up today/)).toBeInTheDocument();
    });

    it("shows the overdue banner when the follow-up date is before the business-zone calendar day", () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date("2026-07-15T12:00:00Z"));

      render(
        <TodayPromiseBanner
          detail={{
            ...mockRequestDetails["mock-req-001"],
            followUpOnDate: "2026-07-14",
            followUpOnReason: "other",
            effectiveAttention: { level: "none", reason: null, dueAtUtc: null, dueOnDate: null, guidanceKey: null },
          }}
          timeZone="America/New_York"
        />,
      );

      expect(screen.getByText(/Overdue follow-up/)).toBeInTheDocument();
    });

    it("uses the business zone's calendar day, not UTC's — still 'today' in America/New_York when UTC has already rolled to the next day", () => {
      // 2026-07-15T03:00:00Z is 2026-07-14 23:00 in America/New_York (UTC-4 in July): UTC has
      // already crossed into the 15th, but the business day is still the 14th.
      vi.useFakeTimers();
      vi.setSystemTime(new Date("2026-07-15T03:00:00Z"));

      render(
        <TodayPromiseBanner
          detail={{
            ...mockRequestDetails["mock-req-001"],
            followUpOnDate: "2026-07-14",
            followUpOnReason: "other",
            effectiveAttention: { level: "none", reason: null, dueAtUtc: null, dueOnDate: null, guidanceKey: null },
          }}
          timeZone="America/New_York"
        />,
      );

      expect(screen.getByText(/Follow up today/)).toBeInTheDocument();
      expect(screen.queryByText(/Overdue follow-up/)).not.toBeInTheDocument();
    });

    it("shows no banner (neutral) for today's date while the business timezone is unresolved", () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date("2026-07-15T12:00:00Z"));

      render(
        <TodayPromiseBanner
          detail={{
            ...mockRequestDetails["mock-req-001"],
            followUpOnDate: "2026-07-15",
            followUpOnReason: "other",
            effectiveAttention: { level: "none", reason: null, dueAtUtc: null, dueOnDate: null, guidanceKey: null },
          }}
        />,
      );

      expect(screen.queryByText(/Follow up today/)).not.toBeInTheDocument();
      expect(screen.queryByText(/Overdue follow-up/)).not.toBeInTheDocument();
    });
  });
});
