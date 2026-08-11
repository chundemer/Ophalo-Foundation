import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type CatalogItemListRowResponse } from "../../lib/apiClient";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

const ERROR_INPUT_CLS = "border-[var(--ophalo-danger)]";

interface CatalogItemPickerProps {
  id: string;
  selectedItemId: string | null;
  selectedItemDisplayName: string | null;
  onSelect: (item: CatalogItemListRowResponse) => void;
  /** Exclude these catalog item ids from results — used when picking an associated item so a
   * primary can't also be added as its own component, and an already-added component doesn't
   * appear twice. */
  excludeIds?: string[];
  disabled?: boolean;
  invalid?: boolean;
  placeholder?: string;
}

/**
 * Shared server-search catalog item picker (Session 3.2c): the catalog is bounded/cursor-paged
 * (Session 2e.3), so this searches on typed input rather than holding the whole catalog in memory,
 * unlike CategoryCombobox's in-memory category list. Only Active items are offered — an inactive
 * item can't become a new primary/component selection.
 */
export function CatalogItemPicker({
  id,
  selectedItemId,
  selectedItemDisplayName,
  onSelect,
  excludeIds = [],
  disabled = false,
  invalid = false,
  placeholder = "Search catalog items…",
}: CatalogItemPickerProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [draftText, setDraftText] = useState("");
  const [debouncedText, setDebouncedText] = useState("");
  const rootRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const handle = setTimeout(() => setDebouncedText(draftText.trim()), 250);
    return () => clearTimeout(handle);
  }, [draftText]);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const { data, isLoading } = useQuery({
    queryKey: ["catalogItems", "picker", debouncedText],
    queryFn: () => api.getCatalogItems({ search: debouncedText || undefined, status: "Active", limit: 20 }),
    enabled: isOpen,
  });

  const results = (data?.items ?? []).filter((row) => !excludeIds.includes(row.item.id));

  return (
    <div ref={rootRef} className="relative">
      <input
        ref={inputRef}
        id={id}
        type="text"
        role="combobox"
        aria-expanded={isOpen}
        aria-controls={`${id}-listbox`}
        autoComplete="off"
        disabled={disabled}
        value={isOpen ? draftText : (selectedItemDisplayName ?? "")}
        placeholder={placeholder}
        onFocus={() => {
          setDraftText("");
          setDebouncedText("");
          setIsOpen(true);
        }}
        onChange={(e) => setDraftText(e.target.value)}
        className={`${INPUT_CLS} ${invalid ? ERROR_INPUT_CLS : ""}`}
      />
      {isOpen && (
        <div
          id={`${id}-listbox`}
          role="listbox"
          className="absolute z-20 mt-1 max-h-64 w-full overflow-y-auto rounded-lg border
            border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-lg"
        >
          {isLoading && <div className="px-3 py-2 text-sm text-[var(--ophalo-muted)]">Searching…</div>}
          {!isLoading && results.length === 0 && (
            <div className="px-3 py-2 text-sm text-[var(--ophalo-muted)]">No active catalog items found.</div>
          )}
          {!isLoading &&
            results.map((row) => (
              <div
                key={row.item.id}
                role="option"
                aria-selected={row.item.id === selectedItemId}
                onMouseDown={(e) => {
                  e.preventDefault();
                  onSelect(row);
                  setIsOpen(false);
                }}
                className={`px-3 py-2 cursor-pointer text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${
                  row.item.id === selectedItemId ? "bg-[var(--ophalo-canvas)]" : ""
                }`}
              >
                <div className="text-sm font-medium">{row.item.displayName}</div>
                {row.item.externalKey && (
                  <div className="text-xs text-[var(--ophalo-muted)]">{row.item.externalKey}</div>
                )}
              </div>
            ))}
        </div>
      )}
    </div>
  );
}
