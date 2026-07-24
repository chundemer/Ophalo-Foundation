import { describe, it, expect } from "vitest";
import { statusLabel, statusBadgeVariant } from "../requestStatus";

describe("statusLabel", () => {
  it.each([
    ["received", "Received"],
    ["scheduled", "Scheduled"],
    ["in_progress", "Active"],
    ["pending_customer", "Pending Customer"],
    ["resolved", "Work completed"],
    ["closed", "Closed"],
    ["cancelled", "Cancelled"],
    ["spam", "Spam"],
    ["test", "Test"],
  ])("labels %s as %s", (status, expected) => {
    expect(statusLabel(status)).toBe(expected);
  });

  it("falls back to a capitalized form for an unrecognized status", () => {
    expect(statusLabel("some_new_status")).toBe("Some New Status");
  });
});

describe("statusBadgeVariant", () => {
  it.each([
    ["received", "info"],
    ["scheduled", "info"],
    ["in_progress", "teal"],
    ["pending_customer", "default"],
    ["resolved", "success"],
    ["closed", "success"],
    ["cancelled", "default"],
    ["spam", "default"],
    ["test", "default"],
  ])("assigns %s the %s variant", (status, expected) => {
    expect(statusBadgeVariant(status)).toBe(expected);
  });

  it("falls back to default for an unrecognized status", () => {
    expect(statusBadgeVariant("some_new_status")).toBe("default");
  });
});
