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
  HeroAttentionBanner: () => <div data-testid="region-attention" />,
  OriginalRequestCard: () => <div data-testid="region-customer-need" />,
  WorkControlsGroup: () => null,
}));
vi.mock("../MobileContactLocationCard", () => ({
  MobileContactLocationCard: () => <div data-testid="region-contact-location" />,
}));
vi.mock("../UnifiedComposer", () => ({ UnifiedComposer: () => <div data-testid="region-communication" /> }));

function baseDetail(): KeepRequestDetailResult {
  return mockRequestDetails["mock-req-001"];
}

function renderCanvas(
  isWide: boolean,
  reviewSuccessMsg: string | null = null,
  detail: KeepRequestDetailResult = baseDetail(),
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
  it("renders one scroll surface with a centered max-width reading frame", () => {
    const { container } = renderCanvas(true);
    const scroll = container.querySelectorAll("[data-request-detail-work-canvas]");
    expect(scroll).toHaveLength(1);
    expect(scroll[0].className).toContain("overflow-y-auto");
    expect(scroll[0].className).toContain("min-w-0");
    expect(scroll[0].querySelector(".max-w-4xl.mx-auto")).not.toBeNull();
  });

  it("desktop: locked region order with Record details above Activity, no mobile contact card", () => {
    const { container } = renderCanvas(true);
    expect(container.querySelector('[data-testid="region-contact-location"]')).toBeNull();
    const positions = order(container, [
      "region-attention",
      "region-customer-need",
      "region-actual-work",
      "region-communication",
      "region-record-details",
      "region-activity",
    ]);
    expect(positions).toEqual([...positions].sort((a, b) => a - b));
  });

  it("places the quiet 'Mark work done' lifecycle block after Actual Work and before the composer only when the server authorizes it", () => {
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
    const communication = idx((el) => el.getAttribute("data-testid") === "region-communication");
    expect(actualWork).toBeGreaterThanOrEqual(0);
    expect(lifecycle).toBeGreaterThan(actualWork);
    expect(communication).toBeGreaterThan(lifecycle);
    expect(getByText(/does not notify the customer or complete internal financial review/i)).toBeInTheDocument();
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
      "region-contact-location",
      "region-customer-need",
      "region-actual-work",
      "region-communication",
      "region-activity",
      "region-record-details",
    ]);
    expect(positions).toEqual([...positions].sort((a, b) => a - b));
  });
});
