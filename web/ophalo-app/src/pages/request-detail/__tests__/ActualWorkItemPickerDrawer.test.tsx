import { describe, it, expect, vi } from "vitest";
import { useRef } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ActualWorkItemPickerDrawer } from "../ActualWorkItemPickerDrawer";

function Harness({
  onClose = vi.fn(),
  connectionFailureBanner,
}: {
  onClose?: () => void;
  connectionFailureBanner?: React.ReactNode;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  return (
    <ActualWorkItemPickerDrawer
      onClose={onClose}
      initialFocus={inputRef}
      connectionFailureBanner={connectionFailureBanner}
    >
      <input ref={inputRef} aria-label="Search" />
    </ActualWorkItemPickerDrawer>
  );
}

describe("ActualWorkItemPickerDrawer", () => {
  it("renders a named modal dialog hosting its children", () => {
    render(<Harness />);

    const dialog = screen.getByRole("dialog", { name: "Add work & materials" });
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(dialog.contains(screen.getByLabelText("Search"))).toBe(true);
  });

  it("moves initial focus to the hosted search input", () => {
    render(<Harness />);

    expect(screen.getByLabelText("Search")).toHaveFocus();
  });

  it("closes on the Done button and on Escape, but not on a backdrop click", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    const { container } = render(<Harness onClose={onClose} />);

    await user.click(container.querySelector<HTMLElement>(".fixed.inset-0")!);
    expect(onClose).not.toHaveBeenCalled();

    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole("button", { name: "Done" }));
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it("renders the connection-failure slot inside the dialog", () => {
    render(<Harness connectionFailureBanner={<div role="alert">Add failed</div>} />);

    const dialog = screen.getByRole("dialog", { name: "Add work & materials" });
    expect(dialog.contains(screen.getByRole("alert"))).toBe(true);
  });
});
