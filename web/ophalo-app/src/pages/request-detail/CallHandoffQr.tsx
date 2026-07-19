import QRCode from "react-qr-code";
import { RefreshCw } from "lucide-react";
import { useCallHandoff } from "./useCallHandoff";

interface CallHandoffQrProps {
  requestId: string;
  size?: number;
  caption?: string;
}

// GAP-020 / ADR-448: shared desktop call-QR rendering — the QR always encodes the opaque
// handoffUrl minted from POST /keep/requests/{requestId}/call-handoff, never tel:{phone}.
export function CallHandoffQr({ requestId, size = 160, caption }: CallHandoffQrProps) {
  const { handoffUrl, isLoading, error, retry } = useCallHandoff(requestId, true);

  if (isLoading) {
    return (
      <div
        className="flex items-center justify-center"
        style={{ height: size, width: size }}
        role="status"
        aria-label="Preparing call link"
      >
        <RefreshCw className="h-5 w-5 animate-spin text-[var(--ophalo-muted)]" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex flex-col items-center gap-2 text-center" style={{ width: size }}>
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

  return (
    <div className="flex flex-col items-center gap-1.5">
      <div className="bg-white p-2 rounded-lg">
        <QRCode value={handoffUrl} size={size} />
      </div>
      {caption && <p className="text-xs text-[var(--ophalo-muted)] text-center">{caption}</p>}
    </div>
  );
}
