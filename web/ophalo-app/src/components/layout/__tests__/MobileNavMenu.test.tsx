import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MobileNavMenu } from "../MobileNavMenu";
import type { NavItem } from "../../../App";

const items: NavItem[] = [
  { id: "requests", label: "Requests", icon: null },
  { id: "home", label: "Getting Started", icon: null },
  { id: "pricebook", label: "Price Book", icon: null },
  { id: "settings", label: "Settings", icon: null },
];

describe("MobileNavMenu", () => {
  it("renders every provided nav item", () => {
    render(
      <MobileNavMenu
        items={items}
        activeId="requests"
        roleLabel="Owner"
        onNavigate={vi.fn()}
        onSignOut={vi.fn()}
        isSigningOut={false}
        onClose={vi.fn()}
      />,
    );
    expect(screen.getByText("Requests")).toBeInTheDocument();
    expect(screen.getByText("Getting Started")).toBeInTheDocument();
    expect(screen.getByText("Price Book")).toBeInTheDocument();
    expect(screen.getByText("Settings")).toBeInTheDocument();
  });

  it("omits Price Book when the caller didn't include it (unentitled account)", () => {
    render(
      <MobileNavMenu
        items={items.filter((i) => i.id !== "pricebook")}
        activeId="requests"
        roleLabel="Owner"
        onNavigate={vi.fn()}
        onSignOut={vi.fn()}
        isSigningOut={false}
        onClose={vi.fn()}
      />,
    );
    expect(screen.queryByText("Price Book")).not.toBeInTheDocument();
  });

  it("calls onNavigate with the selected item's id and does not call onClose itself", async () => {
    const user = userEvent.setup();
    const onNavigate = vi.fn();
    const onClose = vi.fn();
    render(
      <MobileNavMenu
        items={items}
        activeId="requests"
        roleLabel="Owner"
        onNavigate={onNavigate}
        onSignOut={vi.fn()}
        isSigningOut={false}
        onClose={onClose}
      />,
    );

    await user.click(screen.getByText("Price Book"));

    expect(onNavigate).toHaveBeenCalledWith("pricebook");
  });

  it("calls onClose on Escape", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(
      <MobileNavMenu
        items={items}
        activeId="requests"
        roleLabel="Owner"
        onNavigate={vi.fn()}
        onSignOut={vi.fn()}
        isSigningOut={false}
        onClose={onClose}
      />,
    );

    await user.keyboard("{Escape}");

    expect(onClose).toHaveBeenCalled();
  });

  it("renders the role label", () => {
    render(
      <MobileNavMenu
        items={items}
        activeId="requests"
        roleLabel="Admin"
        onNavigate={vi.fn()}
        onSignOut={vi.fn()}
        isSigningOut={false}
        onClose={vi.fn()}
      />,
    );
    expect(screen.getByText("Admin")).toBeInTheDocument();
  });

  it("offers sign out in the mobile menu", async () => {
    const user = userEvent.setup();
    const onSignOut = vi.fn();
    render(
      <MobileNavMenu
        items={items}
        activeId="requests"
        roleLabel="Admin"
        onNavigate={vi.fn()}
        onSignOut={onSignOut}
        isSigningOut={false}
        onClose={vi.fn()}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Sign out" }));

    expect(onSignOut).toHaveBeenCalledTimes(1);
  });
});
