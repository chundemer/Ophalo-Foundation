import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MobileNavMenu } from "../MobileNavMenu";
import type { NavItem } from "../../../App";

const items: NavItem[] = [
  { id: "requests", label: "Requests", icon: null },
  { id: "pricebook", label: "Price Book", icon: null },
  { id: "settings", label: "Settings", icon: null },
];

function renderMenu(overrides: Partial<React.ComponentProps<typeof MobileNavMenu>> = {}) {
  const props: React.ComponentProps<typeof MobileNavMenu> = {
    items,
    activeId: "requests",
    roleLabel: "Owner",
    onNavigate: vi.fn(),
    onNavigateHelp: vi.fn(),
    helpUnseenCount: 0,
    onSignOut: vi.fn(),
    isSigningOut: false,
    onClose: vi.fn(),
    ...overrides,
  };
  return { props, ...render(<MobileNavMenu {...props} />) };
}

describe("MobileNavMenu", () => {
  it("renders every provided nav item", () => {
    renderMenu();
    expect(screen.getByText("Requests")).toBeInTheDocument();
    expect(screen.getByText("Price Book")).toBeInTheDocument();
    expect(screen.getByText("Settings")).toBeInTheDocument();
  });

  it("omits Price Book when the caller didn't include it (unentitled account)", () => {
    renderMenu({ items: items.filter((i) => i.id !== "pricebook") });
    expect(screen.queryByText("Price Book")).not.toBeInTheDocument();
  });

  it("calls onNavigate with the selected item's id and does not call onClose itself", async () => {
    const user = userEvent.setup();
    const { props } = renderMenu();

    await user.click(screen.getByText("Price Book"));

    expect(props.onNavigate).toHaveBeenCalledWith("pricebook");
    expect(props.onClose).not.toHaveBeenCalled();
  });

  it("calls onClose on Escape", async () => {
    const user = userEvent.setup();
    const { props } = renderMenu();

    await user.keyboard("{Escape}");

    expect(props.onClose).toHaveBeenCalled();
  });

  it("renders the role label", () => {
    renderMenu({ roleLabel: "Admin" });
    expect(screen.getByText("Admin")).toBeInTheDocument();
  });

  it("renders the Business Settings group and routes sections through onNavigateSection (ADR-499)", async () => {
    const user = userEvent.setup();
    const onNavigateSection = vi.fn();
    renderMenu({
      items: items.filter((i) => i.id !== "settings"),
      sections: [
        { id: "public-profile", label: "Company Profile & Public Link" },
        { id: "policy", label: "Response Policy (SLAs)" },
        { id: "team", label: "Team Seats & Permissions" },
      ],
      onNavigateSection,
    });

    expect(screen.getByText("Business Settings")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Team Seats & Permissions" }));
    expect(onNavigateSection).toHaveBeenCalledWith("team");
  });

  it("omits the Business Settings group when no sections are provided", () => {
    renderMenu({ items: items.filter((i) => i.id !== "settings"), roleLabel: "Operator" });
    expect(screen.queryByText("Business Settings")).not.toBeInTheDocument();
  });

  it("offers sign out in the mobile menu", async () => {
    const user = userEvent.setup();
    const { props } = renderMenu({ roleLabel: "Admin" });

    await user.click(screen.getByRole("button", { name: "Sign out" }));

    expect(props.onSignOut).toHaveBeenCalledTimes(1);
  });

  it("shows an always-present Help & Updates row that routes through onNavigateHelp", async () => {
    const user = userEvent.setup();
    // No sections (Operator) — the row is still present.
    const { props } = renderMenu({
      items: items.filter((i) => i.id !== "settings"),
      roleLabel: "Operator",
    });

    await user.click(screen.getByRole("button", { name: /Help & Updates/ }));
    expect(props.onNavigateHelp).toHaveBeenCalledTimes(1);
  });

  it("hides the 'Send feedback' row unless onSendFeedback is provided", () => {
    renderMenu();
    expect(screen.queryByRole("button", { name: "Send feedback" })).not.toBeInTheDocument();
  });

  it("shows a 'Send feedback' row that fires onSendFeedback when provided", async () => {
    const user = userEvent.setup();
    const onSendFeedback = vi.fn();
    renderMenu({ onSendFeedback });

    await user.click(screen.getByRole("button", { name: "Send feedback" }));
    expect(onSendFeedback).toHaveBeenCalledTimes(1);
  });

  it("adds the ' · N new' suffix on the Help & Updates row only when unseen > 0", () => {
    const { rerender, props } = renderMenu({ helpUnseenCount: 0 });
    expect(screen.getByRole("button", { name: /Help & Updates/ }).textContent).not.toMatch(/new/);

    rerender(<MobileNavMenu {...props} helpUnseenCount={2} />);
    expect(screen.getByRole("button", { name: /Help & Updates/ }).textContent).toContain("· 2 new");
  });
});
