import type { PhoneLookupResult, PhoneLookupActiveRequest } from "../../lib/apiClient";
import { formatNaPhone } from "./utils";
import { statusLabel, statusBadgeVariant } from "../../lib/requestStatus";
import { KeepBadge } from "../keep/KeepBadge";

interface LookupResultProps {
  lookup: PhoneLookupResult;
  lockedPhone: string;
  onProceed: () => void;
  onUseExistingCustomer: (candidateCustomerId: string) => void;
  onCreateAsNew: () => void;
  onNavigateToRequest: (requestId: string) => void;
  onBack: () => void;
}

export function LookupResultView({
  lookup,
  lockedPhone,
  onProceed,
  onUseExistingCustomer,
  onCreateAsNew,
  onNavigateToRequest,
  onBack,
}: LookupResultProps) {
  const { customer, activeRequests, hasMoreActiveRequests, possibleCustomer } = lookup;

  // Exact-match and possible-existing-customer results are mutually exclusive per ADR-492 — never
  // render both for the same lookup.
  if (!customer && possibleCustomer) {
    return (
      <div className="flex flex-col gap-4">
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-amber-600 mb-1">
            Possible existing customer
          </p>
          <p className="text-sm font-medium text-slate-800">{possibleCustomer.name}</p>
          <p className="text-sm text-slate-500">{formatNaPhone(possibleCustomer.phone)}</p>
          {possibleCustomer.email && <p className="text-xs text-slate-400">{possibleCustomer.email}</p>}
        </div>

        {possibleCustomer.activeRequests.length > 0 ? (
          <div>
            <p className="text-xs font-medium uppercase tracking-wide text-slate-400 mb-2">Active requests</p>
            <ul className="space-y-2">
              {possibleCustomer.activeRequests.map((r) => (
                <ActiveRequestCard
                  key={r.requestId}
                  request={r}
                  onNavigate={() => onNavigateToRequest(r.requestId)}
                />
              ))}
            </ul>
            {possibleCustomer.hasMoreActiveRequests && (
              <p className="mt-2 text-xs text-slate-400">
                More active work exists in the Command Center.
              </p>
            )}
          </div>
        ) : (
          <p className="text-sm text-slate-500">
            No active work right now, but a past request used this phone number.
          </p>
        )}

        <div className="flex flex-col gap-2 pt-2 border-t border-slate-100">
          <button
            type="button"
            onClick={() => onUseExistingCustomer(possibleCustomer.candidateCustomerId)}
            className="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
          >
            Use existing customer details
          </button>
          <button
            type="button"
            onClick={onCreateAsNew}
            className="rounded-md border border-slate-200 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
          >
            Create as new customer
          </button>
          <button
            type="button"
            onClick={onBack}
            className="self-start text-sm text-slate-500 hover:text-slate-700 mt-1"
          >
            ← Back
          </button>
        </div>
      </div>
    );
  }

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
          <KeepBadge variant={statusBadgeVariant(request.status)}>{statusLabel(request.status)}</KeepBadge>
        </div>
        <p className="mt-1 text-sm text-slate-700 line-clamp-2">{request.description}</p>
      </button>
    </li>
  );
}
