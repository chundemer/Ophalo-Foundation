import { useEffect } from "react";
import { useUpdatesFeed } from "../hooks/useUpdatesFeed";
import { UpdatesMarkdown } from "../components/updates/UpdatesMarkdown";
import { KeepBadge, type KeepBadgeVariant } from "../components/keep/KeepBadge";
import type { UpdateEntry, UpdateGuide } from "../lib/apiClient";

// GAP-038 / BL149 (038-1b-i) — the Help & Updates content surface, reachable at `#/help`.
//
// Content: a single sectioned scroll (Known issues → Updates → Coming soon → any other section →
// Guides), the feed's last-updated time, and per-entry dates. Opening the page advances the read
// watermark once the feed has loaded successfully.
//
// 038-2d-ii: the header carries a "Report a problem" action when the shell passes `onReportProblem`
// (only when the `VITE_FEEDBACK_ENABLED` build flag is on). The unread indicator and Requests
// banner are 038-1b-ii / 038-1b-iii and live in the shell, not here.

function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return d.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

/** Per-section default badge, keyed to `UpdatesSection.key` (see useUpdatesFeed's SECTION_ORDER).
 *  A resolved known_issue overrides its section's badge below. */
const SECTION_BADGE: Record<string, { variant: KeepBadgeVariant; label: string }> = {
  known_issue: { variant: "danger", label: "Investigating" },
  whats_new: { variant: "success", label: "New Feature" },
  coming_soon: { variant: "info", label: "Planned" },
};

function entryBadge(entry: UpdateEntry, sectionKey: string) {
  if (entry.status === "resolved") return { variant: "default" as const, label: "Resolved" };
  return SECTION_BADGE[sectionKey] ?? null;
}

function EntryArticle({ entry, sectionKey }: { entry: UpdateEntry; sectionKey: string }) {
  const badge = entryBadge(entry, sectionKey);
  return (
    <article className="rounded-xl border border-[var(--ophalo-border)] bg-white p-4 shadow-sm sm:p-5">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <h3 className="text-[0.9375rem] font-semibold text-[var(--ophalo-ink)]">{entry.title}</h3>
        {badge && <KeepBadge variant={badge.variant}>{badge.label}</KeepBadge>}
        <time className="ml-auto text-[0.8125rem] text-[var(--ophalo-muted)]" dateTime={entry.published_at}>
          {formatDate(entry.published_at)}
        </time>
      </div>
      <div className="mt-2">
        <UpdatesMarkdown markdown={entry.body} />
      </div>
    </article>
  );
}

function GuideArticle({ guide }: { guide: UpdateGuide }) {
  return (
    <article className="rounded-xl border border-[var(--ophalo-border)] bg-white p-4 shadow-sm sm:p-5">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <h3 className="text-[0.9375rem] font-semibold text-[var(--ophalo-ink)]">{guide.title}</h3>
        <time className="ml-auto text-[0.8125rem] text-[var(--ophalo-muted)]" dateTime={guide.updated_at}>
          Updated {formatDate(guide.updated_at)}
        </time>
      </div>
      <div className="mt-2">
        <UpdatesMarkdown markdown={guide.body} />
      </div>
    </article>
  );
}

function Section({ heading, children }: { heading: string; children: React.ReactNode }) {
  return (
    <section className="mt-8 first:mt-0">
      <h2 className="mb-3 text-xs font-bold uppercase tracking-wider text-slate-500">{heading}</h2>
      {children}
    </section>
  );
}

const EMPTY_SECTION_COPY: Record<string, { primary: string; secondary: string }> = {
  "Known issues": {
    primary: "No known issues",
    secondary: "You're all caught up — we'll post here if something needs your attention.",
  },
  Updates: {
    primary: "No updates yet",
    secondary: "New features and improvements will appear here as they ship.",
  },
  "Coming soon": {
    primary: "Nothing planned yet",
    secondary: "Check back for a look at what's next.",
  },
  Guides: {
    primary: "No guides published yet",
    secondary: "Documentation and walkthroughs will appear here as they are released.",
  },
};

function EmptySection({ heading }: { heading: string }) {
  const copy = EMPTY_SECTION_COPY[heading] ?? {
    primary: `No ${heading.toLowerCase()} yet`,
    secondary: "Check back later.",
  };
  return (
    <Section heading={heading}>
      <div className="rounded-xl border-2 border-dashed border-slate-300 bg-slate-50/50 p-6 text-center">
        <p className="text-[0.9375rem] font-semibold text-[var(--ophalo-ink)]">{copy.primary}</p>
        <p className="mt-1 text-[0.8125rem] text-slate-500">{copy.secondary}</p>
      </div>
    </Section>
  );
}

function ChatIcon() {
  return (
    <svg
      viewBox="0 0 20 20"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      className="h-4 w-4"
      aria-hidden="true"
    >
      <path
        d="M3 5.5A1.5 1.5 0 0 1 4.5 4h11A1.5 1.5 0 0 1 17 5.5v6A1.5 1.5 0 0 1 15.5 13H8l-3.5 3v-3H4.5A1.5 1.5 0 0 1 3 11.5v-6Z"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

interface HelpProps {
  /** 038-2d-ii: opens the feedback dialog. Absent unless the shell's `VITE_FEEDBACK_ENABLED`
   *  build flag is on, in which case the header shows a "Report a problem" action. */
  onReportProblem?: () => void;
}

export function Help({ onReportProblem }: HelpProps = {}) {
  const feed = useUpdatesFeed();
  const { isSuccess, markSeen } = feed;

  useEffect(() => {
    if (isSuccess) markSeen();
  }, [isSuccess, markSeen]);

  return (
    <div className="mx-auto w-full max-w-4xl px-4 py-10">
      <header className="mb-6 flex items-start justify-between gap-4">
        <div>
          <h1 className="keep-page-title tracking-tight">Help &amp; Updates</h1>
          {feed.feedLastUpdated && (
            <p className="mt-1 text-xs text-slate-500">Last updated {formatDate(feed.feedLastUpdated)}</p>
          )}
        </div>
        {onReportProblem && (
          <button
            type="button"
            onClick={onReportProblem}
            className="inline-flex shrink-0 items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-[0.8125rem] font-medium text-slate-700 shadow-sm hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
          >
            <ChatIcon />
            Report a problem
          </button>
        )}
      </header>

      {feed.isLoading && (
        <p className="text-[0.9375rem] text-[var(--ophalo-muted)]">Loading…</p>
      )}

      {feed.isError && (
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-white p-4 shadow-sm">
          <p className="text-[0.9375rem] text-[var(--ophalo-ink)]">Couldn't load updates, try again.</p>
          <button
            type="button"
            onClick={feed.refetch}
            className="mt-3 rounded-lg border border-slate-300 px-3 py-1.5 text-[0.8125rem] font-medium text-slate-700 hover:bg-slate-50"
          >
            Try again
          </button>
        </div>
      )}

      {feed.isSuccess && (
        <>
          {feed.sections.map((section) => (
            <Section key={section.key} heading={section.heading}>
              <div className="space-y-4">
                {section.entries.map((entry) => (
                  <EntryArticle key={entry.id} entry={entry} sectionKey={section.key} />
                ))}
              </div>
            </Section>
          ))}

          {feed.sections.length === 0 && (
            <>
              <EmptySection heading="Known issues" />
              <EmptySection heading="Updates" />
              <EmptySection heading="Coming soon" />
            </>
          )}

          {feed.guides.length > 0 ? (
            <Section heading="Guides">
              <div className="space-y-4">
                {feed.guides.map((guide) => (
                  <GuideArticle key={guide.id} guide={guide} />
                ))}
              </div>
            </Section>
          ) : (
            <EmptySection heading="Guides" />
          )}
        </>
      )}
    </div>
  );
}
