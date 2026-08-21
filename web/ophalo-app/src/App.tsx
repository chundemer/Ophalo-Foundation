import { useState, useCallback, useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { AuthGuard } from "./components/AuthGuard";
import { QuickCapture } from "./components/QuickCapture";
import { KeepButton } from "./components/keep/KeepButton";
import { Home } from "./pages/Home";
import { RequestWorkbenchShell } from "./components/requests/RequestWorkbenchShell";
import { RequestDetail } from "./pages/RequestDetail";
import { AccessLimited } from "./pages/AccessLimited";
import { Settings } from "./pages/Settings";
import { PriceBook } from "./pages/PriceBook";
import { CatalogItemDetail } from "./pages/CatalogItemDetail";
import { OfferingAssemblyDetail } from "./pages/OfferingAssemblyDetail";
import { MobileNavMenu } from "./components/layout/MobileNavMenu";
import { Plus, Inbox, Settings as SettingsIcon, Tag, Menu } from "lucide-react";
import { api, type AccountRole, type KeepRequestViewCounts } from "./lib/apiClient";

// ADR-462: AccountCapabilityPackageEnrollment.FeatureKeys.PriceBookQuotesMaterials.
const PRICE_BOOK_FEATURE_KEY = "keep.price_book_quotes_materials";

// Shell-level access flags (isReadOnly, isPastDue) are intentionally not derived here.
// GET /keep/setup/onboarding checks Keep.SettingsManage before account access, so Operators
// always get 403 from that endpoint regardless of commercial state. No endpoint in the current
// system reliably returns a role-neutral 402 or 403 that distinguishes commercial-block from
// permission-denied. Both flags are props on QuickCapture for a future caller that has a
// reliable source (e.g. role in session claims, a dedicated access endpoint).

type AppRoute =
  | { page: "home" }
  | { page: "requests" }
  | { page: "settings"; section?: "public-profile" | "policy" | "team" }
  | { page: "pricebook"; tab?: "items" | "assemblies" | "nudges" }
  | { page: "pricebook-item"; catalogItemId: string; returnToAssembly?: string; returnToAssemblyReason?: "price" | "margin" }
  | { page: "pricebook-assembly"; offeringAssemblyId: string }
  | { page: "detail"; requestId: string; focusPanel?: string };

interface RequestNavContext {
  requestIds: string[];
}

// Assembly endpoint identifiers are GUIDs. Keeping the return context to that shape prevents a
// hand-authored or malformed hash from replacing the normal, safe Catalog Items back target.
function isOfferingAssemblyId(value: string | null): value is string {
  return value !== null && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

function getRouteFromLocation(): AppRoute {
  const hash = window.location.hash;
  const match = hash.match(/^#\/request\/(.+)$/);
  if (match?.[1]) return { page: "detail", requestId: match[1] };
  // Split the Price Book path from its query string before matching detail routes — otherwise
  // `#/pricebook?tab=assemblies` would fail every `#/pricebook/...` pattern and fall through.
  const [hashPath, hashQuery] = hash.split("?");
  // Checked before the generic item-detail pattern below — its broader `(.+)` would otherwise
  // also match "assembly/<id>".
  const assemblyMatch = hashPath.match(/^#\/pricebook\/assembly\/(.+)$/);
  if (assemblyMatch?.[1]) return { page: "pricebook-assembly", offeringAssemblyId: assemblyMatch[1] };
  const itemMatch = hashPath.match(/^#\/pricebook\/(.+)$/);
  if (itemMatch?.[1]) {
    // Repair-loop return context (Step 2 Batch 2, 2026-08-13): an absent, empty, or unrecognized
    // returnToAssembly is safely ignored — Catalog Items remains the normal back destination.
    // returnToAssemblyReason only matters alongside a recognized returnToAssembly — an
    // unrecognized value safely falls back to no contextual guidance rather than a bad label.
    const params = new URLSearchParams(hashQuery ?? "");
    const returnToAssembly = params.get("returnToAssembly");
    const returnToAssemblyReason = params.get("returnToAssemblyReason");
    const validReturn = isOfferingAssemblyId(returnToAssembly);
    return {
      page: "pricebook-item",
      catalogItemId: itemMatch[1],
      returnToAssembly: validReturn ? returnToAssembly : undefined,
      returnToAssemblyReason:
        validReturn && (returnToAssemblyReason === "price" || returnToAssemblyReason === "margin")
          ? returnToAssemblyReason
          : undefined,
    };
  }
  if (hashPath === "#/pricebook") {
    const tab = new URLSearchParams(hashQuery ?? "").get("tab");
    return {
      page: "pricebook",
      tab: tab === "assemblies" ? "assemblies" : tab === "nudges" ? "nudges" : "items",
    };
  }
  return { page: "requests" };
}

export interface NavItem {
  id: "home" | "requests" | "settings" | "pricebook";
  label: string;
  icon: React.ReactNode;
}

// Build 112: Price Book is a first-class top-level item, visible only to an Owner/Admin whose
// account carries the PriceBookQuotesMaterials entitlement — `entitled` is the client-side
// discovery check only; every catalog API remains the authority.
export function getNavItems(role: AccountRole, entitled: boolean): NavItem[] {
  const items: NavItem[] = [
    { id: "requests", label: "Requests", icon: <Inbox className="h-4 w-4" /> },
  ];
  if (role === "owner" || role === "admin") {
    items.push({ id: "home", label: "Getting Started", icon: null });
    if (entitled) {
      items.push({ id: "pricebook", label: "Price Book", icon: <Tag className="h-4 w-4" /> });
    }
    items.push({ id: "settings", label: "Settings", icon: <SettingsIcon className="h-4 w-4" /> });
  }
  return items;
}

function roleLabel(role: AccountRole): string {
  switch (role) {
    case "owner": return "Owner";
    case "admin": return "Admin";
    case "operator": return "Operator";
    case "viewer": return "Viewer";
    default: return "";
  }
}

function AppShell() {
  const [captureOpen, setCaptureOpen] = useState(false);
  const [route, setRoute] = useState<AppRoute>(getRouteFromLocation);
  const [viewCounts, setViewCounts] = useState<KeepRequestViewCounts | null>(null);
  const [navContext, setNavContext] = useState<RequestNavContext | null>(null);
  const handleViewCountsUpdate = useCallback(setViewCounts, []);

  useEffect(() => {
    function onPopState() {
      setRoute(getRouteFromLocation());
    }
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, []);

  const { data: me } = useQuery({
    queryKey: ["me"],
    queryFn: api.getMe,
    staleTime: 5 * 60 * 1000,
  });

  const role: AccountRole = me?.accountRole ?? "unknown";
  const isOwnerOrAdmin = role === "owner" || role === "admin";

  // Owner/Admin-gated server-side (ADR-462) — only fetched for roles that can call it, so an
  // Operator/Viewer never issues a request that's guaranteed to 403.
  const {
    data: capabilityPackages,
    isLoading: capabilityLoading,
    isError: capabilityError,
    refetch: refetchCapabilities,
  } = useQuery({
    queryKey: ["capabilityPackages"],
    queryFn: api.getCapabilityPackages,
    enabled: isOwnerOrAdmin,
    staleTime: 5 * 60 * 1000,
  });
  const priceBookEntitled =
    capabilityPackages?.some((c) => c.featureKey === PRICE_BOOK_FEATURE_KEY && c.enabled) ?? false;

  const navItems = getNavItems(role, priceBookEntitled);
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  function navigate(newRoute: AppRoute) {
    const base = window.location.pathname + window.location.search;
    if (newRoute.page === "detail") {
      history.pushState(null, "", `${base}#/request/${newRoute.requestId}`);
    } else if (newRoute.page === "pricebook") {
      const suffix =
        newRoute.tab === "assemblies"
          ? "#/pricebook?tab=assemblies"
          : newRoute.tab === "nudges"
            ? "#/pricebook?tab=nudges"
            : "#/pricebook";
      history.pushState(null, "", `${base}${suffix}`);
    } else if (newRoute.page === "pricebook-item") {
      let suffix = `#/pricebook/${newRoute.catalogItemId}`;
      if (newRoute.returnToAssembly) {
        const query = new URLSearchParams({ returnToAssembly: newRoute.returnToAssembly });
        if (newRoute.returnToAssemblyReason) query.set("returnToAssemblyReason", newRoute.returnToAssemblyReason);
        suffix += `?${query.toString()}`;
      }
      history.pushState(null, "", `${base}${suffix}`);
    } else if (newRoute.page === "pricebook-assembly") {
      history.pushState(null, "", `${base}#/pricebook/assembly/${newRoute.offeringAssemblyId}`);
    } else {
      history.pushState(null, "", base);
    }
    setRoute(newRoute);
    setMobileMenuOpen(false);
  }

  function openCapture() {
    setCaptureOpen(true);
  }

  function navigateToSettings(section?: "public-profile" | "policy" | "team") {
    navigate({ page: "settings", section });
  }

  function selectRequest(requestId: string, context?: RequestNavContext, focus?: string) {
    navigate({ page: "detail", requestId, focusPanel: focus });
    setNavContext(context ?? null);
  }

  function backToRequests() {
    navigate({ page: "requests" });
    setNavContext(null);
  }

  const currentNavIdx =
    route.page === "detail" && navContext
      ? navContext.requestIds.indexOf(route.requestId)
      : -1;
  const prevRequestId =
    currentNavIdx > 0 ? navContext!.requestIds[currentNavIdx - 1] : undefined;
  const nextRequestId =
    currentNavIdx >= 0 && navContext && currentNavIdx < navContext.requestIds.length - 1
      ? navContext.requestIds[currentNavIdx + 1]
      : undefined;

  const activeNavId: NavItem["id"] =
    route.page === "home" ? "home"
    : route.page === "settings" ? "settings"
    : route.page === "pricebook" || route.page === "pricebook-item" || route.page === "pricebook-assembly" ? "pricebook"
    : "requests";

  const isWorkbench =
    route.page === "requests" ||
    route.page === "detail" ||
    route.page === "pricebook" ||
    route.page === "pricebook-item" ||
    route.page === "pricebook-assembly";

  // Mobile is always column (top bar above content); desktop non-workbench switches to a row
  // (sidebar beside content) — workbench stays column at every size (header above main).
  return (
    <div className={`flex flex-col min-h-screen bg-[var(--ophalo-canvas)] ${isWorkbench ? "" : "md:flex-row"}`}>
      {/* Top bar — mobile only, all routes: logo + hamburger trigger for MobileNavMenu. */}
      {role !== "unknown" && (
        <header className="md:hidden flex items-center justify-between px-4 h-14 shrink-0 bg-[var(--ophalo-card)] border-b border-[var(--ophalo-border)]">
          <button
            type="button"
            onClick={() => navigate({ page: "requests" })}
            aria-label="Go to requests"
            className="flex items-center focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 rounded"
          >
            <img
              src="/brand/ophalo-keep-lockup-color.svg"
              alt="OpHalo Keep"
              className="h-7 w-auto"
              draggable={false}
            />
          </button>
          <button
            type="button"
            onClick={() => setMobileMenuOpen(true)}
            aria-label="Open navigation menu"
            aria-expanded={mobileMenuOpen}
            className="flex items-center justify-center h-9 w-9 rounded-md text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
          >
            <Menu className="h-5 w-5" />
          </button>
        </header>
      )}

      {/* Left sidebar — desktop, non-workbench routes only */}
      {!isWorkbench && (
        <aside className="hidden md:flex md:flex-col md:w-56 lg:w-64 md:shrink-0 bg-[var(--ophalo-card)] border-r border-[var(--ophalo-border)]">
          <div className="px-4 py-4 border-b border-[var(--ophalo-border)]">
            <button
              type="button"
              onClick={() => navigate({ page: "requests" })}
              aria-label="Go to requests"
              className="block focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 rounded"
            >
              <img
                src="/brand/ophalo-keep-lockup-color.svg"
                alt="OpHalo Keep"
                className="h-8 w-auto"
                draggable={false}
              />
            </button>
          </div>
          <nav className="flex-1 px-3 py-4 space-y-0.5">
            {navItems.map((item) => (
              <button
                key={item.id}
                type="button"
                onClick={() => navigate({ page: item.id })}
                className={`w-full flex items-center gap-2.5 rounded-md px-3 py-2.5 text-sm text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
                  activeNavId === item.id
                    ? "font-semibold bg-[var(--keep-accent-bg)] text-[var(--ophalo-navy)]"
                    : "font-medium text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)]"
                }`}
              >
                {item.icon}
                <span>{item.label}</span>
                {item.id === "requests" && viewCounts != null && (() => {
                  const total = (role === "owner" || role === "admin")
                    ? viewCounts.default
                    : viewCounts.assignedToMe + viewCounts.needsAttention;
                  return total > 0 ? (
                    <span className={`ml-auto text-xs font-semibold rounded-full px-1.5 py-0.5 ${
                      activeNavId === "requests"
                        ? "bg-[var(--keep-accent)] text-white"
                        : "bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
                    }`}>
                      {total}
                    </span>
                  ) : null;
                })()}
              </button>
            ))}
          </nav>
          {/* D2: Price Book owns its contextual create action. Requests remains one navigation
              click away, but a second global create button would compete with catalog/assembly
              creation in this desktop workspace. */}
          {activeNavId !== "pricebook" && (
            <div className="px-3 pb-4">
              <KeepButton
                variant="primary"
                onClick={openCapture}
                className="w-full gap-2"
              >
                <Plus className="h-4 w-4" />
                New Request
              </KeepButton>
            </div>
          )}
          {role !== "unknown" && (
            <div className="px-4 py-3 border-t border-[var(--ophalo-border)]">
              <p className="text-xs text-[var(--ophalo-muted)]">{roleLabel(role)}</p>
            </div>
          )}
        </aside>
      )}

      {/* Top nav — desktop workbench (requests + detail) */}
      {isWorkbench && (
        <header className="hidden md:flex items-center gap-3 px-4 h-14 shrink-0 bg-[var(--ophalo-card)] border-b border-[var(--ophalo-border)]">
          <button
            type="button"
            onClick={() => navigate({ page: "requests" })}
            aria-label="Go to requests"
            className="flex items-center focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 rounded shrink-0"
          >
            <img
              src="/brand/ophalo-keep-lockup-color.svg"
              alt="OpHalo Keep"
              className="h-7 w-auto"
              draggable={false}
            />
          </button>

          <nav className="flex items-center gap-1 ml-2">
            <button
              type="button"
              onClick={() => navigate({ page: "requests" })}
              className={`flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
                activeNavId === "requests"
                  ? "bg-[var(--keep-accent-bg)] text-[var(--ophalo-navy)] font-semibold"
                  : "text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)]"
              }`}
            >
              <Inbox className="h-4 w-4" />
              Requests
              {viewCounts != null && (() => {
                const total = (role === "owner" || role === "admin")
                  ? viewCounts.default
                  : viewCounts.assignedToMe + viewCounts.needsAttention;
                return total > 0 ? (
                  <span className={`text-xs font-semibold rounded-full px-1.5 py-0.5 ${
                    activeNavId === "requests"
                      ? "bg-[var(--keep-accent)] text-white"
                      : "bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
                  }`}>
                    {total}
                  </span>
                ) : null;
              })()}
            </button>
            {(role === "owner" || role === "admin") && (
              <>
                <button
                  type="button"
                  onClick={() => navigate({ page: "home" })}
                  className="flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)] transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
                >
                  Getting Started
                </button>
                {priceBookEntitled && (
                  <button
                    type="button"
                    onClick={() => navigate({ page: "pricebook" })}
                    className={`flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
                      activeNavId === "pricebook"
                        ? "bg-[var(--keep-accent-bg)] text-[var(--ophalo-navy)] font-semibold"
                        : "text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)]"
                    }`}
                  >
                    <Tag className="h-4 w-4" />
                    Price Book
                  </button>
                )}
                <button
                  type="button"
                  onClick={() => navigate({ page: "settings" })}
                  className="flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)] transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
                >
                  <SettingsIcon className="h-4 w-4" />
                  Settings
                </button>
              </>
            )}
          </nav>

          <div className="ml-auto flex items-center gap-3">
            {role !== "unknown" && (
              <span className="text-xs text-[var(--ophalo-muted)] font-medium">{roleLabel(role)}</span>
            )}
            {/* PWA UI-quality correction (2026-08-12): Price Book, Catalog Item Detail, and
                Offering/Assembly Detail each carry their own dominant contextual CTA — a second
                global "New Request" competes with it, so it's dropped only on those three routes.
                Requests stays one click away via the nav pill above. */}
            {route.page !== "pricebook" &&
              route.page !== "pricebook-item" &&
              route.page !== "pricebook-assembly" && (
              <KeepButton variant="primary" onClick={openCapture} className="gap-1.5">
                <Plus className="h-4 w-4" />
                New Request
              </KeepButton>
            )}
          </div>
        </header>
      )}

      {/* Main content */}
      <main className="flex-1 min-w-0 flex flex-col">
        {route.page === "requests" && role === "unknown" && (
          <div className="flex flex-1 items-center justify-center">
            <span className="text-[var(--ophalo-muted)] text-sm">Loading…</span>
          </div>
        )}
        {route.page === "requests" && role === "viewer" && <AccessLimited />}
        {/* Viewer/unknown keep the pre-Step-5 standalone detail render, unaffected by the Queue
            pane (which is gated to eligible roles below) — GAP-042: Viewer can reach Detail via a
            direct link even though it can't reach the Requests list. */}
        {route.page === "detail" && (role === "unknown" || role === "viewer") && (
          <RequestDetail
            requestId={route.requestId}
            focusPanel={route.focusPanel}
            onBack={backToRequests}
            prevId={prevRequestId}
            nextId={nextRequestId}
            onNavigate={(id) => selectRequest(id, navContext ?? undefined)}
          />
        )}
        {(route.page === "requests" || route.page === "detail") && role !== "unknown" && role !== "viewer" && (
          <RequestWorkbenchShell
            role={role}
            route={route.page === "detail" ? { page: "detail", requestId: route.requestId, focusPanel: route.focusPanel } : { page: "requests" }}
            viewCounts={viewCounts}
            onViewCountsUpdate={handleViewCountsUpdate}
            onSelectRequest={selectRequest}
            onNavigateSettings={navigateToSettings}
            onStartCapture={openCapture}
            onBack={backToRequests}
            narrowPrevId={prevRequestId}
            narrowNextId={nextRequestId}
            onNarrowNavigate={(id) => selectRequest(id, navContext ?? undefined)}
          />
        )}
        {route.page === "home" && (
          <Home
            onStartCapture={openCapture}
            role={role}
            onNavigateSettings={navigateToSettings}
            onNavigateRequests={() => navigate({ page: "requests" })}
          />
        )}
        {route.page === "settings" && <Settings callerRole={role} scrollToSection={route.section} />}
        {route.page === "pricebook" && (
          <PriceBook
            role={role}
            entitled={priceBookEntitled}
            entitlementLoading={isOwnerOrAdmin && capabilityLoading}
            entitlementError={isOwnerOrAdmin && capabilityError}
            onRetryEntitlement={() => void refetchCapabilities()}
            onSelectItem={(catalogItemId) => navigate({ page: "pricebook-item", catalogItemId })}
            onSelectAssembly={(offeringAssemblyId) => navigate({ page: "pricebook-assembly", offeringAssemblyId })}
            activeTab={route.tab ?? "items"}
            onTabChange={(tab) => navigate({ page: "pricebook", tab })}
          />
        )}
        {route.page === "pricebook-item" && (
          <CatalogItemDetail
            catalogItemId={route.catalogItemId}
            role={role}
            entitled={priceBookEntitled}
            entitlementLoading={isOwnerOrAdmin && capabilityLoading}
            entitlementError={isOwnerOrAdmin && capabilityError}
            onRetryEntitlement={() => void refetchCapabilities()}
            onBack={() =>
              route.returnToAssembly
                ? navigate({ page: "pricebook-assembly", offeringAssemblyId: route.returnToAssembly })
                : navigate({ page: "pricebook", tab: "items" })
            }
            backLabel={route.returnToAssembly ? "Back to assembly" : "Back to Price Book"}
            returnToAssemblyReason={route.returnToAssembly ? route.returnToAssemblyReason : undefined}
          />
        )}
        {route.page === "pricebook-assembly" && (
          <OfferingAssemblyDetail
            offeringAssemblyId={route.offeringAssemblyId}
            role={role}
            entitled={priceBookEntitled}
            entitlementLoading={isOwnerOrAdmin && capabilityLoading}
            entitlementError={isOwnerOrAdmin && capabilityError}
            onRetryEntitlement={() => void refetchCapabilities()}
            onBack={() => navigate({ page: "pricebook", tab: "assemblies" })}
            onSelectCatalogItem={(catalogItemId, reasonKind) =>
              navigate({
                page: "pricebook-item",
                catalogItemId,
                returnToAssembly: route.offeringAssemblyId,
                returnToAssemblyReason: reasonKind,
              })
            }
          />
        )}
      </main>

      {/* Sticky FAB — mobile only. Session 2e.7c: hidden on Price Book routes, which have their
          own "Add catalog item" action — showing global "New Request" there let an owner create
          the wrong thing. */}
      {route.page !== "detail" &&
        route.page !== "pricebook" &&
        route.page !== "pricebook-item" &&
        route.page !== "pricebook-assembly" && (
        <button
          type="button"
          onClick={openCapture}
          aria-label="New Request"
          className="md:hidden fixed bottom-6 right-6 z-30 flex h-14 w-14 items-center justify-center rounded-full bg-[var(--ophalo-navy)] text-white shadow-lg hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
        >
          <Plus className="h-6 w-6" />
        </button>
      )}

      {/* Quick Capture modal/drawer */}
      {captureOpen && (
        <QuickCapture
          onClose={() => setCaptureOpen(false)}
          onSelectRequest={(id) => { selectRequest(id); setCaptureOpen(false); }}
          isOwnerOrAdmin={role === "owner" || role === "admin"}
          onNavigateSettings={navigateToSettings}
        />
      )}

      {/* Mobile overflow nav — the only mobile-discoverable path to Getting Started, Settings,
          and Price Book (Session 2e.4, build-log/112: no manually-known URL required). */}
      {mobileMenuOpen && (
        <MobileNavMenu
          items={navItems}
          activeId={activeNavId}
          roleLabel={roleLabel(role)}
          onNavigate={(id) => navigate({ page: id })}
          onClose={() => setMobileMenuOpen(false)}
        />
      )}
    </div>
  );
}

export function App() {
  return (
    <AuthGuard>
      <AppShell />
    </AuthGuard>
  );
}
