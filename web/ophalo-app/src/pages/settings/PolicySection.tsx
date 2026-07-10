import { useState, useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api, type KeepSetupResult, ApiError } from "../../lib/apiClient";

interface PolicySectionProps {
  setup: KeepSetupResult;
}

export function PolicySection({ setup }: PolicySectionProps) {
  const queryClient = useQueryClient();
  const p = setup.responsePolicy;

  const [firstResponse, setFirstResponse] = useState(String(p.firstResponseTargetMinutes));
  const [standardResponse, setStandardResponse] = useState(String(p.standardResponseTargetMinutes));
  const [priorityResponse, setPriorityResponse] = useState(String(p.priorityResponseTargetMinutes));
  const [statusCheck, setStatusCheck] = useState(String(p.statusCheckThresholdDays));
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    setFirstResponse(String(p.firstResponseTargetMinutes));
    setStandardResponse(String(p.standardResponseTargetMinutes));
    setPriorityResponse(String(p.priorityResponseTargetMinutes));
    setStatusCheck(String(p.statusCheckThresholdDays));
  }, [p.firstResponseTargetMinutes, p.standardResponseTargetMinutes, p.priorityResponseTargetMinutes, p.statusCheckThresholdDays]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (submitting) return;
    setSubmitting(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await api.updatePolicy({
        firstResponseTargetMinutes: Number(firstResponse),
        standardResponseTargetMinutes: Number(standardResponse),
        priorityResponseTargetMinutes: Number(priorityResponse),
        statusCheckThresholdDays: Number(statusCheck),
      });
      queryClient.setQueryData(["setup"], updated);
      setSaved(true);
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else {
        setError("Something went wrong. Please try again.");
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section>
      <h2 className="text-base font-semibold text-slate-900 mb-1.5">Response Policy</h2>
      <p className="text-sm text-slate-500 mb-4">
        Set the response targets your team works toward. The defaults work well for most service businesses — come back and adjust once you've seen how requests flow.
      </p>
      <form onSubmit={handleSubmit} className="space-y-5 max-w-lg">
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-0.5">
            First response <span className="font-normal text-slate-500">(minutes)</span>
          </label>
          <p className="text-xs text-slate-400 mb-1.5">How soon after a new request arrives should your team send an initial reply.</p>
          <input
            type="number"
            min={1}
            value={firstResponse}
            onChange={(e) => { setFirstResponse(e.target.value); setSaved(false); }}
            required
            className="w-36 rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-0.5">
            Standard response <span className="font-normal text-slate-500">(minutes)</span>
          </label>
          <p className="text-xs text-slate-400 mb-1.5">Your normal target for resolving or meaningfully advancing a typical open request.</p>
          <input
            type="number"
            min={1}
            value={standardResponse}
            onChange={(e) => { setStandardResponse(e.target.value); setSaved(false); }}
            required
            className="w-36 rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-0.5">
            Priority response <span className="font-normal text-slate-500">(minutes)</span>
          </label>
          <p className="text-xs text-slate-400 mb-1.5">Faster target for urgent requests that need quicker attention than standard.</p>
          <input
            type="number"
            min={1}
            value={priorityResponse}
            onChange={(e) => { setPriorityResponse(e.target.value); setSaved(false); }}
            required
            className="w-36 rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-0.5">
            Status check <span className="font-normal text-slate-500">(days)</span>
          </label>
          <p className="text-xs text-slate-400 mb-1.5">Flag open requests that haven't had any update in this many days so nothing goes stale.</p>
          <input
            type="number"
            min={1}
            value={statusCheck}
            onChange={(e) => { setStatusCheck(e.target.value); setSaved(false); }}
            required
            className="w-36 rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>

        {error && (
          <p className="text-sm text-red-600">{error}</p>
        )}

        <div className="flex items-center gap-3">
          <button
            type="submit"
            disabled={submitting}
            className="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
          >
            {submitting ? "Saving…" : "Save policy"}
          </button>
          {saved && <span className="text-sm text-green-700">Saved.</span>}
        </div>
      </form>
    </section>
  );
}
