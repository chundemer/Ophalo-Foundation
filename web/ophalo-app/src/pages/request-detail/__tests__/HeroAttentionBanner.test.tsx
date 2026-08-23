import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
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
  it("routes acknowledge_attention to the Clear attention sheet", () => {
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

    expect(screen.getByText("Why")).toBeInTheDocument();
    expect(screen.getByText("Resolve by")).toBeInTheDocument();

    screen.getByRole("button", { name: "Go to Clear attention" }).click();

    expect(onOpenClearAttention).toHaveBeenCalledTimes(1);
    expect(onRecordFollowUp).not.toHaveBeenCalled();
    expect(onContactLaunched).not.toHaveBeenCalled();
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

  it("renders nothing for respond_to_customer when neither a customer update nor contact logging is authorized", () => {
    render(
      <HeroAttentionBanner
        detail={detailWith("respond_to_customer", {
          canSendBusinessUpdate: false,
          canLogExternalContact: false,
        })}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={vi.fn()}
        onOpenClearAttention={vi.fn()}
      />,
    );
    expect(screen.queryByRole("button")).toBeNull();
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

  it("renders nothing when the routed action is not server-authorized", () => {
    render(
      <HeroAttentionBanner
        detail={detailWith("acknowledge_attention", { canAcknowledgeAttention: false })}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={vi.fn()}
        onOpenClearAttention={vi.fn()}
      />,
    );
    expect(screen.queryByRole("button")).toBeNull();
  });
});
