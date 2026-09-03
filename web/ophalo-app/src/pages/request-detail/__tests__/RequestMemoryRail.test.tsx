import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RequestMemoryRail } from "../RequestMemoryRail";
import type { KeepRequestEventItem } from "../../../lib/apiClient";

function event(id: string, eventType: string, actorDisplayName: string, content: string | null = null): KeepRequestEventItem {
  return {
    id,
    eventType,
    content,
    visibility: "internal",
    occurredAtUtc: "2026-09-03T11:30:00Z",
    actorType: "account_user",
    actorAccountUserId: null,
    actorDisplayName,
    statusAfter: null,
    messageIntent: null,
    communicationChannel: null,
    externalContactDirection: null,
    externalContactChannel: null,
    externalContactOutcome: null,
    externalContactRequiresFollowUp: null,
    externalContactSetFirstResponse: null,
    externalContactClearedAttention: null,
    participationAction: null,
    participationTargetAccountUserId: null,
    participationTargetDisplayName: null,
    participationPreviousResponsibleAccountUserId: null,
    participationInternalNote: null,
    plannedForDate: null,
    followUpOnDate: null,
    followUpOnReason: null,
    feedbackWasResolved: null,
    relatedEventId: null,
  };
}

const events = [
  event("priority-change", "business_priority_changed", "Christian Hundemer", "Priority changed from Soon to Urgent"),
  event("assignment", "participation_changed", "Alex Owner"),
  event("business-update", "message_added", "Christian Hundemer", "We have ordered the replacement part."),
];

function renderRail(eventsOverride = events) {
  return render(<RequestMemoryRail events={eventsOverride} details={<p>Owner and planning details</p>} />);
}

describe("RequestMemoryRail", () => {
  beforeEach(() => window.sessionStorage.clear());
  afterEach(() => window.sessionStorage.clear());

  it("defaults to compact Request history with actor and full timestamp context", () => {
    renderRail();
    expect(screen.getByRole("tab", { name: "Request history" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText((_, element) => element?.tagName === "P" && element.textContent?.includes("2 events") === true)).toBeInTheDocument();
    expect(screen.getByText("Priority changed from Soon to Urgent")).toBeInTheDocument();
    expect(screen.queryByText("We have ordered the replacement part.")).not.toBeInTheDocument();
    expect(screen.getByText((_, element) => element?.tagName === "P" && element.textContent?.includes("Christian Hundemer · Sep 3, 2026") === true)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Contact customer" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add internal note" })).not.toBeInTheDocument();
  });

  it("persists Details across request-selection remounts", async () => {
    const first = renderRail();
    await userEvent.setup().click(screen.getByRole("tab", { name: "Details" }));
    first.unmount();
    renderRail([]);
    expect(screen.getByRole("tab", { name: "Details" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Owner and planning details")).toBeInTheDocument();
  });

  it("migrates the removed Communications preference to Request history", () => {
    window.sessionStorage.setItem("ophalo.request-memory-tab", "communications");
    renderRail();
    expect(screen.getByRole("tab", { name: "Request history" })).toHaveAttribute("aria-selected", "true");
  });

  it("supports arrow-key tab navigation", async () => {
    renderRail();
    const history = screen.getByRole("tab", { name: "Request history" });
    history.focus();
    await userEvent.setup().keyboard("{ArrowRight}");
    expect(screen.getByRole("tab", { name: "Details" })).toHaveFocus();
    expect(screen.getByRole("tab", { name: "Details" })).toHaveAttribute("aria-selected", "true");
  });
});
