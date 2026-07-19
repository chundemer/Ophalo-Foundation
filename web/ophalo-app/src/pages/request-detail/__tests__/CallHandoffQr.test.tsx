import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CallHandoffQr } from "../CallHandoffQr";
import { CustomerContactStrip } from "../CustomerContactStrip";
import { LogContactModal } from "../../RequestDetail";
import { mockRequestDetails } from "../../../mocks/fixtures";

// GAP-020 / ADR-448: the desktop call QR must always encode the opaque handoffUrl minted from
// POST /keep/requests/{requestId}/call-handoff — never tel:{phone}. These tests cover the shared
// component/hook directly, plus a wiring assertion per desktop entry point.

const mockCreateCallHandoff = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    createCallHandoff: (...args: unknown[]) => mockCreateCallHandoff(...args),
    logExternalContact: vi.fn(),
  },
  ApiError: class ApiError extends Error {
    status: number;
    constructor(status: number, message: string) {
      super(message);
      this.status = status;
    }
  },
}));

// Stand-in for react-qr-code that exposes the encoded value directly, so tests assert on the
// payload rather than parsing rendered SVG pixels.
vi.mock("react-qr-code", () => ({
  default: ({ value }: { value: string }) => <div data-testid="qr" data-value={value} />,
}));

beforeEach(() => {
  vi.clearAllMocks();
  // CustomerContactStrip reads this for an unrelated (non-call) customer-page link; stub it so
  // the component under test doesn't need the full Vite env in isolation.
  vi.stubEnv("VITE_PUBLIC_BASE_URL", "http://localhost:3000");
});

describe("CallHandoffQr (shared component/hook)", () => {
  it("mints a handoff for the supplied request ID on mount", async () => {
    mockCreateCallHandoff.mockResolvedValue({
      handoffUrl: "https://app.ophalo.com/keep/share-call/abc123",
      expiresAtUtc: "2026-07-19T23:00:00Z",
    });

    render(<CallHandoffQr requestId="req-77" />);

    await waitFor(() => expect(mockCreateCallHandoff).toHaveBeenCalledWith("req-77"));
    expect(mockCreateCallHandoff).toHaveBeenCalledTimes(1);
  });

  it("renders the QR with the opaque handoffUrl, never a tel: payload", async () => {
    mockCreateCallHandoff.mockResolvedValue({
      handoffUrl: "https://app.ophalo.com/keep/share-call/abc123",
      expiresAtUtc: "2026-07-19T23:00:00Z",
    });

    render(<CallHandoffQr requestId="req-77" />);

    const qr = await screen.findByTestId("qr");
    expect(qr.getAttribute("data-value")).toBe("https://app.ophalo.com/keep/share-call/abc123");
    expect(qr.getAttribute("data-value")).not.toMatch(/^tel:/);
  });

  it("shows a loading state while the handoff is minting", () => {
    mockCreateCallHandoff.mockReturnValue(new Promise(() => {})); // never resolves

    render(<CallHandoffQr requestId="req-77" />);

    expect(screen.getByRole("status", { name: "Preparing call link" })).toBeInTheDocument();
    expect(screen.queryByTestId("qr")).not.toBeInTheDocument();
  });

  it("shows an error with retry when minting fails, and retry re-mints", async () => {
    const user = userEvent.setup();
    mockCreateCallHandoff
      .mockRejectedValueOnce(new Error("network down"))
      .mockResolvedValueOnce({
        handoffUrl: "https://app.ophalo.com/keep/share-call/retried",
        expiresAtUtc: "2026-07-19T23:00:00Z",
      });

    render(<CallHandoffQr requestId="req-77" />);

    expect(await screen.findByText("Could not create call link. Try again.")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Try again" }));

    const qr = await screen.findByTestId("qr");
    expect(qr.getAttribute("data-value")).toBe("https://app.ophalo.com/keep/share-call/retried");
    expect(mockCreateCallHandoff).toHaveBeenCalledTimes(2);
  });
});

describe("Wiring — both desktop call-QR entry points use CallHandoffQr", () => {
  it("CustomerContactStrip's desktop 'Scan to call' trigger renders the shared component", async () => {
    const user = userEvent.setup();
    mockCreateCallHandoff.mockResolvedValue({
      handoffUrl: "https://app.ophalo.com/keep/share-call/strip-token",
      expiresAtUtc: "2026-07-19T23:00:00Z",
    });

    render(
      <CustomerContactStrip
        requestId="req-strip"
        phone="5555550101"
        email={null}
        customerName="Marcus Webb"
        pageToken="page-token"
        onContactLaunched={() => {}}
      />
    );

    await user.click(screen.getByRole("button", { name: /Scan to call/i }));

    await waitFor(() => expect(mockCreateCallHandoff).toHaveBeenCalledWith("req-strip"));
    const qr = await screen.findByTestId("qr");
    expect(qr.getAttribute("data-value")).toBe("https://app.ophalo.com/keep/share-call/strip-token");
  });

  it("RequestDetail's Log external contact modal renders the shared component for its desktop QR", async () => {
    mockCreateCallHandoff.mockResolvedValue({
      handoffUrl: "https://app.ophalo.com/keep/share-call/log-contact-token",
      expiresAtUtc: "2026-07-19T23:00:00Z",
    });

    render(
      <LogContactModal
        requestId="req-log"
        detail={mockRequestDetails["mock-req-001"]}
        initialDirection="outbound"
        initialChannel="phone"
        onDetailUpdated={() => {}}
        onClose={() => {}}
      />
    );

    await waitFor(() => expect(mockCreateCallHandoff).toHaveBeenCalledWith("req-log"));
    const qr = await screen.findByTestId("qr");
    expect(qr.getAttribute("data-value")).toBe("https://app.ophalo.com/keep/share-call/log-contact-token");
  });
});
