import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PublicLinkSection } from "../PublicLinkSection";
import { ApiError } from "../../../lib/apiClient";
import type { IntakeStatusResult } from "../../../lib/apiClient";

// GAP-036b: replacement is a destructive-dialog contract — the confirm action stays
// disabled until an exact, case-sensitive "REPLACE" value is typed, and the dialog must
// leave the link untouched on cancel/escape/validation failure.

const mockGetIntake = vi.fn();
const mockReplaceIntake = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>(
    "../../../lib/apiClient",
  );
  return {
    ...actual,
    api: {
      ...actual.api,
      getIntake: (...args: unknown[]) => mockGetIntake(...args),
      replaceIntake: (...args: unknown[]) => mockReplaceIntake(...args),
    },
  };
});

const activeIntake: IntakeStatusResult = {
  hasActiveLink: true,
  publicSlug: "apex-home-services",
  createdAtUtc: "2026-07-01T00:00:00Z",
};

function renderSection() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <PublicLinkSection businessName="Apex Home Services" logoUrl="" />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  mockGetIntake.mockReset();
  mockReplaceIntake.mockReset();
  mockGetIntake.mockResolvedValue(activeIntake);
});

describe("PublicLinkSection replace-link dialog", () => {
  it("opens an accessible dialog with the confirm action disabled until REPLACE is typed exactly", async () => {
    const user = userEvent.setup();
    renderSection();

    await user.click(await screen.findByText("Replace link (breaks old shared links)"));

    const dialog = screen.getByRole("dialog", { name: /replace this link/i });
    const confirmButton = screen.getByRole("button", { name: /yes, replace link/i });
    expect(confirmButton).toBeDisabled();

    const input = screen.getByLabelText(/type replace to confirm/i);
    await user.type(input, "replace");
    expect(confirmButton).toBeDisabled();

    await user.clear(input);
    await user.type(input, "REPLACE");
    expect(confirmButton).toBeEnabled();
    expect(mockReplaceIntake).not.toHaveBeenCalled();

    void dialog;
  });

  it("cancel leaves the link unchanged and returns focus to the trigger", async () => {
    const user = userEvent.setup();
    renderSection();

    const trigger = await screen.findByText("Replace link (breaks old shared links)");
    await user.click(trigger);
    await user.click(screen.getByRole("button", { name: /cancel/i }));

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(mockReplaceIntake).not.toHaveBeenCalled();
    expect(trigger).toHaveFocus();
  });

  it("escape leaves the link unchanged and returns focus to the trigger", async () => {
    const user = userEvent.setup();
    renderSection();

    const trigger = await screen.findByText("Replace link (breaks old shared links)");
    await user.click(trigger);
    await user.keyboard("{Escape}");

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(mockReplaceIntake).not.toHaveBeenCalled();
    expect(trigger).toHaveFocus();
  });

  it("contains Tab focus within the dialog (does not leak to controls behind it)", async () => {
    const user = userEvent.setup();
    renderSection();

    await user.click(await screen.findByText("Replace link (breaks old shared links)"));
    const input = screen.getByLabelText(/type replace to confirm/i);
    expect(input).toHaveFocus();

    // Enable the confirm button so all three dialog controls are in the tab order.
    await user.type(input, "REPLACE");
    const confirmButton = screen.getByRole("button", { name: /yes, replace link/i });
    const cancelButton = screen.getByRole("button", { name: /cancel/i });

    await user.tab();
    expect(confirmButton).toHaveFocus();

    await user.tab();
    expect(cancelButton).toHaveFocus();

    // Forward from the last control wraps back to the first — never escapes to the page.
    await user.tab();
    expect(input).toHaveFocus();

    // Backward from the first control wraps to the last.
    await user.tab({ shift: true });
    expect(cancelButton).toHaveFocus();
  });

  it("blocks Escape and backdrop dismissal while a replace request is in flight", async () => {
    const user = userEvent.setup();
    let resolveReplace: (value: unknown) => void = () => {};
    mockReplaceIntake.mockImplementation(
      () => new Promise((resolve) => { resolveReplace = resolve; }),
    );
    renderSection();

    await user.click(await screen.findByText("Replace link (breaks old shared links)"));
    await user.type(screen.getByLabelText(/type replace to confirm/i), "REPLACE");
    await user.click(screen.getByRole("button", { name: /yes, replace link/i }));

    expect(await screen.findByRole("button", { name: /replacing…/i })).toBeInTheDocument();

    await user.keyboard("{Escape}");
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    await user.click(screen.getByTestId("replace-link-dialog-backdrop"));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(mockReplaceIntake).toHaveBeenCalledTimes(1);

    resolveReplace({ rawToken: "new-raw-token", publicSlug: "apex-home-services-2", staleLinksWarning: true });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("surfaces a server confirmation-mismatch error and leaves the dialog open", async () => {
    const user = userEvent.setup();
    mockReplaceIntake.mockRejectedValue(
      new ApiError(400, "KeepPublicIntakeLink.ReplaceConfirmationInvalid", "Type REPLACE to confirm this action."),
    );
    renderSection();

    await user.click(await screen.findByText("Replace link (breaks old shared links)"));
    await user.type(screen.getByLabelText(/type replace to confirm/i), "REPLACE");
    await user.click(screen.getByRole("button", { name: /yes, replace link/i }));

    expect(await screen.findByText("Type REPLACE to confirm this action.")).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("successful replacement closes the dialog and shows the new one-time raw URL", async () => {
    const user = userEvent.setup();
    mockReplaceIntake.mockResolvedValue({
      rawToken: "new-raw-token",
      publicSlug: "apex-home-services-2",
      staleLinksWarning: true,
    });
    renderSection();

    await user.click(await screen.findByText("Replace link (breaks old shared links)"));
    await user.type(screen.getByLabelText(/type replace to confirm/i), "REPLACE");
    await user.click(screen.getByRole("button", { name: /yes, replace link/i }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(mockReplaceIntake).toHaveBeenCalledWith("REPLACE");
    expect(screen.getByText(/link created — copy now/i)).toBeInTheDocument();
  });
});
