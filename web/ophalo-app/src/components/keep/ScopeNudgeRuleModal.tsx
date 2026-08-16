import { useState, type FormEvent } from "react";
import { useMutation } from "@tanstack/react-query";
import { KeepModal } from "./KeepModal";
import { CatalogItemPicker } from "./CatalogItemPicker";
import { AssemblyPicker } from "./AssemblyPicker";
import {
  api,
  ApiError,
  type CatalogItemListRowResponse,
  type OfferingAssemblyListRowResponse,
  type ScopeNudgeRuleConfigRowResponse,
} from "../../lib/apiClient";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const MAX_SUGGESTIONS = 3;

type TargetKind = "catalogItem" | "assembly";

interface DraftSuggestion {
  key: string;
  kind: TargetKind;
  id: string | null;
  displayName: string | null;
}

interface ScopeNudgeRuleModalProps {
  mode: "create" | "edit";
  /** Required for mode "edit"; ignored for "create". */
  existingRule?: ScopeNudgeRuleConfigRowResponse;
  onClose: () => void;
  onSaved: () => void;
}

const ERROR_MESSAGES: Record<string, string> = {
  "ScopeNudgeRule.TriggerRequired": "A trigger is required.",
  "ScopeNudgeRule.TriggerMustHaveExactlyOneTarget": "Choose exactly one trigger.",
  "ScopeNudgeRule.SuggestionsRequired": "At least one suggestion is required.",
  "ScopeNudgeRule.TooManySuggestions": "A rule allows at most three suggestions.",
  "ScopeNudgeRule.DuplicateSuggestion": "The same item or assembly can't be suggested twice.",
  "ScopeNudgeRule.TargetNotFound": "One of the selected items or assemblies could not be found.",
  "ScopeNudgeRule.DuplicateTrigger": "A rule for this trigger already exists.",
  "ScopeNudgeRule.NotFound": "This rule no longer exists.",
};

function newSuggestion(): DraftSuggestion {
  return { key: `${Date.now()}-${Math.random()}`, kind: "catalogItem", id: null, displayName: null };
}

/**
 * Owner/Admin create/edit modal for Paired Nudges rules (build-log/124, Session 4). Create picks
 * exactly one trigger (catalog item or assembly) plus 1-3 ordered suggestions; edit only replaces
 * the suggestion list — the trigger is shown but not editable, matching the PUT contract accepting
 * no trigger fields.
 */
export function ScopeNudgeRuleModal({ mode, existingRule, onClose, onSaved }: ScopeNudgeRuleModalProps) {
  const [triggerKind, setTriggerKind] = useState<TargetKind>(
    existingRule?.triggerOfferingAssemblyId ? "assembly" : "catalogItem",
  );
  const [triggerId, setTriggerId] = useState<string | null>(
    existingRule ? (existingRule.triggerCatalogItemId ?? existingRule.triggerOfferingAssemblyId) : null,
  );
  const [triggerDisplayName, setTriggerDisplayName] = useState<string | null>(
    existingRule?.triggerDisplayName ?? null,
  );
  const [suggestions, setSuggestions] = useState<DraftSuggestion[]>(
    existingRule
      ? existingRule.suggestions.map((s) => ({
          key: s.id,
          kind: s.suggestedOfferingAssemblyId ? "assembly" : "catalogItem",
          id: s.suggestedCatalogItemId ?? s.suggestedOfferingAssemblyId,
          displayName: s.targetDisplayName,
        }))
      : [newSuggestion()],
  );
  const [triggerError, setTriggerError] = useState<string | null>(null);
  const [suggestionsError, setSuggestionsError] = useState<string | null>(null);
  const [generalError, setGeneralError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => {
      const suggestionBodies = suggestions
        .filter((s) => s.id)
        .map((s) => ({
          catalogItemId: s.kind === "catalogItem" ? s.id : null,
          offeringAssemblyId: s.kind === "assembly" ? s.id : null,
        }));
      if (mode === "create") {
        return api.createScopeNudgeRule({
          triggerCatalogItemId: triggerKind === "catalogItem" ? triggerId : null,
          triggerOfferingAssemblyId: triggerKind === "assembly" ? triggerId : null,
          suggestions: suggestionBodies,
        });
      }
      return api.updateScopeNudgeRule(existingRule!.id, { suggestions: suggestionBodies });
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
    onError: (err) => {
      setTriggerError(null);
      setSuggestionsError(null);
      setGeneralError(null);
      if (err instanceof ApiError && err.code) {
        const message = ERROR_MESSAGES[err.code] ?? "Something went wrong. Try again.";
        if (err.code.startsWith("ScopeNudgeRule.Trigger") || err.code === "ScopeNudgeRule.DuplicateTrigger") {
          setTriggerError(message);
          return;
        }
        if (err.code.includes("Suggestion")) {
          setSuggestionsError(message);
          return;
        }
        setGeneralError(message);
        return;
      }
      setGeneralError("Something went wrong. Try again.");
    },
  });

  function validateBeforeSubmit(): boolean {
    let ok = true;
    if (mode === "create" && !triggerId) {
      setTriggerError("A trigger is required.");
      ok = false;
    } else {
      setTriggerError(null);
    }
    const filled = suggestions.filter((s) => s.id);
    if (filled.length < 1) {
      setSuggestionsError("At least one suggestion is required.");
      ok = false;
    } else {
      setSuggestionsError(null);
    }
    return ok;
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (mutation.isPending) return;
    if (!validateBeforeSubmit()) return;
    mutation.mutate();
  }

  function addSuggestionRow() {
    setSuggestions((prev) => (prev.length >= MAX_SUGGESTIONS ? prev : [...prev, newSuggestion()]));
  }

  function updateSuggestionRow(key: string, patch: Partial<DraftSuggestion>) {
    setSuggestions((prev) => prev.map((s) => (s.key === key ? { ...s, ...patch } : s)));
  }

  function removeSuggestionRow(key: string) {
    setSuggestions((prev) => prev.filter((s) => s.key !== key));
  }

  function moveSuggestionRow(key: string, direction: -1 | 1) {
    setSuggestions((prev) => {
      const index = prev.findIndex((s) => s.key === key);
      const target = index + direction;
      if (index < 0 || target < 0 || target >= prev.length) return prev;
      const next = [...prev];
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
  }

  const title = mode === "create" ? "Add nudge rule" : "Edit nudge rule";

  return (
    <KeepModal
      onClose={onClose}
      label={title}
      backdropClassName="bg-black/30"
      panelClassName="fixed z-50 top-0 right-0 h-[100dvh] max-h-[100dvh] w-full sm:w-[520px] bg-[var(--ophalo-card)] shadow-xl flex flex-col"
    >
      <form onSubmit={handleSubmit} className="h-full min-h-0 flex flex-col">
        <div className="shrink-0 px-4 sm:px-6 py-4 border-b border-[var(--ophalo-border)] flex items-center justify-between">
          <h2 className="font-serif text-lg font-semibold text-[var(--ophalo-ink)]">{title}</h2>
          <button
            type="button"
            onClick={onClose}
            className={`rounded-lg px-2 py-1 text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
            aria-label="Close"
          >
            ×
          </button>
        </div>

        <div className="flex-1 min-h-0 overflow-y-auto px-4 sm:px-6 py-4 space-y-4">
          {generalError && (
            <div className="rounded-lg bg-[var(--ophalo-danger-bg)] px-3 py-2 text-sm text-[var(--ophalo-danger)]">
              {generalError}
            </div>
          )}

          <div>
            <span className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">Trigger</span>
            {mode === "edit" ? (
              <div className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-sm text-[var(--ophalo-ink)]">
                {triggerDisplayName}
                <span className="block text-xs text-[var(--ophalo-muted)]">Trigger can't be changed after creation.</span>
              </div>
            ) : (
              <div className="space-y-2">
                <div className="flex gap-4">
                  {(["catalogItem", "assembly"] as const).map((kind) => (
                    <label key={kind} className="flex items-center gap-2 text-sm text-[var(--ophalo-ink)]">
                      <input
                        type="radio"
                        name="triggerKind"
                        checked={triggerKind === kind}
                        onChange={() => {
                          setTriggerKind(kind);
                          setTriggerId(null);
                          setTriggerDisplayName(null);
                        }}
                      />
                      {kind === "catalogItem" ? "Catalog item" : "Assembly"}
                    </label>
                  ))}
                </div>
                {triggerKind === "catalogItem" ? (
                  <CatalogItemPicker
                    id="nudge-rule-trigger"
                    selectedItemId={triggerId}
                    selectedItemDisplayName={triggerDisplayName}
                    onSelect={(row: CatalogItemListRowResponse) => {
                      setTriggerId(row.item.id);
                      setTriggerDisplayName(row.item.displayName);
                    }}
                    invalid={!!triggerError}
                  />
                ) : (
                  <AssemblyPicker
                    id="nudge-rule-trigger"
                    selectedAssemblyId={triggerId}
                    selectedAssemblyDisplayName={triggerDisplayName}
                    onSelect={(row: OfferingAssemblyListRowResponse) => {
                      setTriggerId(row.id);
                      setTriggerDisplayName(row.name);
                    }}
                    invalid={!!triggerError}
                  />
                )}
              </div>
            )}
            {triggerError && <p className="mt-1 text-sm text-[var(--ophalo-danger)]">{triggerError}</p>}
          </div>

          <div>
            <div className="flex items-center justify-between mb-1">
              <span className="block text-sm font-medium text-[var(--ophalo-ink)]">Suggestions (1–3)</span>
              <button
                type="button"
                onClick={addSuggestionRow}
                disabled={suggestions.length >= MAX_SUGGESTIONS}
                className={`text-sm font-medium text-[var(--keep-accent)] hover:underline disabled:opacity-40 ${FOCUS_RING}`}
              >
                + Add suggestion
              </button>
            </div>
            <div className="space-y-2">
              {suggestions.map((s, index) => (
                <div key={s.key} className="rounded-lg border border-[var(--ophalo-border)] p-2 space-y-2">
                  <div className="flex items-center justify-between">
                    <div className="flex gap-3">
                      {(["catalogItem", "assembly"] as const).map((kind) => (
                        <label key={kind} className="flex items-center gap-1 text-xs text-[var(--ophalo-muted)]">
                          <input
                            type="radio"
                            name={`suggestionKind-${s.key}`}
                            checked={s.kind === kind}
                            onChange={() => updateSuggestionRow(s.key, { kind, id: null, displayName: null })}
                          />
                          {kind === "catalogItem" ? "Catalog item" : "Assembly"}
                        </label>
                      ))}
                    </div>
                    <div className="flex items-center gap-1">
                      <button
                        type="button"
                        onClick={() => moveSuggestionRow(s.key, -1)}
                        disabled={index === 0}
                        aria-label="Move up"
                        className="text-xs text-[var(--ophalo-muted)] disabled:opacity-30"
                      >
                        ↑
                      </button>
                      <button
                        type="button"
                        onClick={() => moveSuggestionRow(s.key, 1)}
                        disabled={index === suggestions.length - 1}
                        aria-label="Move down"
                        className="text-xs text-[var(--ophalo-muted)] disabled:opacity-30"
                      >
                        ↓
                      </button>
                      <button
                        type="button"
                        onClick={() => removeSuggestionRow(s.key)}
                        disabled={suggestions.length <= 1}
                        aria-label="Remove suggestion"
                        className="text-xs text-[var(--ophalo-danger)] disabled:opacity-30"
                      >
                        Remove
                      </button>
                    </div>
                  </div>
                  {s.kind === "catalogItem" ? (
                    <CatalogItemPicker
                      id={`nudge-suggestion-${s.key}`}
                      selectedItemId={s.id}
                      selectedItemDisplayName={s.displayName}
                      onSelect={(row: CatalogItemListRowResponse) =>
                        updateSuggestionRow(s.key, { id: row.item.id, displayName: row.item.displayName })
                      }
                    />
                  ) : (
                    <AssemblyPicker
                      id={`nudge-suggestion-${s.key}`}
                      selectedAssemblyId={s.id}
                      selectedAssemblyDisplayName={s.displayName}
                      onSelect={(row: OfferingAssemblyListRowResponse) =>
                        updateSuggestionRow(s.key, { id: row.id, displayName: row.name })
                      }
                    />
                  )}
                </div>
              ))}
            </div>
            {suggestionsError && <p className="mt-1 text-sm text-[var(--ophalo-danger)]">{suggestionsError}</p>}
          </div>
        </div>

        <div className="shrink-0 px-4 sm:px-6 py-4 border-t border-[var(--ophalo-border)] flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className={`rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={mutation.isPending}
            className={`rounded-lg bg-[var(--keep-accent)] px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-60 ${FOCUS_RING}`}
          >
            {mutation.isPending ? "Saving…" : "Save"}
          </button>
        </div>
      </form>
    </KeepModal>
  );
}
