import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type IntakeStatusResult, ApiError } from "../../lib/apiClient";

export function PublicLinkSection() {
  const publicBaseUrl = import.meta.env.VITE_PUBLIC_BASE_URL as string;
  const queryClient = useQueryClient();
  const { data: intake, isLoading } = useQuery({
    queryKey: ["intake"],
    queryFn: api.getIntake,
    staleTime: 5 * 60 * 1000,
  });

  // shown-once raw-token banner (ensure / replace only)
  const [newIntakeRawUrl, setNewIntakeRawUrl] = useState<string | null>(null);
  const [rawUrlCopied, setRawUrlCopied] = useState(false);

  // durable slug-URL copy feedback
  const [slugCopied, setSlugCopied] = useState(false);

  // ensure / replace / edit state
  const [ensuring, setEnsuring] = useState(false);
  const [replacing, setReplacing] = useState(false);
  const [confirmReplace, setConfirmReplace] = useState(false);
  const [editingName, setEditingName] = useState(false);
  const [nameInput, setNameInput] = useState("");
  const [savingName, setSavingName] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function slugUrl(slug: string) {
    return `${publicBaseUrl}/keep/s/${slug}`;
  }

  async function handleEnsure() {
    setEnsuring(true);
    setError(null);
    setNewIntakeRawUrl(null);
    try {
      const result = await api.ensureIntake();
      queryClient.setQueryData<IntakeStatusResult>(["intake"], {
        hasActiveLink: true,
        publicSlug: result.publicSlug,
        createdAtUtc: null,
      });
      if (result.rawToken) {
        setNewIntakeRawUrl(`${publicBaseUrl}/keep/intake/${result.rawToken}`);
      }
    } catch {
      setError("Something went wrong. Please try again.");
    } finally {
      setEnsuring(false);
    }
  }

  async function handleReplace() {
    setReplacing(true);
    setError(null);
    setConfirmReplace(false);
    setNewIntakeRawUrl(null);
    try {
      const result = await api.replaceIntake();
      queryClient.setQueryData<IntakeStatusResult>(["intake"], {
        hasActiveLink: true,
        publicSlug: result.publicSlug,
        createdAtUtc: null,
      });
      setNewIntakeRawUrl(`${publicBaseUrl}/keep/intake/${result.rawToken}`);
    } catch {
      setError("Something went wrong. Please try again.");
    } finally {
      setReplacing(false);
    }
  }

  async function handleCopyRawUrl() {
    if (!newIntakeRawUrl) return;
    try {
      await navigator.clipboard.writeText(newIntakeRawUrl);
      setRawUrlCopied(true);
      setTimeout(() => setRawUrlCopied(false), 2000);
    } catch {
      // clipboard denied
    }
  }

  async function handleCopySlugUrl() {
    const slug = intake?.publicSlug;
    if (!slug) return;
    try {
      await navigator.clipboard.writeText(slugUrl(slug));
      setSlugCopied(true);
      setTimeout(() => setSlugCopied(false), 2000);
    } catch {
      // clipboard denied
    }
  }

  async function handleSaveName() {
    const name = nameInput.trim();
    if (!name) return;
    setSavingName(true);
    setError(null);
    try {
      const result = await api.updateIntakeLinkName(name);
      queryClient.setQueryData<IntakeStatusResult>(["intake"], (prev) =>
        prev ? { ...prev, publicSlug: result.publicSlug } : prev,
      );
      setEditingName(false);
      setNameInput("");
    } catch (err) {
      if (err instanceof ApiError && err.code === "keep.public_intake.slug_taken") {
        setError("That link name is already in use. Try a different name.");
      } else {
        setError("Something went wrong. Please try again.");
      }
    } finally {
      setSavingName(false);
    }
  }

  const activeSlug = intake?.publicSlug ?? null;

  return (
    <section className="space-y-6">
      <div>
        <h2 className="text-base font-semibold text-slate-900 mb-1">Public link</h2>
        <p className="text-sm text-slate-500">
          Customers use this link to send you a request. Copy it anywhere — your website, email signature, or text messages.
        </p>
      </div>

      {isLoading && <p className="text-sm text-slate-400">Loading…</p>}

      {/* shown-once raw-token banner (after ensure or replace) */}
      {newIntakeRawUrl && (
        <div className="rounded-md border border-emerald-200 bg-emerald-50 p-3 space-y-2">
          <p className="text-xs font-medium text-emerald-800">
            Link created — copy now. This direct URL is shown once.
          </p>
          <p className="text-xs font-mono text-emerald-900 break-all">{newIntakeRawUrl}</p>
          <button
            onClick={() => void handleCopyRawUrl()}
            className="rounded-md bg-emerald-700 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-600"
          >
            {rawUrlCopied ? "Copied!" : "Copy"}
          </button>
        </div>
      )}

      {/* no active link */}
      {!isLoading && intake && !intake.hasActiveLink && (
        <div className="space-y-3">
          <p className="text-sm text-slate-600">No public link yet.</p>
          <button
            onClick={() => void handleEnsure()}
            disabled={ensuring}
            className="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
          >
            {ensuring ? "Creating…" : "Create public link"}
          </button>
        </div>
      )}

      {/* active link — durable copy/open + edit name + replace */}
      {intake?.hasActiveLink && activeSlug && (
        <div className="space-y-4">
          {/* durable slug URL */}
          <div className="rounded-md border border-slate-200 bg-slate-50 p-3">
            <p className="text-xs text-slate-500 mb-1">Your public link</p>
            <p className="text-sm font-mono text-slate-900 break-all mb-2">{slugUrl(activeSlug)}</p>
            <div className="flex gap-2">
              <button
                onClick={() => void handleCopySlugUrl()}
                className="rounded-md bg-slate-900 px-3 py-1.5 text-xs font-medium text-white hover:bg-slate-700"
              >
                {slugCopied ? "Copied!" : "Copy link"}
              </button>
              <a
                href={slugUrl(activeSlug)}
                target="_blank"
                rel="noreferrer"
                className="rounded-md border border-slate-300 px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-100"
              >
                Open ↗
              </a>
            </div>
          </div>

          {/* edit link name */}
          {!editingName ? (
            <button
              onClick={() => { setEditingName(true); setError(null); }}
              className="text-sm text-slate-500 hover:text-slate-800 underline"
            >
              Edit link name
            </button>
          ) : (
            <div className="space-y-2">
              <p className="text-xs text-slate-500">
                Enter your business name or a short label. Previous link names keep working and redirect customers to your current link.
              </p>
              <div className="flex gap-2 items-center flex-wrap">
                <input
                  type="text"
                  value={nameInput}
                  onChange={(e) => setNameInput(e.target.value)}
                  placeholder="e.g. Acme Plumbing"
                  disabled={savingName}
                  className="flex-1 min-w-0 rounded-md border border-slate-300 px-3 py-1.5 text-sm focus:border-slate-500 focus:outline-none disabled:opacity-50"
                  onKeyDown={(e) => { if (e.key === "Enter") void handleSaveName(); }}
                />
                <button
                  onClick={() => void handleSaveName()}
                  disabled={savingName || !nameInput.trim()}
                  className="rounded-md bg-slate-900 px-3 py-1.5 text-xs font-medium text-white hover:bg-slate-700 disabled:opacity-50"
                >
                  {savingName ? "Saving…" : "Save"}
                </button>
                <button
                  onClick={() => { setEditingName(false); setNameInput(""); setError(null); }}
                  disabled={savingName}
                  className="rounded-md border border-slate-300 px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                >
                  Cancel
                </button>
              </div>
            </div>
          )}

          {/* replace link — destructive / exceptional */}
          <div className="pt-1 border-t border-slate-100">
            {!confirmReplace ? (
              <button
                onClick={() => { setConfirmReplace(true); setError(null); }}
                className="text-xs text-slate-400 hover:text-slate-700 underline"
              >
                Replace link (breaks old shared links)
              </button>
            ) : (
              <div className="space-y-2 rounded-md border border-amber-200 bg-amber-50 p-3">
                <p className="text-sm font-medium text-amber-800">Replace this link?</p>
                <p className="text-xs text-amber-700">
                  All previously shared links — including any you emailed or posted — will stop working immediately. This cannot be undone.
                </p>
                <div className="flex gap-2 pt-1">
                  <button
                    onClick={() => void handleReplace()}
                    disabled={replacing}
                    className="rounded-md bg-amber-800 px-3 py-1.5 text-xs font-medium text-white hover:bg-amber-700 disabled:opacity-50"
                  >
                    {replacing ? "Replacing…" : "Yes, replace link"}
                  </button>
                  <button
                    onClick={() => setConfirmReplace(false)}
                    className="rounded-md border border-slate-300 px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
                  >
                    Cancel
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {error && <p className="text-sm text-red-600">{error}</p>}

      {/* phone-sized customer preview */}
      {intake?.hasActiveLink && (
        <div className="pt-2">
          <p className="text-xs font-medium text-slate-400 uppercase tracking-wide mb-2">Customer preview</p>
          <div className="mx-auto max-w-[300px] rounded-2xl border-2 border-slate-200 bg-white shadow-md overflow-hidden">
            <div className="bg-slate-800 h-5 flex items-center justify-center">
              <div className="w-10 h-1 rounded-full bg-slate-600" />
            </div>
            <div className="px-4 py-4 space-y-3">
              <div>
                <p className="text-sm font-semibold text-slate-900">Submit a request</p>
                <p className="text-[11px] text-slate-500 mt-0.5 leading-snug">
                  Fill out the form and the business will follow up with you.
                </p>
              </div>
              <div className="space-y-2">
                {["Your name", "Phone number", "Email (optional)"].map((label) => (
                  <div key={label}>
                    <p className="text-[9px] text-slate-400 mb-0.5">{label}</p>
                    <div className="h-5 rounded border border-slate-200 bg-slate-50" />
                  </div>
                ))}
                <div>
                  <p className="text-[9px] text-slate-400 mb-0.5">What do you need help with?</p>
                  <div className="h-10 rounded border border-slate-200 bg-slate-50" />
                </div>
              </div>
              <div className="h-6 rounded bg-slate-800" />
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
