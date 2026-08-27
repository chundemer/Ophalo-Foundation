import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ActualWorkRecoveryDrawer } from "../ActualWorkRecoveryDrawer";

const mockGetActualWorkRecorderCandidates = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    getActualWorkRecorderCandidates: (...args: unknown[]) => mockGetActualWorkRecorderCandidates(...args),
  },
}));

function renderWithClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

const draft = {
  id: "draft-1",
  status: "Draft",
  outcome: null,
  completionNote: null,
  submittedAtUtc: null,
  concurrencyVersion: "v3",
  isRecorder: false as const,
  recorderAccountUserId: "au-current",
  recorderDisplayName: "Sam Field",
  lines: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  mockGetActualWorkRecorderCandidates.mockResolvedValue({
    candidates: [
      { accountUserId: "au-current", displayName: "Sam Field", role: "Operator" },
      { accountUserId: "au-next", displayName: "Jordan Lead", role: "Operator" },
    ],
  });
});

describe("ActualWorkRecoveryDrawer", () => {
  it("excludes the current recorder from the candidate list", async () => {
    renderWithClient(<ActualWorkRecoveryDrawer draft={draft} onClose={vi.fn()} onTransfer={vi.fn()} />);
    await waitFor(() => expect(screen.getByRole("option", { name: /Jordan Lead/ })).toBeInTheDocument());
    expect(screen.queryByRole("option", { name: /Sam Field/ })).not.toBeInTheDocument();
  });

  it("requires a recorder and a reason before calling onTransfer", async () => {
    const onTransfer = vi.fn();
    renderWithClient(<ActualWorkRecoveryDrawer draft={draft} onClose={vi.fn()} onTransfer={onTransfer} />);
    await waitFor(() => screen.getByRole("option", { name: /Jordan Lead/ }));

    await userEvent.click(screen.getByRole("button", { name: "Reassign recorder" }));
    expect(screen.getByText(/Choose a team member/)).toBeInTheDocument();
    expect(onTransfer).not.toHaveBeenCalled();

    await userEvent.selectOptions(screen.getByLabelText("New recorder"), "au-next");
    await userEvent.click(screen.getByRole("button", { name: "Reassign recorder" }));
    expect(screen.getByText(/reason is required/)).toBeInTheDocument();
    expect(onTransfer).not.toHaveBeenCalled();
  });

  it("submits the selected recorder and reason, then closes on 'transferred'", async () => {
    const onTransfer = vi.fn().mockResolvedValue("transferred");
    const onClose = vi.fn();
    renderWithClient(<ActualWorkRecoveryDrawer draft={draft} onClose={onClose} onTransfer={onTransfer} />);
    await waitFor(() => screen.getByRole("option", { name: /Jordan Lead/ }));

    await userEvent.selectOptions(screen.getByLabelText("New recorder"), "au-next");
    await userEvent.type(screen.getByLabelText("Reason"), "Sam went home sick");
    await userEvent.click(screen.getByRole("button", { name: "Reassign recorder" }));

    await waitFor(() =>
      expect(onTransfer).toHaveBeenCalledWith("au-next", "Jordan Lead", "Sam went home sick"),
    );
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("closes the drawer on a 'stale' outcome (the card surfaces the warning)", async () => {
    const onTransfer = vi.fn().mockResolvedValue("stale");
    const onClose = vi.fn();
    renderWithClient(<ActualWorkRecoveryDrawer draft={draft} onClose={onClose} onTransfer={onTransfer} />);
    await waitFor(() => screen.getByRole("option", { name: /Jordan Lead/ }));

    await userEvent.selectOptions(screen.getByLabelText("New recorder"), "au-next");
    await userEvent.type(screen.getByLabelText("Reason"), "reason");
    await userEvent.click(screen.getByRole("button", { name: "Reassign recorder" }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("keeps the drawer open and shows a message on 'ineligible'", async () => {
    const onTransfer = vi.fn().mockResolvedValue("ineligible");
    const onClose = vi.fn();
    renderWithClient(<ActualWorkRecoveryDrawer draft={draft} onClose={onClose} onTransfer={onTransfer} />);
    await waitFor(() => screen.getByRole("option", { name: /Jordan Lead/ }));

    await userEvent.selectOptions(screen.getByLabelText("New recorder"), "au-next");
    await userEvent.type(screen.getByLabelText("Reason"), "reason");
    await userEvent.click(screen.getByRole("button", { name: "Reassign recorder" }));

    await waitFor(() => expect(screen.getByText(/can't be assigned as the recorder/)).toBeInTheDocument());
    expect(onClose).not.toHaveBeenCalled();
  });

  it("disables submit and explains when no other team member is eligible", async () => {
    mockGetActualWorkRecorderCandidates.mockResolvedValueOnce({
      candidates: [{ accountUserId: "au-current", displayName: "Sam Field", role: "Operator" }],
    });
    renderWithClient(<ActualWorkRecoveryDrawer draft={draft} onClose={vi.fn()} onTransfer={vi.fn()} />);
    await waitFor(() => expect(screen.getByText(/No other team member is eligible/)).toBeInTheDocument());
    expect(screen.getByRole("button", { name: "Reassign recorder" })).toBeDisabled();
  });
});
