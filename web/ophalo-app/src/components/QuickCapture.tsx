import { useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Clipboard, Phone, X, Copy, ExternalLink, RefreshCw, AlertTriangle, Loader2 } from "lucide-react";
import {
  api,
  ApiError,
  type PhoneLookupResult,
  type PhoneLookupActiveRequest,
} from "../lib/apiClient";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type Stage =
  | { kind: "lookup" }
  | { kind: "result"; lookup: PhoneLookupResult; lockedPhone: string }
  | { kind: "capture"; prefill: { name?: string; email?: string } | null; lockedPhone: string }
  | { kind: "success"; requestId: string; referenceCode: string; pageToken: string };

const SOURCE_OPTIONS = [
  { label: "Phone Call", value: "phone" },
  { label: "Voicemail", value: "voicemail" },
  { label: "Text Thread", value: "text" },
  { label: "Email", value: "email" },
  { label: "Walk-In", value: "walk_in" },
  { label: "Referral", value: "referral" },
  { label: "Other", value: "other" },
] as const;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function stripToDigits(raw: string): string {
  return raw.replace(/\D/g, "");
}

function isPhoneShaped(text: string): boolean {
  const digits = stripToDigits(text);
  return digits.length >= 7 && digits.length <= 15;
}

function formatStatus(slug: string): string {
  const map: Record<string, string> = {
    received: "Received",
    scheduled: "Scheduled",
    in_progress: "In Progress",
    pending_customer: "Waiting on Customer",
    resolved: "Resolved",
    closed: "Closed",
    cancelled: "Cancelled",
    spam: "Spam",
    test: "Test",
  };
  return map[slug] ?? slug;
}

// ---------------------------------------------------------------------------
// Phone Lookup Gate
// ---------------------------------------------------------------------------

interface LookupGateProps {
  onClose: () => void;
  onLookupSuccess: (result: PhoneLookupResult, phone: string) => void;
  isPastDue: boolean;
  isReadOnly: boolean;
}

function LookupGate({ onClose, onLookupSuccess, isPastDue, isReadOnly }: LookupGateProps) {
  const [raw, setRaw] = useState("");
  const [clipboardPrompt, setClipboardPrompt] = useState(false);
  const [pendingClipboard, setPendingClipboard] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  const supportsContactPicker =
    typeof window !== "undefined" && "contacts" in navigator;

  const { mutate: doLookup, isPending, error } = useMutation({
    mutationFn: (digits: string) => api.lookupRequestByPhone(digits),
    onSuccess: (result, digits) => onLookupSuccess(result, digits),
  });

  function handleChange(e: React.ChangeEvent<HTMLInputElement>) {
    const digits = stripToDigits(e.target.value);
    setRaw(digits);
    if (digits.length === 10) {
      doLookup(digits);
    }
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Enter" && raw.length >= 7) {
      doLookup(raw);
    }
  }

  async function handlePaste(e: React.ClipboardEvent<HTMLInputElement>) {
    const text = e.clipboardData.getData("text");
    const digits = stripToDigits(text);
    if (digits.length >= 7) {
      e.preventDefault();
      setRaw(digits);
      if (digits.length === 10) {
        doLookup(digits);
      }
    }
  }

  async function tryClipboard() {
    try {
      const text = await navigator.clipboard.readText();
      if (isPhoneShaped(text)) {
        const digits = stripToDigits(text);
        setPendingClipboard(digits);
        setClipboardPrompt(true);
      }
    } catch {
      // clipboard read denied or unavailable
    }
  }

  function acceptClipboard() {
    setRaw(pendingClipboard);
    setClipboardPrompt(false);
    if (pendingClipboard.length === 10) {
      doLookup(pendingClipboard);
    }
  }

  async function handleContactPicker() {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const contacts = await (navigator as any).contacts.select(["tel"], { multiple: false });
      if (contacts?.length > 0) {
        const tel: string | undefined = contacts[0]?.tel?.[0];
        if (tel) {
          const digits = stripToDigits(tel);
          setRaw(digits);
          if (digits.length === 10) {
            doLookup(digits);
          }
        }
      }
    } catch {
      // picker cancelled or unavailable
    }
  }

  const apiError = error instanceof ApiError ? error : null;
  const is403 = apiError?.status === 403;
  const is402 = apiError?.status === 402;

  return (
    <div className="flex flex-col gap-4">
      {isPastDue && (
        <div className="rounded-md bg-amber-50 border border-amber-200 px-3 py-2 flex items-center gap-2 text-amber-800 text-sm">
          <AlertTriangle className="h-4 w-4 shrink-0" />
          Account past due — some actions may be restricted.
        </div>
      )}

      <div>
        <label className="block text-sm font-medium text-slate-700 mb-1" htmlFor="phone-lookup">
          Customer phone number
        </label>
        <div className="flex items-center gap-2">
          <div className="relative flex-1">
            <input
              id="phone-lookup"
              ref={inputRef}
              type="tel"
              inputMode="numeric"
              placeholder="Enter digits"
              value={raw}
              onChange={handleChange}
              onKeyDown={handleKeyDown}
              onPaste={handlePaste}
              disabled={isPending || isReadOnly}
              maxLength={15}
              className="block w-full rounded-md border border-slate-300 px-3 py-2 pr-8 text-sm text-slate-900 placeholder:text-slate-400 focus:border-slate-500 focus:outline-none focus:ring-1 focus:ring-slate-500 disabled:bg-slate-50 disabled:text-slate-400"
            />
            {isPending && (
              <Loader2 className="absolute right-2 top-2.5 h-4 w-4 animate-spin text-slate-400" />
            )}
          </div>
          {supportsContactPicker && (
            <button
              type="button"
              onClick={handleContactPicker}
              disabled={isPending || isReadOnly}
              title="Choose from contacts"
              className="rounded-md border border-slate-300 p-2 text-slate-500 hover:bg-slate-50 disabled:opacity-40"
            >
              <Phone className="h-4 w-4" />
            </button>
          )}
          <button
            type="button"
            onClick={tryClipboard}
            disabled={isPending || isReadOnly}
            title="Paste from clipboard"
            className="rounded-md border border-slate-300 p-2 text-slate-500 hover:bg-slate-50 disabled:opacity-40"
          >
            <Clipboard className="h-4 w-4" />
          </button>
        </div>

        {clipboardPrompt && (
          <div className="mt-2 rounded-md border border-slate-200 bg-slate-50 px-3 py-2 flex items-center justify-between gap-3 text-sm">
            <span className="text-slate-600">Use <span className="font-mono font-medium">{pendingClipboard}</span> from clipboard?</span>
            <div className="flex gap-2 shrink-0">
              <button
                type="button"
                onClick={acceptClipboard}
                className="text-slate-800 font-medium hover:underline"
              >
                Use it
              </button>
              <button
                type="button"
                onClick={() => setClipboardPrompt(false)}
                className="text-slate-500 hover:underline"
              >
                Dismiss
              </button>
            </div>
          </div>
        )}

        <p className="mt-1 text-xs text-slate-400">
          Digits only · Lookup fires automatically at 10 digits or press Enter
        </p>
      </div>

      {is403 && (
        <p className="text-sm text-red-600">You do not have permission to look up requests.</p>
      )}
      {is402 && (
        <p className="text-sm text-amber-700">Account access required. Contact your account owner.</p>
      )}
      {apiError && !is403 && !is402 && (
        <p className="text-sm text-red-600">Lookup failed. Try again.</p>
      )}

      <div className="flex justify-between items-center pt-2 border-t border-slate-100">
        <button
          type="button"
          onClick={onClose}
          className="text-sm text-slate-500 hover:text-slate-700"
        >
          Cancel
        </button>
        <button
          type="button"
          disabled={isPending || raw.length < 7 || isReadOnly}
          onClick={() => doLookup(raw)}
          title={isReadOnly ? "Read-only permission" : undefined}
          className="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-40"
        >
          {isPending ? "Looking up…" : "Look Up"}
        </button>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Lookup Result — customer match cards
// ---------------------------------------------------------------------------

interface LookupResultProps {
  lookup: PhoneLookupResult;
  lockedPhone: string;
  onProceed: () => void;
  onNavigateToRequest: (requestId: string) => void;
  onBack: () => void;
}

function LookupResultView({
  lookup,
  lockedPhone,
  onProceed,
  onNavigateToRequest,
  onBack,
}: LookupResultProps) {
  const { customer, activeRequests, hasMoreActiveRequests } = lookup;

  return (
    <div className="flex flex-col gap-4">
      {customer ? (
        <div>
          <p className="text-sm font-medium text-slate-800">{customer.name}</p>
          <p className="text-sm text-slate-500">{customer.phone}</p>
          {customer.email && <p className="text-xs text-slate-400">{customer.email}</p>}
        </div>
      ) : (
        <div>
          <p className="text-sm text-slate-500">No customer found for <span className="font-mono font-medium">{lockedPhone}</span>.</p>
        </div>
      )}

      {activeRequests.length > 0 && (
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-slate-400 mb-2">Active requests</p>
          <ul className="space-y-2">
            {activeRequests.map((r) => (
              <ActiveRequestCard
                key={r.requestId}
                request={r}
                onNavigate={() => onNavigateToRequest(r.requestId)}
              />
            ))}
          </ul>
          {hasMoreActiveRequests && (
            <p className="mt-2 text-xs text-slate-400">
              More active work exists in the Command Center.
            </p>
          )}
        </div>
      )}

      <div className="flex justify-between items-center pt-2 border-t border-slate-100">
        <button
          type="button"
          onClick={onBack}
          className="text-sm text-slate-500 hover:text-slate-700"
        >
          ← Back
        </button>
        <button
          type="button"
          onClick={onProceed}
          className="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
        >
          {customer
            ? `Create New Request for ${customer.name}`
            : "Create New Request"}
        </button>
      </div>
    </div>
  );
}

function ActiveRequestCard({
  request,
  onNavigate,
}: {
  request: PhoneLookupActiveRequest;
  onNavigate: () => void;
}) {
  return (
    <li>
      <button
        type="button"
        onClick={onNavigate}
        className="w-full text-left rounded-md border border-slate-200 bg-white px-3 py-2 hover:bg-slate-50 focus:outline-none focus:ring-1 focus:ring-slate-400"
      >
        <div className="flex items-center justify-between gap-2">
          <span className="text-xs font-mono text-slate-500">{request.referenceCode}</span>
          <span className="text-xs rounded-full bg-slate-100 px-2 py-0.5 text-slate-600">
            {formatStatus(request.status)}
          </span>
        </div>
        <p className="mt-1 text-sm text-slate-700 line-clamp-2">{request.description}</p>
      </button>
    </li>
  );
}

// ---------------------------------------------------------------------------
// Capture form
// ---------------------------------------------------------------------------

interface CaptureFormProps {
  lockedPhone: string;
  prefill: { name?: string; email?: string } | null;
  isPastDue: boolean;
  isReadOnly: boolean;
  onSuccess: (requestId: string, referenceCode: string, pageToken: string) => void;
  onBack: () => void;
  onClose: () => void;
}

function CaptureForm({
  lockedPhone,
  prefill,
  isPastDue,
  isReadOnly,
  onSuccess,
  onBack,
  onClose,
}: CaptureFormProps) {
  const [name, setName] = useState(prefill?.name ?? "");
  const [email, setEmail] = useState(prefill?.email ?? "");
  const [description, setDescription] = useState("");
  const [source, setSource] = useState<string>("");

  const { mutate, isPending, error } = useMutation({
    mutationFn: () =>
      api.createRequest({
        customerName: name.trim(),
        customerPhone: lockedPhone,
        customerEmail: email.trim() || undefined,
        description: description.trim(),
        source,
      }),
    onSuccess: (result) => onSuccess(result.requestId, result.referenceCode, result.pageToken),
  });

  const apiError = error instanceof ApiError ? error : null;
  const is403 = apiError?.status === 403;
  const is402 = apiError?.status === 402;
  const isValidationError = apiError?.status === 400 || apiError?.status === 422;

  const canSubmit =
    !isPending &&
    !isReadOnly &&
    name.trim().length > 0 &&
    description.trim().length > 0 &&
    source.length > 0;

  return (
    <div className="flex flex-col gap-4">
      {isPastDue && (
        <div className="rounded-md bg-amber-50 border border-amber-200 px-3 py-2 flex items-center gap-2 text-amber-800 text-sm">
          <AlertTriangle className="h-4 w-4 shrink-0" />
          Account past due — new requests may be restricted.
        </div>
      )}

      <div>
        <label className="block text-sm font-medium text-slate-700 mb-1">Phone</label>
        <input
          type="text"
          value={lockedPhone}
          readOnly
          className="block w-full rounded-md border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-500 cursor-not-allowed"
        />
      </div>

      <div>
        <label htmlFor="customer-name" className="block text-sm font-medium text-slate-700 mb-1">
          Customer name <span className="text-red-500">*</span>
        </label>
        <input
          id="customer-name"
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          readOnly={!!prefill?.name}
          disabled={isReadOnly}
          placeholder="Full name"
          className="block w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-slate-500 focus:outline-none focus:ring-1 focus:ring-slate-500 read-only:bg-slate-50 read-only:text-slate-500 disabled:bg-slate-50 disabled:text-slate-400"
        />
      </div>

      <div>
        <label htmlFor="customer-email" className="block text-sm font-medium text-slate-700 mb-1">
          Email <span className="text-slate-400 text-xs">(optional)</span>
        </label>
        <input
          id="customer-email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          readOnly={!!prefill?.email}
          disabled={isReadOnly}
          placeholder="customer@example.com"
          className="block w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-slate-500 focus:outline-none focus:ring-1 focus:ring-slate-500 read-only:bg-slate-50 read-only:text-slate-500 disabled:bg-slate-50 disabled:text-slate-400"
        />
      </div>

      <div>
        <label htmlFor="source" className="block text-sm font-medium text-slate-700 mb-1">
          Source <span className="text-red-500">*</span>
        </label>
        <select
          id="source"
          value={source}
          onChange={(e) => setSource(e.target.value)}
          disabled={isReadOnly}
          className="block w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 focus:border-slate-500 focus:outline-none focus:ring-1 focus:ring-slate-500 disabled:bg-slate-50 disabled:text-slate-400"
        >
          <option value="">Select source…</option>
          {SOURCE_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      </div>

      <div>
        <label htmlFor="description" className="block text-sm font-medium text-slate-700 mb-1">
          Description <span className="text-red-500">*</span>
        </label>
        <textarea
          id="description"
          rows={3}
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          disabled={isReadOnly}
          placeholder="What does the customer need?"
          className="block w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-slate-500 focus:outline-none focus:ring-1 focus:ring-slate-500 disabled:bg-slate-50 disabled:text-slate-400 resize-none"
        />
      </div>

      {is403 && (
        <p className="text-sm text-red-600">You do not have permission to create requests.</p>
      )}
      {is402 && (
        <p className="text-sm text-amber-700">Account access required. Contact your account owner.</p>
      )}
      {isValidationError && (
        <p className="text-sm text-red-600">Check the form — some fields are invalid.</p>
      )}
      {apiError && !is403 && !is402 && !isValidationError && (
        <p className="text-sm text-red-600">Something went wrong. Try again.</p>
      )}

      <div className="flex justify-between items-center pt-2 border-t border-slate-100">
        <button
          type="button"
          onClick={onBack}
          disabled={isPending}
          className="text-sm text-slate-500 hover:text-slate-700 disabled:opacity-40"
        >
          ← Back
        </button>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onClose}
            disabled={isPending}
            className="text-sm text-slate-500 hover:text-slate-700 disabled:opacity-40"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={!canSubmit}
            onClick={() => mutate()}
            title={isReadOnly ? "Read-only permission" : undefined}
            className="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-40 flex items-center gap-2"
          >
            {isPending && <Loader2 className="h-4 w-4 animate-spin" />}
            {isPending ? "Saving…" : "Capture Request"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Success panel (desktop/tablet)
// ---------------------------------------------------------------------------

interface SuccessPanelProps {
  requestId: string;
  referenceCode: string;
  pageToken: string;
  publicBaseUrl: string;
  onCaptureAnother: () => void;
  onViewRequest: () => void;
}

function SuccessPanel({
  requestId,
  referenceCode,
  pageToken,
  publicBaseUrl,
  onCaptureAnother,
  onViewRequest,
}: SuccessPanelProps) {
  const [copied, setCopied] = useState(false);
  const trackerUrl = `${publicBaseUrl}/keep/r/${pageToken}`;

  async function handleCopyLink() {
    try {
      await navigator.clipboard.writeText(trackerUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
      void api.recordShareIntent(requestId, "copy_link").catch(() => {});
    } catch {
      // clipboard write denied; no-op
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="rounded-md bg-emerald-50 border border-emerald-200 px-4 py-3">
        <p className="text-sm font-medium text-emerald-800">Request captured</p>
        <p className="text-xs text-emerald-600 font-mono mt-0.5">{referenceCode}</p>
      </div>

      <p className="text-xs text-amber-700 font-medium">
        Share the tracker link with the customer so they can follow progress.
      </p>

      <div className="flex flex-col gap-2">
        <button
          type="button"
          onClick={handleCopyLink}
          className="w-full flex items-center justify-center gap-2 rounded-md bg-slate-900 px-4 py-2.5 text-sm font-medium text-white hover:bg-slate-700"
        >
          <Copy className="h-4 w-4" />
          {copied ? "Copied!" : "Copy Tracker Link"}
        </button>

        <button
          type="button"
          onClick={onViewRequest}
          className="w-full flex items-center justify-center gap-2 rounded-md border border-slate-300 px-4 py-2.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          <ExternalLink className="h-4 w-4" />
          View Request Workbench
        </button>

        <button
          type="button"
          onClick={onCaptureAnother}
          className="w-full flex items-center justify-center gap-2 rounded-md border border-slate-300 px-4 py-2.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          <RefreshCw className="h-4 w-4" />
          Capture Another
        </button>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// QuickCapture — shell wrapper (modal on mobile, slide-over drawer on desktop)
// ---------------------------------------------------------------------------

export interface QuickCaptureProps {
  onClose: () => void;
  onSelectRequest?: (requestId: string) => void;
  isPastDue?: boolean;
  isReadOnly?: boolean;
}

export function QuickCapture({ onClose, onSelectRequest, isPastDue = false, isReadOnly = false }: QuickCaptureProps) {
  const publicBaseUrl = import.meta.env.VITE_PUBLIC_BASE_URL as string;
  const isMobile = typeof window !== "undefined" && window.innerWidth < 768;

  const [stage, setStage] = useState<Stage>({ kind: "lookup" });

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

  function handleCaptureSuccess(requestId: string, referenceCode: string, pageToken: string) {
    if (isMobile) {
      if (onSelectRequest) {
        onSelectRequest(requestId);
        onClose();
      } else {
        window.location.href = `/keep/requests/${requestId}`;
      }
      return;
    }
    setStage({ kind: "success", requestId, referenceCode, pageToken });
  }

  function handleCaptureAnother() {
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

  function handleBack() {
    setStage({ kind: "lookup" });
  }

  const title =
    stage.kind === "lookup"
      ? "Look Up Customer"
      : stage.kind === "result"
        ? "Customer Found"
        : stage.kind === "capture"
          ? "New Request"
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
          onBack={handleBack}
        />
      );
    }

    if (stage.kind === "capture") {
      return (
        <CaptureForm
          lockedPhone={stage.lockedPhone}
          prefill={stage.prefill}
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
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={[
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
      </div>
    </>
  );
}
