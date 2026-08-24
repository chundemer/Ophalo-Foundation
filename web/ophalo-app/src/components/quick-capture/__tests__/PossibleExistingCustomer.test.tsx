import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { QuickCapture } from "../../QuickCapture";
import type { PhoneLookupResult } from "../../../lib/apiClient";

// ADR-492: a request-phone-only match is continuity evidence, not confirmed identity. Staff must
// explicitly choose "Use existing customer details" or "Create as new customer" — neither path
// may fire implicitly from the lookup itself.

const mockLookup = vi.fn();
const mockCreate = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    lookupRequestByPhone: (...args: unknown[]) => mockLookup(...args),
    createRequest: (...args: unknown[]) => mockCreate(...args),
  },
  ApiError: class ApiError extends Error {
    status: number;
    constructor(status: number, message: string) {
      super(message);
      this.status = status;
    }
  },
}));

vi.mock("../SuccessPanel", () => ({
  SuccessPanel: () => <div>Success</div>,
}));

const CANDIDATE_ID = "11111111-1111-1111-1111-111111111111";

const POSSIBLE_CUSTOMER_RESULT: PhoneLookupResult = {
  customer: null,
  activeRequests: [],
  hasMoreActiveRequests: false,
  possibleCustomer: {
    candidateCustomerId: CANDIDATE_ID,
    name: "Jordan Reyes",
    phone: "5555550199",
    email: "jordan@example.com",
    activeRequests: [
      {
        requestId: "req-1",
        referenceCode: "KC-042",
        status: "in_progress",
        description: "Leaking faucet in kitchen",
        lastActivityAtUtc: null,
      },
    ],
    hasMoreActiveRequests: false,
  },
};

function renderQuickCapture() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const onSelectRequest = vi.fn();
  render(
    <QueryClientProvider client={queryClient}>
      <QuickCapture onClose={() => {}} onSelectRequest={onSelectRequest} />
    </QueryClientProvider>,
  );
  return { onSelectRequest };
}

async function submitPhoneLookup(user: ReturnType<typeof userEvent.setup>) {
  // LookupGate auto-fires the lookup once a full 10-digit number is entered — no submit click.
  const input = screen.getByLabelText(/phone/i);
  await user.type(input, "5555550199");
  await waitFor(() => expect(mockLookup).toHaveBeenCalled());
}

beforeEach(() => {
  mockLookup.mockReset();
  mockCreate.mockReset();
  mockLookup.mockResolvedValue(POSSIBLE_CUSTOMER_RESULT);
  mockCreate.mockResolvedValue({
    requestId: "req-new",
    referenceCode: "KC-099",
    pageToken: "tok",
  });
});

describe("Possible existing customer lookup result", () => {
  it("labels the result and shows active request cards", async () => {
    const user = userEvent.setup();
    renderQuickCapture();
    await submitPhoneLookup(user);

    await waitFor(() => expect(screen.getByText("Possible existing customer")).toBeInTheDocument());
    expect(screen.getByText("Jordan Reyes")).toBeInTheDocument();
    expect(screen.getByText("KC-042")).toBeInTheDocument();
  });

  it("navigates to the active request on card click", async () => {
    const user = userEvent.setup();
    const { onSelectRequest } = renderQuickCapture();
    await submitPhoneLookup(user);

    await waitFor(() => expect(screen.getByText("KC-042")).toBeInTheDocument());
    await user.click(screen.getByText("KC-042"));

    expect(onSelectRequest).toHaveBeenCalledWith("req-1");
  });

  it("sends existingCustomerId only via the explicit reuse action", async () => {
    const user = userEvent.setup();
    renderQuickCapture();
    await submitPhoneLookup(user);

    await waitFor(() => expect(screen.getByRole("button", { name: /use existing customer details/i })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /use existing customer details/i }));

    await user.type(screen.getByLabelText(/description/i), "Follow-up on leaking faucet");
    await user.selectOptions(screen.getByLabelText(/source/i), "phone");
    await user.click(screen.getByRole("button", { name: /capture request/i }));

    await waitFor(() => expect(mockCreate).toHaveBeenCalled());
    expect(mockCreate.mock.calls[0][0]).toMatchObject({ existingCustomerId: CANDIDATE_ID });
  });

  it("omits existingCustomerId when staff explicitly creates as new customer", async () => {
    const user = userEvent.setup();
    renderQuickCapture();
    await submitPhoneLookup(user);

    await waitFor(() => expect(screen.getByRole("button", { name: /create as new customer/i })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /create as new customer/i }));

    await user.type(screen.getByLabelText(/customer name/i), "New Customer");
    await user.type(screen.getByLabelText(/description/i), "New issue");
    await user.selectOptions(screen.getByLabelText(/source/i), "phone");
    await user.click(screen.getByRole("button", { name: /capture request/i }));

    await waitFor(() => expect(mockCreate).toHaveBeenCalled());
    expect(mockCreate.mock.calls[0][0].existingCustomerId).toBeUndefined();
  });
});
