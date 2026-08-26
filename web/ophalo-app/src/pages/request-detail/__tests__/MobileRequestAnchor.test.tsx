import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MobileRequestAnchor, MobileActionRail } from "../MobileRequestAnchor";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

function renderRail(detail: KeepRequestDetailResult, hidden = false) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MobileActionRail
        requestId="req-1"
        detail={detail}
        highlights={{}}
        showProminentFeedbackCard={false}
        onDetailUpdated={vi.fn()}
        onContactLaunched={vi.fn()}
        onEditLocation={vi.fn()}
        onOpenReassignOwner={vi.fn()}
        onOpenWatchers={vi.fn()}
        onRecordFollowUp={vi.fn()}
        onCreateFollowUp={vi.fn()}
        onReviewSuccess={vi.fn()}
        onOpenClearAttention={vi.fn()}
        onActivateCustomerUpdateComposer={vi.fn()}
        hidden={hidden}
      />
    </QueryClientProvider>,
  );
}

describe("MobileRequestAnchor", () => {
  it("renders reference/status badges and customer identity", () => {
    const detail = mockRequestDetails["mock-req-001"];
    render(<MobileRequestAnchor detail={detail} />);
    expect(screen.getByText(detail.referenceCode)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: detail.customerName })).toBeInTheDocument();
  });
});

describe("MobileActionRail", () => {
  it("renders the server-designated primary action when no attention is active", () => {
    const detail = mockRequestDetails["mock-req-001"];
    renderRail(detail);
    expect(screen.getByRole("button", { name: "Mark work done" })).toBeInTheDocument();
  });

  it("does not render — attention/no-attention exclusivity — when attention is active (HeroAttentionBanner owns the slot)", () => {
    const detail = mockRequestDetails["mock-req-002"];
    const { container } = renderRail(detail);
    expect(container).toBeEmptyDOMElement();
  });

  it("stays mounted but translated off-screen while hidden (keyboard-safe hide, not unmount)", () => {
    const detail = mockRequestDetails["mock-req-001"];
    renderRail(detail, true);
    const button = screen.getByRole("button", { name: "Mark work done", hidden: true });
    expect(button.closest(".translate-y-full")).not.toBeNull();
  });

  it("marks the hidden rail inert, removing its action from the keyboard tab order (jsdom does not enforce inert's focus-blocking behavior, so this asserts the DOM contract the browser acts on)", () => {
    const detail = mockRequestDetails["mock-req-001"];
    const { container } = renderRail(detail, true);
    const rail = container.firstElementChild as HTMLElement;
    expect(rail).toHaveAttribute("inert");
  });

  it("does not mark the rail inert while visible", () => {
    const detail = mockRequestDetails["mock-req-001"];
    const { container } = renderRail(detail, false);
    const rail = container.firstElementChild as HTMLElement;
    expect(rail).not.toHaveAttribute("inert");
  });
});
