import { useState } from "react";
import { Share2, Copy, Check } from "lucide-react";
import { api, type ShareIntentMethod } from "../lib/apiClient";
import { ApiError } from "../lib/apiClient";

interface NeedsShareBannerProps {
  requestId: string;
  pageToken: string;
  onCleared: () => void;
}

export function NeedsShareBanner({ requestId, pageToken, onCleared }: NeedsShareBannerProps) {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const publicBaseUrl = import.meta.env.VITE_PUBLIC_BASE_URL as string;
  const trackerUrl = `${publicBaseUrl}/keep/r/${pageToken}`;
  const canNativeShare = typeof navigator !== "undefined" && typeof navigator.share === "function";

  async function submit(method: ShareIntentMethod, browserAction?: () => Promise<void>) {
    if (isSubmitting) return;
    setIsSubmitting(true);
    setError(null);
    try {
      if (browserAction) await browserAction();
      await api.recordShareIntent(requestId, method);
      onCleared();
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") {
        // user cancelled native share — don't record intent
      } else if (e instanceof ApiError) {
        setError("Could not record share. Try again.");
      } else {
        setError("Could not complete share. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  async function handleCopyLink() {
    await submit("copy_link", async () => {
      await navigator.clipboard.writeText(trackerUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }

  async function handleNativeShare() {
    await submit("native_share", async () => {
      await navigator.share({ url: trackerUrl, title: "Track Your Request" });
    });
  }

  async function handleMarkShared() {
    await submit("manual_mark_shared");
  }

  return (
    <div className="md:hidden sticky top-0 z-20 bg-amber-500 px-4 py-3">
      <p className="text-sm font-semibold text-white mb-2">
        Customer tracker link not shared.
      </p>
      <div className="flex items-center gap-2">
        {canNativeShare ? (
          <button
            type="button"
            onClick={() => void handleNativeShare()}
            disabled={isSubmitting}
            className="flex items-center gap-1.5 rounded-md bg-white px-3 py-1.5 text-sm font-medium text-amber-700 hover:bg-amber-50 disabled:opacity-60"
          >
            <Share2 className="h-3.5 w-3.5" />
            Share Link
          </button>
        ) : (
          <button
            type="button"
            onClick={() => void handleCopyLink()}
            disabled={isSubmitting}
            className="flex items-center gap-1.5 rounded-md bg-white px-3 py-1.5 text-sm font-medium text-amber-700 hover:bg-amber-50 disabled:opacity-60"
          >
            {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
            {copied ? "Copied!" : "Copy Link"}
          </button>
        )}
        <button
          type="button"
          onClick={() => void handleMarkShared()}
          disabled={isSubmitting}
          className="text-sm font-medium text-amber-100 hover:text-white disabled:opacity-60"
        >
          Mark as shared
        </button>
      </div>
      {error && <p className="mt-1.5 text-xs text-amber-100">{error}</p>}
    </div>
  );
}
