import { useEffect, useMemo, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { ChevronDown } from "lucide-react";
import { api, ApiError, type CatalogCategoryResponse } from "../../lib/apiClient";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 pr-8 ${FOCUS_RING}`;

const ERROR_INPUT_CLS = "border-[var(--ophalo-danger)]";

const DEFAULT_NONE_LABEL = "No category";
const DEFAULT_CREATABLE_PLACEHOLDER = "Search or create category…";
const CREATE_HINT = "Type a new name to create category";

function normalize(name: string): string {
  return name.trim().toLowerCase();
}

type ComboOption =
  | { kind: "none" }
  | { kind: "category"; category: CatalogCategoryResponse }
  | { kind: "create"; name: string };

interface CategoryComboboxProps {
  id: string;
  categories: CatalogCategoryResponse[];
  currentCategoryId: string | null;
  onSelect: (categoryId: string | null) => void;
  /** Reuses the existing create-and-race-recover contract from the original select+reveal flow
   * (build-log/112/114): an exact normalized-name match selects the existing category instead of
   * creating a duplicate, and a `CatalogCategory.NameAlreadyExists` conflict re-fetches and selects
   * the concurrently created category rather than surfacing a failure. */
  creatable?: boolean;
  disabled?: boolean;
  invalid?: boolean;
  placeholder?: string;
  /** Label for the "no selection" option — defaults to "No category" (the create/edit forms'
   * clear choice); a read-only filter should pass something like "All categories" instead, since
   * there the null selection means "don't filter," not "assign no category." */
  noneLabel?: string;
  /** Fires after a successful create/conflict-resolve so the consumer can invalidate its own
   * `["catalogCategories"]` query. */
  onCategoriesChanged?: () => void;
  /** True from the moment a create attempt starts until it resolves (success, or a conflict/error
   * that still awaits a retry or an explicit different selection) — callers must block their save
   * mutation while this is true so it can never fire against an uncommitted category intent. */
  onPendingChange?: (pending: boolean) => void;
}

function CategoryOption({
  option,
  optionId,
  index,
  highlighted,
  currentCategoryId,
  noneLabel,
  onSelect,
  onHighlight,
  className = "",
}: {
  option: ComboOption;
  optionId: string;
  index: number;
  highlighted: boolean;
  currentCategoryId: string | null;
  noneLabel: string;
  onSelect: (option: ComboOption) => void;
  onHighlight: (index: number) => void;
  className?: string;
}) {
  const isCreate = option.kind === "create";
  return (
    <div
      id={optionId}
      role="option"
      aria-selected={
        option.kind === "none"
          ? currentCategoryId === null
          : option.kind === "category" && option.category.id === currentCategoryId
      }
      onMouseDown={(e) => {
        e.preventDefault();
        onSelect(option);
      }}
      onMouseEnter={() => onHighlight(index)}
      className={`px-3 py-2 cursor-pointer ${
        isCreate
          ? `font-semibold text-[var(--keep-accent)] ${highlighted ? "bg-[var(--keep-accent-bg)]" : ""}`
          : `text-[var(--ophalo-ink)] ${highlighted ? "bg-[var(--ophalo-canvas)]" : ""}`
      } ${className}`}
    >
      {option.kind === "none" ? noneLabel : isCreate ? `+ Create "${option.name}"` : option.category.name}
    </div>
  );
}

/**
 * Shared accessible category combobox (Session 2e.7b, build-log/114 decision 8): search-to-filter,
 * explicit "No category", and — when `creatable` — create-on-no-normalized-match with the same
 * duplicate-safe race recovery previously duplicated per consumer. A typed, uncommitted search term
 * never changes `currentCategoryId` by itself; only Enter/click on an option (existing, "No
 * category", or "Create …") commits a selection, so a stray keystroke can never silently swap or
 * clear the caller's intended category.
 */
export function CategoryCombobox({
  id,
  categories,
  currentCategoryId,
  onSelect,
  creatable = false,
  disabled = false,
  invalid = false,
  placeholder,
  noneLabel = DEFAULT_NONE_LABEL,
  onCategoriesChanged,
  onPendingChange,
}: CategoryComboboxProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [draftText, setDraftText] = useState("");
  // The field shows the current selection's full name on open (so it's visible before typing),
  // but the option list should only actually narrow once the user starts typing — otherwise a
  // pre-filled "Refrigerant" would hide "No category" and every other option the moment it opens.
  const [isFiltering, setIsFiltering] = useState(false);
  const [highlightedIndex, setHighlightedIndex] = useState(0);
  const [localResolvedCategory, setLocalResolvedCategory] = useState<CatalogCategoryResponse | null>(null);
  const [createError, setCreateError] = useState<string | null>(null);
  const [conflictName, setConflictName] = useState<string | null>(null);
  const [pendingCreateName, setPendingCreateName] = useState<string | null>(null);

  const inputRef = useRef<HTMLInputElement>(null);
  const rootRef = useRef<HTMLDivElement>(null);

  const selectedCategory = useMemo(() => {
    if (currentCategoryId === null) return null;
    const fromProp = categories.find((c) => c.id === currentCategoryId);
    if (fromProp) return fromProp;
    return localResolvedCategory && localResolvedCategory.id === currentCategoryId ? localResolvedCategory : null;
  }, [categories, currentCategoryId, localResolvedCategory]);

  // Sync the displayed text to the committed selection whenever it changes externally (initial
  // load, a parent form reset) — but never while the user has the listbox open and is typing.
  // Depending on `selectedCategory?.name` (not just `currentCategoryId`) matters: `categories` can
  // still be loading when `currentCategoryId` is first set (e.g. opening Edit before the
  // categories query resolves), which would otherwise leave the field showing blank forever even
  // though a category is genuinely selected — resyncing once the name actually resolves fixes that
  // without re-running on every unrelated parent re-render (categories array identity is not a
  // dependency here on purpose; it's rebuilt on most renders).
  useEffect(() => {
    if (!isOpen) {
      setDraftText(selectedCategory?.name ?? "");
      setIsFiltering(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentCategoryId, selectedCategory?.name]);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) {
        setIsOpen(false);
        setDraftText(selectedCategory?.name ?? "");
        setIsFiltering(false);
      }
    }
    if (isOpen) document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen]);

  const filterText = isFiltering ? draftText : "";

  const filteredCategories = useMemo(() => {
    const term = normalize(filterText);
    if (term === "") return categories;
    return categories.filter((c) => normalize(c.name).includes(term));
  }, [categories, filterText]);

  const exactMatch = useMemo(
    () => categories.find((c) => normalize(c.name) === normalize(filterText)) ?? null,
    [categories, filterText],
  );

  const trimmedDraft = filterText.trim();

  const options: ComboOption[] = useMemo(() => {
    const opts: ComboOption[] = [];
    if (trimmedDraft === "") opts.push({ kind: "none" });
    for (const category of filteredCategories) opts.push({ kind: "category", category });
    if (creatable && trimmedDraft !== "" && !exactMatch) opts.push({ kind: "create", name: trimmedDraft });
    return opts;
  }, [trimmedDraft, filteredCategories, creatable, exactMatch]);

  // build-log/114 (2e.7b UX correction): the create affordance — when offered — is the default
  // highlighted option, not whatever partial category match happens to sort first, since creating
  // is almost always the intended action once no exact match exists.
  useEffect(() => {
    const createIndex = options.findIndex((o) => o.kind === "create");
    setHighlightedIndex(createIndex >= 0 ? createIndex : 0);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterText, isOpen]);

  const createMutation = useMutation({
    mutationFn: (name: string) => api.createCatalogCategory({ name, displayOrder: categories.length }),
    onSuccess: (category) => {
      setLocalResolvedCategory(category);
      setDraftText(category.name);
      setIsOpen(false);
      setIsFiltering(false);
      setCreateError(null);
      setConflictName(null);
      setPendingCreateName(null);
      onCategoriesChanged?.();
      onSelect(category.id);
      onPendingChange?.(false);
    },
    onError: (err: unknown, name) => {
      if (err instanceof ApiError && err.code === "CatalogCategory.NameAlreadyExists") {
        void resolveConflict(name);
        return;
      }
      setCreateError("Couldn't add that category. Try again.");
    },
  });

  async function resolveConflict(name: string) {
    try {
      const fresh = await api.getCatalogCategories();
      onCategoriesChanged?.();
      const match = fresh.categories.find((c) => normalize(c.name) === normalize(name));
      if (match) {
        setLocalResolvedCategory(match);
        setDraftText(match.name);
        setIsOpen(false);
        setIsFiltering(false);
        setCreateError(null);
        setConflictName(null);
        setPendingCreateName(null);
        onSelect(match.id);
        onPendingChange?.(false);
      } else {
        setCreateError("Another category with this name exists, but we couldn't find it. Try again.");
        setConflictName(name);
      }
    } catch {
      setCreateError("Couldn't confirm the category. Try again.");
      setConflictName(name);
    }
  }

  function startCreate(name: string) {
    setCreateError(null);
    setConflictName(null);
    setPendingCreateName(name);
    onPendingChange?.(true);
    createMutation.mutate(name);
  }

  function handleRetry() {
    if (!pendingCreateName) return;
    if (conflictName) {
      void resolveConflict(pendingCreateName);
    } else {
      onPendingChange?.(true);
      createMutation.mutate(pendingCreateName);
    }
  }

  function commit(option: Extract<ComboOption, { kind: "none" | "category" }>) {
    setIsOpen(false);
    setIsFiltering(false);
    // Explicitly picking a different category abandons any stuck create/conflict retry rather than
    // leaving it silently pending.
    setCreateError(null);
    setConflictName(null);
    setPendingCreateName(null);
    onPendingChange?.(false);
    if (option.kind === "none") {
      setDraftText("");
      onSelect(null);
    } else {
      setDraftText(option.category.name);
      onSelect(option.category.id);
    }
  }

  function selectOption(option: ComboOption) {
    if (option.kind === "create") {
      startCreate(option.name);
      return;
    }
    commit(option);
  }

  const locked = disabled || createMutation.isPending;

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (locked) return;
    if (e.key === "ArrowDown") {
      e.preventDefault();
      if (!isOpen) {
        setIsOpen(true);
        return;
      }
      setHighlightedIndex((i) => Math.min(i + 1, options.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      if (!isOpen) {
        setIsOpen(true);
        return;
      }
      setHighlightedIndex((i) => Math.max(i - 1, 0));
    } else if (e.key === "Enter") {
      e.preventDefault();
      if (!isOpen) {
        setIsOpen(true);
        return;
      }
      const option = options[highlightedIndex];
      if (option) selectOption(option);
    } else if (e.key === "Escape") {
      if (isOpen) {
        e.preventDefault();
        setIsOpen(false);
        setIsFiltering(false);
        setDraftText(selectedCategory?.name ?? "");
      }
    }
  }

  function handleBlur() {
    // build-log/114 (2e.7b UX correction): Tab must never create or change a category — it's
    // normal focus traversal. Closing here and reverting the draft (same as Escape) means a fast
    // Tab through the form can't leave a stray open popup or an accidental typo behind.
    setIsOpen(false);
    setIsFiltering(false);
    setDraftText(selectedCategory?.name ?? "");
  }

  const effectivePlaceholder = placeholder ?? (creatable ? DEFAULT_CREATABLE_PLACEHOLDER : noneLabel);
  const showCreateHint = creatable && isOpen && !locked && trimmedDraft === "";
  const hintId = `${id}-hint`;
  const listboxId = `${id}-listbox`;

  // Pinned top ("No category") / scrollable middle (ordinary categories) / pinned bottom
  // (create action) split — see the render below for why. `options`' order and each entry's index
  // remain the single source of truth for keyboard navigation and `aria-activedescendant`.
  const noneIndex = options.findIndex((o) => o.kind === "none");
  const pinnedNoneOption = noneIndex >= 0 ? options[noneIndex] : null;
  const createIndex = options.findIndex((o) => o.kind === "create");
  const pinnedCreateOption = createIndex >= 0 ? options[createIndex] : null;
  const categoryEntries = options
    .map((option, index) => ({ option, index }))
    .filter((entry): entry is { option: Extract<ComboOption, { kind: "category" }>; index: number } =>
      entry.option.kind === "category",
    );

  function optionId(option: ComboOption): string {
    if (option.kind === "none") return `${id}-option-none`;
    if (option.kind === "create") return `${id}-option-create`;
    return `${id}-option-${option.category.id}`;
  }

  return (
    <div className="relative" ref={rootRef}>
      <div className="relative">
        <input
          ref={inputRef}
          id={id}
          type="text"
          role="combobox"
          aria-expanded={isOpen}
          aria-controls={listboxId}
          aria-autocomplete="list"
          aria-activedescendant={isOpen && options[highlightedIndex] ? optionId(options[highlightedIndex]) : undefined}
          aria-describedby={creatable ? hintId : undefined}
          autoComplete="off"
          value={draftText}
          placeholder={effectivePlaceholder}
          disabled={locked}
          onFocus={() => setIsOpen(true)}
          onClick={() => setIsOpen(true)}
          onChange={(e) => {
            setDraftText(e.target.value);
            setIsFiltering(true);
            setIsOpen(true);
          }}
          onKeyDown={handleKeyDown}
          onBlur={handleBlur}
          className={`${INPUT_CLS} ${invalid ? ERROR_INPUT_CLS : ""}`}
        />
        <ChevronDown className="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 h-4 w-4 text-[var(--ophalo-muted)]" />
      </div>

      {isOpen && !locked && (
        <div className="absolute z-10 mt-1 w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-lg overflow-hidden text-sm">
          {/* build-log/114 (2e.7b scale/ordering correction): with 15-50 account categories a
              single scrolling list would bury "No category" and the create action. Only the
              ordinary category rows scroll (capped ~240px); "No category" stays pinned above that
              region and the create action/discovery hint stays pinned below it — both always
              reachable without scrolling. `role="listbox"` spans the whole group; the nested
              scroll wrapper is a plain container, not a semantic break in the option list. */}
          <div id={listboxId} role="listbox" aria-label="Category options">
            {pinnedNoneOption && (
              <CategoryOption
                option={pinnedNoneOption}
                optionId={optionId(pinnedNoneOption)}
                index={noneIndex}
                highlighted={highlightedIndex === noneIndex}
                currentCategoryId={currentCategoryId}
                noneLabel={noneLabel}
                onSelect={selectOption}
                onHighlight={setHighlightedIndex}
                className="border-b border-[var(--ophalo-border)]"
              />
            )}
            <div className="max-h-60 overflow-y-auto py-1">
              {categoryEntries.length === 0 && (
                <div className="px-3 py-2 text-[var(--ophalo-muted)]">No matching categories</div>
              )}
              {categoryEntries.map(({ option, index }) => (
                <CategoryOption
                  key={optionId(option)}
                  option={option}
                  optionId={optionId(option)}
                  index={index}
                  highlighted={highlightedIndex === index}
                  currentCategoryId={currentCategoryId}
                  noneLabel={noneLabel}
                  onSelect={selectOption}
                  onHighlight={setHighlightedIndex}
                />
              ))}
            </div>
            {pinnedCreateOption && (
              <CategoryOption
                option={pinnedCreateOption}
                optionId={optionId(pinnedCreateOption)}
                index={createIndex}
                highlighted={highlightedIndex === createIndex}
                currentCategoryId={currentCategoryId}
                noneLabel={noneLabel}
                onSelect={selectOption}
                onHighlight={setHighlightedIndex}
                className="border-t border-[var(--ophalo-border)]"
              />
            )}
          </div>
          {showCreateHint && (
            <div
              id={hintId}
              role="presentation"
              className="px-3 py-1.5 text-xs text-[var(--ophalo-muted)] border-t border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)]"
            >
              💡 {CREATE_HINT}
            </div>
          )}
        </div>
      )}
      {creatable && !showCreateHint && (
        <span id={hintId} className="sr-only">
          {CREATE_HINT}
        </span>
      )}

      {createError && (
        <div className="flex items-center gap-2 mt-1.5">
          <span className="text-sm text-[var(--ophalo-danger)]">{createError}</span>
          <button type="button" onClick={handleRetry} className={`text-sm font-medium text-[var(--keep-accent)] hover:underline ${FOCUS_RING}`}>
            Try again
          </button>
        </div>
      )}
      {createMutation.isPending && <span className="text-sm text-[var(--ophalo-muted)] mt-1.5 block">Adding…</span>}
    </div>
  );
}
