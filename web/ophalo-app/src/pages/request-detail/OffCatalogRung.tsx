import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { api, ApiError } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import { type ScopeCaptureRungProps } from "./ProposedScopeCaptureModal";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

const DESCRIPTION_MAX_LENGTH = 200;
const CONFLICT_NOTICE = "This proposed scope changed elsewhere — refreshed with the latest scope. Try again.";

/**
 * ADR-461 rung 5: always-available off-catalog capture — never blocks the workflow. The office
 * reviews and, later, may curate a promotion (ADR-476); this rung only ever writes a plain
 * OffCatalogItem line. Description trim/control-char rejection/200-char truncation is server-side
 * (Session 3.4d); the client mirrors the length cap so the counter matches what will be accepted.
 */
export function OffCatalogRung({ proposedScopeId, version, onCommitted, onConflict }: ScopeCaptureRungProps) {
  const [description, setDescription] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [descriptionError, setDescriptionError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const addMutation = useMutation({
    mutationFn: () =>
      api.fieldSelectProposedScopeLine(
        proposedScopeId,
        { lineType: "OffCatalogItem", offCatalogDescription: description.trim(), quantity: Number(quantity) },
        version,
      ),
    onSuccess: () => {
      setError(null);
      setDescriptionError(null);
      setDescription("");
      setQuantity("1");
      onCommitted();
    },
    onError: (err) => {
      if (err instanceof ApiError && err.status === 409) {
        onConflict(CONFLICT_NOTICE);
        return;
      }
      if (!(err instanceof ApiError)) {
        onConflict(CONFLICT_NOTICE);
        return;
      }
      if (err.code === "ProposedScope.LineOffCatalogDescriptionInvalidCharacters") {
        setDescriptionError("This description contains characters that aren't allowed.");
        return;
      }
      if (err.code === "ProposedScope.LineQuantityMustBePositive") {
        setError("Quantity must be greater than zero.");
        return;
      }
      setError("Something went wrong. Try again.");
    },
  });

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (addMutation.isPending) return;
    if (!description.trim()) {
      setDescriptionError("A description is required.");
      return;
    }
    setDescriptionError(null);
    setError(null);
    addMutation.mutate();
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3">
      <div>
        <label htmlFor="off-catalog-description" className="block text-xs text-[var(--ophalo-muted)] mb-1">
          Description
        </label>
        <textarea
          id="off-catalog-description"
          value={description}
          maxLength={DESCRIPTION_MAX_LENGTH}
          onChange={(e) => setDescription(e.target.value)}
          rows={2}
          className={INPUT_CLS}
        />
        <p className="text-xs text-[var(--ophalo-muted)] mt-1">
          {description.length}/{DESCRIPTION_MAX_LENGTH}
        </p>
        {descriptionError && <p className="text-sm text-[var(--ophalo-danger)]">{descriptionError}</p>}
      </div>
      <div>
        <label htmlFor="off-catalog-quantity" className="block text-xs text-[var(--ophalo-muted)] mb-1">
          Quantity
        </label>
        <input
          id="off-catalog-quantity"
          type="number"
          min="0"
          step="any"
          value={quantity}
          onChange={(e) => setQuantity(e.target.value)}
          className={INPUT_CLS}
        />
      </div>
      {error && <p className="text-sm text-[var(--ophalo-danger)]">{error}</p>}
      <KeepButton type="submit" variant="teal" disabled={addMutation.isPending} className="w-full">
        Add to scope
      </KeepButton>
    </form>
  );
}
