import { Link2, PenLine } from "lucide-react";

interface ChoicePanelProps {
  onChooseCustomerLink: () => void;
  onChooseRecordMyself: () => void;
}

// BL142 Session 3: an explicit decision point rather than dropping Owner/Admin
// straight into the text-a-link handoff panel. Each card states what happens next
// so "share a link" is never implied to create a request by itself.
export function ChoicePanel({ onChooseCustomerLink, onChooseRecordMyself }: ChoicePanelProps) {
  return (
    <div className="flex flex-col gap-3">
      <button
        type="button"
        onClick={onChooseCustomerLink}
        className="flex items-start gap-3 rounded-md border border-slate-200 p-4 text-left hover:border-slate-400 hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-slate-500"
      >
        <Link2 className="h-5 w-5 shrink-0 text-slate-500" />
        <span>
          <span className="block text-sm font-medium text-slate-900">
            Let the customer submit it
          </span>
          <span className="mt-0.5 block text-xs text-slate-500">
            Share or text the public link. No request is created until the customer submits the
            form.
          </span>
        </span>
      </button>

      <button
        type="button"
        onClick={onChooseRecordMyself}
        className="flex items-start gap-3 rounded-md border border-slate-200 p-4 text-left hover:border-slate-400 hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-slate-500"
      >
        <PenLine className="h-5 w-5 shrink-0 text-slate-500" />
        <span>
          <span className="block text-sm font-medium text-slate-900">Record it yourself</span>
          <span className="mt-0.5 block text-xs text-slate-500">
            For a call, voicemail, walk-in, text, or email already received. Creates the request
            now.
          </span>
        </span>
      </button>
    </div>
  );
}
