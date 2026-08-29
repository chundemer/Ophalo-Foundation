import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NoChargeDispositionForm } from "../NoChargeDispositionForm";

describe("NoChargeDispositionForm", () => {
  it("submits a trimmed reason", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue({ kind: "success" });
    render(<NoChargeDispositionForm busy={false} onSubmit={onSubmit} />);
    await user.type(screen.getByLabelText(/^Reason/), "  Warranty callback  ");
    await user.click(screen.getByRole("button", { name: /Record no charge/ }));
    expect(onSubmit).toHaveBeenCalledWith("Warranty callback");
  });

  it("blocks an empty reason and focuses the field", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(<NoChargeDispositionForm busy={false} onSubmit={onSubmit} />);
    await user.click(screen.getByRole("button", { name: /Record no charge/ }));
    expect(onSubmit).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent(/reason is required/i);
    expect(screen.getByLabelText(/^Reason/)).toHaveFocus();
  });

  it("keeps the draft and re-focuses on a server validation failure", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue({ kind: "validation-failure", code: "ActualWork.DispositionReasonTooLong" });
    render(<NoChargeDispositionForm busy={false} onSubmit={onSubmit} />);
    await user.type(screen.getByLabelText(/^Reason/), "some reason");
    await user.click(screen.getByRole("button", { name: /Record no charge/ }));
    expect(screen.getByLabelText(/^Reason/)).toHaveValue("some reason");
    expect(screen.getByLabelText(/^Reason/)).toHaveFocus();
  });

  it("disables the control while busy", () => {
    render(<NoChargeDispositionForm busy onSubmit={vi.fn()} />);
    expect(screen.getByRole("button", { name: /Saving…/ })).toBeDisabled();
  });
});
