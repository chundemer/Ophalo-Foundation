import { useState, useEffect } from "react";
import { X } from "lucide-react";
import { api, ApiError } from "../lib/apiClient";
import type { KeepRequestSummary, KeepQuickAction, LogExternalContactBody } from "../lib/apiClient";
import { KeepBadge } from "./keep/KeepBadge";
import { KeepButton } from "./keep/KeepButton";
import { ExternalContactForm } from "./ExternalContactForm";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const ATTENTION_REASON_LABELS: Record<string, string> = {
  complaint: "Complaint",
  cancellation_requested: "Cancel requested",
  unresolved_feedback: "Feedback pending",
  first_response_due: "First response due",
  no_first_response: "First response pending",
  schedule_change_request: "Schedule change",
  timing_change_requested: "Timing change",
  call_requested: "Call requested",
  customer_message: "Customer replied",
  update_request: "Update requested",
  change_or_cancel_request: "Change/cancel",
};

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_BASE =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

// ---------------------------------------------------------------------------
// Modal shell
// ---------------------------------------------------------------------------

interface RequestRowActionModalProps {
  row: KeepRequestSummary;
  action: KeepQuickAction;
  onClose: () => void;
  onSuccess: () => void;
}

export function RequestRowActionModal({ row, action, onClose, onSuccess }: RequestRowActionModalProps) {
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  const title =
    action.code === "post_customer_update" ? "Send customer update"
    : action.code === "contact_customer" ? "Log external contact"
    : action.code === "acknowledge_attention" ? "Acknowledge attention"
    : action.code === "add_internal_note" ? "Add internal note"
    : action.label;

  return (
    <div className="fixed inset-0 z-50" role="dialog" aria-modal="true" aria-label={title}>
      {/* Backdrop */}
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />

      {/* Sheet on mobile, centered modal on desktop */}
      <div className="absolute inset-x-0 bottom-0 sm:inset-0 sm:flex sm:items-center sm:justify-center sm:p-4">
        <div
          className="relative bg-[var(--ophalo-card)] rounded-t-2xl sm:rounded-2xl shadow-xl w-full sm:max-w-lg max-h-[90vh] overflow-y-auto"
          onClick={(e) => e.stopPropagation()}
        >
          {/* Handle bar on mobile */}
          <div className="sm:hidden flex justify-center pt-3 pb-1">
            <div className="h-1 w-10 rounded-full bg-[var(--ophalo-border)]" />
          </div>

          {/* Header */}
          <div className="flex items-start justify-between px-5 py-4 border-b border-[var(--ophalo-border)]">
            <div className="min-w-0">
              <h2 className="text-base font-semibold text-[var(--ophalo-ink)]">{title}</h2>
              <p className="text-sm text-[var(--ophalo-muted)] mt-0.5 truncate">
                {row.customerName} · {row.referenceCode}
              </p>
            </div>
            <button
              type="button"
              onClick={onClose}
              aria-label="Close"
              className={`shrink-0 ml-4 rounded-lg p-1 text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING}`}
            >
              <X className="h-5 w-5" />
            </button>
          </div>

          {/* Body — variant-specific */}
          <div className="px-5 py-5">
            {action.code === "post_customer_update" && (
              <PostCustomerUpdateBody row={row} onClose={onClose} onSuccess={onSuccess} />
            )}
            {action.code === "contact_customer" && (
              <ContactCustomerBody row={row} onClose={onClose} onSuccess={onSuccess} />
            )}
            {action.code === "acknowledge_attention" && (
              <AcknowledgeAttentionBody row={row} onClose={onClose} onSuccess={onSuccess} />
            )}
            {action.code === "add_internal_note" && (
              <AddInternalNoteBody row={row} onClose={onClose} onSuccess={onSuccess} />
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Shared error/cancel footer helpers
// ---------------------------------------------------------------------------

function ConflictMessage({ children }: { children: React.ReactNode }) {
  return (
    <p className="text-sm text-[var(--ophalo-attention)] bg-[var(--ophalo-attention-bg)] rounded-lg px-3 py-2">
      {children}
    </p>
  );
}

function ErrorMessage({ children }: { children: React.ReactNode }) {
  return (
    <p className="text-sm text-[var(--ophalo-danger)] bg-[var(--ophalo-danger-bg)] rounded-lg px-3 py-2">
      {children}
    </p>
  );
}

interface BodyProps {
  row: KeepRequestSummary;
  onClose: () => void;
  onSuccess: () => void;
}

// ---------------------------------------------------------------------------
// Variant 1 — post_customer_update
// ---------------------------------------------------------------------------

function PostCustomerUpdateBody({ row, onClose, onSuccess }: BodyProps) {
  const [message, setMessage] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!message.trim() || submitting) return;
    setSubmitting(true);
    setError(null);
    setConflict(false);
    try {
      await api.postBusinessUpdate(row.id, { message: message.trim() }, row.version);
      onSuccess();
    } catch (err) {
      setSubmitting(false);
      if (err instanceof ApiError && err.status === 409) {
        setConflict(true);
      } else {
        setError("Something went wrong. Please try again or open detail.");
      }
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <KeepBadge variant="teal" className="self-start">Customer visible</KeepBadge>
        <p className="text-sm text-[var(--ophalo-muted)]">
          This update will appear on the customer request page.
        </p>
      </div>

      <div className="flex flex-col gap-1.5">
        <label className="text-sm font-medium text-[var(--ophalo-ink)]">
          Message
        </label>
        <textarea
          value={message}
          onChange={(e) => setMessage(e.target.value)}
          placeholder="Write your update for the customer…"
          rows={4}
          className={INPUT_BASE}
          disabled={submitting}
          required
        />
      </div>

      {conflict && (
        <ConflictMessage>
          This request was updated by someone else. Your message is preserved — open detail to continue.
        </ConflictMessage>
      )}
      {error && <ErrorMessage>{error}</ErrorMessage>}

      <div className="flex items-center justify-end gap-3 pt-1">
        <button
          type="button"
          onClick={onClose}
          className={`text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors rounded ${FOCUS_RING}`}
        >
          Cancel
        </button>
        <KeepButton variant="teal" type="submit" disabled={!message.trim() || submitting}>
          {submitting ? "Sending…" : "Send update"}
        </KeepButton>
      </div>
    </form>
  );
}

// ---------------------------------------------------------------------------
// Variant 2 — contact_customer
// ---------------------------------------------------------------------------

function ContactCustomerBody({ row, onClose, onSuccess }: BodyProps) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  async function handleSubmit(body: LogExternalContactBody) {
    if (submitting) return;
    setSubmitting(true);
    setError(null);
    setConflict(false);
    try {
      await api.logExternalContact(row.id, body, row.version);
      onSuccess();
    } catch (err) {
      setSubmitting(false);
      if (err instanceof ApiError && err.status === 409) {
        setConflict(true);
      } else {
        setError("Something went wrong. Please try again or open detail.");
      }
    }
  }

  return (
    <>
      <KeepBadge variant="default">Internal record — not visible to customer</KeepBadge>
      <div className="mt-4">
        <ExternalContactForm
          loading={submitting}
          disabled={conflict}
          error={conflict
            ? "This request was updated by someone else. Your notes are preserved — open detail to continue."
            : error}
          onSubmit={(body) => void handleSubmit(body)}
          onCancel={onClose}
        />
      </div>
    </>
  );
}

// ---------------------------------------------------------------------------
// Variant 3 — acknowledge_attention
// ---------------------------------------------------------------------------

function AcknowledgeAttentionBody({ row, onClose, onSuccess }: BodyProps) {
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  const attentionLabel = row.attention.attentionReason
    ? (ATTENTION_REASON_LABELS[row.attention.attentionReason] ?? row.attention.attentionReason)
    : null;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!reason.trim() || submitting) return;
    setSubmitting(true);
    setError(null);
    setConflict(false);
    try {
      await api.acknowledgeAttention(row.id, reason.trim(), row.version);
      onSuccess();
    } catch (err) {
      setSubmitting(false);
      if (err instanceof ApiError && err.status === 409) {
        setConflict(true);
      } else {
        setError("Something went wrong. Please try again or open detail.");
      }
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <KeepBadge variant="default">Internal — clears attention flag</KeepBadge>

      {attentionLabel && (
        <p className="text-sm text-[var(--ophalo-ink)]">
          Clearing: <span className="font-medium">{attentionLabel}</span>
        </p>
      )}

      <div className="flex flex-col gap-1.5">
        <label className="text-sm font-medium text-[var(--ophalo-ink)]">
          Reason <span className="text-[var(--ophalo-danger)]">*</span>
        </label>
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="Briefly explain why this attention is being cleared…"
          rows={3}
          className={INPUT_BASE}
          disabled={submitting}
          required
        />
      </div>

      {conflict && (
        <ConflictMessage>
          This request was updated by someone else. Your note is preserved — open detail to continue.
        </ConflictMessage>
      )}
      {error && <ErrorMessage>{error}</ErrorMessage>}

      <div className="flex items-center justify-end gap-3 pt-1">
        <button
          type="button"
          onClick={onClose}
          className={`text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors rounded ${FOCUS_RING}`}
        >
          Cancel
        </button>
        <KeepButton variant="primary" type="submit" disabled={!reason.trim() || submitting}>
          {submitting ? "Acknowledging…" : "Acknowledge"}
        </KeepButton>
      </div>
    </form>
  );
}

// ---------------------------------------------------------------------------
// Variant 4 — add_internal_note
// ---------------------------------------------------------------------------

function AddInternalNoteBody({ row, onClose, onSuccess }: BodyProps) {
  const [note, setNote] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!note.trim() || submitting) return;
    setSubmitting(true);
    setError(null);
    setConflict(false);
    try {
      await api.addInternalNote(row.id, { note: note.trim() }, row.version);
      onSuccess();
    } catch (err) {
      setSubmitting(false);
      if (err instanceof ApiError && err.status === 409) {
        setConflict(true);
      } else {
        setError("Something went wrong. Please try again or open detail.");
      }
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <KeepBadge variant="default" className="self-start">Internal note</KeepBadge>
        <p className="text-sm text-[var(--ophalo-muted)]">
          Not visible to customer.
        </p>
      </div>

      <div className="flex flex-col gap-1.5">
        <label className="text-sm font-medium text-[var(--ophalo-ink)]">Note</label>
        <textarea
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Add a private note for your team…"
          rows={4}
          className={INPUT_BASE}
          disabled={submitting}
          required
        />
      </div>

      {conflict && (
        <ConflictMessage>
          This request was updated by someone else. Your note is preserved — open detail to continue.
        </ConflictMessage>
      )}
      {error && <ErrorMessage>{error}</ErrorMessage>}

      <div className="flex items-center justify-end gap-3 pt-1">
        <button
          type="button"
          onClick={onClose}
          className={`text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors rounded ${FOCUS_RING}`}
        >
          Cancel
        </button>
        <KeepButton variant="primary" type="submit" disabled={!note.trim() || submitting}>
          {submitting ? "Saving…" : "Save note"}
        </KeepButton>
      </div>
    </form>
  );
}
