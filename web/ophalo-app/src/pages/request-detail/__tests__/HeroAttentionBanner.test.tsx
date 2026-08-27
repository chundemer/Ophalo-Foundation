import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HeroAttentionBanner } from "../DetailPanels";
import { mockRequestDetails, OWNER_ACTIONS } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// Attention/no-attention primary-action mount split (2026-08-25): while attention is active, this
// banner is the SOLE renderer of the server-authored `detail.availableActions.primaryAction`
// (via the shared `PrimaryActionSlot`) — the Anchor above the canvas does not mount it for the
// same request at the same time. The banner never derives its own action from `guidanceKey`; it
// only ever renders whatever `primaryAction` the fixture/mock server already computed.

function requiredProps(overrides: Partial<Parameters<typeof HeroAttentionBanner>[0]> = {}) {
  return {
    requestId: "req-1",
    onDetailUpdated: vi.fn(),
    onOpenClearAttention: vi.fn(),
    onRecordFollowUp: vi.fn(),
    onContactLaunched: vi.fn(),
    onActivateCustomerUpdateComposer: vi.fn(),
    ...overrides,
  };
}

function detailWith(
  guidanceKey: string | null,
  availableActionsOverride: Partial<KeepRequestDetailResult["availableActions"]>,
): KeepRequestDetailResult {
  return {
    ...mockRequestDetails["mock-req-001"],
    availableActions: { ...OWNER_ACTIONS, ...availableActionsOverride },
    attentionLevel: "normal",
    attentionReason: null,
    effectiveAttention: {
      level: guidanceKey ? "overdue" : "none",
      reason: guidanceKey ? "customer_message" : null,
      dueAtUtc: null,
      dueOnDate: guidanceKey === "resolve_follow_up" ? "2026-07-01" : null,
      guidanceKey,
    },
  };
}

describe("HeroAttentionBanner — active Customer message (respond_to_customer)", () => {
  it("shows the teal server-authored 'Respond to customer' primary action and the quiet Clear attention secondary", () => {
    const detail = detailWith("respond_to_customer", {
      canSendBusinessUpdate: true,
      canAcknowledgeAttention: true,
      primaryAction: { key: "respond_to_customer", label: "Respond to customer", target: "customer_update_composer", requiresConfirmation: false, confirmationCopy: null },
    });
    render(<HeroAttentionBanner detail={detail} {...requiredProps()} />);

    expect(screen.getByRole("button", { name: "Respond to customer" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Clear attention" })).toBeInTheDocument();
  });

  it("activates the inline composer when the teal primary action is clicked, not a new sheet", async () => {
    const onActivateCustomerUpdateComposer = vi.fn();
    const detail = detailWith("respond_to_customer", {
      canSendBusinessUpdate: true,
      primaryAction: { key: "respond_to_customer", label: "Respond to customer", target: "customer_update_composer", requiresConfirmation: false, confirmationCopy: null },
    });
    render(<HeroAttentionBanner detail={detail} {...requiredProps({ onActivateCustomerUpdateComposer })} />);

    await userEvent.setup().click(screen.getByRole("button", { name: "Respond to customer" }));
    expect(onActivateCustomerUpdateComposer).toHaveBeenCalledTimes(1);
  });
});

describe("HeroAttentionBanner — active acknowledgement-only attention", () => {
  it("renders Clear attention as the rail's filled primary action and omits the redundant secondary link", async () => {
    const onOpenClearAttention = vi.fn();
    const detail = detailWith("acknowledge_attention", {
      canAcknowledgeAttention: true,
      primaryAction: { key: "acknowledge_attention", label: "Acknowledge attention", target: "attention_sheet", requiresConfirmation: false, confirmationCopy: null },
    });
    render(<HeroAttentionBanner detail={detail} {...requiredProps({ onOpenClearAttention })} />);

    // acknowledge_attention already routes the primary CTA to Clear attention, so no redundant
    // quiet secondary entry point renders alongside it.
    expect(screen.getAllByRole("button", { name: "Acknowledge attention" })).toHaveLength(1);
    expect(screen.queryByRole("button", { name: "Clear attention" })).not.toBeInTheDocument();

    await userEvent.setup().click(screen.getByRole("button", { name: "Acknowledge attention" }));
    expect(onOpenClearAttention).toHaveBeenCalledTimes(1);
  });
});

describe("HeroAttentionBanner — unknown/malformed target", () => {
  it("fails safely with factual unavailable feedback, never falling back to capability-flag inference", () => {
    const detail = detailWith("respond_to_customer", {
      canSendBusinessUpdate: true,
      primaryAction: {
        key: "respond_to_customer",
        label: "Respond to customer",
        target: "unknown_future_target",
        requiresConfirmation: false,
        confirmationCopy: null,
      } as unknown as KeepRequestDetailResult["availableActions"]["primaryAction"],
    });
    render(<HeroAttentionBanner detail={detail} {...requiredProps()} />);

    expect(screen.getByText("Primary action unavailable")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Respond to customer" })).not.toBeInTheDocument();
  });
});

describe("HeroAttentionBanner — phone-width action wrapping", () => {
  // Mobile responsive-layout regression (2026-08-27): the outer banner row wraps (flex-wrap), so
  // the server-authored primary action drops onto its own line at phone widths instead of forcing
  // horizontal overflow / clipping off the right edge. The action group itself stays shrink-0 so
  // the button never collapses; the outer row does the wrapping.
  it("wraps via the outer row while the primary-action group stays shrink-0, and keeps the action inside the banner", () => {
    const detail = detailWith("respond_to_customer", {
      canSendBusinessUpdate: true,
      canAcknowledgeAttention: true,
      primaryAction: { key: "respond_to_customer", label: "Respond to customer", target: "customer_update_composer", requiresConfirmation: false, confirmationCopy: null },
    });
    const { container } = render(<HeroAttentionBanner detail={detail} {...requiredProps()} />);

    const banner = container.querySelector("section")!;
    const outerRow = banner.querySelector(":scope > div")!;
    expect(outerRow.className).toContain("flex-wrap");

    const primary = screen.getByRole("button", { name: "Respond to customer" });
    const clear = screen.getByRole("button", { name: "Clear attention" });
    expect(banner.contains(primary)).toBe(true);

    const actionGroup = primary.closest("div")!;
    expect(actionGroup.className).toContain("shrink-0");
    // Attention/no-attention primary-action exclusivity is unaffected: the secondary Clear
    // attention link and the single server-authored primary action still co-exist here.
    expect(actionGroup.contains(clear)).toBe(true);
  });
});

describe("HeroAttentionBanner — guidance disclosure and empty states", () => {
  it("shows the guidance badge and Why/Resolve-by disclosure", async () => {
    const user = userEvent.setup();
    const detail = detailWith("acknowledge_attention", { canAcknowledgeAttention: true });
    render(<HeroAttentionBanner detail={detail} {...requiredProps()} />);

    expect(screen.getByText("Needs attention")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Why this needs attention" }));
    expect(screen.getByText("Why")).toBeInTheDocument();
    expect(screen.getByText("Resolve by")).toBeInTheDocument();
  });

  it("renders nothing when there is no guidanceKey", () => {
    render(<HeroAttentionBanner detail={detailWith(null, {})} {...requiredProps()} />);
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("shows no timeline-sourced evidence in the guidance disclosure when no distinct quote exists", async () => {
    const user = userEvent.setup();
    const detail = { ...detailWith("respond_to_customer", { canSendBusinessUpdate: true }), events: [] };
    render(<HeroAttentionBanner detail={detail} {...requiredProps()} />);
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
    render(<HeroAttentionBanner detail={detail} {...requiredProps()} />);
    await user.click(screen.getByRole("button", { name: "Why this needs attention" }));
    expect(screen.getByText('"Can someone come out tomorrow instead?"')).toBeInTheDocument();
    expect(screen.queryByText(`"${detail.description}"`)).not.toBeInTheDocument();
  });
});
