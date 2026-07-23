import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { HandoffPanel } from "../HandoffPanel";
import type { IntakeStatusResult } from "../../../lib/apiClient";

// GAP-051 regression: Quick Capture -> "Text a Link" showed raw digits
// (5555555555) instead of (555) 555-5555 while the customer submits the same
// number formatted as they type on the public intake page.

const mockGetIntake = vi.fn();
const mockCreateHandoff = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>(
    "../../../lib/apiClient",
  );
  return {
    ...actual,
    api: {
      ...actual.api,
      getIntake: (...args: unknown[]) => mockGetIntake(...args),
      createIntakeSmsHandoff: (...args: unknown[]) => mockCreateHandoff(...args),
    },
  };
});

const activeIntake: IntakeStatusResult = {
  hasActiveLink: true,
  publicSlug: "apex-home-services",
  createdAtUtc: "2026-07-01T00:00:00Z",
};

function renderPanel() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <HandoffPanel onEnterForCustomer={() => {}} onNavigateSettings={() => {}} />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  mockGetIntake.mockReset();
  mockCreateHandoff.mockReset();
  mockGetIntake.mockResolvedValue(activeIntake);
});

describe("HandoffPanel phone formatting", () => {
  it("displays a full number formatted as (555) 555-5555, not raw digits", async () => {
    const user = userEvent.setup();
    renderPanel();

    const input = await screen.findByLabelText(/mobile number/i);
    await user.type(input, "5555555555");

    expect(input).toHaveValue("(555) 555-5555");
  });

  it("formats partial entry as the user types", async () => {
    const user = userEvent.setup();
    renderPanel();

    const input = await screen.findByLabelText(/mobile number/i);
    await user.type(input, "555555");

    expect(input).toHaveValue("(555) 555");
  });

  it("accepts a pasted +1-prefixed number and normalizes it for submission", async () => {
    const user = userEvent.setup();
    mockCreateHandoff.mockResolvedValue({
      handoffUrl: "https://example.com/h/abc",
      customerPhone: "5555555555",
      messageBody: "hi",
    });
    renderPanel();

    const input = await screen.findByLabelText(/mobile number/i);
    await user.click(input);
    await user.paste("+1 (555) 555-5555");

    expect(input).toHaveValue("(555) 555-5555");

    await user.click(screen.getByRole("button", { name: "Prepare text" }));

    await waitFor(() => expect(mockCreateHandoff).toHaveBeenCalledWith("5555555555"));
  });

  it("rejects an invalid (too short) number from submission", async () => {
    const user = userEvent.setup();
    renderPanel();

    const input = await screen.findByLabelText(/mobile number/i);
    await user.type(input, "555555");

    expect(screen.getByRole("button", { name: "Prepare text" })).toBeDisabled();
    expect(screen.getByText("Please enter a 10-digit phone number.")).toBeInTheDocument();
  });

  it("supports correction after a typo without corrupting the canonical value", async () => {
    const user = userEvent.setup();
    mockCreateHandoff.mockResolvedValue({
      handoffUrl: "https://example.com/h/abc",
      customerPhone: "5555555555",
      messageBody: "hi",
    });
    renderPanel();

    const input = await screen.findByLabelText(/mobile number/i);
    await user.type(input, "5555559999");
    await user.type(input, "{Backspace}{Backspace}{Backspace}{Backspace}5555");

    expect(input).toHaveValue("(555) 555-5555");

    await user.click(screen.getByRole("button", { name: "Prepare text" }));
    await waitFor(() => expect(mockCreateHandoff).toHaveBeenCalledWith("5555555555"));
  });
});
