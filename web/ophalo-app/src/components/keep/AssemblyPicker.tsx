import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type OfferingAssemblyListRowResponse } from "../../lib/apiClient";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

const ERROR_INPUT_CLS = "border-[var(--ophalo-danger)]";

interface AssemblyPickerProps {
  id: string;
  selectedAssemblyId: string | null;
  selectedAssemblyDisplayName: string | null;
  onSelect: (assembly: OfferingAssemblyListRowResponse) => void;
  disabled?: boolean;
  invalid?: boolean;
  placeholder?: string;
}

/**
 * Owner/Admin assembly selector for Paired Nudges (build-log/124, Session 4). Unlike
 * CatalogItemPicker, there is no admin-gated search endpoint for assemblies
 * (`GET /keep/pricebook/offering-assemblies` only supports status + cursor), so this is a
 * browse-only cursor-paginated dropdown rather than a type-to-search combobox.
 */
export function AssemblyPicker({
  id,
  selectedAssemblyId,
  selectedAssemblyDisplayName,
  onSelect,
  disabled = false,
  invalid = false,
  placeholder = "Browse assemblies…",
}: AssemblyPickerProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [cursors, setCursors] = useState<(string | undefined)[]>([undefined]);
  const [cursorIndex, setCursorIndex] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const activeCursor = cursors[cursorIndex];

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
    queryKey: ["offeringAssemblies", "picker", "Active", activeCursor ?? null],
    queryFn: () => api.getOfferingAssemblies({ status: "Active", cursor: activeCursor, limit: 20 }),
    enabled: isOpen,
  });

  const results = data?.items ?? [];

  function goNext() {
    if (!data?.nextCursor) return;
    const nextCursor = data.nextCursor;
    setCursors((prev) => [...prev.slice(0, cursorIndex + 1), nextCursor]);
    setCursorIndex((prev) => prev + 1);
  }

  function goPrev() {
    setCursorIndex((prev) => Math.max(0, prev - 1));
  }

  return (
    <div ref={rootRef} className="relative">
      <input
        id={id}
        type="text"
        role="combobox"
        aria-expanded={isOpen}
        aria-controls={`${id}-listbox`}
        autoComplete="off"
        readOnly
        disabled={disabled}
        value={selectedAssemblyDisplayName ?? ""}
        placeholder={placeholder}
        onFocus={() => setIsOpen(true)}
        onClick={() => setIsOpen(true)}
        className={`${INPUT_CLS} cursor-pointer ${invalid ? ERROR_INPUT_CLS : ""}`}
      />
      {isOpen && (
        <div
          id={`${id}-listbox`}
          role="listbox"
          className="absolute z-20 mt-1 max-h-64 w-full overflow-y-auto rounded-lg border
            border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-lg"
        >
          {isLoading && <div className="px-3 py-2 text-sm text-[var(--ophalo-muted)]">Loading…</div>}
          {!isLoading && results.length === 0 && (
            <div className="px-3 py-2 text-sm text-[var(--ophalo-muted)]">No active assemblies found.</div>
          )}
          {!isLoading &&
            results.map((row) => (
              <div
                key={row.id}
                role="option"
                aria-selected={row.id === selectedAssemblyId}
                onMouseDown={(e) => {
                  e.preventDefault();
                  onSelect(row);
                  setIsOpen(false);
                }}
                className={`px-3 py-2 cursor-pointer text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${
                  row.id === selectedAssemblyId ? "bg-[var(--ophalo-canvas)]" : ""
                }`}
              >
                <div className="text-sm font-medium">{row.name}</div>
              </div>
            ))}
          {!isLoading && (cursorIndex > 0 || data?.nextCursor) && (
            <div className="flex items-center justify-between border-t border-[var(--ophalo-border)] px-3 py-2">
              <button
                type="button"
                disabled={cursorIndex === 0}
                onMouseDown={(e) => {
                  e.preventDefault();
                  goPrev();
                }}
                className="text-xs text-[var(--ophalo-muted)] disabled:opacity-40"
              >
                Previous
              </button>
              <button
                type="button"
                disabled={!data?.nextCursor}
                onMouseDown={(e) => {
                  e.preventDefault();
                  goNext();
                }}
                className="text-xs text-[var(--ophalo-muted)] disabled:opacity-40"
              >
                Next
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
