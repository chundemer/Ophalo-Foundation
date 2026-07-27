import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NotifyCustomerPanel } from "../NotifyCustomerPanel";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// GAP-052b / ADR-451: post, prepare, and confirm are three separately attested actions.
// Launching the SMS/email draft (an external app handoff) must never itself confirm anything —
// only an explicit "I sent it" click may call ConfirmUpdateNotification. Phase is derived from
// detail.pendingNotification (not local-only state) so a reload/navigate-away-and-return
// resumes truthfully from the server's durable obligation.

const mockPrepare = vi.fn();
const mockConfirm = vi.fn();
const mockCreateSmsHandoff = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    prepareUpdateNotification: (...args: unknown[]) => mockPrepare(...args),
    confirmUpdateNotification: (...args: unknown[]) => mockConfirm(...args),
    createSmsHandoff: (...args: unknown[]) => mockCreateSmsHandoff(...args),
  },
  ApiError: class ApiError extends Error {
    status: number;
    code?: string;
    constructor(status: number, code: string | undefined, message: string) {
      super(message);
      this.status = status;
      this.code = code;
    }
  },
}));

vi.mock("react-qr-code", () => ({
  default: ({ value }: { value: string }) => <div data-testid="qr" data-value={value} />,
}));

function baseDetail(overrides: Partial<KeepRequestDetailResult> = {}): KeepRequestDetailResult {
  return {
    ...mockRequestDetails["mock-req-001"],
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubEnv("VITE_PUBLIC_BASE_URL", "http://localhost:3000");
  mockCreateSmsHandoff.mockResolvedValue({
    handoffUrl: "https://app.ophalo.com/keep/share-sms/mock-token",
    expiresAtUtc: "2026-07-26T23:00:00Z",
  });
});

describe("NotifyCustomerPanel — selection phase", () => {
  it("renders channel choice and preselects the customer's preferred channel", () => {
    const detail = baseDetail({
      customerEmail: "marcus@example.com",
      contactPreference: "email",
      pendingNotification: null,
    });

    render(
      <NotifyCustomerPanel
        requestId="req-77"
        detail={detail}
        relatedUpdateEventId="event-1"
        onDetailUpdated={() => {}}
        onDone={() => {}}
      />
    );

    expect(screen.getByRole("radio", { name: /Email.*preferred/ })).toHaveAttribute("aria-checked", "true");
  });

  it("hides the email option when the customer has no email on file", () => {
    const detail = baseDetail({ customerEmail: null, pendingNotification: null });

    render(
      <NotifyCustomerPanel
        requestId="req-77"
        detail={detail}
        relatedUpdateEventId="event-1"
        onDetailUpdated={() => {}}
        onDone={() => {}}
      />
    );

    expect(screen.queryByRole("radio", { name: /Email/ })).not.toBeInTheDocument();
  });

  it("'Not now' dismisses without calling prepare", async () => {
    const user = userEvent.setup();
    const onDone = vi.fn();
    const detail = baseDetail({ pendingNotification: null });

    render(
      <NotifyCustomerPanel
        requestId="req-77"
        detail={detail}
        relatedUpdateEventId="event-1"
        onDetailUpdated={() => {}}
        onDone={onDone}
      />
    );

    await user.click(screen.getByRole("button", { name: "Not now" }));

    expect(onDone).toHaveBeenCalledTimes(1);
    expect(mockPrepare).not.toHaveBeenCalled();
  });

  it("Continue calls prepare with the related update event and selected channel, never confirm", async () => {
    const user = userEvent.setup();
    const detail = baseDetail({ pendingNotification: null, version: "v1" });
    mockPrepare.mockResolvedValue({
      ...detail,
      version: "v2",
      pendingNotification: {
        relatedUpdateEventId: "event-1",
        channel: "sms",
        preparedAtUtc: "2026-07-26T20:00:00Z",
        canConfirmAsCurrentUser: true,
      },
    });

    render(
      <NotifyCustomerPanel
        requestId="req-77"
        detail={detail}
        relatedUpdateEventId="event-1"
        onDetailUpdated={() => {}}
        onDone={() => {}}
      />
    );

    await user.click(screen.getByRole("button", { name: "Continue" }));

    await waitFor(() =>
      expect(mockPrepare).toHaveBeenCalledWith(
        "req-77",
        { relatedUpdateEventId: "event-1", channel: "sms" },
        "v1",
      ),
    );
    expect(mockConfirm).not.toHaveBeenCalled();
  });
});

describe("NotifyCustomerPanel — prepared phase (mine)", () => {
  function preparedDetail(channel: "sms" | "email") {
    return baseDetail({
      customerEmail: "marcus@example.com",
      pendingNotification: {
        relatedUpdateEventId: "event-1",
        channel,
        preparedAtUtc: "2026-07-26T20:00:00Z",
        canConfirmAsCurrentUser: true,
      },
    });
  }

  it("desktop SMS: mints an opaque QR (never raw phone/message text)", async () => {
    const detail = preparedDetail("sms");

    render(
      <NotifyCustomerPanel
        requestId="req-77"
        detail={detail}
        relatedUpdateEventId="event-1"
        onDetailUpdated={() => {}}
        onDone={() => {}}
      />
    );

    await waitFor(() => expect(mockCreateSmsHandoff).toHaveBeenCalledWith("req-77", expect.any(String)));
    const qr = await screen.findByTestId("qr");
    expect(qr.getAttribute("data-value")).toBe("https://app.ophalo.com/keep/share-sms/mock-token");
    expect(qr.getAttribute("data-value")).not.toContain(detail.customerPhone);
  });

  it("email: renders a mailto: draft link including the private page link", () => {
    const detail = preparedDetail("email");

    render(
      <NotifyCustomerPanel
        requestId="req-77"
        detail={detail}
        relatedUpdateEventId="event-1"
        onDetailUpdated={() => {}}
        onDone={() => {}}
      />
    );

    const link = screen.getByRole("link", { name: "Open email draft" });
    expect(link.getAttribute("href")).toMatch(/^mailto:marcus@example\.com\?/);
    expect(decodeURIComponent(link.getAttribute("href") ?? "")).toContain(
      `http://localhost:3000/keep/r/${detail.pageToken}`,
    );
  });

  it("launching the draft never calls confirm — only the explicit confirm button does", async () => {
    const user = userEvent.setup();
    const detail = preparedDetail("email");
    mockConfirm.mockResolvedValue({ ...detail, pendingNotification: null });

    render(
      <NotifyCustomerPanel
        requestId="req-77"
        detail={detail}
        relatedUpdateEventId="event-1"
        onDetailUpdated={() => {}}
        onDone={() => {}}
      />
    );

    // Rendering the draft link is not a launch, but confirms it exists without side effects.
    expect(screen.getByRole("link", { name: "Open email draft" })).toBeInTheDocument();
    expect(mockConfirm).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "I sent it — Confirm" }));

    await waitFor(() =>
      expect(mockConfirm).toHaveBeenCalledWith(
        "req-77",
        { relatedUpdateEventId: "event-1", channel: "email" },
        detail.version,
      ),
    );
  });
});

describe("NotifyCustomerPanel — reload recovery / cross-actor", () => {
  it("resumes the prepared phase from detail.pendingNotification alone (no fresh post needed)", async () => {
    const detail = baseDetail({
      pendingNotification: {
        relatedUpdateEventId: "event-1",
        channel: "sms",
        preparedAtUtc: "2026-07-26T20:00:00Z",
        canConfirmAsCurrentUser: true,
      },
    });

    render(
      <NotifyCustomerPanel
        requestId="req-77"
        detail={detail}
        relatedUpdateEventId="event-1"
        onDetailUpdated={() => {}}
        onDone={() => {}}
      />
    );

    expect(screen.getByText(/Notify customer — text message/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "I sent it — Confirm" })).toBeInTheDocument();
  });

  it("shows a truthful non-confirmable state when another teammate prepared it", () => {
    const detail = baseDetail({
      pendingNotification: {
        relatedUpdateEventId: "event-1",
        channel: "sms",
        preparedAtUtc: "2026-07-26T20:00:00Z",
        canConfirmAsCurrentUser: false,
      },
    });

    render(
      <NotifyCustomerPanel
        requestId="req-77"
        detail={detail}
        relatedUpdateEventId="event-1"
        onDetailUpdated={() => {}}
        onDone={() => {}}
      />
    );

    expect(screen.getByText(/prepared by another teammate/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "I sent it — Confirm" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Prepare a new one" })).toBeInTheDocument();
  });

  it("confirm surfaces a truthful message on NotificationConfirmerMismatch instead of a generic error", async () => {
    const user = userEvent.setup();
    const detail = baseDetail({
      pendingNotification: {
        relatedUpdateEventId: "event-1",
        channel: "sms",
        preparedAtUtc: "2026-07-26T20:00:00Z",
        canConfirmAsCurrentUser: true,
      },
    });
    const { ApiError } = await import("../../../lib/apiClient");
    mockConfirm.mockRejectedValue(
      new ApiError(400, "KeepRequest.NotificationConfirmerMismatch", "mismatch"),
    );

    render(
      <NotifyCustomerPanel
        requestId="req-77"
        detail={detail}
        relatedUpdateEventId="event-1"
        onDetailUpdated={() => {}}
        onDone={() => {}}
      />
    );

    await user.click(screen.getByRole("button", { name: "I sent it — Confirm" }));

    expect(await screen.findByText(/prepared by another teammate/i)).toBeInTheDocument();
  });
});
