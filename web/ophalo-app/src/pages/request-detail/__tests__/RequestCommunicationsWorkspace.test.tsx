import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RequestCommunicationsWorkspace } from "../RequestCommunicationsWorkspace";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestEventItem } from "../../../lib/apiClient";

function event(
  id: string,
  eventType: string,
  content: string | null,
  overrides: Partial<KeepRequestEventItem> = {},
): KeepRequestEventItem {
  return {
    id,
    eventType,
    content,
    visibility: "all",
    occurredAtUtc: "2026-09-03T11:30:00Z",
    actorType: "account_user",
    actorAccountUserId: null,
    actorDisplayName: "Christian Hundemer",
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
    ...overrides,
  };
}

function renderWorkspace(events: KeepRequestEventItem[]) {
  const base = mockRequestDetails["mock-req-001"];
  return render(
    <RequestCommunicationsWorkspace
      detail={{ ...base, events }}
      composer={<div data-testid="composer">Composer</div>}
    />,
  );
}

describe("RequestCommunicationsWorkspace", () => {
  it("shows who said what, the communication context, and a full timestamp", () => {
    renderWorkspace([
      event("customer", "message_added", "The heater is still making noise.", {
        actorType: "customer",
        actorDisplayName: "Marcus Webb",
      }),
      event("business", "message_added", "We can return tomorrow at 9 AM."),
      event("internal", "internal_note_added", "Bring the flush kit.", { visibility: "internal" }),
      event("contact", "external_contact_logged", "Customer confirmed the appointment.", {
        externalContactDirection: "outbound",
        externalContactChannel: "phone",
        externalContactOutcome: "reached_customer",
      }),
    ]);

    expect(screen.getByTestId("composer")).toBeInTheDocument();
    expect(screen.getByText("Marcus Webb")).toBeInTheDocument();
    expect(screen.getAllByText("Christian Hundemer")).toHaveLength(3);
    expect(screen.getByText("The heater is still making noise.")).toBeInTheDocument();
    expect(screen.getByText("We can return tomorrow at 9 AM.")).toBeInTheDocument();
    expect(screen.getByText("Bring the flush kit.")).toBeInTheDocument();
    expect(screen.getByText("Customer confirmed the appointment.")).toBeInTheDocument();
    expect(screen.getByText("Contact summary")).toBeInTheDocument();
    expect(screen.getAllByText(/Sep 3, 2026/)).toHaveLength(4);
  });

  it("includes customer-visible text recorded with a status change", () => {
    renderWorkspace([
      event("combined", "status_changed", "Your appointment is now scheduled.", {
        statusAfter: "scheduled",
        messageIntent: "status_update",
      }),
    ]);
    expect(screen.getByText("Business update with status change")).toBeInTheDocument();
    expect(screen.getByText("Your appointment is now scheduled.")).toBeInTheDocument();
  });

  it("filters customer-facing communication and internal notes", async () => {
    renderWorkspace([
      event("customer", "message_added", "Customer words", { actorType: "customer", actorDisplayName: "Marcus Webb" }),
      event("internal", "internal_note_added", "Team-only words", { visibility: "internal" }),
    ]);
    const filters = screen.getByRole("group", { name: "Communication filter" });
    await userEvent.setup().click(within(filters).getByRole("button", { name: "customer" }));
    expect(screen.getByText("Customer words")).toBeInTheDocument();
    expect(screen.queryByText("Team-only words")).not.toBeInTheDocument();
    await userEvent.setup().click(within(filters).getByRole("button", { name: "internal" }));
    expect(screen.getByText("Team-only words")).toBeInTheDocument();
    expect(screen.queryByText("Customer words")).not.toBeInTheDocument();
  });

  it("collapses long messages and expands the complete content in place", async () => {
    const longMessage = `Opening context ${"detailed conversation ".repeat(25)}closing context.`;
    renderWorkspace([event("long", "message_added", longMessage)]);
    const content = screen.getByText(longMessage);
    expect(content).toHaveClass("line-clamp-4");
    const toggle = screen.getByRole("button", { name: "Show full message" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    await userEvent.setup().click(toggle);
    expect(content).not.toHaveClass("line-clamp-4");
    expect(screen.getByRole("button", { name: "Show less" })).toHaveAttribute("aria-expanded", "true");
  });
});
