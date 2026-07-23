import type { PhoneLookupResult, PhoneLookupActiveRequest } from "../../lib/apiClient";
import { formatStatus, formatNaPhone } from "./utils";

interface LookupResultProps {
  lookup: PhoneLookupResult;
  lockedPhone: string;
  onProceed: () => void;
  onNavigateToRequest: (requestId: string) => void;
  onBack: () => void;
}

export function LookupResultView({
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
          <p className="text-sm text-slate-500">{formatNaPhone(customer.phone)}</p>
          {customer.email && <p className="text-xs text-slate-400">{customer.email}</p>}
        </div>
      ) : (
        <div>
          <p className="text-sm text-slate-500">No customer found for <span className="font-mono font-medium">{formatNaPhone(lockedPhone)}</span>.</p>
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
