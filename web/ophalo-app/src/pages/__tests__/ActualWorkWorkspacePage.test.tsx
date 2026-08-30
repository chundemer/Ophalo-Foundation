import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ActualWorkWorkspacePage } from "../ActualWorkWorkspacePage";

// BL136 4f-i: dedicated Actual Work Ticket Workspace route shell + field region.

const workspace = {
  capture: {
    state: { status: "loading" } as Record<string, unknown>,
    createDraft: vi.fn().mockResolvedValue("created"),
    refetchDraft: vi.fn(),
    reconcileAfterConflict: vi.fn(),
    clearConflictNotice: vi.fn(),
    retryReconciliation: vi.fn(),
    markSubmitted: vi.fn(),
    setDefaultPerformer: vi.fn(),
    setVisitNote: vi.fn(),
    setZeroLineDisposition: vi.fn(),
    handOffToOffice: vi.fn(),
    conflictNotice: null,
    replacementCorrection: false,
  },
  history: { state: { status: "loaded", submittedVisits: [] } as Record<string, unknown>, retry: vi.fn() },
  requestQuery: {
    data: {
      customerName: "Jane Doe",
      referenceCode: "R-100",
      status: "InProgress",
      description: "Furnace not igniting on the second floor; homeowner reports intermittent clicking.",
      serviceAddressLine1: "42 Elm Street",
      serviceAddressLine2: null,
      serviceCity: "Springfield",
      serviceState: "OR",
      serviceZip: "97403",
    },
  },
  submittedVisit: vi.fn().mockReturnValue(null),
};

vi.mock("../request-detail/useActualWorkWorkspace", () => ({
  useActualWorkWorkspace: () => workspace,
}));
vi.mock("../request-detail/ActualWorkComposer", () => ({
  ActualWorkComposer: ({ onClose }: { onClose: () => void }) => (
    <div>
      <span>MOCK COMPOSER</span>
      <button onClick={onClose}>composer-back</button>
    </div>
  ),
}));
vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return { ...actual, api: { ...actual.api, getMe: vi.fn().mockResolvedValue({ accountUserId: "u1" }) } };
});

// jsdom has no layout, so the band's two-line clamp measurement always reads 0. Stub the pair
// the effect compares (`scrollHeight - clientHeight > 1`) to drive the overflow branch.
function stubClamp({ scrollHeight, clientHeight }: { scrollHeight: number; clientHeight: number }) {
  const sh = Object.getOwnPropertyDescriptor(HTMLElement.prototype, "scrollHeight");
  const ch = Object.getOwnPropertyDescriptor(HTMLElement.prototype, "clientHeight");
  Object.defineProperty(HTMLElement.prototype, "scrollHeight", { configurable: true, get: () => scrollHeight });
  Object.defineProperty(HTMLElement.prototype, "clientHeight", { configurable: true, get: () => clientHeight });
  return () => {
    if (sh) Object.defineProperty(HTMLElement.prototype, "scrollHeight", sh);
    if (ch) Object.defineProperty(HTMLElement.prototype, "clientHeight", ch);
  };
}

const originalMatchMedia = window.matchMedia;
let mediaMatches = true;
function stubMatchMedia() {
  window.matchMedia = ((query: string) => ({
    matches: mediaMatches,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })) as unknown as typeof window.matchMedia;
}

function renderPage(props: Partial<React.ComponentProps<typeof ActualWorkWorkspacePage>> = {}) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <ActualWorkWorkspacePage
        requestId="req-1"
        visit="draft"
        onExit={props.onExit ?? vi.fn()}
        onResolvedToDraft={props.onResolvedToDraft ?? vi.fn()}
        {...props}
      />
    </QueryClientProvider>,
  );
}

describe("ActualWorkWorkspacePage", () => {
  beforeEach(() => {
    mediaMatches = true;
    stubMatchMedia();
    workspace.capture.state = { status: "draft", draft: { id: "d1", status: "Draft" } };
    workspace.history.state = { status: "loaded", submittedVisits: [] };
    workspace.submittedVisit.mockReturnValue(null);
    vi.clearAllMocks();
  });

  afterEach(() => {
    window.matchMedia = originalMatchMedia;
  });

  it("renders the hosted composer for an open Draft", async () => {
    renderPage({ visit: "draft" });
    expect(await screen.findByText("MOCK COMPOSER")).toBeInTheDocument();
  });

  it("routes composer close back to Request Detail", async () => {
    const onExit = vi.fn();
    renderPage({ visit: "draft", onExit });
    await userEvent.click(await screen.findByText("composer-back"));
    expect(onExit).toHaveBeenCalled();
  });

  it("renders a read-only submitted visit (lines, performer, visit note) and no composer", async () => {
    workspace.submittedVisit.mockReturnValue({
      id: "aw-42",
      status: "Submitted",
      outcome: null,
      completionNote: null,
      visitNote: "Checked the condenser",
      submittedAtUtc: "2026-08-20T10:00:00Z",
      lines: [
        {
          id: "l1",
          displayNameSnapshot: "Capacitor",
          unitOfMeasureSnapshot: "each",
          actualQuantity: 1,
          note: "swapped",
          performedByAccountUserId: "u-tech",
          performerDisplayName: "Dana Tech",
        },
      ],
    });
    renderPage({ visit: "aw-42" });
    expect(await screen.findByText("Capacitor")).toBeInTheDocument();
    expect(screen.getByText("Dana Tech")).toBeInTheDocument();
    expect(screen.getByText("Checked the condenser")).toBeInTheDocument();
    expect(screen.queryByText("MOCK COMPOSER")).not.toBeInTheDocument();
  });

  it("focuses the heading on mount for the read-only view", async () => {
    workspace.submittedVisit.mockReturnValue({
      id: "aw-42", status: "Submitted", outcome: null, completionNote: null, visitNote: null,
      submittedAtUtc: null, lines: [],
    });
    renderPage({ visit: "aw-42" });
    await waitFor(() => expect(screen.getByRole("heading", { level: 1 })).toHaveFocus());
  });

  it("shows the ticket-context band above the editable composer", async () => {
    renderPage({ visit: "draft" });

    expect(await screen.findByText("MOCK COMPOSER")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Jane Doe" })).toBeInTheDocument();
    expect(screen.getByText("R-100")).toBeInTheDocument();
    expect(screen.getByText(/42 Elm Street/)).toBeInTheDocument();
    expect(screen.getByText(/Springfield, OR 97403/)).toBeInTheDocument();
    expect(screen.getByText("Customer need")).toBeInTheDocument();
    expect(screen.getByText(/Furnace not igniting/)).toBeInTheDocument();
  });

  it("reveals the full Customer Need on demand and returns via the band's Back to Request", async () => {
    const restore = stubClamp({ scrollHeight: 120, clientHeight: 48 });
    try {
      const onExit = vi.fn();
      renderPage({ visit: "draft", onExit });
      await screen.findByText("MOCK COMPOSER");

      const toggle = await screen.findByRole("button", { name: /Show full customer need/ });
      expect(toggle).toHaveAttribute("aria-expanded", "false");
      await userEvent.click(toggle);
      expect(screen.getByRole("button", { name: /Show less/ })).toHaveAttribute("aria-expanded", "true");

      await userEvent.click(screen.getByRole("button", { name: "← Back to Request" }));
      expect(onExit).toHaveBeenCalled();
    } finally {
      restore();
    }
  });

  it("omits the Customer Need toggle when the need fits the collapsed presentation", async () => {
    const restore = stubClamp({ scrollHeight: 44, clientHeight: 48 });
    try {
      renderPage({ visit: "draft" });
      await screen.findByText("MOCK COMPOSER");
      expect(screen.getByText(/Furnace not igniting/)).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /customer need/i })).not.toBeInTheDocument();
    } finally {
      restore();
    }
  });

  it("shows the ticket-context band above a read-only submitted visit", async () => {
    workspace.submittedVisit.mockReturnValue({
      id: "aw-42", status: "Submitted", outcome: null, completionNote: null, visitNote: null,
      submittedAtUtc: "2026-08-20T10:00:00Z", superseded: false, lines: [],
    });
    renderPage({ visit: "aw-42" });

    expect(await screen.findByText("R-100")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Jane Doe" })).toBeInTheDocument();
    expect(screen.getByText(/Furnace not igniting/)).toBeInTheDocument();
    expect(screen.queryByText("MOCK COMPOSER")).not.toBeInTheDocument();
  });

  it("redirects a narrow viewport back to Request Detail and renders nothing", async () => {
    mediaMatches = false;
    stubMatchMedia();
    const onExit = vi.fn();
    const { container } = renderPage({ visit: "draft", onExit });
    await waitFor(() => expect(onExit).toHaveBeenCalled());
    expect(container).toBeEmptyDOMElement();
  });
});
