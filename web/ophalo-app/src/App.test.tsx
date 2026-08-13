import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { getNavItems, App } from "./App";

// The mobile top bar and the desktop workbench header both render in jsdom regardless of the
// `md:hidden` / `hidden md:flex` classes that decide which one is visually shown at runtime, and
// each carries its own "New Request" control (a FAB on mobile). Scope queries to the desktop
// header — the one whose class list opts into `md:flex` rather than `md:hidden` — so assertions
// exercise only the desktop CTA this pass changes, not mobile behavior this pass leaves alone.
function getDesktopHeader(container: HTMLElement): HTMLElement {
  const header = Array.from(container.querySelectorAll("header")).find((h) =>
    h.className.includes("md:flex"),
  );
  if (!header) throw new Error("desktop workbench header not found");
  return header as HTMLElement;
}

const mockGetMe = vi.fn();
const mockGetCapabilityPackages = vi.fn();
const mockGetCatalogItems = vi.fn();
const mockGetCatalogCategories = vi.fn();
const mockGetCatalogItem = vi.fn();
const mockGetOfferingAssembly = vi.fn();
const mockGetOfferingAssemblies = vi.fn();

vi.mock("./lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("./lib/apiClient")>("./lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getMe: (...args: unknown[]) => mockGetMe(...args),
      getCapabilityPackages: (...args: unknown[]) => mockGetCapabilityPackages(...args),
      getCatalogItems: (...args: unknown[]) => mockGetCatalogItems(...args),
      getCatalogCategories: (...args: unknown[]) => mockGetCatalogCategories(...args),
      getCatalogItem: (...args: unknown[]) => mockGetCatalogItem(...args),
      getOfferingAssembly: (...args: unknown[]) => mockGetOfferingAssembly(...args),
      getOfferingAssemblies: (...args: unknown[]) => mockGetOfferingAssemblies(...args),
    },
  };
});

function renderApp() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>,
  );
}

// PWA UI-quality correction (2026-08-12): the global desktop "New Request" CTA must not compete
// with Price Book's own contextual CTA on Price Book, Catalog Item Detail, or Offering/Assembly
// Detail — while ordinary primary navigation (the "Requests" nav pill) stays reachable.
describe("App — desktop New Request CTA on Price Book routes", () => {
  beforeEach(() => {
    window.location.hash = "";
    mockGetMe.mockReset().mockResolvedValue({
      accountUserId: "u1",
      accountId: "a1",
      isAuthenticated: true,
      isVerified: true,
      accountRole: "owner",
      businessName: "Acme HVAC",
    });
    mockGetCapabilityPackages.mockReset().mockResolvedValue([
      { featureKey: "keep.price_book_quotes_materials", enabled: true },
    ]);
    mockGetCatalogItems.mockReset().mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    mockGetCatalogCategories.mockReset().mockResolvedValue({ categories: [] });
    mockGetCatalogItem.mockReset().mockReturnValue(new Promise(() => {}));
    mockGetOfferingAssembly.mockReset().mockReturnValue(new Promise(() => {}));
  });

  it("is present on the Requests desktop workbench route", async () => {
    window.location.hash = "";
    const { container } = renderApp();
    await waitFor(() => expect(screen.getByRole("button", { name: "Price Book" })).toBeInTheDocument());

    const header = within(getDesktopHeader(container));
    expect(header.getByRole("button", { name: "New Request" })).toBeInTheDocument();
  });

  it("is absent on the Price Book list route, while Requests nav stays reachable", async () => {
    window.location.hash = "#/pricebook";
    const { container } = renderApp();
    await waitFor(() => expect(screen.getAllByText("Price Book").length).toBeGreaterThan(0));

    const header = within(getDesktopHeader(container));
    expect(header.queryByRole("button", { name: "New Request" })).not.toBeInTheDocument();
    expect(header.getByRole("button", { name: "Requests" })).toBeInTheDocument();
  });

  it("is absent on a Catalog Item Detail route", async () => {
    window.location.hash = "#/pricebook/item-1";
    const { container } = renderApp();
    await waitFor(() => expect(mockGetCatalogItem).toHaveBeenCalledWith("item-1"));

    const header = within(getDesktopHeader(container));
    expect(header.queryByRole("button", { name: "New Request" })).not.toBeInTheDocument();
    expect(header.getByRole("button", { name: "Requests" })).toBeInTheDocument();
  });

  it("is absent on an Offering/Assembly Detail route", async () => {
    window.location.hash = "#/pricebook/assembly/assembly-1";
    const { container } = renderApp();
    await waitFor(() => expect(mockGetOfferingAssembly).toHaveBeenCalledWith("assembly-1"));

    const header = within(getDesktopHeader(container));
    expect(header.queryByRole("button", { name: "New Request" })).not.toBeInTheDocument();
    expect(header.getByRole("button", { name: "Requests" })).toBeInTheDocument();
  });
});

// URL-addressable Price Book tabs (2026-08-12): the hash query string is the source of truth for
// which tab renders, not in-memory component state — direct load, refresh, and back/forward must
// all resolve the same way tab selection does.
describe("App — Price Book tab URL synchronization", () => {
  beforeEach(() => {
    window.location.hash = "";
    mockGetMe.mockReset().mockResolvedValue({
      accountUserId: "u1",
      accountId: "a1",
      isAuthenticated: true,
      isVerified: true,
      accountRole: "owner",
      businessName: "Acme HVAC",
    });
    mockGetCapabilityPackages.mockReset().mockResolvedValue([
      { featureKey: "keep.price_book_quotes_materials", enabled: true },
    ]);
    mockGetCatalogItems.mockReset().mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    mockGetCatalogCategories.mockReset().mockResolvedValue({ categories: [] });
    mockGetOfferingAssemblies.mockReset().mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    mockGetCatalogItem.mockReset().mockReturnValue(new Promise(() => {}));
    mockGetOfferingAssembly.mockReset().mockReturnValue(new Promise(() => {}));
  });

  it("renders Catalog Items on the canonical #/pricebook route", async () => {
    window.location.hash = "#/pricebook";
    renderApp();

    const tab = await screen.findByRole("tab", { name: "Catalog Items" });
    expect(tab).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Offerings & Assemblies" })).toHaveAttribute("aria-selected", "false");
  });

  it("renders Offerings & Assemblies on direct #/pricebook?tab=assemblies", async () => {
    window.location.hash = "#/pricebook?tab=assemblies";
    renderApp();

    const tab = await screen.findByRole("tab", { name: "Offerings & Assemblies" });
    expect(tab).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Catalog Items" })).toHaveAttribute("aria-selected", "false");
  });

  it("accepts #/pricebook?tab=catalog as Catalog Items for compatibility", async () => {
    window.location.hash = "#/pricebook?tab=catalog";
    renderApp();

    const tab = await screen.findByRole("tab", { name: "Catalog Items" });
    expect(tab).toHaveAttribute("aria-selected", "true");
  });

  it("selecting Offerings & Assemblies updates the hash to ?tab=assemblies", async () => {
    window.location.hash = "#/pricebook";
    renderApp();
    const user = userEvent.setup();

    await user.click(await screen.findByRole("tab", { name: "Offerings & Assemblies" }));

    await waitFor(() => expect(window.location.hash).toBe("#/pricebook?tab=assemblies"));
  });

  it("selecting Catalog Items updates the hash to the canonical #/pricebook", async () => {
    window.location.hash = "#/pricebook?tab=assemblies";
    renderApp();
    const user = userEvent.setup();

    await user.click(await screen.findByRole("tab", { name: "Catalog Items" }));

    await waitFor(() => expect(window.location.hash).toBe("#/pricebook"));
  });

  it("does not mistake ?tab=assemblies for a catalog-item id or fall through to Requests", async () => {
    window.location.hash = "#/pricebook?tab=assemblies";
    renderApp();

    await screen.findByRole("tab", { name: "Offerings & Assemblies" });
    expect(mockGetCatalogItem).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "New Request" })).not.toBeInTheDocument();
  });

  it("Assembly Detail shows 'Back to Assemblies' and navigates to the assemblies tab URL", async () => {
    window.location.hash = "#/pricebook/assembly/assembly-1";
    mockGetOfferingAssembly.mockResolvedValue({
      id: "assembly-1",
      name: "Test Assembly",
      primaryCatalogItemId: "item-primary",
      primaryCatalogItemDisplayName: "Primary Item",
      priceTreatment: "Summed",
      activeState: "Active",
      concurrencyVersion: "v1",
      items: [],
      isOperationallyEligible: true,
      eligibilityReasons: [],
    });
    renderApp();
    const user = userEvent.setup();

    const backButton = await screen.findByRole("button", { name: /Back to Assemblies/ });
    await user.click(backButton);

    await waitFor(() => expect(window.location.hash).toBe("#/pricebook?tab=assemblies"));
  });
});

describe("getNavItems", () => {
  it("operator sees only Requests, regardless of entitlement", () => {
    const ids = getNavItems("operator", true).map((i) => i.id);
    expect(ids).toEqual(["requests"]);
  });

  it("viewer sees only Requests, regardless of entitlement", () => {
    const ids = getNavItems("viewer", true).map((i) => i.id);
    expect(ids).toEqual(["requests"]);
  });

  it("owner without the Price Book entitlement does not see Price Book", () => {
    const ids = getNavItems("owner", false).map((i) => i.id);
    expect(ids).toEqual(["requests", "home", "settings"]);
  });

  it("admin without the Price Book entitlement does not see Price Book", () => {
    const ids = getNavItems("admin", false).map((i) => i.id);
    expect(ids).toEqual(["requests", "home", "settings"]);
  });

  it("owner with the Price Book entitlement sees it between Getting Started and Settings", () => {
    const ids = getNavItems("owner", true).map((i) => i.id);
    expect(ids).toEqual(["requests", "home", "pricebook", "settings"]);
  });

  it("admin with the Price Book entitlement sees it", () => {
    const ids = getNavItems("admin", true).map((i) => i.id);
    expect(ids).toEqual(["requests", "home", "pricebook", "settings"]);
  });
});
