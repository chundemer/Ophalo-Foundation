import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Request Tracker",
  robots: { index: false, follow: false },
  other: { referrer: "no-referrer" },
};

interface CustomerEventItem {
  eventType: string;
  content: string | null;
  occurredAtUtc: string;
  actorLabel: string;
}

interface CustomerPageData {
  businessName: string;
  referenceCode: string;
  status: string;
  description: string | null;
  currentStatusText: string | null;
  isTerminal: boolean | null;
  events: CustomerEventItem[] | null;
}

type PageState =
  | { kind: "unavailable" }
  | { kind: "expired"; businessName: string; referenceCode: string }
  | { kind: "active"; page: CustomerPageData };

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
      referenceCode:
        typeof b?.referenceCode === "string" ? b.referenceCode : "",
    };
  }

  if (!res.ok) return { kind: "unavailable" };

  const data: unknown = await res.json().catch(() => null);
  if (data == null || typeof data !== "object") return { kind: "unavailable" };

  return { kind: "active", page: data as CustomerPageData };
}

function statusLabel(status: string): string {
  switch (status) {
    case "received":
      return "Received";
    case "scheduled":
      return "Scheduled";
    case "in_progress":
      return "In Progress";
    case "pending_customer":
      return "Pending Your Response";
    case "resolved":
      return "Resolved";
    case "closed":
      return "Closed";
    case "cancelled":
      return "Cancelled";
    default:
      return status;
  }
}

function actorDisplay(actorLabel: string): string {
  switch (actorLabel) {
    case "customer":
      return "You";
    case "business":
      return "Business";
    default:
      return "";
  }
}

function eventFallbackLabel(eventType: string): string {
  switch (eventType) {
    case "request_created":
      return "Request created";
    case "status_changed":
      return "Status updated";
    case "message_added":
      return "Message";
    case "request_closed":
      return "Request closed";
    case "request_cancelled":
      return "Request cancelled";
    case "attention_acknowledged":
      return "Message acknowledged";
    default:
      return "";
  }
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

export default async function CustomerTrackerPage({
  params,
}: {
  params: Promise<{ pageToken: string }>;
}) {
  const { pageToken } = await params;
  const state = await fetchPage(pageToken);

  if (state.kind === "unavailable") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>This link is not available.</h1>
          <p>
            This tracker link is not available. If you were sent this link,
            please contact the business directly for assistance.
          </p>
        </div>
      </div>
    );
  }

  if (state.kind === "expired") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>This tracker link has expired.</h1>
          <p>
            The tracker for your request with{" "}
            <strong>{state.businessName}</strong> is no longer active.
          </p>
          {state.referenceCode && (
            <p>
              Reference:{" "}
              <strong className="auth-reference-code">
                {state.referenceCode}
              </strong>
            </p>
          )}
        </div>
      </div>
    );
  }

  const { page } = state;
  const events = page.events ?? [];

  return (
    <div className="auth-page">
      <div className="container">
        <h1>{page.businessName}</h1>
        <p>
          Reference:{" "}
          <strong className="auth-reference-code">{page.referenceCode}</strong>
        </p>
        <p>
          <strong>Status:</strong> {statusLabel(page.status)}
          {page.currentStatusText && ` — ${page.currentStatusText}`}
        </p>
        {page.description && (
          <p>
            <strong>Your request:</strong> {page.description}
          </p>
        )}
        {events.length > 0 && (
          <section>
            <h2>Activity</h2>
            <ul className="tracker-events">
              {events.map((ev, i) => {
                const actor = actorDisplay(ev.actorLabel);
                const content =
                  ev.content ?? eventFallbackLabel(ev.eventType);
                return (
                  <li key={i} className="tracker-event">
                    <p className="tracker-event-meta">
                      {formatDate(ev.occurredAtUtc)}
                      {actor && ` · ${actor}`}
                    </p>
                    {content && (
                      <p className="tracker-event-content">{content}</p>
                    )}
                  </li>
                );
              })}
            </ul>
          </section>
        )}
      </div>
    </div>
  );
}
