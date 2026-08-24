import { useState } from "react";
import { X } from "lucide-react";
import { type PhoneLookupResult } from "../lib/apiClient";
import { type Stage, type CaptureFormDraft } from "./quick-capture/utils";
import { HandoffPanel } from "./quick-capture/HandoffPanel";
import { LookupGate } from "./quick-capture/LookupGate";
import { LookupResultView } from "./quick-capture/LookupResultView";
import { CaptureForm } from "./quick-capture/CaptureForm";
import { SuccessPanel } from "./quick-capture/SuccessPanel";
import { KeepModal } from "./keep/KeepModal";

export interface QuickCaptureProps {
  onClose: () => void;
  onSelectRequest?: (requestId: string) => void;
  isPastDue?: boolean;
  isReadOnly?: boolean;
  isOwnerOrAdmin?: boolean;
  onNavigateSettings?: (section?: "public-profile" | "policy" | "team") => void;
  // Intentional bypass of the phone lookup gate — used only by Create follow-up request.
  // The phone has already been verified by the original closed request; re-running the lookup
  // would surface that closed request and confuse the duplicate-detection UX.
  followUpPrefill?: { phone: string; name?: string; email?: string; description?: string; wasTruncated?: boolean };
}

export function QuickCapture({ onClose, onSelectRequest, isPastDue = false, isReadOnly = false, isOwnerOrAdmin = false, onNavigateSettings, followUpPrefill }: QuickCaptureProps) {
  const publicBaseUrl = import.meta.env.VITE_PUBLIC_BASE_URL as string;

  const [stage, setStage] = useState<Stage>(
    followUpPrefill
      ? { kind: "capture", lockedPhone: followUpPrefill.phone, prefill: followUpPrefill }
      : isOwnerOrAdmin
        ? { kind: "handoff" }
        : { kind: "lookup" }
  );
  const [captureFormDraft, setCaptureFormDraft] = useState<CaptureFormDraft | null>(null);

  function handleLookupSuccess(result: PhoneLookupResult, phone: string) {
    if (result.customer || result.possibleCustomer) {
      // Exact match and possible-existing-customer (ADR-492) are mutually exclusive results,
      // but both route through the same decision screen — always show it before capture, whether
      // active requests exist or not.
      setStage({ kind: "result", lookup: result, lockedPhone: phone });
    } else {
      // No match — advance directly to capture with locked phone, no prefill.
      setStage({ kind: "capture", lockedPhone: phone, prefill: null });
    }
  }

  function handleCaptureSuccess(requestId: string, referenceCode: string, pageToken: string, customerPhone: string, customerEmail: string | null, customerName: string) {
    setStage({ kind: "success", requestId, referenceCode, pageToken, customerPhone, customerEmail, customerName });
  }

  function handleCaptureAnother() {
    setCaptureFormDraft(null);
    setStage({ kind: "lookup" });
  }

  function handleViewRequest(requestId: string) {
    if (onSelectRequest) {
      onSelectRequest(requestId);
      onClose();
    } else {
      window.location.href = `/keep/requests/${requestId}`;
    }
  }

  function handleNavigateToExisting(requestId: string) {
    onClose();
    if (onSelectRequest) {
      onSelectRequest(requestId);
    } else {
      window.location.href = `/keep/requests/${requestId}`;
    }
  }

  function handleBack(draft: CaptureFormDraft) {
    setCaptureFormDraft(draft);
    setStage({ kind: "lookup" });
  }

  const title =
    stage.kind === "handoff"
      ? "Text a Link"
      : stage.kind === "lookup"
        ? "Look Up Customer"
        : stage.kind === "result"
          ? stage.lookup.customer
            ? "Customer Found"
            : "Possible Existing Customer"
          : stage.kind === "capture"
            ? followUpPrefill ? "Create Follow-up Request" : "New Request"
            : "Request Captured";

  const content = (() => {
    if (stage.kind === "handoff") {
      return (
        <HandoffPanel
          onEnterForCustomer={() => setStage({ kind: "lookup" })}
          onNavigateSettings={() => { onClose(); onNavigateSettings?.("public-profile"); }}
        />
      );
    }

    if (stage.kind === "lookup") {
      return (
        <LookupGate
          onClose={onClose}
          onLookupSuccess={handleLookupSuccess}
          isPastDue={isPastDue}
          isReadOnly={isReadOnly}
        />
      );
    }

    if (stage.kind === "result") {
      const { lookup, lockedPhone } = stage;
      return (
        <LookupResultView
          lookup={lookup}
          lockedPhone={lockedPhone}
          onProceed={() =>
            setStage({
              kind: "capture",
              lockedPhone,
              prefill: lookup.customer
                ? { name: lookup.customer.name, email: lookup.customer.email ?? undefined }
                : null,
            })
          }
          onUseExistingCustomer={(candidateCustomerId) =>
            setStage({
              kind: "capture",
              lockedPhone,
              prefill: lookup.possibleCustomer
                ? { name: lookup.possibleCustomer.name, email: lookup.possibleCustomer.email ?? undefined }
                : null,
              existingCustomerId: candidateCustomerId,
            })
          }
          onCreateAsNew={() =>
            // No candidate id and no prefill — a bare phone lookup must never silently reuse
            // possible-existing-customer identity (ADR-492).
            setStage({ kind: "capture", lockedPhone, prefill: null })
          }
          onNavigateToRequest={handleNavigateToExisting}
          onBack={() => setStage({ kind: "lookup" })}
        />
      );
    }

    if (stage.kind === "capture") {
      return (
        <CaptureForm
          lockedPhone={stage.lockedPhone}
          prefill={stage.prefill}
          existingCustomerId={stage.existingCustomerId}
          initialDraft={captureFormDraft ?? undefined}
          isPastDue={isPastDue}
          isReadOnly={isReadOnly}
          onSuccess={handleCaptureSuccess}
          onBack={handleBack}
          onClose={onClose}
        />
      );
    }

    return (
      <SuccessPanel
        requestId={stage.requestId}
        referenceCode={stage.referenceCode}
        pageToken={stage.pageToken}
        publicBaseUrl={publicBaseUrl}
        customerPhone={stage.customerPhone}
        customerEmail={stage.customerEmail}
        customerName={stage.customerName}
        onCaptureAnother={handleCaptureAnother}
        onViewRequest={() => handleViewRequest(stage.requestId)}
      />
    );
  })();

  // Desktop: slide-over right drawer
  // Mobile: full-screen sheet
  return (
    <KeepModal
      onClose={onClose}
      label={title}
      backdropClassName="bg-black/30"
      panelClassName={[
        "fixed z-50 bg-white shadow-xl flex flex-col",
        "md:right-0 md:top-0 md:bottom-0 md:w-[420px] md:max-w-full",
        "max-md:inset-0",
      ].join(" ")}
    >
      {/* Header */}
      <div className="flex items-center justify-between px-5 py-4 border-b border-slate-200 shrink-0">
        <h2 className="font-serif text-base font-semibold text-slate-900">{title}</h2>
        <button
          type="button"
          onClick={onClose}
          className="rounded-md p-1 text-slate-400 hover:text-slate-600"
          aria-label="Close"
        >
          <X className="h-5 w-5" />
        </button>
      </div>

      {/* Body */}
      <div className="flex-1 overflow-y-auto px-5 py-5">
        {content}
      </div>
    </KeepModal>
  );
}
