import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CustomerContactStrip } from "../CustomerContactStrip";

// 0.6b: "Scan to text" reuses the existing opaque SMS-handoff token/resolver (POST
// /keep/requests/{id}/sms-handoff) and the established QR modal pattern from "Scan to call" —
// desktop shows an editable draft + QR, mobile launches sms: directly. Scanning/launching must
// never itself log contact; only the existing separate "record this text" -> Log external contact
// flow does that (asserted via onContactLaunched).

const mockCreateSmsHandoff = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    createSmsHandoff: (...args: unknown[]) => mockCreateSmsHandoff(...args),
    createCallHandoff: vi.fn(),
  },
}));

// Stand-in for react-qr-code that exposes the encoded value directly.
vi.mock("react-qr-code", () => ({
  default: ({ value }: { value: string }) => <div data-testid="qr" data-value={value} />,
}));

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubEnv("VITE_PUBLIC_BASE_URL", "http://localhost:3000");
});

function renderStrip(onContactLaunched = vi.fn()) {
  render(
    <CustomerContactStrip
      requestId="req-strip"
      phone="5555550101"
      email={null}
      customerName="Marcus Webb"
      pageToken="page-token"
      onContactLaunched={onContactLaunched}
    />
  );
  return onContactLaunched;
}

describe("CustomerContactStrip — Scan to text", () => {
  it("mints an SMS handoff for the default message on open", async () => {
    mockCreateSmsHandoff.mockResolvedValue({
      handoffUrl: "https://app.ophalo.com/keep/share-sms/strip-token",
      expiresAtUtc: "2026-07-19T23:00:00Z",
    });
    const user = userEvent.setup();
    renderStrip();

    await user.click(screen.getByRole("button", { name: /Scan to text/i }));

    await waitFor(() =>
      expect(mockCreateSmsHandoff).toHaveBeenCalledWith(
        "req-strip",
        expect.stringContaining("http://localhost:3000/keep/r/page-token")
      )
    );
  });

  it("renders the QR with the opaque handoffUrl, never the raw phone/message", async () => {
    mockCreateSmsHandoff.mockResolvedValue({
      handoffUrl: "https://app.ophalo.com/keep/share-sms/strip-token",
      expiresAtUtc: "2026-07-19T23:00:00Z",
    });
    const user = userEvent.setup();
    renderStrip();

    await user.click(screen.getByRole("button", { name: /Scan to text/i }));

    const qr = await screen.findByTestId("qr");
    expect(qr.getAttribute("data-value")).toBe("https://app.ophalo.com/keep/share-sms/strip-token");
    expect(qr.getAttribute("data-value")).not.toContain("5555550101");
  });

  it("marks the QR stale on edit and remints only after explicit update", async () => {
    mockCreateSmsHandoff
      .mockResolvedValueOnce({
        handoffUrl: "https://app.ophalo.com/keep/share-sms/original",
        expiresAtUtc: "2026-07-19T23:00:00Z",
      })
      .mockResolvedValueOnce({
        handoffUrl: "https://app.ophalo.com/keep/share-sms/updated",
        expiresAtUtc: "2026-07-19T23:00:00Z",
      });
    const user = userEvent.setup();
    renderStrip();

    await user.click(screen.getByRole("button", { name: /Scan to text/i }));
    await screen.findByTestId("qr");

    const textarea = screen.getByRole("textbox");
    await user.type(textarea, " edited");

    // Stale: prior QR is hidden, remint not yet triggered.
    expect(screen.queryByTestId("qr")).not.toBeInTheDocument();
    expect(mockCreateSmsHandoff).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole("button", { name: /Update QR for this message/i }));

    const qr = await screen.findByTestId("qr");
    expect(qr.getAttribute("data-value")).toBe("https://app.ophalo.com/keep/share-sms/updated");
    expect(mockCreateSmsHandoff).toHaveBeenCalledTimes(2);
  });

  it("a slow initial mint that resolves after an edit does not clear staleness or show the stale QR", async () => {
    let resolveInitial: (value: { handoffUrl: string; expiresAtUtc: string }) => void;
    const initialMint = new Promise<{ handoffUrl: string; expiresAtUtc: string }>((resolve) => {
      resolveInitial = resolve;
    });
    mockCreateSmsHandoff.mockReturnValueOnce(initialMint);
    const user = userEvent.setup();
    renderStrip();

    await user.click(screen.getByRole("button", { name: /Scan to text/i }));

    // Edit the draft while the initial mint is still in flight.
    const textarea = screen.getByRole("textbox");
    await user.type(textarea, " edited");
    expect(screen.queryByTestId("qr")).not.toBeInTheDocument();

    // The stale initial mint resolves after the edit — it must not overwrite stale state.
    resolveInitial!({
      handoffUrl: "https://app.ophalo.com/keep/share-sms/stale-original",
      expiresAtUtc: "2026-07-19T23:00:00Z",
    });

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Update QR for this message/i })).not.toBeDisabled()
    );
    expect(screen.queryByTestId("qr")).not.toBeInTheDocument();
  });

  it("calls onContactLaunched only after the explicit 'Done' confirmation, not on open", async () => {
    mockCreateSmsHandoff.mockResolvedValue({
      handoffUrl: "https://app.ophalo.com/keep/share-sms/strip-token",
      expiresAtUtc: "2026-07-19T23:00:00Z",
    });
    const user = userEvent.setup();
    const onContactLaunched = renderStrip();

    await user.click(screen.getByRole("button", { name: /Scan to text/i }));
    await screen.findByTestId("qr");
    expect(onContactLaunched).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: /Done — record this text/i }));
    expect(onContactLaunched).toHaveBeenCalledWith("outbound", "sms");
  });

  it("mobile 'Text' link launches sms: directly with the default message, without minting", () => {
    renderStrip();

    const link = screen.getByRole("link", { name: /^Text$/i });
    expect(link.getAttribute("href")).toBe(
      `sms:5555550101?&body=${encodeURIComponent(
        "Here is a link to your private request page: http://localhost:3000/keep/r/page-token"
      )}`
    );
    expect(mockCreateSmsHandoff).not.toHaveBeenCalled();
  });
});
