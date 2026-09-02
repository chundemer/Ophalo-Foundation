import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BusinessUpdateSection } from "../BusinessSection";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// GAP-052b: a page-only update never notifies the customer by itself. The notify step should
// only surface for a submission that actually created a customer-visible business-update event
// (messageIntent "business_update", visibility "all") — never for a status-only change with no
// message, and it must reference that exact event's id (never a stale/status-changed one).

const mockPostBusinessUpdate = vi.fn();
const mockPatchRequestStatus = vi.fn();
const mockPrepareUpdateNotification = vi.fn();
const notifyPanelSpy = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    postBusinessUpdate: (...args: unknown[]) => mockPostBusinessUpdate(...args),
    patchRequestStatus: (...args: unknown[]) => mockPatchRequestStatus(...args),
    prepareUpdateNotification: (...args: unknown[]) => mockPrepareUpdateNotification(...args),
  },
  ApiError: class ApiError extends Error {
    status: number;
    constructor(status: number, message: string) {
      super(message);
      this.status = status;
    }
  },
}));

vi.mock("../NotifyCustomerPanel", () => ({
  NotifyCustomerPanel: (props: { relatedUpdateEventId: string }) => {
    notifyPanelSpy(props.relatedUpdateEventId);
    return <div data-testid="notify-panel">{props.relatedUpdateEventId}</div>;
  },
}));

function baseDetail(overrides: Partial<KeepRequestDetailResult> = {}): KeepRequestDetailResult {
  return { ...mockRequestDetails["mock-req-001"], pendingNotification: null, ...overrides };
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("BusinessUpdateSection — notify-step wiring (GAP-052b)", () => {
  it("shows NotifyCustomerPanel for the newly created customer-visible event after sending a message", async () => {
    const user = userEvent.setup();
    const detail = baseDetail();
    mockPostBusinessUpdate.mockResolvedValue({
      ...detail,
      events: [
        ...detail.events,
        {
          id: "new-event-99",
          eventType: "message_added",
          content: "We're on our way.",
          visibility: "all",
          occurredAtUtc: "2026-07-26T20:00:00Z",
          actorType: "AccountUser",
          actorAccountUserId: "user-1",
          actorDisplayName: "Jamie Reyes",
          statusAfter: null,
          messageIntent: "business_update",
          communicationChannel: "in_app",
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
        },
      ],
    });
    mockPrepareUpdateNotification.mockResolvedValue({ ...detail, pendingNotification: null });

    render(
      <BusinessUpdateSection
        requestId="req-77"
        detail={detail}
        onDetailUpdated={() => {}}
        draft="We're on our way."
        onDraftChange={() => {}}
        draftStatus=""
        onDraftStatusChange={() => {}}
        composerMode
      />
    );

    await user.click(screen.getByRole("button", { name: "Post & prepare text" }));

    await waitFor(() => expect(screen.getByTestId("notify-panel")).toBeInTheDocument());
    expect(notifyPanelSpy).toHaveBeenCalledWith("new-event-99");
    expect(mockPrepareUpdateNotification).toHaveBeenCalledWith(
      "req-77",
      { relatedUpdateEventId: "new-event-99", channel: "sms" },
      detail.version,
    );
  });

  it("does not show NotifyCustomerPanel when the operator explicitly picks page-only", async () => {
    const user = userEvent.setup();
    const detail = baseDetail();
    mockPostBusinessUpdate.mockResolvedValue({
      ...detail,
      events: [
        ...detail.events,
        {
          id: "new-event-100",
          eventType: "message_added",
          content: "We're on our way.",
          visibility: "all",
          occurredAtUtc: "2026-07-26T20:00:00Z",
          actorType: "AccountUser",
          actorAccountUserId: "user-1",
          actorDisplayName: "Jamie Reyes",
          statusAfter: null,
          messageIntent: "business_update",
          communicationChannel: "in_app",
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
        },
      ],
    });

    render(
      <BusinessUpdateSection
        requestId="req-77"
        detail={detail}
        onDetailUpdated={() => {}}
        draft="We're on our way."
        onDraftChange={() => {}}
        draftStatus=""
        onDraftStatusChange={() => {}}
        composerMode
      />
    );

    await user.click(screen.getByRole("button", { name: "More notify options" }));
    await user.click(screen.getByRole("menuitem", { name: "Post to page only (no notify)" }));

    await waitFor(() => expect(mockPostBusinessUpdate).toHaveBeenCalled());
    expect(mockPrepareUpdateNotification).not.toHaveBeenCalled();
    expect(screen.queryByTestId("notify-panel")).not.toBeInTheDocument();
    expect(await screen.findByRole("status")).toHaveTextContent("Posted to the customer page.");
  });

  it("does not show NotifyCustomerPanel for a status-only change with no message", async () => {
    const user = userEvent.setup();
    const detail = baseDetail({
      availableActions: { ...mockRequestDetails["mock-req-001"].availableActions, canChangeStatus: true },
    });
    mockPatchRequestStatus.mockResolvedValue({ ...detail, status: "scheduled" });

    render(
      <BusinessUpdateSection
        requestId="req-77"
        detail={detail}
        onDetailUpdated={() => {}}
        draft=""
        onDraftChange={() => {}}
        draftStatus="scheduled"
        onDraftStatusChange={() => {}}
        composerMode
      />
    );

    await user.click(screen.getByRole("button", { name: "Update status" }));

    await waitFor(() => expect(mockPatchRequestStatus).toHaveBeenCalled());
    expect(screen.queryByTestId("notify-panel")).not.toBeInTheDocument();
  });

  // GAP-067 Slice 4: the customer-update composer submit is a customer-resolution primary, so the
  // KeepSplitButton renders the `request-primary` fill on BOTH halves (main action + caret menu
  // trigger), not the quiet `--keep-accent`.
  it("renders the customer-update split-button submit with the request-primary fill on both halves", () => {
    const detail = baseDetail();
    render(
      <BusinessUpdateSection
        requestId="req-77"
        detail={detail}
        onDetailUpdated={() => {}}
        draft="We're on our way."
        onDraftChange={() => {}}
        draftStatus=""
        onDraftStatusChange={() => {}}
        composerMode
      />
    );

    const mainHalf = screen.getByRole("button", { name: "Post & prepare text" });
    const caretHalf = screen.getByRole("button", { name: "More notify options" });
    for (const half of [mainHalf, caretHalf]) {
      expect(half.className).toContain("bg-[var(--keep-request-primary)]");
      expect(half.className).toContain("hover:bg-[var(--keep-request-primary-hover)]");
      expect(half.className).not.toContain("bg-[var(--keep-accent)]");
    }
  });
});
