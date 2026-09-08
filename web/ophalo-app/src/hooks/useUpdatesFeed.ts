import { useCallback, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type UpdatesFeed, type UpdateEntry, type UpdateGuide } from "../lib/apiClient";

// GAP-038 / BL149 (038-1b-i) — the Help & Updates feed hook.
//
// Responsibilities kept in this one module (there is no shared localStorage helper in `src`, and
// folding the watermark here holds the 1b-i file count):
//   * fetch `GET /updates` through react-query;
//   * group entries into the fixed Help-page section order;
//   * derive the feed's last-updated timestamp (entries *and* guides) for display;
//   * read / advance the single client-side `keep_updates_watermark` (epoch-ms string);
//   * derive the Requests-list highlight banner and track per-entry dismissals
//     (`keep_dismissed_banners`) — 038-1b-iii.
//
// The backend always returns a schema-valid body — a fresh read, a per-instance last-known-good
// copy, or the empty fallback — so a successful response is treated as ordinary fresh data. There
// is deliberately no frontend "stale content" state; the only non-success path is a transport
// failure (offline), surfaced as `isError`.

/** Single source of truth for the watermark key; 038-1b-ii reads it for the unread indicator. */
export const WATERMARK_KEY = "keep_updates_watermark";

/**
 * 038-1b-iii — ids of highlight entries the user has dismissed from the Requests-list banner.
 * A JSON string array in localStorage. Dismissal is independent of the watermark: it hides one
 * banner without marking the whole feed seen.
 */
export const DISMISSED_BANNERS_KEY = "keep_dismissed_banners";

/** A `whats_new` highlight banners for 14 days from `published_at` (inclusive). */
const BANNER_WHATS_NEW_MAX_AGE_MS = 14 * 24 * 60 * 60 * 1000;

/** V1 sections in Help-page render order. Anything else falls through to a neutral heading. */
const SECTION_ORDER = ["known_issue", "whats_new", "coming_soon"] as const;

const SECTION_HEADINGS: Record<string, string> = {
  known_issue: "Known issues",
  whats_new: "Updates",
  coming_soon: "Coming soon",
};

const FALLBACK_HEADING = "More updates";
const FALLBACK_KEY = "__other__";

export interface UpdatesSection {
  key: string;
  heading: string;
  entries: UpdateEntry[];
}

export interface UseUpdatesFeedResult {
  isLoading: boolean;
  isError: boolean;
  isSuccess: boolean;
  refetch: () => void;
  /** Sections in fixed order, each with at least one entry. Empty when the feed carries none. */
  sections: UpdatesSection[];
  guides: UpdateGuide[];
  /** ISO string of the newest `published_at` / `updated_at` across the feed, or null when empty. */
  feedLastUpdated: string | null;
  /** Persisted watermark (epoch ms), or null when unset / unreadable. */
  watermark: number | null;
  /**
   * Count of feed `entries` published after the stored watermark (guides never count). Drives the
   * shell "Help & Updates · N new" suffix and the trigger dot. An absent / unreadable watermark
   * makes every entry unseen. Only ever over-reports, never the reverse.
   */
  unseenCount: number;
  /**
   * The single highlight entry to show in the Requests-list banner — the most-recently-published
   * qualifying entry — or null when none qualifies. `null` on loading / error / empty feed.
   */
  bannerEntry: UpdateEntry | null;
  /** Count of *other* qualifying highlight entries beyond `bannerEntry` (drives "N more updates →"). */
  bannerMoreCount: number;
  /**
   * Dismiss one highlight entry from the banner (adds its id to `keep_dismissed_banners`). Hides
   * the banner immediately; does not touch the watermark. No-ops on unavailable storage.
   */
  dismissBanner: (entryId: string) => void;
  /**
   * Advance the watermark to the newest *past* entry (`published_at <= now`). Guides never move
   * it. No-ops on loading / error / empty feed, or when storage is unavailable — the watermark is
   * only ever written after a successful load that yielded a valid maximum.
   */
  markSeen: () => void;
}

export function readWatermark(): number | null {
  try {
    const raw = window.localStorage.getItem(WATERMARK_KEY);
    if (raw == null) return null;
    const n = Number(raw);
    return Number.isFinite(n) ? n : null;
  } catch {
    return null;
  }
}

function writeWatermark(value: number): void {
  try {
    window.localStorage.setItem(WATERMARK_KEY, String(value));
  } catch {
    // Private-mode / quota / disabled storage — a missed watermark only over-reports "new", never
    // the reverse, so swallow it.
  }
}

export function readDismissedBanners(): string[] {
  try {
    const raw = window.localStorage.getItem(DISMISSED_BANNERS_KEY);
    if (raw == null) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((v): v is string => typeof v === "string");
  } catch {
    return [];
  }
}

function writeDismissedBanners(ids: string[]): void {
  try {
    window.localStorage.setItem(DISMISSED_BANNERS_KEY, JSON.stringify(ids));
  } catch {
    // Storage unavailable — the banner reappears on reload, which is acceptable.
  }
}

/** True when `entry` is inside its banner lifetime relative to `nowMs`. */
function isWithinBannerLifetime(entry: UpdateEntry, nowMs: number): boolean {
  if (entry.banner_until != null) {
    // Hard override in both directions: a future timestamp qualifies regardless of section/age,
    // a past (or unparseable) one disqualifies regardless.
    const until = Date.parse(entry.banner_until);
    return Number.isFinite(until) && until > nowMs;
  }
  if (entry.section === "known_issue") return entry.status === "active";
  if (entry.section === "whats_new") {
    const published = Date.parse(entry.published_at);
    if (!Number.isFinite(published) || published > nowMs) return false;
    return nowMs - published <= BANNER_WHATS_NEW_MAX_AGE_MS;
  }
  return false;
}

/**
 * Pick the banner entry: `highlight === true`, not dismissed, and within its lifetime; the
 * most-recently-published wins and the rest become `moreCount`.
 */
export function computeBannerState(
  entries: UpdateEntry[],
  dismissedIds: string[],
  now: Date,
): { entry: UpdateEntry | null; moreCount: number } {
  const nowMs = now.getTime();
  const dismissed = new Set(dismissedIds);
  const qualifying = entries.filter(
    (e) => e.highlight === true && !dismissed.has(e.id) && isWithinBannerLifetime(e, nowMs),
  );
  if (qualifying.length === 0) return { entry: null, moreCount: 0 };
  const [newest] = [...qualifying].sort(
    (a, b) => Date.parse(b.published_at) - Date.parse(a.published_at),
  );
  return { entry: newest, moreCount: qualifying.length - 1 };
}

function sortEntries(section: string, entries: UpdateEntry[]): UpdateEntry[] {
  const byNewest = (a: UpdateEntry, b: UpdateEntry) =>
    Date.parse(b.published_at) - Date.parse(a.published_at);
  if (section !== "known_issue") return [...entries].sort(byNewest);
  // Known issues: unresolved first, then resolved; newest within each group.
  const rank = (e: UpdateEntry) => (e.status === "resolved" ? 1 : 0);
  return [...entries].sort((a, b) => rank(a) - rank(b) || byNewest(a, b));
}

export function groupSections(entries: UpdateEntry[]): UpdatesSection[] {
  const buckets = new Map<string, UpdateEntry[]>();
  for (const entry of entries) {
    const key = (SECTION_ORDER as readonly string[]).includes(entry.section)
      ? entry.section
      : FALLBACK_KEY;
    const list = buckets.get(key);
    if (list) list.push(entry);
    else buckets.set(key, [entry]);
  }

  const sections: UpdatesSection[] = [];
  for (const key of SECTION_ORDER) {
    const list = buckets.get(key);
    if (list && list.length > 0) {
      sections.push({ key, heading: SECTION_HEADINGS[key], entries: sortEntries(key, list) });
    }
  }
  const other = buckets.get(FALLBACK_KEY);
  if (other && other.length > 0) {
    sections.push({ key: FALLBACK_KEY, heading: FALLBACK_HEADING, entries: sortEntries(FALLBACK_KEY, other) });
  }
  return sections;
}

/** Number of entries published strictly after `watermark` (null watermark → all entries). */
export function computeUnseenCount(entries: UpdateEntry[], watermark: number | null): number {
  let count = 0;
  for (const entry of entries) {
    const t = Date.parse(entry.published_at);
    if (!Number.isFinite(t)) continue;
    if (watermark === null || t > watermark) count += 1;
  }
  return count;
}

/** Newest `published_at` across entries that are not future-dated relative to `now`. */
export function computeFeedMax(entries: UpdateEntry[], now: Date): number | null {
  const nowMs = now.getTime();
  let max: number | null = null;
  for (const entry of entries) {
    const t = Date.parse(entry.published_at);
    if (!Number.isFinite(t) || t > nowMs) continue;
    if (max === null || t > max) max = t;
  }
  return max;
}

function computeLastUpdated(feed: UpdatesFeed): string | null {
  let maxMs: number | null = null;
  let maxIso: string | null = null;
  const consider = (iso: string) => {
    const t = Date.parse(iso);
    if (!Number.isFinite(t)) return;
    if (maxMs === null || t > maxMs) {
      maxMs = t;
      maxIso = iso;
    }
  };
  for (const e of feed.entries) consider(e.published_at);
  for (const g of feed.guides) consider(g.updated_at);
  return maxIso;
}

const EMPTY_FEED: UpdatesFeed = { schema: 1, entries: [], guides: [] };

export function useUpdatesFeed(options: { now?: Date } = {}): UseUpdatesFeedResult {
  const nowRef = useRef(options.now);
  nowRef.current = options.now;

  const query = useQuery({
    queryKey: ["updates-feed"],
    queryFn: () => api.getUpdates(),
    // Matches the backend's `Cache-Control: private, max-age=300`.
    staleTime: 5 * 60 * 1000,
  });

  const feed = query.data ?? EMPTY_FEED;

  const sections = useMemo(() => groupSections(feed.entries), [feed.entries]);
  const feedLastUpdated = useMemo(() => computeLastUpdated(feed), [feed]);

  const watermark = readWatermark();
  const unseenCount = query.isSuccess ? computeUnseenCount(feed.entries, watermark) : 0;

  const [dismissedBannerIds, setDismissedBannerIds] = useState<string[]>(readDismissedBanners);
  const dismissBanner = useCallback((entryId: string) => {
    setDismissedBannerIds((current) => {
      if (current.includes(entryId)) return current;
      const next = [...current, entryId];
      writeDismissedBanners(next);
      return next;
    });
  }, []);

  const banner = query.isSuccess
    ? computeBannerState(feed.entries, dismissedBannerIds, nowRef.current ?? new Date())
    : { entry: null, moreCount: 0 };

  const feedMaxRef = useRef<number | null>(null);
  feedMaxRef.current = query.isSuccess
    ? computeFeedMax(feed.entries, nowRef.current ?? new Date())
    : null;

  const markSeen = useCallback(() => {
    const feedMax = feedMaxRef.current;
    if (feedMax === null) return;
    writeWatermark(feedMax);
  }, []);

  return {
    isLoading: query.isLoading,
    isError: query.isError,
    isSuccess: query.isSuccess,
    refetch: () => {
      void query.refetch();
    },
    sections,
    guides: feed.guides,
    feedLastUpdated,
    watermark,
    unseenCount,
    bannerEntry: banner.entry,
    bannerMoreCount: banner.moreCount,
    dismissBanner,
    markSeen,
  };
}
