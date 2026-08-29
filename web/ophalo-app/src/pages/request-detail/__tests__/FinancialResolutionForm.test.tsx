import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FinancialResolutionForm } from "../FinancialResolutionForm";

const costBlocker = { lineId: "line-1", displayNameSnapshot: "Capacitor", sellPriceMissing: false, standardExpectedDirectCostMissing: true };
const bothBlocker = { lineId: "line-1", displayNameSnapshot: "Capacitor", sellPriceMissing: true, standardExpectedDirectCostMissing: true };

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function renderForm(onSubmit: any, busy = false, blocker = costBlocker) {
  render(<FinancialResolutionForm blocker={blocker} busy={busy} onSubmit={onSubmit} />);
}

describe("FinancialResolutionForm", () => {
  it("submits only the missing component with basis and reason", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue({ kind: "success" });
    renderForm(onSubmit);
    await user.type(screen.getByLabelText(/Unit standard direct cost/), "42");
    await user.selectOptions(screen.getByLabelText(/How was this determined/), "SupplierReceipt");
    await user.type(screen.getByLabelText(/^Reason/), "Receipt attached");
    await user.click(screen.getByRole("button", { name: /Save resolution/ }));
    expect(onSubmit).toHaveBeenCalledWith("line-1", {
      resolvedUnitSellPrice: null,
      resolvedUnitStandardExpectedDirectCost: 42,
      basis: "SupplierReceipt",
      reason: "Receipt attached",
    });
    expect(screen.queryByLabelText(/Unit sell price/)).not.toBeInTheDocument();
  });

  it("blocks a client-side submit with no reason and never calls onSubmit", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    renderForm(onSubmit);
    await user.type(screen.getByLabelText(/Unit standard direct cost/), "42");
    await user.selectOptions(screen.getByLabelText(/How was this determined/), "Other");
    await user.click(screen.getByRole("button", { name: /Save resolution/ }));
    expect(onSubmit).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent(/reason is required/i);
  });

  it("preserves the draft and focuses the reason field on a server validation failure", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue({ kind: "validation-failure", code: "ActualWork.FinancialResolutionReasonTooLong" });
    renderForm(onSubmit);
    await user.type(screen.getByLabelText(/Unit standard direct cost/), "42");
    await user.selectOptions(screen.getByLabelText(/How was this determined/), "Other");
    await user.type(screen.getByLabelText(/^Reason/), "too long");
    await user.click(screen.getByRole("button", { name: /Save resolution/ }));
    expect(screen.getByLabelText(/Unit standard direct cost/)).toHaveValue(42);
    expect(screen.getByLabelText(/^Reason/)).toHaveFocus();
  });

  it("resolves only price when both components are missing, sending cost as null", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue({ kind: "success" });
    renderForm(onSubmit, false, bothBlocker);
    await user.type(screen.getByLabelText(/Unit sell price/), "120");
    await user.selectOptions(screen.getByLabelText(/How was this determined/), "OwnerSetPrice");
    await user.type(screen.getByLabelText(/^Reason/), "Owner-set");
    await user.click(screen.getByRole("button", { name: /Save resolution/ }));
    expect(onSubmit).toHaveBeenCalledWith("line-1", {
      resolvedUnitSellPrice: 120,
      resolvedUnitStandardExpectedDirectCost: null,
      basis: "OwnerSetPrice",
      reason: "Owner-set",
    });
  });

  it("resolves only cost when both components are missing, sending price as null", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue({ kind: "success" });
    renderForm(onSubmit, false, bothBlocker);
    await user.type(screen.getByLabelText(/Unit standard direct cost/), "48");
    await user.selectOptions(screen.getByLabelText(/How was this determined/), "SupplierReceipt");
    await user.type(screen.getByLabelText(/^Reason/), "Receipt");
    await user.click(screen.getByRole("button", { name: /Save resolution/ }));
    expect(onSubmit).toHaveBeenCalledWith("line-1", {
      resolvedUnitSellPrice: null,
      resolvedUnitStandardExpectedDirectCost: 48,
      basis: "SupplierReceipt",
      reason: "Receipt",
    });
  });

  it("blocks a submit with neither component entered", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    renderForm(onSubmit, false, bothBlocker);
    await user.selectOptions(screen.getByLabelText(/How was this determined/), "Other");
    await user.type(screen.getByLabelText(/^Reason/), "n/a");
    await user.click(screen.getByRole("button", { name: /Save resolution/ }));
    expect(onSubmit).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent(/at least one of the missing values/i);
    expect(screen.getByLabelText(/Unit sell price/)).toHaveFocus();
  });

  it("rejects a negative value client-side and focuses the field", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    renderForm(onSubmit);
    fireEvent.change(screen.getByLabelText(/Unit standard direct cost/), { target: { value: "-5" } });
    await user.selectOptions(screen.getByLabelText(/How was this determined/), "Other");
    await user.type(screen.getByLabelText(/^Reason/), "typo");
    await user.click(screen.getByRole("button", { name: /Save resolution/ }));
    expect(onSubmit).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent(/zero or more/i);
    expect(screen.getByLabelText(/Unit standard direct cost/)).toHaveFocus();
  });

  it("disables inputs and the button while busy", () => {
    renderForm(vi.fn(), true);
    expect(screen.getByLabelText(/Unit standard direct cost/)).toBeDisabled();
    expect(screen.getByRole("button", { name: /Saving…/ })).toBeDisabled();
  });
});
