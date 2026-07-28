import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TriagePanel } from "../DetailPanels";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// GAP-047: priority mutation failures must not fail silently — a conflict disables further
// edits until refresh, and any other failure restores the server value with a local message.

const mockSetBusinessPriority = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    setBusinessPriority: (...args: unknown[]) => mockSetBusinessPriority(...args),
  },
  ApiError: class ApiError extends Error {
    status: number;
    constructor(status: number, _code: string | undefined, message: string) {
      super(message);
      this.status = status;
    }
  },
}));

function baseDetail(overrides: Partial<KeepRequestDetailResult> = {}): KeepRequestDetailResult {
  return { ...mockRequestDetails["mock-req-001"], businessPriority: null, ...overrides };
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("TriagePanel — priority failure handling (GAP-047)", () => {
  it("disables the select while submitting and re-enables it after success", async () => {
    const user = userEvent.setup();
    const detail = baseDetail();
    const onDetailUpdated = vi.fn();
    let resolveMutation: (value: KeepRequestDetailResult) => void = () => {};
    mockSetBusinessPriority.mockReturnValue(
      new Promise((resolve) => { resolveMutation = resolve; }),
    );

    render(<TriagePanel detail={detail} onDetailUpdated={onDetailUpdated} />);
    const select = screen.getByRole("combobox");
    await user.selectOptions(select, "urgent");

    expect(select).toBeDisabled();
    resolveMutation({ ...detail, businessPriority: "urgent", version: detail.version + 1 });

    await waitFor(() => expect(select).not.toBeDisabled());
    expect(onDetailUpdated).toHaveBeenCalled();
  });

  it("on a 409 conflict, disables the select and shows the conflict message", async () => {
    const user = userEvent.setup();
    const detail = baseDetail();
    const { ApiError } = await import("../../../lib/apiClient");
    mockSetBusinessPriority.mockRejectedValue(new ApiError(409, "RequestChanged", "conflict"));

    render(<TriagePanel detail={detail} onDetailUpdated={vi.fn()} />);
    const select = screen.getByRole("combobox");
    await user.selectOptions(select, "urgent");

    await waitFor(() => expect(select).toBeDisabled());
    expect(screen.getByRole("alert")).toHaveTextContent(/updated by another team member/i);
    // The reverted display value is the pre-edit server value, not the stale optimistic pick.
    expect(select).toHaveValue("");
  });

  it("on a non-conflict failure, restores the server value and shows a generic message", async () => {
    const user = userEvent.setup();
    const detail = baseDetail();
    mockSetBusinessPriority.mockRejectedValue(new Error("network down"));

    render(<TriagePanel detail={detail} onDetailUpdated={vi.fn()} />);
    const select = screen.getByRole("combobox");
    await user.selectOptions(select, "urgent");

    await waitFor(() => expect(select).not.toBeDisabled());
    expect(screen.getByRole("alert")).toHaveTextContent(/could not save priority/i);
    expect(select).toHaveValue("");
  });
});
