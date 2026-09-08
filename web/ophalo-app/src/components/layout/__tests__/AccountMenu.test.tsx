import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AccountMenu, getBusinessSettingsSections } from "../AccountMenu";

const ownerSections = getBusinessSettingsSections("owner");

function renderMenu(overrides: Partial<React.ComponentProps<typeof AccountMenu>> = {}) {
  const props: React.ComponentProps<typeof AccountMenu> = {
    businessName: "Acme HVAC",
    userName: "Christian",
    roleLabel: "Owner",
    sections: ownerSections,
    settingsActive: false,
    onNavigateSection: vi.fn(),
    onNavigateHelp: vi.fn(),
    helpUnseenCount: 0,
    onSignOut: vi.fn(),
    isSigningOut: false,
    ...overrides,
  };
  return { props, ...render(<AccountMenu {...props} />) };
}

describe("getBusinessSettingsSections", () => {
  it("returns the three sections for Owner and Admin, nothing for Operator/Viewer", () => {
    expect(getBusinessSettingsSections("owner").map((s) => s.id)).toEqual([
      "public-profile",
      "policy",
      "team",
    ]);
    expect(getBusinessSettingsSections("admin")).toHaveLength(3);
    expect(getBusinessSettingsSections("operator")).toEqual([]);
    expect(getBusinessSettingsSections("viewer")).toEqual([]);
  });
});

describe("AccountMenu", () => {
  it("shows the identity line, falling back to the business name when the user has no name", () => {
    const { rerender } = renderMenu();
    expect(screen.getByText("Christian · Owner")).toBeInTheDocument();

    rerender(
      <AccountMenu
        businessName="Acme HVAC"
        userName={null}
        roleLabel="Owner"
        sections={ownerSections}
        settingsActive={false}
        onNavigateSection={vi.fn()}
        onNavigateHelp={vi.fn()}
        helpUnseenCount={0}
        onSignOut={vi.fn()}
        isSigningOut={false}
      />,
    );
    expect(screen.getByText("Acme HVAC · Owner")).toBeInTheDocument();
  });

  it("is closed at rest and opens on click", async () => {
    const user = userEvent.setup();
    renderMenu();
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /account menu/i }));
    expect(screen.getByRole("menu")).toBeInTheDocument();
  });

  it("renders the Business Settings sections for an Owner and routes on click", async () => {
    const user = userEvent.setup();
    const onNavigateSection = vi.fn();
    renderMenu({ onNavigateSection });

    await user.click(screen.getByRole("button", { name: /account menu/i }));
    await user.click(screen.getByRole("menuitem", { name: "Response Policy (SLAs)" }));

    expect(onNavigateSection).toHaveBeenCalledWith("policy");
  });

  it("omits the Business Settings group for Operator/Viewer (empty sections)", async () => {
    const user = userEvent.setup();
    renderMenu({ sections: [], userName: "Sam", roleLabel: "Operator" });

    await user.click(screen.getByRole("button", { name: /account menu/i }));
    expect(screen.queryByText("Business Settings")).not.toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Sign out" })).toBeInTheDocument();
  });

  it("invokes onSignOut and reflects the signing-out state", async () => {
    const user = userEvent.setup();
    const onSignOut = vi.fn();
    const { rerender, props } = renderMenu({ onSignOut });

    await user.click(screen.getByRole("button", { name: /account menu/i }));
    await user.click(screen.getByRole("menuitem", { name: "Sign out" }));
    expect(onSignOut).toHaveBeenCalledTimes(1);

    // The menu stays open through sign-out so the pending state is visible.
    rerender(<AccountMenu {...props} isSigningOut />);
    expect(screen.getByRole("menuitem", { name: "Signing out…" })).toBeDisabled();
  });

  it("closes on Escape and restores focus to the trigger", async () => {
    const user = userEvent.setup();
    renderMenu();
    const trigger = screen.getByRole("button", { name: /account menu/i });

    await user.click(trigger);
    expect(screen.getByRole("menu")).toBeInTheDocument();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it("shows an all-roles Help & Updates row that routes on click", async () => {
    const user = userEvent.setup();
    const onNavigateHelp = vi.fn();
    renderMenu({ sections: [], userName: "Sam", roleLabel: "Operator", onNavigateHelp });

    await user.click(screen.getByRole("button", { name: /account menu/i }));
    await user.click(screen.getByRole("menuitem", { name: /Help & Updates/ }));
    expect(onNavigateHelp).toHaveBeenCalledTimes(1);
  });

  it("adds the ' · N new' suffix and the '(updates available)' trigger name only when unseen > 0", async () => {
    const user = userEvent.setup();
    const { rerender, props } = renderMenu({ helpUnseenCount: 0 });

    expect(
      screen.queryByRole("button", { name: /updates available/i }),
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /account menu/i }));
    expect(screen.getByRole("menuitem", { name: /Help & Updates/ }).textContent).not.toMatch(/new/);

    rerender(<AccountMenu {...props} helpUnseenCount={3} />);
    expect(screen.getByRole("button", { name: /updates available/i })).toBeInTheDocument();
    expect(
      screen.getByRole("menuitem", { name: /Help & Updates/ }).textContent,
    ).toContain("· 3 new");
  });

  it("moves focus between items with ArrowDown/ArrowUp", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: /account menu/i }));

    const items = screen.getAllByRole("menuitem");
    expect(items[0]).toHaveFocus();
    await user.keyboard("{ArrowDown}");
    expect(items[1]).toHaveFocus();
    await user.keyboard("{ArrowUp}");
    expect(items[0]).toHaveFocus();
  });
});
