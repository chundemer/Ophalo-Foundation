import { Share2 } from "lucide-react";

interface NeedsShareBannerProps {
  onOpenShareDrawer: () => void;
}

export function NeedsShareBanner({ onOpenShareDrawer }: NeedsShareBannerProps) {
  return (
    <div className="md:hidden sticky top-0 z-20 bg-amber-500 px-4 py-3 flex items-center justify-between gap-3">
      <p className="text-sm font-semibold text-white">
        Customer page not shared.
      </p>
      <button
        type="button"
        onClick={onOpenShareDrawer}
        className="flex items-center gap-1.5 rounded-md bg-white px-3 py-1.5 text-sm font-medium text-amber-700 hover:bg-amber-50 shrink-0"
      >
        <Share2 className="h-3.5 w-3.5" />
        Share Link
      </button>
    </div>
  );
}
