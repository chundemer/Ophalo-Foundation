import { useState } from "react";
import { api, ApiError, type KeepRequestDetailResult } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import { INPUT_CLS } from "./helpers";
import {
  type HighlightLevel,
  highlightBorderCls,
  highlightBgCls,
  highlightBoxShadow,
  RecommendedActionBadge,
} from "./highlights";
import { BusinessUpdateSection } from "./BusinessSection";

interface UnifiedComposerProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  customerUpdateDraft: string;
  onCustomerUpdateDraftChange: (v: string) => void;
  customerUpdateDraftStatus: string;
  onCustomerUpdateDraftStatusChange: (v: string) => void;
  highlight?: HighlightLevel;
}

type ActiveTab = "customerUpdate" | "internalNote";

const NOTE_CONFLICT_MESSAGE =
  "This request was updated. Refresh to see the latest state. Your note is saved here.";

export function UnifiedComposer({
  requestId,
  detail,
  onDetailUpdated,
  customerUpdateDraft,
  onCustomerUpdateDraftChange,
  customerUpdateDraftStatus,
  onCustomerUpdateDraftStatusChange,
  highlight,
}: UnifiedComposerProps) {
  const { canSendBusinessUpdate, canAddInternalNote } = detail.availableActions;
  const defaultTab: ActiveTab = canSendBusinessUpdate ? "customerUpdate" : "internalNote";
  const [activeTab, setActiveTab] = useState<ActiveTab>(defaultTab);

  const [note, setNote] = useState("");
  const [noteSubmitting, setNoteSubmitting] = useState(false);
  const [noteConflictDisabled, setNoteConflictDisabled] = useState(false);
  const [noteError, setNoteError] = useState<string | null>(null);

  if (!canSendBusinessUpdate && !canAddInternalNote) return null;

  async function submitNote() {
    if (!note.trim() || noteSubmitting || noteConflictDisabled) return;
    setNoteSubmitting(true);
    setNoteError(null);
    try {
      const updated = await api.addInternalNote(
        requestId,
        { note: note.trim() },
        detail.version,
      );
      onDetailUpdated(updated);
      setNote("");
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setNoteConflictDisabled(true);
        setNoteError(NOTE_CONFLICT_MESSAGE);
      } else {
        setNoteError("Could not save note. Try again.");
      }
    } finally {
      setNoteSubmitting(false);
    }
  }

  function handleNoteKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if ((e.metaKey || e.ctrlKey) && e.key === "Enter") {
      e.preventDefault();
      void submitNote();
    }
  }

  return (
    <div
      className={`rounded-xl border px-5 py-5 scroll-mt-4 transition-[border-color,background-color,box-shadow] ${highlightBorderCls(highlight)} ${highlightBgCls()}`}
      style={{ boxShadow: highlightBoxShadow(highlight) }}
    >
      {/* Tab bar */}
      <div className="mb-4 flex items-center justify-between gap-2">
        <div
          role="tablist"
          aria-label="Composer"
          className="flex rounded-lg overflow-hidden border border-[var(--ophalo-border)]"
        >
          {canSendBusinessUpdate && (
            <button
              id="tab-customer-update"
              role="tab"
              aria-selected={activeTab === "customerUpdate"}
              aria-controls="panel-customer-update"
              onClick={() => setActiveTab("customerUpdate")}
              className={`px-4 py-1.5 text-sm font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--ophalo-navy)] ${
                activeTab === "customerUpdate"
                  ? "bg-[var(--ophalo-navy)] text-white"
                  : "bg-[var(--ophalo-card)] text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
              }`}
            >
              Customer update
            </button>
          )}
          {canAddInternalNote && (
            <button
              id="tab-internal-note"
              role="tab"
              aria-selected={activeTab === "internalNote"}
              aria-controls="panel-internal-note"
              onClick={() => setActiveTab("internalNote")}
              className={`px-4 py-1.5 text-sm font-semibold transition-colors border-l border-[var(--ophalo-border)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--ophalo-navy)] ${
                activeTab === "internalNote"
                  ? "bg-[var(--ophalo-navy)] text-white"
                  : "bg-[var(--ophalo-card)] text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
              }`}
            >
              Internal note
            </button>
          )}
        </div>
        <RecommendedActionBadge level={highlight} />
      </div>

      {/* Customer Update panel — always mounted; hidden when inactive */}
      {canSendBusinessUpdate && (
        <div
          id="panel-customer-update"
          role="tabpanel"
          aria-labelledby="tab-customer-update"
          hidden={activeTab !== "customerUpdate"}
        >
          <BusinessUpdateSection
            requestId={requestId}
            detail={detail}
            onDetailUpdated={onDetailUpdated}
            draft={customerUpdateDraft}
            onDraftChange={onCustomerUpdateDraftChange}
            draftStatus={customerUpdateDraftStatus}
            onDraftStatusChange={onCustomerUpdateDraftStatusChange}
            composerMode
          />
        </div>
      )}

      {/* Internal Note panel — always mounted; hidden when inactive */}
      {canAddInternalNote && (
        <div
          id="panel-internal-note"
          role="tabpanel"
          aria-labelledby="tab-internal-note"
          hidden={activeTab !== "internalNote"}
        >
          <p className="text-xs font-medium text-[var(--ophalo-danger)] mb-3">
            Internal only — never visible to customer
          </p>
          {noteError && (
            <div
              aria-live="polite"
              className={`mb-3 rounded-lg p-3 text-xs ${
                noteConflictDisabled
                  ? "bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)]"
                  : "bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]"
              }`}
            >
              {noteError}
            </div>
          )}
          <form onSubmit={(e) => { e.preventDefault(); void submitNote(); }} className="space-y-2">
            <div>
              <label htmlFor="internal-note-textarea" className="sr-only">
                Internal note — not visible to customer
              </label>
              <textarea
                id="internal-note-textarea"
                value={note}
                onChange={(e) => setNote(e.target.value)}
                onKeyDown={handleNoteKeyDown}
                maxLength={detail.validation.internalNoteMaxLength}
                disabled={noteConflictDisabled}
                placeholder="Add a note for your team…"
                rows={4}
                className={`${INPUT_CLS} resize-none`}
              />
            </div>
            <p className="text-xs text-[var(--ophalo-muted)]">Cmd/Ctrl + Enter to save.</p>
            <KeepButton
              type="submit"
              variant="secondary"
              disabled={noteSubmitting || noteConflictDisabled || !note.trim()}
              className="w-full"
            >
              {noteSubmitting ? "Saving…" : "Save internal note"}
            </KeepButton>
          </form>
        </div>
      )}
    </div>
  );
}
