import type { Metadata } from "next";
import { CustomerTrackerView, type CustomerPageData } from "./CustomerTrackerView";
import { TrackerExpiredView } from "./TrackerExpiredView";

const baseMetadata: Metadata = {
  robots: { index: false, follow: false },
  other: { referrer: "no-referrer" },
};

type PageState =
  | { kind: "unavailable" }
  | {
      kind: "expired";
      businessName: string;
      logoUrl: string | null;
      websiteUrl: string | null;
      phone: string | null;
      referenceCode: string;
    }
  | { kind: "active"; page: CustomerPageData; pageToken: string };

const trackerCanvasStyle = { backgroundColor: "var(--ophalo-canvas)" };

async function fetchPage(pageToken: string): Promise<PageState> {
  const apiBase = process.env.NEXT_PUBLIC_API_BASE_URL;
  if (!apiBase) return { kind: "unavailable" };

  let res: Response;
  try {
    res = await fetch(`${apiBase}/keep/r/${encodeURIComponent(pageToken)}`, {
      cache: "no-store",
    });
  } catch {
    return { kind: "unavailable" };
  }

  if (res.status === 410) {
    const body: unknown = await res.json().catch(() => null);
    const b =
      body != null && typeof body === "object"
        ? (body as Record<string, unknown>)
        : null;
    return {
      kind: "expired",
      businessName:
        typeof b?.businessName === "string" ? b.businessName : "the business",
      logoUrl: typeof b?.logoUrl === "string" ? b.logoUrl : null,
      websiteUrl: typeof b?.websiteUrl === "string" ? b.websiteUrl : null,
      phone: typeof b?.phone === "string" ? b.phone : null,
      referenceCode:
        typeof b?.referenceCode === "string" ? b.referenceCode : "",
    };
  }

  if (!res.ok) return { kind: "unavailable" };

  const data: unknown = await res.json().catch(() => null);
  if (data == null || typeof data !== "object") return { kind: "unavailable" };

  return { kind: "active", page: data as CustomerPageData, pageToken };
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ pageToken: string }>;
}): Promise<Metadata> {
  const { pageToken } = await params;
  const state = await fetchPage(pageToken);

  const businessName =
    state.kind === "active" ? state.page.businessName
      : state.kind === "expired" ? state.businessName
      : null;

  return {
    ...baseMetadata,
    title: businessName ? `${businessName} — Request Tracker` : "Request Tracker",
  };
}

export default async function CustomerTrackerPage({
  params,
  searchParams,
}: {
  params: Promise<{ pageToken: string }>;
  searchParams: Promise<{ welcome?: string }>;
}) {
  const { pageToken } = await params;
  const { welcome } = await searchParams;
  const state = await fetchPage(pageToken);

  if (state.kind === "unavailable") {
    return (
      <main className="min-h-screen px-4 py-6 sm:py-10" style={trackerCanvasStyle}>
        <div className="mx-auto w-full max-w-2xl">
          <h1 className="text-2xl font-bold leading-tight tracking-tight text-foreground">
            This link is not available.
          </h1>
          <p className="mt-3 text-sm leading-6 text-muted-foreground">
            This tracker link is not available. If you were sent this link,
            please contact the business directly for assistance.
          </p>
        </div>
      </main>
    );
  }

  if (state.kind === "expired") {
    return (
      <TrackerExpiredView
        businessName={state.businessName}
        logoUrl={state.logoUrl}
        websiteUrl={state.websiteUrl}
        phone={state.phone}
        referenceCode={state.referenceCode}
      />
    );
  }

  return (
    <CustomerTrackerView
      initialPage={state.page}
      pageToken={state.pageToken}
      showWelcome={welcome === "1"}
    />
  );
}
