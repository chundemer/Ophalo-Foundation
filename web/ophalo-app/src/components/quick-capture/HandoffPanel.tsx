import { useState } from "react";
import { useQuery, useMutation } from "@tanstack/react-query";
import QRCode from "react-qr-code";
import { MessageSquare } from "lucide-react";
import { api, ApiError } from "../../lib/apiClient";
import { normalizeNaPhoneInput, formatNaPhone } from "./utils";

interface HandoffPanelProps {
  onEnterForCustomer: () => void;
  onNavigateSettings: () => void;
}

export function HandoffPanel({ onEnterForCustomer, onNavigateSettings }: HandoffPanelProps) {
  const [raw, setRaw] = useState("");

  const { data: intake, isLoading: intakeLoading } = useQuery({
    queryKey: ["intake"],
    queryFn: api.getIntake,
    staleTime: 5 * 60 * 1000,
  });

  const { mutate: sendHandoff, data: handoff, isPending, error, reset } = useMutation({
    mutationFn: (phone: string) => api.createIntakeSmsHandoff(phone),
  });

  const apiError = error instanceof ApiError ? error : null;
  const is403 = apiError?.status === 403;
  const is402 = apiError?.status === 402;
  const isValidationError = apiError?.status === 400 || apiError?.status === 422;

  function handleChange(e: React.ChangeEvent<HTMLInputElement>) {
    setRaw(normalizeNaPhoneInput(e.target.value));
    if (handoff || error) reset();
  }

  function handleSubmit() {
    if (raw.length !== 10) return;
    sendHandoff(raw);
  }

  const isIos = /iphone|ipad|ipod/i.test(navigator.userAgent.toLowerCase());
  const smsUri = handoff
    ? `sms:${handoff.customerPhone}${isIos ? "&" : "?"}body=${encodeURIComponent(handoff.messageBody)}`
    : null;

  if (!intakeLoading && intake && !intake.hasActiveLink) {
    return (
      <div className="flex flex-col gap-4">
        <p className="text-sm text-slate-600">
          Set up your public link before sending a text handoff to a customer.
        </p>
        <button
          type="button"
          onClick={onNavigateSettings}
          className="self-start rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
        >
          Go to Settings
        </button>
        <div className="pt-3 border-t border-slate-100">
          <button
            type="button"
            onClick={onEnterForCustomer}
            className="text-sm text-slate-500 hover:text-slate-700 underline"
          >
            Enter request for customer
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <div>
        <label htmlFor="handoff-phone" className="block text-sm font-medium text-slate-700 mb-1">
          Customer&rsquo;s mobile number for a text link
        </label>
        <p className="text-xs text-slate-500 mb-2">
          Confirm with the customer that this number can receive texts.
        </p>
        <div className="flex items-center gap-2">
          <input
            id="handoff-phone"
            type="tel"
            inputMode="numeric"
            placeholder="(555) 555-5555"
            value={formatNaPhone(raw)}
            onChange={handleChange}
            disabled={isPending}
            className="block w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-slate-500 focus:outline-none focus:ring-1 focus:ring-slate-500 disabled:bg-slate-50 disabled:text-slate-400"
          />
          <button
            type="button"
            onClick={handleSubmit}
            disabled={isPending || raw.length !== 10}
            className="shrink-0 rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-40"
          >
            {isPending ? "Preparing…" : "Prepare text"}
          </button>
        </div>
        {raw.length > 0 && raw.length !== 10 && (
          <p className="mt-1 text-xs text-red-600">Please enter a 10-digit phone number.</p>
        )}
      </div>

      {is403 && (
        <p className="text-sm text-red-600">You do not have permission to send a text link.</p>
      )}
      {is402 && (
        <p className="text-sm text-amber-700">Account access required. Contact your account owner.</p>
      )}
      {isValidationError && (
        <p className="text-sm text-red-600">Check the phone number and try again.</p>
      )}
      {apiError && !is403 && !is402 && !isValidationError && (
        <p className="text-sm text-red-600">Could not create text link. Try again.</p>
      )}

      {handoff && (
        <div className="flex flex-col gap-3 rounded-md border border-slate-200 bg-slate-50 p-4">
          <div className="hidden md:flex flex-col items-center gap-2">
            <div className="rounded-lg bg-white p-2 shadow-sm">
              <QRCode value={handoff.handoffUrl} size={140} />
            </div>
            <p className="text-xs text-slate-500 text-center">
              Scan with your phone to send a pre-addressed text
            </p>
          </div>

          {smsUri && (
            <a
              href={smsUri}
              className="md:hidden w-full flex items-center justify-center gap-2 rounded-md bg-slate-900 px-4 py-2.5 text-sm font-medium text-white hover:bg-slate-700"
            >
              <MessageSquare className="h-4 w-4" />
              Open Text Message
            </a>
          )}
        </div>
      )}

      <div className="pt-2 border-t border-slate-100">
        <button
          type="button"
          onClick={onEnterForCustomer}
          className="text-sm text-slate-500 hover:text-slate-700 underline"
        >
          Enter request for customer
        </button>
      </div>
    </div>
  );
}
