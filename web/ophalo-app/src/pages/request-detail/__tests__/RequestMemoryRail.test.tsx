import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RequestMemoryRail } from "../RequestMemoryRail";
import type { KeepRequestEventItem } from "../../../lib/apiClient";

function event(
  id: string,
  eventType: string,
  occurredAtUtc: string,
  visibility = "customer",
): KeepRequestEventItem {
  return {
    id,
    eventType,
    content: null,
    visibility,
    occurredAtUtc,
    actorType: visibility === "internal" ? "account_user" : "customer",
    actorAccountUserId: null,
    actorDisplayName: null,
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
  event("customer-message", "message_added", "2026-09-03T10:00:00Z"),
  event("internal-note", "internal_note_added", "2026-09-03T11:00:00Z", "internal"),
  event("status-change", "status_changed", "2026-09-03T12:00:00Z", "internal"),
];

function renderRail(overrides: Partial<React.ComponentProps<typeof RequestMemoryRail>> = {}) {
  const onContactCustomer = vi.fn();
  const onAddInternalNote = vi.fn();
  const result = render(
    <RequestMemoryRail
      events={events}
      details={<p>Owner and planning details</p>}
      canLogExternalContact
      canAddInternalNote
      onContactCustomer={onContactCustomer}
      onAddInternalNote={onAddInternalNote}
      {...overrides}
    />,
  );
  return { ...result, onContactCustomer, onAddInternalNote };
}

describe("RequestMemoryRail", () => {
  beforeEach(() => window.sessionStorage.clear());
  afterEach(() => window.sessionStorage.clear());

  it("defaults to Communications and separates customer communication from internal notes", async () => {
    const user = userEvent.setup();
    renderRail();

    expect(screen.getByRole("tab", { name: "Communications" })).toHaveAttribute("aria-selected", "true");
    const panel = screen.getByRole("tabpanel", { name: "Communications" });
    expect(within(panel).getByText("Customer message")).toBeInTheDocument();
    expect(within(panel).getByText("Internal note")).toBeInTheDocument();
    expect(within(panel).queryByText("Status changed")).not.toBeInTheDocument();

    await user.click(within(screen.getByRole("group", { name: "Communication filter" })).getByRole("button", { name: "internal" }));
    expect(within(panel).getByText("Internal note")).toBeInTheDocument();
    expect(within(panel).queryByText("Customer message")).not.toBeInTheDocument();
  });

  it("shows the complete event lineage in Request history", async () => {
    renderRail();
    await userEvent.setup().click(screen.getByRole("tab", { name: "Request history" }));

    expect(screen.getByText("3 events")).toBeInTheDocument();
    expect(screen.getByText("Status changed")).toBeInTheDocument();
  });

  it("persists the selected tab across request-selection remounts", async () => {
    const first = renderRail();
    await userEvent.setup().click(screen.getByRole("tab", { name: "Details" }));
    first.unmount();

    renderRail({ events: [] });
    expect(screen.getByRole("tab", { name: "Details" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Owner and planning details")).toBeInTheDocument();
  });

  it("exposes durable contact logging and internal-note entry", async () => {
    const user = userEvent.setup();
    const { onContactCustomer, onAddInternalNote } = renderRail();

    await user.click(screen.getByRole("button", { name: "Contact customer" }));
    await user.click(screen.getByRole("button", { name: "Add internal note" }));
    expect(onContactCustomer).toHaveBeenCalledOnce();
    expect(onAddInternalNote).toHaveBeenCalledOnce();
  });

  it("supports arrow-key tab navigation", async () => {
    renderRail();
    const communications = screen.getByRole("tab", { name: "Communications" });
    communications.focus();
    await userEvent.setup().keyboard("{ArrowRight}");
    expect(screen.getByRole("tab", { name: "Request history" })).toHaveFocus();
    expect(screen.getByRole("tab", { name: "Request history" })).toHaveAttribute("aria-selected", "true");
  });
});
