import { createRef, act } from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { UnifiedComposer, type UnifiedComposerHandle } from "../UnifiedComposer";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// The Anchor's server-authored "respond_to_customer" primary action (Session 0A) activates the
// always-mounted inline composer imperatively — desktop has no separate sheet for this. Activation
// must only ever be triggered by an explicit tap, never merely because the request loaded.

function baseDetail(overrides: Partial<KeepRequestDetailResult> = {}): KeepRequestDetailResult {
  return {
    ...mockRequestDetails["mock-req-001"],
    ...overrides,
  };
}

function renderComposer(ref: React.RefObject<UnifiedComposerHandle | null>) {
  return render(
    <div data-request-detail-work-canvas style={{ height: 200, overflow: "auto" }}>
      <div id="focus-panel-update" tabIndex={-1}>
        <UnifiedComposer
          ref={ref}
          requestId="req-1"
          detail={baseDetail()}
          onDetailUpdated={vi.fn()}
          customerUpdateDraft=""
          onCustomerUpdateDraftChange={vi.fn()}
          customerUpdateDraftStatus=""
          onCustomerUpdateDraftStatusChange={vi.fn()}
        />
      </div>
    </div>,
  );
}

beforeEach(() => {
  vi.stubGlobal("matchMedia", vi.fn().mockReturnValue({ matches: false }));
  if (!HTMLElement.prototype.scrollTo) {
    HTMLElement.prototype.scrollTo = vi.fn() as typeof HTMLElement.prototype.scrollTo;
  }
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("UnifiedComposer — imperative activation handles", () => {
  it("does not switch tabs or focus the message textarea merely because the request loaded", () => {
    const ref = createRef<UnifiedComposerHandle>();
    renderComposer(ref);

    const textarea = screen.getByLabelText("Customer update message") as HTMLTextAreaElement;
    expect(document.activeElement).not.toBe(textarea);
  });

  it("on activation, switches back to the customer-update tab (from internal note) and focuses the message textarea", async () => {
    const ref = createRef<UnifiedComposerHandle>();
    renderComposer(ref);

    // Simulate the user having switched away to the internal-note tab first.
    await userEvent.setup().click(screen.getByRole("tab", { name: "Internal note" }));
    expect(screen.getByRole("tab", { name: "Customer-page update" })).toHaveAttribute("aria-selected", "false");

    act(() => {
      ref.current?.activateCustomerUpdate();
    });

    expect(screen.getByRole("tab", { name: "Customer-page update" })).toHaveAttribute("aria-selected", "true");
    const textarea = screen.getByLabelText("Customer update message") as HTMLTextAreaElement;
    expect(document.activeElement).toBe(textarea);
  });

  it("respects prefers-reduced-motion by scrolling instantly rather than smoothly", () => {
    vi.stubGlobal("matchMedia", vi.fn().mockReturnValue({ matches: true }));
    const ref = createRef<UnifiedComposerHandle>();
    renderComposer(ref);
    const canvas = document.querySelector("[data-request-detail-work-canvas]") as HTMLElement;
    const scrollToSpy = vi.spyOn(canvas, "scrollTo");

    act(() => {
      ref.current?.activateCustomerUpdate();
    });

    expect(scrollToSpy).toHaveBeenCalledWith(expect.objectContaining({ behavior: "auto" }));
  });

});
