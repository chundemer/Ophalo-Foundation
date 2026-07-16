import { useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Clipboard, Phone, AlertTriangle, Loader2 } from "lucide-react";
import { api, ApiError, type PhoneLookupResult } from "../../lib/apiClient";
import { stripToDigits, isPhoneShaped } from "./utils";

interface LookupGateProps {
  onClose: () => void;
  onLookupSuccess: (result: PhoneLookupResult, phone: string) => void;
  isPastDue: boolean;
  isReadOnly: boolean;
}

export function LookupGate({ onClose, onLookupSuccess, isPastDue, isReadOnly }: LookupGateProps) {
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
    if (e.key === "Enter" && raw.length === 10) {
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

        {raw.length > 0 && raw.length !== 10 && (
          <p className="mt-1 text-xs text-red-600">Please enter a 10-digit phone number.</p>
        )}
        {(raw.length === 0 || raw.length === 10) && (
          <p className="mt-1 text-xs text-slate-400">
            Digits only · Lookup fires automatically at 10 digits
          </p>
        )}
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
          disabled={isPending || raw.length !== 10 || isReadOnly}
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
