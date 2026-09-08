import { useEffect } from "react";
import { useUpdatesFeed } from "../hooks/useUpdatesFeed";
import { UpdatesMarkdown } from "../components/updates/UpdatesMarkdown";
import type { UpdateEntry, UpdateGuide } from "../lib/apiClient";

// GAP-038 / BL149 (038-1b-i) — the Help & Updates content surface, reachable at `#/help`.
//
// Content only: a single sectioned scroll (Known issues → Updates → Coming soon → any other
// section → Guides), the feed's last-updated time, and per-entry dates. No "Report a problem"
// header action, no unread indicator, no Requests banner — those are 038-1b-ii / 038-2. Opening
// the page advances the read watermark once the feed has loaded successfully.

function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return d.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

function EntryArticle({ entry }: { entry: UpdateEntry }) {
  const resolved = entry.status === "resolved";
  return (
    <article className="border-b border-[var(--ophalo-border-subtle)] pb-5 last:border-b-0 last:pb-0">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <h3 className="font-serif text-base font-semibold text-[var(--ophalo-ink)]">
          {entry.title}
        </h3>
        {resolved && (
          <span className="text-[0.75rem] font-medium uppercase tracking-wide text-[var(--ophalo-muted)]">
            Resolved
          </span>
        )}
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
    <article className="border-b border-[var(--ophalo-border-subtle)] pb-5 last:border-b-0 last:pb-0">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <h3 className="font-serif text-base font-semibold text-[var(--ophalo-ink)]">{guide.title}</h3>
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
      <h2 className="mb-4 font-serif text-lg font-semibold text-[var(--ophalo-navy)]">{heading}</h2>
      {children}
    </section>
  );
}

function EmptySection({ heading }: { heading: string }) {
  return (
    <Section heading={heading}>
      <p className="text-[0.9375rem] text-[var(--ophalo-muted)]">No updates yet.</p>
    </Section>
  );
}

export function Help() {
  const feed = useUpdatesFeed();
  const { isSuccess, markSeen } = feed;

  useEffect(() => {
    if (isSuccess) markSeen();
  }, [isSuccess, markSeen]);

  return (
    <div className="mx-auto w-full max-w-2xl px-5 py-8">
      <header className="mb-6">
        <h1 className="font-serif text-2xl font-semibold text-[var(--ophalo-ink)]">Help &amp; Updates</h1>
        {feed.feedLastUpdated && (
          <p className="mt-1 text-[0.8125rem] text-[var(--ophalo-muted)]">
            Last updated {formatDate(feed.feedLastUpdated)}
          </p>
        )}
      </header>

      {feed.isLoading && (
        <p className="text-[0.9375rem] text-[var(--ophalo-muted)]">Loading…</p>
      )}

      {feed.isError && (
        <div className="rounded-md border border-[var(--ophalo-border)] bg-[var(--ophalo-surface-muted)] p-4">
          <p className="text-[0.9375rem] text-[var(--ophalo-ink)]">Couldn't load updates, try again.</p>
          <button
            type="button"
            onClick={feed.refetch}
            className="mt-3 rounded border border-[var(--ophalo-border)] px-3 py-1.5 text-[0.8125rem] font-medium text-[var(--ophalo-navy)] hover:bg-white"
          >
            Try again
          </button>
        </div>
      )}

      {feed.isSuccess && (
        <>
          {feed.sections.map((section) => (
            <Section key={section.key} heading={section.heading}>
              <div className="space-y-5">
                {section.entries.map((entry) => (
                  <EntryArticle key={entry.id} entry={entry} />
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
              <div className="space-y-5">
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
