import { useId, useRef, type ReactNode, type RefObject } from "react";
import { KeepModal } from "../../components/keep/KeepModal";
import { KeepButton } from "../../components/keep/KeepButton";

// RD-058B-2 correction: the confirm step for a request-lifecycle mutation (Mark work done,
// Close request, Classify) is its own focused dialog — never an inline row that expands the
// Request Anchor and displaces the request identity. Centered on desktop, bottom-sheet on narrow.
// `KeepModal` owns dialog semantics: focus trap, Escape-to-close, and focus restoration to the
// triggering control on unmount. The underlying page is not re-laid-out while this is open.
//
// GAP-063: `content` is a generic slot for a mutation-specific field (e.g. an optional reason
// textarea) — kept generic here rather than baking classification-specific state into this shared
// dialog. When `initialContentFocus` is given, that ref receives initial focus instead of Cancel;
// callers pass the reason field's ref for a deliberate default of "compose the reason first," and
// omit it to keep the original Cancel-first deliberate-destructive-action focus.
interface MutationConfirmDialogProps {
  title: string;
  /** Extra advisory shown under the title; omit when it would merely repeat the title. */
  body?: string | null;
  /** Optional mutation-specific content (e.g. a reason field) rendered between body and actions. */
  content?: ReactNode;
  /** Focus target on open when `content` includes a field that should be focused first. */
  initialContentFocus?: RefObject<HTMLElement | null>;
  confirmLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
}

export function MutationConfirmDialog({
  title,
  body,
  content,
  initialContentFocus,
  confirmLabel,
  onConfirm,
  onCancel,
}: MutationConfirmDialogProps) {
  const titleId = useId();
  const cancelRef = useRef<HTMLButtonElement>(null);

  return (
    <KeepModal
      onClose={onCancel}
      labelledBy={titleId}
      initialFocus={initialContentFocus ?? cancelRef}
      backdropClassName="bg-black/40"
      overlayClassName="flex items-end justify-center sm:items-center sm:p-4"
      panelClassName="w-full sm:max-w-md rounded-t-2xl sm:rounded-2xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-5 shadow-xl"
    >
      <h2 id={titleId} className="text-base font-semibold text-[var(--ophalo-ink)]">
        {title}
      </h2>
      {body && (
        <p className="mt-2 text-sm leading-6 text-[var(--ophalo-muted)]">{body}</p>
      )}
      {content && <div className="mt-3">{content}</div>}
      <div className="mt-5 flex justify-end gap-2">
        <KeepButton ref={cancelRef} type="button" variant="secondary" onClick={onCancel}>
          Cancel
        </KeepButton>
        <KeepButton type="button" variant="teal" onClick={onConfirm}>
          {confirmLabel}
        </KeepButton>
      </div>
    </KeepModal>
  );
}
