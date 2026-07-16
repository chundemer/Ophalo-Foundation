import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CaptureForm } from "../CaptureForm";
import { QuickCapture } from "../../QuickCapture";
import type { CaptureFormDraft } from "../utils";

// ---------------------------------------------------------------------------
// API mock
// ---------------------------------------------------------------------------
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

// SuccessPanel uses VITE_PUBLIC_BASE_URL and makes additional queries — mock it
// to a minimal stub so we can test the "Capture Another" callback in isolation.
vi.mock("../SuccessPanel", () => ({
  SuccessPanel: ({ onCaptureAnother }: { onCaptureAnother: () => void }) => (
    <button type="button" onClick={onCaptureAnother}>Capture Another</button>
  ),
}));

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
function queryClient() {
  return new QueryClient({ defaultOptions: { mutations: { retry: false } } });
}

function Wrapper({ children }: { children: React.ReactNode }) {
  return <QueryClientProvider client={queryClient()}>{children}</QueryClientProvider>;
}

const NO_MATCH = { customer: null, activeRequests: [] };

beforeEach(() => {
  vi.clearAllMocks();
  mockLookup.mockResolvedValue(NO_MATCH);
});

// ---------------------------------------------------------------------------
// CaptureForm isolation — onBack contract
// ---------------------------------------------------------------------------
describe("CaptureForm.onBack", () => {
  it("passes all current field values including address disclosure to onBack", async () => {
    const user = userEvent.setup();
    const onBack = vi.fn();

    render(
      <Wrapper>
        <CaptureForm
          lockedPhone="5551234567"
          prefill={null}
          isPastDue={false}
          isReadOnly={false}
          onSuccess={vi.fn()}
          onBack={onBack}
          onClose={vi.fn()}
        />
      </Wrapper>
    );

    await user.type(screen.getByLabelText(/customer name/i), "Jane Doe");
    await user.type(screen.getByLabelText(/email/i), "jane@example.com");
    await user.type(screen.getByLabelText(/description/i), "Needs an estimate");
    await user.selectOptions(screen.getByLabelText(/source/i), "phone");

    await user.click(screen.getByRole("button", { name: /\+ add service address/i }));
    await user.type(screen.getByPlaceholderText("Address line 1"), "123 Main St");
    await user.type(screen.getByPlaceholderText("City"), "Springfield");
    await user.type(screen.getByPlaceholderText("State"), "IL");
    await user.type(screen.getByPlaceholderText("ZIP"), "62701");

    await user.click(screen.getByRole("button", { name: /← back/i }));

    expect(onBack).toHaveBeenCalledOnce();
    const draft: CaptureFormDraft = onBack.mock.calls[0][0];
    expect(draft.name).toBe("Jane Doe");
    expect(draft.email).toBe("jane@example.com");
    expect(draft.description).toBe("Needs an estimate");
    expect(draft.source).toBe("phone");
    expect(draft.showAddress).toBe(true);
    expect(draft.addrLine1).toBe("123 Main St");
    expect(draft.addrCity).toBe("Springfield");
    expect(draft.addrState).toBe("IL");
    expect(draft.addrZip).toBe("62701");
  });

  it("passes draft via Change link as well as Back button", async () => {
    const user = userEvent.setup();
    const onBack = vi.fn();

    render(
      <Wrapper>
        <CaptureForm
          lockedPhone="5559876543"
          prefill={null}
          isPastDue={false}
          isReadOnly={false}
          onSuccess={vi.fn()}
          onBack={onBack}
          onClose={vi.fn()}
        />
      </Wrapper>
    );

    await user.type(screen.getByLabelText(/customer name/i), "Bob");
    await user.click(screen.getByRole("button", { name: /change/i }));

    expect(onBack).toHaveBeenCalledOnce();
    expect(onBack.mock.calls[0][0].name).toBe("Bob");
  });
});

// ---------------------------------------------------------------------------
// CaptureForm isolation — initialDraft initialization
// ---------------------------------------------------------------------------
describe("CaptureForm.initialDraft", () => {
  it("initializes all fields from initialDraft including address", () => {
    const draft: CaptureFormDraft = {
      name: "Alice",
      email: "alice@example.com",
      description: "Replace faucet",
      source: "walk_in",
      showAddress: true,
      addrLine1: "456 Oak Ave",
      addrLine2: "Apt 2",
      addrCity: "Portland",
      addrState: "OR",
      addrZip: "97201",
    };

    render(
      <Wrapper>
        <CaptureForm
          lockedPhone="5550001111"
          prefill={null}
          initialDraft={draft}
          isPastDue={false}
          isReadOnly={false}
          onSuccess={vi.fn()}
          onBack={vi.fn()}
          onClose={vi.fn()}
        />
      </Wrapper>
    );

    expect(screen.getByLabelText(/customer name/i)).toHaveValue("Alice");
    expect(screen.getByLabelText(/email/i)).toHaveValue("alice@example.com");
    expect(screen.getByLabelText(/description/i)).toHaveValue("Replace faucet");
    expect(screen.getByLabelText(/source/i)).toHaveValue("walk_in");
    expect(screen.getByPlaceholderText("Address line 1")).toHaveValue("456 Oak Ave");
    expect(screen.getByPlaceholderText("Address line 2 (optional)")).toHaveValue("Apt 2");
    expect(screen.getByPlaceholderText("City")).toHaveValue("Portland");
    expect(screen.getByPlaceholderText("State")).toHaveValue("OR");
    expect(screen.getByPlaceholderText("ZIP")).toHaveValue("97201");
  });

  it("initialDraft takes precedence over prefill", () => {
    const draft: CaptureFormDraft = {
      name: "Draft Name",
      email: "draft@example.com",
      description: "Draft desc",
      source: "",
      showAddress: false,
      addrLine1: "",
      addrLine2: "",
      addrCity: "",
      addrState: "",
      addrZip: "",
    };

    render(
      <Wrapper>
        <CaptureForm
          lockedPhone="5550002222"
          prefill={{ name: "Prefill Name", email: "prefill@example.com" }}
          initialDraft={draft}
          isPastDue={false}
          isReadOnly={false}
          onSuccess={vi.fn()}
          onBack={vi.fn()}
          onClose={vi.fn()}
        />
      </Wrapper>
    );

    expect(screen.getByLabelText(/customer name/i)).toHaveValue("Draft Name");
    expect(screen.getByLabelText(/email/i)).toHaveValue("draft@example.com");
  });
});

// ---------------------------------------------------------------------------
// QuickCapture integration — draft cycle
// ---------------------------------------------------------------------------
describe("QuickCapture draft cycle", () => {
  it("restores typed form values after Change → lookup → re-entry", async () => {
    const user = userEvent.setup();

    render(
      <Wrapper>
        <QuickCapture onClose={vi.fn()} />
      </Wrapper>
    );

    // Enter 10-digit phone → auto-triggers lookup → no match → capture stage
    await user.type(screen.getByLabelText(/customer phone/i), "5551234567");
    await waitFor(() => screen.getByLabelText(/customer name/i));

    // Fill form including address disclosure
    await user.type(screen.getByLabelText(/customer name/i), "Maria");
    await user.type(screen.getByLabelText(/description/i), "Water heater repair");
    await user.selectOptions(screen.getByLabelText(/source/i), "text");
    await user.click(screen.getByRole("button", { name: /\+ add service address/i }));
    await user.type(screen.getByPlaceholderText("Address line 1"), "789 Pine Rd");

    // Click Change → draft saved, back to lookup
    await user.click(screen.getByRole("button", { name: /change/i }));
    await waitFor(() => screen.getByLabelText(/customer phone/i));

    // New lookup with same or different phone → re-enters capture with draft restored
    await user.clear(screen.getByLabelText(/customer phone/i));
    await user.type(screen.getByLabelText(/customer phone/i), "5559876543");
    await waitFor(() => screen.getByLabelText(/customer name/i));

    expect(screen.getByLabelText(/customer name/i)).toHaveValue("Maria");
    expect(screen.getByLabelText(/description/i)).toHaveValue("Water heater repair");
    expect(screen.getByLabelText(/source/i)).toHaveValue("text");
    expect(screen.getByPlaceholderText("Address line 1")).toHaveValue("789 Pine Rd");
  });

  it("Capture Another starts with a clean form", async () => {
    const user = userEvent.setup();

    mockCreate.mockResolvedValue({
      requestId: "req-1",
      referenceCode: "REF-001",
      pageToken: "tok-abc",
    });

    render(
      <Wrapper>
        <QuickCapture onClose={vi.fn()} />
      </Wrapper>
    );

    // First lookup → capture
    await user.type(screen.getByLabelText(/customer phone/i), "5551234567");
    await waitFor(() => screen.getByLabelText(/customer name/i));

    // Fill required fields and submit
    await user.type(screen.getByLabelText(/customer name/i), "Carlos");
    await user.type(screen.getByLabelText(/description/i), "Broken pipe");
    await user.selectOptions(screen.getByLabelText(/source/i), "voicemail");
    await user.click(screen.getByRole("button", { name: /capture request/i }));

    // Wait for success panel
    await waitFor(() => screen.getByRole("button", { name: /capture another/i }));

    // Capture Another → back to lookup
    await user.click(screen.getByRole("button", { name: /capture another/i }));
    await waitFor(() => screen.getByLabelText(/customer phone/i));

    // Second lookup → should enter clean capture form
    await user.type(screen.getByLabelText(/customer phone/i), "5550001111");
    await waitFor(() => screen.getByLabelText(/customer name/i));

    expect(screen.getByLabelText(/customer name/i)).toHaveValue("");
    expect(screen.getByLabelText(/description/i)).toHaveValue("");
    expect(screen.getByLabelText(/source/i)).toHaveValue("");
  });
});
