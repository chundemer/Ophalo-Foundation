import { describe, it, expect } from "vitest";
import { useState } from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { KeepModal } from "../KeepModal";

function Harness({
  children,
  backdropClosable,
}: {
  children: React.ReactNode;
  backdropClosable?: boolean;
}) {
  const [open, setOpen] = useState(false);
  return (
    <div>
      <button type="button" onClick={() => setOpen(true)}>
        Open modal
      </button>
      {open && (
        <KeepModal onClose={() => setOpen(false)} backdropClosable={backdropClosable}>
          {children}
        </KeepModal>
      )}
    </div>
  );
}

describe("KeepModal", () => {
  it("focuses the first focusable element inside the panel on open", async () => {
    const user = userEvent.setup();
    render(
      <Harness>
        <button type="button">First</button>
        <button type="button">Second</button>
      </Harness>,
    );
    await user.click(screen.getByText("Open modal"));
    expect(screen.getByText("First")).toHaveFocus();
  });

  it("focuses the panel itself when it has no focusable child, so focus cannot escape", async () => {
    const user = userEvent.setup();
    render(
      <Harness>
        <p>Nothing focusable here.</p>
      </Harness>,
    );
    await user.click(screen.getByText("Open modal"));
    const panel = screen.getByRole("dialog");
    expect(panel).toHaveFocus();

    // Tab must not move focus out of the panel when there's nothing to trap between.
    await user.tab();
    expect(panel).toHaveFocus();
  });

  it("traps Tab forward from the last focusable element back to the first", async () => {
    const user = userEvent.setup();
    render(
      <Harness>
        <button type="button">First</button>
        <button type="button">Last</button>
      </Harness>,
    );
    await user.click(screen.getByText("Open modal"));
    screen.getByText("Last").focus();
    await user.tab();
    expect(screen.getByText("First")).toHaveFocus();
  });

  it("traps Shift+Tab from the first focusable element to the last", async () => {
    const user = userEvent.setup();
    render(
      <Harness>
        <button type="button">First</button>
        <button type="button">Last</button>
      </Harness>,
    );
    await user.click(screen.getByText("Open modal"));
    expect(screen.getByText("First")).toHaveFocus();
    await user.tab({ shift: true });
    expect(screen.getByText("Last")).toHaveFocus();
  });

  it("calls onClose on Escape", async () => {
    const user = userEvent.setup();
    render(
      <Harness>
        <button type="button">Only</button>
      </Harness>,
    );
    await user.click(screen.getByText("Open modal"));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("calls onClose on backdrop click when backdropClosable (default)", async () => {
    const user = userEvent.setup();
    render(
      <Harness>
        <button type="button">Only</button>
      </Harness>,
    );
    await user.click(screen.getByText("Open modal"));
    // The overlay container itself (not the panel) is the click-to-close target.
    fireEvent.click(screen.getByRole("dialog").parentElement!);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("does not close on backdrop click when backdropClosable is false", async () => {
    const user = userEvent.setup();
    render(
      <Harness backdropClosable={false}>
        <button type="button">Only</button>
      </Harness>,
    );
    await user.click(screen.getByText("Open modal"));
    fireEvent.click(screen.getByRole("dialog").parentElement!);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("does not close when clicking inside the panel", async () => {
    const user = userEvent.setup();
    render(
      <Harness>
        <button type="button">Only</button>
      </Harness>,
    );
    await user.click(screen.getByText("Open modal"));
    await user.click(screen.getByText("Only"));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("restores focus to the trigger element on close", async () => {
    const user = userEvent.setup();
    render(
      <Harness>
        <button type="button">Only</button>
      </Harness>,
    );
    const trigger = screen.getByText("Open modal");
    await user.click(trigger);
    await user.keyboard("{Escape}");
    expect(trigger).toHaveFocus();
  });

  it("does not throw and skips restoration when the trigger was removed from the DOM while open", async () => {
    function RemovableTriggerHarness() {
      const [showTrigger, setShowTrigger] = useState(true);
      const [open, setOpen] = useState(false);
      return (
        <div>
          {showTrigger && (
            <button
              type="button"
              onClick={() => {
                setOpen(true);
              }}
            >
              Open modal
            </button>
          )}
          <button type="button" onClick={() => setShowTrigger(false)}>
            Remove trigger
          </button>
          {open && (
            <KeepModal onClose={() => setOpen(false)}>
              <button type="button">Only</button>
            </KeepModal>
          )}
        </div>
      );
    }

    const user = userEvent.setup();
    render(<RemovableTriggerHarness />);
    await user.click(screen.getByText("Open modal"));
    // Remove the original trigger from the DOM while the modal is still open.
    fireEvent.click(screen.getByText("Remove trigger"));

    expect(() => {
      fireEvent.keyDown(document, { key: "Escape" });
    }).not.toThrow();
    // Nothing should hold focus that isn't actually connected/focusable — just verify
    // the app didn't crash and the dialog closed cleanly.
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("skips restoration to a disabled prior element instead of throwing", async () => {
    function DisablingTriggerHarness() {
      const [disabled, setDisabled] = useState(false);
      const [open, setOpen] = useState(false);
      return (
        <div>
          <button
            type="button"
            disabled={disabled}
            onClick={() => setOpen(true)}
          >
            Open modal
          </button>
          <button type="button" onClick={() => setDisabled(true)}>
            Disable trigger
          </button>
          {open && (
            <KeepModal onClose={() => setOpen(false)}>
              <button type="button">Only</button>
            </KeepModal>
          )}
        </div>
      );
    }

    const user = userEvent.setup();
    render(<DisablingTriggerHarness />);
    await user.click(screen.getByText("Open modal"));
    fireEvent.click(screen.getByText("Disable trigger"));

    expect(() => {
      fireEvent.keyDown(document, { key: "Escape" });
    }).not.toThrow();
    expect(screen.getByText("Open modal")).not.toHaveFocus();
  });
});
