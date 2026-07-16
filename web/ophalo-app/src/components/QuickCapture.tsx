import { useState, useEffect, useRef } from "react";
import { X } from "lucide-react";
import { type PhoneLookupResult } from "../lib/apiClient";
import { type Stage, type CaptureFormDraft } from "./quick-capture/utils";
import { LookupGate } from "./quick-capture/LookupGate";
import { LookupResultView } from "./quick-capture/LookupResultView";
import { CaptureForm } from "./quick-capture/CaptureForm";
import { SuccessPanel } from "./quick-capture/SuccessPanel";

export interface QuickCaptureProps {
  onClose: () => void;
  onSelectRequest?: (requestId: string) => void;
  isPastDue?: boolean;
  isReadOnly?: boolean;
  // Intentional bypass of the phone lookup gate — used only by Create follow-up request.
  // The phone has already been verified by the original closed request; re-running the lookup
  // would surface that closed request and confuse the duplicate-detection UX.
  followUpPrefill?: { phone: string; name?: string; email?: string; description?: string };
}

export function QuickCapture({ onClose, onSelectRequest, isPastDue = false, isReadOnly = false, followUpPrefill }: QuickCaptureProps) {
  const publicBaseUrl = import.meta.env.VITE_PUBLIC_BASE_URL as string;

  const panelRef = useRef<HTMLDivElement>(null);
  const previousFocusRef = useRef<HTMLElement | null>(null);

  // Capture trigger element for focus restoration on unmount
  useEffect(() => {
    previousFocusRef.current = document.activeElement as HTMLElement;
    panelRef.current?.focus();
    return () => { previousFocusRef.current?.focus(); };
  }, []);

  // Escape to close
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") { e.preventDefault(); onClose(); }
    }
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  const [stage, setStage] = useState<Stage>(
    followUpPrefill
      ? { kind: "capture", lockedPhone: followUpPrefill.phone, prefill: followUpPrefill }
      : { kind: "lookup" }
  );
  const [captureFormDraft, setCaptureFormDraft] = useState<CaptureFormDraft | null>(null);

  function handleLookupSuccess(result: PhoneLookupResult, phone: string) {
    if (result.customer) {
      // Always show the result view when a customer is found — whether they have active requests
      // or not. "Customer match, no active requests → show customer name + Create New Request."
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
    stage.kind === "lookup"
      ? "Look Up Customer"
      : stage.kind === "result"
        ? "Customer Found"
        : stage.kind === "capture"
          ? followUpPrefill ? "Create Follow-up Request" : "New Request"
          : "Request Captured";

  const content = (() => {
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
    <>
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black/30 z-40"
        onClick={onClose}
        aria-hidden="true"
      />

      {/* Panel */}
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        className={[
          "fixed z-50 bg-white shadow-xl flex flex-col focus:outline-none",
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
      </div>
    </>
  );
}
