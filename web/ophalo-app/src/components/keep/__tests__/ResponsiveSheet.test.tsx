import { describe, it, expect } from "vitest";
import { useState } from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ResponsiveSheet } from "../ResponsiveSheet";

function Harness({
  header,
  footer,
  children,
}: {
  header?: React.ReactNode;
  footer?: React.ReactNode;
  children: React.ReactNode;
}) {
  const [open, setOpen] = useState(false);
  return (
    <div>
      <button type="button" onClick={() => setOpen(true)}>
        Open sheet
      </button>
      {open && (
        <ResponsiveSheet
          onClose={() => setOpen(false)}
          labelledBy="sheet-heading"
          header={header ?? <h2 id="sheet-heading">Sheet title</h2>}
          footer={footer}
        >
          {children}
        </ResponsiveSheet>
      )}
    </div>
  );
}

describe("ResponsiveSheet", () => {
  it("renders as a dialog and focuses the first focusable descendant", async () => {
    const user = userEvent.setup();
    render(
      <Harness>
        <button type="button">First</button>
      </Harness>,
    );
    await user.click(screen.getByText("Open sheet"));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("First")).toHaveFocus();
  });

  it("closes on Escape and restores focus to the trigger", async () => {
    const user = userEvent.setup();
    render(
      <Harness>
        <button type="button">Body content</button>
      </Harness>,
    );
    const trigger = screen.getByText("Open sheet");
    await user.click(trigger);
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it("closes on backdrop click by default", async () => {
    const user = userEvent.setup();
    render(
      <Harness>
        <button type="button">Body content</button>
      </Harness>,
    );
    await user.click(screen.getByText("Open sheet"));
    const dialog = screen.getByRole("dialog");
    const overlay = dialog.parentElement!;
    fireEvent.click(overlay);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("renders header and footer as non-scrolling regions around the body", async () => {
    const user = userEvent.setup();
    render(
      <Harness
        header={<h2 id="sheet-heading">Sheet title</h2>}
        footer={<button type="button">Save</button>}
      >
        <p>Body</p>
      </Harness>,
    );
    await user.click(screen.getByText("Open sheet"));
    expect(screen.getByText("Sheet title")).toBeInTheDocument();
    expect(screen.getByText("Body")).toBeInTheDocument();
    expect(screen.getByText("Save")).toBeInTheDocument();
  });

  it("exposes an accessible name from labelledBy", async () => {
    const user = userEvent.setup();
    render(
      <Harness>
        <p>Body</p>
      </Harness>,
    );
    await user.click(screen.getByText("Open sheet"));
    expect(screen.getByRole("dialog", { name: "Sheet title" })).toBeInTheDocument();
  });

  it("exposes an accessible name from label when no header heading exists", async () => {
    const user = userEvent.setup();
    function LabelHarness() {
      const [open, setOpen] = useState(false);
      return (
        <div>
          <button type="button" onClick={() => setOpen(true)}>
            Open sheet
          </button>
          {open && (
            <ResponsiveSheet onClose={() => setOpen(false)} label="Edit service location">
              <p>Body</p>
            </ResponsiveSheet>
          )}
        </div>
      );
    }
    render(<LabelHarness />);
    await user.click(screen.getByText("Open sheet"));
    expect(screen.getByRole("dialog", { name: "Edit service location" })).toBeInTheDocument();
  });
});
