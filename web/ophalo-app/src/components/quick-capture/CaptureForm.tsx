import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { AlertTriangle, Loader2 } from "lucide-react";
import { api, ApiError } from "../../lib/apiClient";
import { SOURCE_OPTIONS } from "./utils";

interface CaptureFormProps {
  lockedPhone: string;
  prefill: { name?: string; email?: string; description?: string } | null;
  isPastDue: boolean;
  isReadOnly: boolean;
  onSuccess: (requestId: string, referenceCode: string, pageToken: string, customerPhone: string, customerEmail: string | null, customerName: string) => void;
  onBack: () => void;
  onClose: () => void;
}

export function CaptureForm({
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
  const [description, setDescription] = useState(prefill?.description ?? "");
  const [source, setSource] = useState<string>("");
  const [showAddress, setShowAddress] = useState(false);
  const [addrLine1, setAddrLine1] = useState("");
  const [addrLine2, setAddrLine2] = useState("");
  const [addrCity, setAddrCity] = useState("");
  const [addrState, setAddrState] = useState("");
  const [addrZip, setAddrZip] = useState("");

  const { mutate, isPending, error } = useMutation({
    mutationFn: () =>
      api.createRequest({
        customerName: name.trim(),
        customerPhone: lockedPhone,
        customerEmail: email.trim() || undefined,
        description: description.trim(),
        source,
        ...(showAddress && addrLine1.trim() ? {
          serviceAddressLine1: addrLine1.trim(),
          serviceAddressLine2: addrLine2.trim() || undefined,
          serviceCity: addrCity.trim() || undefined,
          serviceState: addrState.trim() || undefined,
          serviceZip: addrZip.trim() || undefined,
        } : {}),
      }),
    onSuccess: (result) => onSuccess(result.requestId, result.referenceCode, result.pageToken, lockedPhone, email.trim() || null, name.trim()),
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

      <div>
        {!showAddress ? (
          <button
            type="button"
            onClick={() => setShowAddress(true)}
            className="text-sm text-slate-500 hover:text-slate-700 underline-offset-2 hover:underline"
          >
            + Add service address (optional)
          </button>
        ) : (
          <div className="flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <span className="text-sm font-medium text-slate-700">Service address</span>
              <button
                type="button"
                onClick={() => { setShowAddress(false); setAddrLine1(""); setAddrLine2(""); setAddrCity(""); setAddrState(""); setAddrZip(""); }}
                className="text-xs text-slate-400 hover:text-slate-600"
              >
                Remove
              </button>
            </div>
            <input
              type="text"
              value={addrLine1}
              onChange={(e) => setAddrLine1(e.target.value)}
              disabled={isReadOnly}
              placeholder="Address line 1"
              className="block w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-slate-500 focus:outline-none focus:ring-1 focus:ring-slate-500 disabled:bg-slate-50 disabled:text-slate-400"
            />
            <input
              type="text"
              value={addrLine2}
              onChange={(e) => setAddrLine2(e.target.value)}
              disabled={isReadOnly}
              placeholder="Address line 2 (optional)"
              className="block w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-slate-500 focus:outline-none focus:ring-1 focus:ring-slate-500 disabled:bg-slate-50 disabled:text-slate-400"
            />
            <div className="flex gap-2">
              <input
                type="text"
                value={addrCity}
                onChange={(e) => setAddrCity(e.target.value)}
                disabled={isReadOnly}
                placeholder="City"
                className="block flex-1 rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-slate-500 focus:outline-none focus:ring-1 focus:ring-slate-500 disabled:bg-slate-50 disabled:text-slate-400"
              />
              <input
                type="text"
                value={addrState}
                onChange={(e) => setAddrState(e.target.value)}
                disabled={isReadOnly}
                placeholder="State"
                maxLength={2}
                className="block w-16 rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-slate-500 focus:outline-none focus:ring-1 focus:ring-slate-500 disabled:bg-slate-50 disabled:text-slate-400"
              />
              <input
                type="text"
                value={addrZip}
                onChange={(e) => setAddrZip(e.target.value)}
                disabled={isReadOnly}
                placeholder="ZIP"
                maxLength={10}
                className="block w-24 rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-slate-500 focus:outline-none focus:ring-1 focus:ring-slate-500 disabled:bg-slate-50 disabled:text-slate-400"
              />
            </div>
          </div>
        )}
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
