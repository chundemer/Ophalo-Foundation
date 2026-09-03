import { describe, it, expect, vi } from "vitest";
import { render } from "@testing-library/react";
import { createRef } from "react";
import { RequestDetailWorkCanvas } from "../RequestDetailWorkCanvas";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";
import type { UnifiedComposerHandle } from "../UnifiedComposer";

// RD-019A: the Work Canvas is layout-only. These tests pin the canvas contract that
// `RequestDetailContent` relies on: the single scroll surface, the centered reading frame, the
// locked region order, and the desktop/mobile swap of the Activity vs Record-details tail — with
// every region supplied as an opaque node so nothing here exercises data or policy.

vi.mock("../DetailHero", () => ({ TodayPromiseBanner: () => null }));
vi.mock("../DetailPanels", () => ({
  ProminentFeedbackCard: () => null,
  HeroAttentionBanner: ({ inlineComposer }: { inlineComposer?: React.ReactNode }) => <div data-testid="region-attention">{inlineComposer}</div>,
  OriginalRequestCard: () => <div data-testid="region-customer-need" />,
  WorkControlsGroup: () => null,
}));
vi.mock("../MobileContactLocationCard", () => ({
  MobileContactLocationCard: () => <div data-testid="region-contact-location" />,
}));
vi.mock("../UnifiedComposer", () => ({ UnifiedComposer: () => <div data-testid="region-communication" /> }));
vi.mock("../RequestCommunicationsWorkspace", () => ({
  RequestCommunicationsWorkspace: ({ composer }: { composer: React.ReactNode }) => <div data-testid="communications-workspace">{composer}</div>,
}));

function baseDetail(): KeepRequestDetailResult {
  return mockRequestDetails["mock-req-001"];
}

function renderCanvas(
  isWide: boolean,
  reviewSuccessMsg: string | null = null,
  detail: KeepRequestDetailResult = baseDetail(),
  activeWorkspaceTab: "work" | "communications" = "work",
) {
  return render(
    <RequestDetailWorkCanvas
      isWide={isWide}
      requestId="req-1"
      detail={detail}
      highlights={{}}
      showProminentFeedbackCard={false}
      onDetailUpdated={vi.fn()}
      onContactLaunched={vi.fn()}
      onEditLocation={vi.fn()}
      onRecordFollowUp={vi.fn()}
      onCreateFollowUp={vi.fn()}
      onReviewSuccess={vi.fn()}
      onOpenClearAttention={vi.fn()}
      onActivateCustomerUpdateComposer={vi.fn()}
      composerRef={createRef<UnifiedComposerHandle>()}
      customerUpdateDraft=""
      onCustomerUpdateDraftChange={vi.fn()}
      customerUpdateDraftStatus="idle"
      onCustomerUpdateDraftStatusChange={vi.fn()}
      reviewSuccessMsg={reviewSuccessMsg}
      actualWorkSection={<div data-testid="region-actual-work" />}
      activityBlock={<div data-testid="region-activity" />}
      recordDetailsBlock={<div data-testid="region-record-details" />}
      activeWorkspaceTab={activeWorkspaceTab}
      onWorkspaceTabChange={vi.fn()}
    />,
  );
}

function order(container: HTMLElement, ids: string[]): number[] {
  const all = Array.from(container.querySelectorAll("[data-testid]"));
  return ids.map((id) => {
    const el = container.querySelector(`[data-testid="${id}"]`);
    expect(el, `expected to find ${id}`).not.toBeNull();
    return all.indexOf(el!);
  });
}

describe("RequestDetailWorkCanvas", () => {
  it("renders one shared scroll surface with the centered 1440px three-column frame", () => {
    const { container } = renderCanvas(true);
    const scroll = container.querySelectorAll("[data-request-detail-work-canvas]");
    expect(scroll).toHaveLength(1);
    expect(scroll[0].className).toContain("overflow-y-auto");
    expect(scroll[0].className).toContain("min-w-0");
    const frame = scroll[0].firstElementChild as HTMLElement;
    expect(frame.className).toContain("max-w-[1440px]");
    expect(frame.className).toContain("mx-auto");
    const context = scroll[0].querySelector("[data-request-three-column-workbench]");
    expect(context?.className).toContain("grid-cols-[minmax(0,1fr)_300px]");
  });

  it("desktop: Work is the primary workspace while passive history and details occupy the supporting rail", () => {
    const { container } = renderCanvas(true);
    expect(container.querySelector('[data-testid="region-contact-location"]')).toBeNull();
    expect(container.querySelector('[data-testid="region-customer-need"]')).toBeNull();
    expect(container.querySelector("#request-workspace-panel-work")).not.toHaveAttribute("hidden");
    expect(container.querySelector("#request-workspace-panel-communications")).toHaveAttribute("hidden");
    const positions = order(container, ["region-attention", "region-actual-work", "region-record-details", "region-activity"]);
    expect(positions).toEqual([...positions].sort((a, b) => a - b));
  });

  it("keeps the quiet lifecycle block after Actual Work in the Work tab", () => {
    const base = baseDetail();
    const withoutSecondary = renderCanvas(true);
    expect(withoutSecondary.queryByText("Request lifecycle")).toBeNull();
    withoutSecondary.unmount();

    const detail = {
      ...base,
      availableActions: {
        ...base.availableActions,
        markWorkDoneSecondary: { label: "Mark work done", target: "mutation" as const, consequence: "attention_remains" as const },
      },
    };
    const { container, getByText, getByRole } = renderCanvas(true, null, detail);

    expect(getByRole("button", { name: "Mark work done, attention remains" })).toBeInTheDocument();
    const all = Array.from(container.querySelectorAll("[data-testid], p"));
    const idx = (predicate: (el: Element) => boolean) => all.findIndex(predicate);
    const actualWork = idx((el) => el.getAttribute("data-testid") === "region-actual-work");
    const lifecycle = idx((el) => el.textContent === "Request lifecycle");
    expect(actualWork).toBeGreaterThanOrEqual(0);
    expect(lifecycle).toBeGreaterThan(actualWork);
    expect(getByText(/does not notify the customer or complete internal financial review/i)).toBeInTheDocument();
  });

  it("renders the composer and conversation together in the Communications tab", () => {
    const { container, getByRole } = renderCanvas(true, null, baseDetail(), "communications");
    expect(getByRole("tab", { name: "Communications" })).toHaveAttribute("aria-selected", "true");
    expect(container.querySelector("#request-workspace-panel-work")).toHaveAttribute("hidden");
    expect(container.querySelector("#request-workspace-panel-communications")).not.toHaveAttribute("hidden");
    expect(container.querySelector('[data-testid="communications-workspace"] [data-testid="region-communication"]')).not.toBeNull();
  });

  it("announces the internal-financial-review success message as a status region when supplied", () => {
    const { getByRole } = renderCanvas(
      true,
      "Internal financial review completed. The customer request status is unchanged.",
    );
    expect(getByRole("status")).toHaveTextContent(
      "Internal financial review completed. The customer request status is unchanged.",
    );
  });

  it("mobile: inserts the contact/location card after attention and swaps to Activity above Record details", () => {
    const { container } = renderCanvas(false);
    const positions = order(container, [
      "region-attention",
      "region-communication",
      "region-contact-location",
      "region-customer-need",
      "region-actual-work",
      "region-activity",
      "region-record-details",
    ]);
    expect(positions).toEqual([...positions].sort((a, b) => a - b));
  });
});
