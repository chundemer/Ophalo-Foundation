import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { LookupGate } from "../LookupGate";
import type { PhoneLookupResult } from "../../../lib/apiClient";

// GAP-051: the phone-lookup input must format the number as the staff member
// types/pastes it, while still firing lookup with the canonical 10-digit value.

const mockLookup = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>(
    "../../../lib/apiClient",
  );
  return {
    ...actual,
    api: {
      ...actual.api,
      lookupRequestByPhone: (...args: unknown[]) => mockLookup(...args),
    },
  };
});

const emptyResult: PhoneLookupResult = {
  customer: null,
  activeRequests: [],
  hasMoreActiveRequests: false,
};

function renderGate() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onLookupSuccess = vi.fn();
  render(
    <QueryClientProvider client={queryClient}>
      <LookupGate
        onClose={() => {}}
        onLookupSuccess={onLookupSuccess}
        isPastDue={false}
        isReadOnly={false}
      />
    </QueryClientProvider>,
  );
  return { onLookupSuccess };
}

beforeEach(() => {
  mockLookup.mockReset();
  mockLookup.mockResolvedValue(emptyResult);
});

describe("LookupGate phone formatting", () => {
  it("formats typed digits and fires lookup with the canonical value", async () => {
    const user = userEvent.setup();
    renderGate();

    const input = screen.getByLabelText(/customer phone number/i);
    await user.type(input, "5555555555");

    expect(input).toHaveValue("(555) 555-5555");
    await waitFor(() => expect(mockLookup).toHaveBeenCalledWith("5555555555"));
  });

  it("normalizes a formatted paste with a +1 prefix", async () => {
    const user = userEvent.setup();
    renderGate();

    const input = screen.getByLabelText(/customer phone number/i);
    await user.click(input);
    await user.paste("+1 (555) 555-5555");

    expect(input).toHaveValue("(555) 555-5555");
    await waitFor(() => expect(mockLookup).toHaveBeenCalledWith("5555555555"));
  });

  it("shows an error hint for an invalid (too short) number and does not look up", async () => {
    const user = userEvent.setup();
    renderGate();

    const input = screen.getByLabelText(/customer phone number/i);
    await user.type(input, "555555");

    expect(screen.getByText("Please enter a 10-digit phone number.")).toBeInTheDocument();
    expect(mockLookup).not.toHaveBeenCalled();
  });
});
