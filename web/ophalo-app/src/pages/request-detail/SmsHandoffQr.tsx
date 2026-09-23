import { useCallback } from "react";
import { RefreshCw } from "lucide-react";
import QRCode from "react-qr-code";
import { api } from "../../lib/apiClient";
import { useHandoffMint } from "./useHandoffMint";

// Maintainability review item 6: split out of RequestDetail.tsx, mirroring the already-extracted
// CallHandoffQr / useCallHandoff pattern for the SMS (rather than call) handoff flow. No behavior
// change — previously a private (unexported) helper used only by LogContactModal.

interface SmsHandoffQrProps {
  requestId: string;
  message: string;
}

export function SmsHandoffQr({ requestId, message }: SmsHandoffQrProps) {
  const mint = useCallback(() => api.createSmsHandoff(requestId, message), [requestId, message]);
  const { handoffUrl, isLoading, error, retry } = useHandoffMint(
    true,
    mint,
    "Could not create text link. Try again.",
  );

  if (isLoading) {
    return (
      <div
        className="flex items-center justify-center"
        style={{ height: 108, width: 108 }}
        role="status"
        aria-label="Preparing text link"
      >
        <RefreshCw className="h-5 w-5 animate-spin text-[var(--ophalo-muted)]" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex flex-col items-center gap-2 text-center" style={{ width: 108 }}>
        <p className="text-xs text-[var(--ophalo-danger)]">{error}</p>
        <button
          type="button"
          onClick={() => void retry()}
          className="text-xs font-medium text-[var(--keep-accent)] hover:underline"
        >
          Try again
        </button>
      </div>
    );
  }

  if (!handoffUrl) return null;
  return <div className="bg-white p-2 rounded-lg"><QRCode value={handoffUrl} size={108} /></div>;
}
