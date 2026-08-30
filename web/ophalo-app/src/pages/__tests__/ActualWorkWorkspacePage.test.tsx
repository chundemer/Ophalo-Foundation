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
  requestQuery: { data: { customerName: "Jane Doe", referenceCode: "R-100", status: "InProgress" } },
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

  it("redirects a narrow viewport back to Request Detail and renders nothing", async () => {
    mediaMatches = false;
    stubMatchMedia();
    const onExit = vi.fn();
    const { container } = renderPage({ visit: "draft", onExit });
    await waitFor(() => expect(onExit).toHaveBeenCalled());
    expect(container).toBeEmptyDOMElement();
  });
});
