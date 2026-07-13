import { useState } from "react";
import { Copy, ExternalLink, RefreshCw } from "lucide-react";
import { api } from "../../lib/apiClient";

interface SuccessPanelProps {
  requestId: string;
  referenceCode: string;
  pageToken: string;
  publicBaseUrl: string;
  onCaptureAnother: () => void;
  onViewRequest: () => void;
}

export function SuccessPanel({
  requestId,
  referenceCode,
  pageToken,
  publicBaseUrl,
  onCaptureAnother,
  onViewRequest,
}: SuccessPanelProps) {
  const [copied, setCopied] = useState(false);
  const customerPageUrl = `${publicBaseUrl}/keep/r/${pageToken}`;

  async function handleCopyLink() {
    try {
      await navigator.clipboard.writeText(customerPageUrl);
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
        Share the customer page with the customer so they can follow progress.
      </p>

      <div className="flex flex-col gap-2">
        <button
          type="button"
          onClick={handleCopyLink}
          className="w-full flex items-center justify-center gap-2 rounded-md bg-slate-900 px-4 py-2.5 text-sm font-medium text-white hover:bg-slate-700"
        >
          <Copy className="h-4 w-4" />
          {copied ? "Copied!" : "Copy customer page link"}
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
