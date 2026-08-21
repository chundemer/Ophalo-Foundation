import { describe, it, expect } from "vitest";
import {
  getTabsForRole,
  getSecondaryViewsForRole,
  getOfficeReviewMembersForRole,
  countForTab,
} from "../requestsWorkspace";
import { mockViewCounts } from "../../mocks/fixtures";

// UI-004 amendment (2026-08-21): primary tabs are exactly 3 in a locked order, "My Work" is
// the single primary-tab label for assigned_to_me on both roles, Watching lives in Views, and
// Ready to Close/Feedback Review/Actual Work Review live in the Owner/Admin-only Office Review
// disclosure — none of the three are primary tabs any longer.

describe("getTabsForRole", () => {
  it("returns Owner/Admin's 3 primary tabs in locked order: Needs Attention, All Work, My Work", () => {
    for (const role of ["owner", "admin"] as const) {
      const tabs = getTabsForRole(role);
      expect(tabs.map((t) => t.id)).toEqual(["needs_attention", "default", "assigned_to_me"]);
      expect(tabs.map((t) => t.label)).toEqual(["Needs Attention", "All Work", "My Work"]);
    }
  });

  it("returns Operator's 3 primary tabs in locked order: My Work, Needs Attention, Available", () => {
    const tabs = getTabsForRole("operator");
    expect(tabs.map((t) => t.id)).toEqual(["assigned_to_me", "needs_attention", "available_work"]);
    expect(tabs.map((t) => t.label)).toEqual(["My Work", "Needs Attention", "Available Work"]);
  });

  it("never includes Watching, Ready to Close, Feedback Review, or Actual Work Review", () => {
    for (const role of ["owner", "admin", "operator"] as const) {
      const ids = getTabsForRole(role).map((t) => t.id);
      expect(ids).not.toContain("watching");
      expect(ids).not.toContain("ready_to_close");
      expect(ids).not.toContain("feedback_review");
      expect(ids).not.toContain("actual_work_review");
    }
  });
});

describe("getSecondaryViewsForRole", () => {
  it("returns only Watching, for both roles", () => {
    expect(getSecondaryViewsForRole("owner").map((t) => t.id)).toEqual(["watching"]);
    expect(getSecondaryViewsForRole("operator").map((t) => t.id)).toEqual(["watching"]);
  });
});

describe("getOfficeReviewMembersForRole", () => {
  it("returns Ready to Close, Feedback Review, Actual Work Review for Owner/Admin", () => {
    for (const role of ["owner", "admin"] as const) {
      expect(getOfficeReviewMembersForRole(role).map((t) => t.id)).toEqual([
        "ready_to_close",
        "feedback_review",
        "actual_work_review",
      ]);
    }
  });

  it("returns none for Operator", () => {
    expect(getOfficeReviewMembersForRole("operator")).toEqual([]);
  });
});

describe("countForTab", () => {
  it("sources Actual Work Review's count from the authoritative count argument, never a list length", () => {
    const tab = getOfficeReviewMembersForRole("owner").find((t) => t.id === "actual_work_review")!;
    expect(countForTab(tab, mockViewCounts, 3)).toBe(3);
    // Not yet resolved — null, never a guessed 0.
    expect(countForTab(tab, mockViewCounts, null)).toBeNull();
    expect(countForTab(tab, mockViewCounts, undefined)).toBeNull();
  });

  it("sources Ready to Close / Feedback Review from server view counts", () => {
    const [readyToClose, feedbackReview] = getOfficeReviewMembersForRole("owner");
    expect(countForTab(readyToClose, mockViewCounts)).toBe(mockViewCounts.readyToClose);
    expect(countForTab(feedbackReview, mockViewCounts)).toBe(mockViewCounts.feedbackReview);
  });

  it("sources Watching from server view counts", () => {
    const [watching] = getSecondaryViewsForRole("owner");
    expect(countForTab(watching, mockViewCounts)).toBe(mockViewCounts.watching);
  });
});
