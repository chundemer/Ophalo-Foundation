import { useState, useCallback, useEffect, useLayoutEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { AuthGuard } from "./components/AuthGuard";
import { QuickCapture } from "./components/QuickCapture";
import { KeepButton } from "./components/keep/KeepButton";
import { Home } from "./pages/Home";
import { RequestWorkbenchShell } from "./components/requests/RequestWorkbenchShell";
import { RequestDetail } from "./pages/RequestDetail";
import { ActualWorkWorkspacePage } from "./pages/ActualWorkWorkspacePage";
import { AccessLimited } from "./pages/AccessLimited";
import { Settings } from "./pages/Settings";
import { PriceBook } from "./pages/PriceBook";
import { CatalogItemDetail } from "./pages/CatalogItemDetail";
import { OfferingAssemblyDetail } from "./pages/OfferingAssemblyDetail";
import { MobileNavMenu } from "./components/layout/MobileNavMenu";
import { LiveAnnouncerRegion } from "./components/a11y/LiveAnnouncerRegion";
import { Plus, Inbox, Settings as SettingsIcon, Tag, Menu } from "lucide-react";
import { api, type AccountRole, type KeepRequestViewCounts } from "./lib/apiClient";

// ADR-462: AccountCapabilityPackageEnrollment.FeatureKeys.PriceBookQuotesMaterials.
const PRICE_BOOK_FEATURE_KEY = "keep.price_book_quotes_materials";

// Application-shell nav boundary is Tailwind's md:/768px — the same breakpoint that gates the
// mobile top bar's hamburger trigger (the only way to open MobileNavMenu) and the desktop
// sidebar/aside. This is a different boundary from RequestWorkbenchShell's 1001px request-
// workspace pane split; do not conflate the two (correction, 2026-08-26). Slice 5c: the phone
// menu omits Price Book/Settings/Account Administration (which lives inside Settings, with no
// independent entry) unconditionally — there is no reachable width where the phone menu opens
// and desktop/tablet nav (sidebar) is also available, so the omission does not need to be width-
// gated in code; it already only ever renders in the phone menu.
const PHONE_OMITTED_NAV_IDS: ReadonlySet<NavItem["id"]> = new Set(["pricebook", "settings"]);

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
  | { page: "detail"; requestId: string; focusPanel?: string }
  // BL136 4f-i (D7): the dedicated Actual Work Ticket Workspace route. `visit` is `"new"` (a
  // transient/deep-link entry that self-creates a Draft then replaces the URL with `"draft"`),
  // `"draft"` (the request's one open Draft), or a submitted visit id (read-only). Capture intent
  // (record-mine / transcribe) is chosen on the card and never encoded in the URL.
  | { page: "actual-work"; requestId: string; visit: "new" | "draft" | (string & {}) };

interface RequestNavContext {
  requestIds: string[];
}

// Assembly endpoint identifiers are GUIDs. Keeping the return context to that shape prevents a
// hand-authored or malformed hash from replacing the normal, safe Catalog Items back target.
function isOfferingAssemblyId(value: string | null): value is string {
  return value !== null && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

export function getRouteFromLocation(): AppRoute {
  const hash = window.location.hash;
  // Checked before the generic `#/request/(.+)` pattern below — its `(.+)` would otherwise
  // swallow `<id>/actual-work/<seg>` as the request id. Segment: `new` | `draft` | visit id.
  const workspaceMatch = hash.match(/^#\/request\/([^/]+)\/actual-work\/([^/]+)$/);
  if (workspaceMatch?.[1] && workspaceMatch?.[2]) {
    return { page: "actual-work", requestId: workspaceMatch[1], visit: workspaceMatch[2] };
  }
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
  const phoneNavItems = navItems.filter((item) => !PHONE_OMITTED_NAV_IDS.has(item.id));
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  // Incremented only by an explicit Requests navigation. The wide workbench consumes this as a
  // fresh-entry intent without remounting its Queue pane (so filters and scroll still persist).
  const [requestEntryIntent, setRequestEntryIntent] = useState(0);
  // True only while RequestWorkbenchShell measures itself at the wide pane-split width — the
  // narrow fallback, Price Book, and every other route keep normal document scroll. Drives a
  // real h-dvh/overflow-hidden ancestor for RequestWorkbenchShell's internal per-pane scroll
  // regions, which otherwise have no bounded-height parent to resolve against.
  const [workbenchWideActive, setWorkbenchWideActive] = useState(false);

  // The app root bounds the Workbench panes, but the browser still owns html/body document
  // scrolling unless it is explicitly locked. During a desktop resize, native scroll clamping
  // could otherwise retain a document scroll position and carry the shell/Anchor away. Pane mode
  // has exactly two scroll owners (Queue and Work Canvas), so enter it at document top and prevent
  // html/body from becoming a third one. Layout effect avoids a visible one-frame jump.
  useLayoutEffect(() => {
    if (!workbenchWideActive) return;

    document.documentElement.classList.add("workbench-scroll-lock");
    window.scrollTo(0, 0);

    return () => {
      document.documentElement.classList.remove("workbench-scroll-lock");
    };
  }, [workbenchWideActive]);

  function navigate(newRoute: AppRoute) {
    const base = window.location.pathname + window.location.search;
    if (newRoute.page === "detail") {
      history.pushState(null, "", `${base}#/request/${newRoute.requestId}`);
    } else if (newRoute.page === "actual-work") {
      history.pushState(null, "", `${base}#/request/${newRoute.requestId}/actual-work/${newRoute.visit}`);
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

  function navigateToRequests() {
    setRequestEntryIntent((current) => current + 1);
    navigate({ page: "requests" });
    setNavContext(null);
  }

  function selectRequest(requestId: string, context?: RequestNavContext, focus?: string) {
    navigate({ page: "detail", requestId, focusPanel: focus });
    setNavContext(context ?? null);
  }

  // BL136 4f-i: open the dedicated Actual Work Ticket Workspace route (wide screens only — the
  // card entry point never calls this below 1001px, and the page itself redirects a narrow
  // deep-link back to Request Detail).
  function navigateToActualWorkspace(
    requestId: string,
    visit: "new" | "draft" | (string & {}) = "draft",
  ) {
    navigate({ page: "actual-work", requestId, visit });
  }

  function backToRequests() {
    navigate({ page: "requests" });
    setNavContext(null);
  }

  // GAP-061: a queue switch moved the working context off the open Request Detail. Replace
  // (not push) the history entry in both cases so browser Back does not return to the stale
  // request under the new queue label.
  function openDestinationRequest(requestId: string, requestIds: string[]) {
    const base = window.location.pathname + window.location.search;
    history.replaceState(null, "", `${base}#/request/${requestId}`);
    setRoute({ page: "detail", requestId });
    setNavContext(requestIds.length > 0 ? { requestIds } : null);
  }

  function exitStaleDetail() {
    const base = window.location.pathname + window.location.search;
    history.replaceState(null, "", base);
    setRoute({ page: "requests" });
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

  // V2 application shell: one horizontal top-nav header above the content, no desktop
  // left sidebar. Every authenticated desktop route now uses it.
  const usesTopNavShell =
    route.page === "requests" ||
    route.page === "detail" ||
    route.page === "home" ||
    route.page === "settings" ||
    route.page === "pricebook" ||
    route.page === "pricebook-item" ||
    route.page === "pricebook-assembly" ||
    route.page === "actual-work";

  // BL136 4f-iii: the Actual Work workspace hosts the composer inline (not as a full-bleed
  // modal), so it needs the same bounded-height ancestor the wide Workbench uses — its header
  // band stays pinned and the capture surface owns its own scroll, rather than the document
  // growing past the viewport.
  const boundedShell = workbenchWideActive || route.page === "actual-work";
  // Financial work uses one cool workspace canvas: Actual Work review and the entitlement-gated
  // Price Book. Request communication/data keeps the warm operational canvas.
  const usesFinancialCanvas =
    route.page === "actual-work" ||
    route.page === "pricebook" ||
    route.page === "pricebook-item" ||
    route.page === "pricebook-assembly";

  // Every route is a column: mobile top bar or desktop top-nav header above the content.
  return (
    <div
      className={`flex flex-col ${usesFinancialCanvas ? "bg-[var(--keep-workspace-canvas)]" : "bg-[var(--ophalo-canvas)]"} ${
        boundedShell ? "h-dvh overflow-hidden" : "min-h-screen"
      } ${usesTopNavShell ? "" : "md:flex-row"}`}
    >
      {/* Top bar — mobile only, all routes: logo + hamburger trigger for MobileNavMenu. */}
      {role !== "unknown" && (
        <header className="md:hidden flex items-center justify-between px-4 min-h-14 shrink-0 bg-[var(--ophalo-card)] border-b border-[var(--ophalo-border)] pt-[env(safe-area-inset-top)]">
          <button
            type="button"
            onClick={navigateToRequests}
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

      {/* Top nav — every authenticated desktop route */}
      {usesTopNavShell && (
        <header className="hidden md:flex items-center gap-3 px-4 h-14 shrink-0 bg-[var(--ophalo-card)] border-b border-[var(--ophalo-border)]">
          <button
            type="button"
            onClick={navigateToRequests}
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
              onClick={navigateToRequests}
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
                  className={`flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
                    activeNavId === "home"
                      ? "bg-[var(--keep-accent-bg)] text-[var(--ophalo-navy)] font-semibold"
                      : "text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)]"
                  }`}
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
                  className={`flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
                    activeNavId === "settings"
                      ? "bg-[var(--keep-accent-bg)] text-[var(--ophalo-navy)] font-semibold"
                      : "text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)]"
                  }`}
                >
                  <SettingsIcon className="h-4 w-4" />
                  Settings
                </button>
              </>
            )}
          </nav>

          <div className="ml-auto flex items-center gap-3">
            {role !== "unknown" && (
              <span className="text-xs text-[var(--ophalo-muted)] font-medium">
                {me?.userName ? `${me.userName} · ${roleLabel(role)}` : roleLabel(role)}
              </span>
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
      <main className={`flex-1 min-w-0 flex flex-col ${boundedShell ? "min-h-0 overflow-hidden" : ""}`}>
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
            onNavigateToActualWorkspace={navigateToActualWorkspace}
          />
        )}
        {route.page === "actual-work" && role !== "unknown" && (
          <ActualWorkWorkspacePage
            requestId={route.requestId}
            visit={route.visit}
            onExit={() => navigate({ page: "detail", requestId: route.requestId })}
            onResolvedToDraft={() => {
              history.replaceState(
                null,
                "",
                `${window.location.pathname}${window.location.search}#/request/${route.requestId}/actual-work/draft`,
              );
              setRoute({ page: "actual-work", requestId: route.requestId, visit: "draft" });
            }}
            onSwitchVisit={(visitId) => {
              // BL138 Slice 2: retain the exact-visit URL, but replace (not push) so browser Back
              // does not walk through half-reviewed visits — mirrors the GAP-061 "context moved"
              // pattern and `onResolvedToDraft` above.
              history.replaceState(
                null,
                "",
                `${window.location.pathname}${window.location.search}#/request/${route.requestId}/actual-work/${visitId}`,
              );
              setRoute({ page: "actual-work", requestId: route.requestId, visit: visitId });
            }}
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
            requestEntryIntent={requestEntryIntent}
            onBack={backToRequests}
            onOpenDestinationRequest={openDestinationRequest}
            onExitStaleDetail={exitStaleDetail}
            narrowPrevId={prevRequestId}
            narrowNextId={nextRequestId}
            onNarrowNavigate={(id) => selectRequest(id, navContext ?? undefined)}
            onWideModeChange={setWorkbenchWideActive}
            onNavigateToActualWorkspace={navigateToActualWorkspace}
          />
        )}
        {route.page === "home" && (
          <Home
            onStartCapture={openCapture}
            role={role}
            onNavigateSettings={navigateToSettings}
            onNavigateRequests={navigateToRequests}
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

      {/* Mobile overflow nav — the only mobile-discoverable path to Getting Started (Session 2e.4,
          build-log/112: no manually-known URL required). Settings and Price Book (and, since
          Account Administration lives inside Settings, that too) are unconditionally omitted from
          `items` — phone pilot posture locked/corrected 2026-08-26. This menu only ever opens
          below md:/768px (the header holding its trigger is `md:hidden`), which is also the only
          width where the desktop sidebar/aside — where these routes remain reachable — is absent;
          there is no width where both this menu and the sidebar are unavailable. */}
      {mobileMenuOpen && (
        <MobileNavMenu
          items={phoneNavItems}
          activeId={activeNavId}
          roleLabel={roleLabel(role)}
          onNavigate={(id) => id === "requests" ? navigateToRequests() : navigate({ page: id })}
          onClose={() => setMobileMenuOpen(false)}
        />
      )}
    </div>
  );
}

export function App() {
  return (
    <AuthGuard>
      <LiveAnnouncerRegion />
      <AppShell />
    </AuthGuard>
  );
}
