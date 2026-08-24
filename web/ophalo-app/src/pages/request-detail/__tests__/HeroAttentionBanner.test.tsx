import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HeroAttentionBanner } from "../DetailPanels";
import { mockRequestDetails, OWNER_ACTIONS } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

beforeEach(() => {
  Element.prototype.scrollIntoView = vi.fn();
});

function appendWorkCanvasTarget(id: string) {
  const canvas = document.createElement("div");
  canvas.dataset.requestDetailWorkCanvas = "";
  canvas.scrollTo = vi.fn();
  const target = document.createElement("div");
  target.id = id;
  target.tabIndex = -1;
  canvas.appendChild(target);
  document.body.appendChild(canvas);
  return { canvas, target };
}

function detailWith(
  guidanceKey: string | null,
  availableActionsOverride: Partial<KeepRequestDetailResult["availableActions"]>,
): KeepRequestDetailResult {
  return {
    ...mockRequestDetails["mock-req-001"],
    availableActions: { ...OWNER_ACTIONS, ...availableActionsOverride },
    // legacy fields intentionally left stale (normal/null) to prove routing reads only
    // effectiveAttention.guidanceKey, matching the completed EffectiveAttention migration.
    attentionLevel: "normal",
    attentionReason: null,
    effectiveAttention: {
      level: guidanceKey ? "overdue" : "none",
      reason: guidanceKey ? "follow_up_due" : null,
      dueAtUtc: null,
      dueOnDate: guidanceKey === "resolve_follow_up" ? "2026-07-01" : null,
      guidanceKey,
    },
  };
}

describe("HeroAttentionBanner", () => {
  it("routes acknowledge_attention to the Clear attention sheet", async () => {
    const user = userEvent.setup();
    const onRecordFollowUp = vi.fn();
    const onContactLaunched = vi.fn();
    const onOpenClearAttention = vi.fn();
    render(
      <HeroAttentionBanner
        detail={detailWith("acknowledge_attention", { canAcknowledgeAttention: true })}
        onRecordFollowUp={onRecordFollowUp}
        onContactLaunched={onContactLaunched}
        onOpenClearAttention={onOpenClearAttention}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Why this needs attention" }));
    expect(screen.getByText("Why")).toBeInTheDocument();
    expect(screen.getByText("Resolve by")).toBeInTheDocument();

    // acknowledge_attention already routes the primary CTA to Clear attention, so no
    // redundant secondary entry point renders alongside it.
    expect(screen.getAllByRole("button", { name: "Go to Clear attention" })).toHaveLength(1);
    screen.getByRole("button", { name: "Go to Clear attention" }).click();

    expect(onOpenClearAttention).toHaveBeenCalledTimes(1);
    expect(onRecordFollowUp).not.toHaveBeenCalled();
    expect(onContactLaunched).not.toHaveBeenCalled();
  });

  it("shows a secondary Clear attention entry point alongside a different primary CTA when acknowledgement is separately authorized", () => {
    const onOpenClearAttention = vi.fn();
    render(
      <HeroAttentionBanner
        detail={detailWith("resolve_follow_up", { canSetFollowUpOn: true, canAcknowledgeAttention: true })}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={vi.fn()}
        onOpenClearAttention={onOpenClearAttention}
      />,
    );

    screen.getByRole("button", { name: "Resolve follow-up" });
    screen.getByRole("button", { name: "Clear attention" }).click();
    expect(onOpenClearAttention).toHaveBeenCalledTimes(1);
  });

  it("routes resolve_follow_up (due Follow Up On) to the controller callback", () => {
    const onRecordFollowUp = vi.fn();
    render(
      <HeroAttentionBanner
        detail={detailWith("resolve_follow_up", { canSetFollowUpOn: true })}
        onRecordFollowUp={onRecordFollowUp}
        onContactLaunched={vi.fn()}
        onOpenClearAttention={vi.fn()}
      />,
    );

    screen.getByRole("button", { name: "Resolve follow-up" }).click();

    expect(onRecordFollowUp).toHaveBeenCalledTimes(1);
  });

  it("routes respond_to_customer to the composer container when a customer update is authorized", () => {
    const { canvas, target } = appendWorkCanvasTarget("focus-panel-update");
    const focusSpy = vi.spyOn(target, "focus");
    const onContactLaunched = vi.fn();

    render(
      <HeroAttentionBanner
        detail={detailWith("respond_to_customer", {
          canSendBusinessUpdate: true,
          canLogExternalContact: true,
        })}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={onContactLaunched}
        onOpenClearAttention={vi.fn()}
      />,
    );

    screen.getByRole("button", { name: "Respond to customer" }).click();

    expect(canvas.scrollTo).toHaveBeenCalled();
    expect(target.scrollIntoView).not.toHaveBeenCalled();
    expect(focusSpy).toHaveBeenCalled();
    expect(onContactLaunched).not.toHaveBeenCalled();
    document.body.removeChild(canvas);
  });

  it("routes respond_to_customer to Log contact when a customer update is unavailable but contact logging is authorized", () => {
    const onContactLaunched = vi.fn();
    render(
      <HeroAttentionBanner
        detail={detailWith("respond_to_customer", {
          canSendBusinessUpdate: false,
          canLogExternalContact: true,
        })}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={onContactLaunched}
        onOpenClearAttention={vi.fn()}
      />,
    );

    screen.getByRole("button", { name: "Log contact" }).click();

    expect(onContactLaunched).toHaveBeenCalledTimes(1);
    expect(onContactLaunched).toHaveBeenCalledWith("outbound", expect.any(String));
  });

  it("routes log_external_contact directly to the Log contact workflow", () => {
    const onContactLaunched = vi.fn();
    render(
      <HeroAttentionBanner
        detail={detailWith("log_external_contact", { canLogExternalContact: true })}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={onContactLaunched}
        onOpenClearAttention={vi.fn()}
      />,
    );

    screen.getByRole("button", { name: "Log contact" }).click();

    expect(onContactLaunched).toHaveBeenCalledWith("outbound", expect.any(String));
  });

  it("renders no CTA for respond_to_customer when neither a customer update nor contact logging is authorized", () => {
    render(
      <HeroAttentionBanner
        detail={detailWith("respond_to_customer", {
          canSendBusinessUpdate: false,
          canLogExternalContact: false,
          canAcknowledgeAttention: false,
        })}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={vi.fn()}
        onOpenClearAttention={vi.fn()}
      />,
    );
    // The info disclosure trigger still renders (why/resolve-by guidance is independent of
    // CTA authorization); no primary or secondary action button does.
    expect(screen.queryByRole("button", { name: "Respond to customer" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Log contact" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Clear attention" })).toBeNull();
    expect(screen.getByRole("button", { name: "Why this needs attention" })).toBeInTheDocument();
  });

  it("renders nothing when there is no guidanceKey", () => {
    render(
      <HeroAttentionBanner
        detail={detailWith(null, {})}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={vi.fn()}
        onOpenClearAttention={vi.fn()}
      />,
    );
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("shows no timeline-sourced evidence in the guidance disclosure when no distinct quote exists", async () => {
    const user = userEvent.setup();
    const detail = { ...detailWith("respond_to_customer", { canSendBusinessUpdate: true }), events: [] };
    render(
      <HeroAttentionBanner
        detail={detail}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={vi.fn()}
        onOpenClearAttention={vi.fn()}
      />,
    );
    await user.click(screen.getByRole("button", { name: "Why this needs attention" }));
    expect(screen.queryByText(`"${detail.description}"`)).not.toBeInTheDocument();
  });

  it("shows the distinct timeline-sourced quote in the guidance disclosure when one exists", async () => {
    const user = userEvent.setup();
    const detail = {
      ...detailWith("respond_to_customer", { canSendBusinessUpdate: true }),
      events: [
        {
          id: "e1",
          eventType: "message_added",
          content: "Can someone come out tomorrow instead?",
          visibility: "customer",
          occurredAtUtc: "2026-07-05T12:00:00Z",
          actorType: "customer",
          actorAccountUserId: null,
          actorDisplayName: "Jane Customer",
          statusAfter: null,
          messageIntent: null,
          communicationChannel: null,
          externalContactDirection: null,
          externalContactChannel: null,
          externalContactOutcome: null,
          externalContactRequiresFollowUp: false,
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
    };
    render(
      <HeroAttentionBanner
        detail={detail}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={vi.fn()}
        onOpenClearAttention={vi.fn()}
      />,
    );
    await user.click(screen.getByRole("button", { name: "Why this needs attention" }));
    expect(screen.getByText('"Can someone come out tomorrow instead?"')).toBeInTheDocument();
    expect(screen.queryByText(`"${detail.description}"`)).not.toBeInTheDocument();
  });

  it("renders no CTA when the routed action is not server-authorized", () => {
    render(
      <HeroAttentionBanner
        detail={detailWith("acknowledge_attention", { canAcknowledgeAttention: false })}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={vi.fn()}
        onOpenClearAttention={vi.fn()}
      />,
    );
    expect(screen.queryByRole("button", { name: "Go to Clear attention" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Clear attention" })).toBeNull();
  });
});
