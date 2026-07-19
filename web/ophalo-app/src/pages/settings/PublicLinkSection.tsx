import { useEffect, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type IntakeStatusResult, ApiError } from "../../lib/apiClient";

const REPLACE_CONFIRMATION_VALUE = "REPLACE";

// Simple local fallback treatment for the preview only — not the public page.
function businessInitials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "?";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}

interface ReplaceLinkDialogProps {
  replacing: boolean;
  error: string | null;
  onConfirm: (confirmation: string) => void;
  onClose: () => void;
  triggerRef: React.RefObject<HTMLButtonElement | null>;
}

// Accessible destructive-action dialog (GAP-036b): requires an exact, case-sensitive
// "REPLACE" value before the confirm action is enabled. The server independently
// validates the same value — this is a UX gate, not the security boundary.
function ReplaceLinkDialog({ replacing, error, onConfirm, onClose, triggerRef }: ReplaceLinkDialogProps) {
  const [confirmation, setConfirmation] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  // Read via refs inside the keydown handler so the listener can be installed once
  // (on mount) while still seeing the latest in-flight/close state on every keystroke.
  const replacingRef = useRef(replacing);
  replacingRef.current = replacing;
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  useEffect(() => {
    inputRef.current?.focus();

    function getFocusable(): HTMLElement[] {
      if (!panelRef.current) return [];
      return Array.from(
        panelRef.current.querySelectorAll<HTMLElement>(
          'button:not([disabled]), input:not([disabled]), [href], [tabindex]:not([tabindex="-1"])',
        ),
      );
    }

    function onKey(e: KeyboardEvent) {
      // Block all dismissal paths while a replace request is in flight.
      if (e.key === "Escape") {
        if (!replacingRef.current) onCloseRef.current();
        return;
      }

      // Contain Tab focus within the dialog — aria-modal alone does not do this.
      if (e.key === "Tab") {
        const focusable = getFocusable();

        // While replacing, every control is disabled — nothing to cycle through.
        // Keep focus pinned to the panel itself rather than letting Tab fall through
        // to controls behind the dialog.
        if (focusable.length === 0) {
          e.preventDefault();
          panelRef.current?.focus();
          return;
        }

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const active = document.activeElement;
        const withinPanel = active instanceof Node && panelRef.current?.contains(active);

        if (e.shiftKey) {
          if (!withinPanel || active === first) {
            e.preventDefault();
            last.focus();
          }
        } else {
          if (!withinPanel || active === last) {
            e.preventDefault();
            first.focus();
          }
        }
      }
    }

    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("keydown", onKey);
      triggerRef.current?.focus();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Disabling the focused control (entering the replacing state) makes the browser blur
  // it to the document body, which would otherwise let focus fall outside the dialog
  // before the user ever presses Tab. Reclaim it onto the panel itself.
  useEffect(() => {
    if (!replacing) return;
    const active = document.activeElement;
    const withinPanel = active instanceof Node && panelRef.current?.contains(active);
    if (!withinPanel) panelRef.current?.focus();
  }, [replacing]);

  const canConfirm = confirmation === REPLACE_CONFIRMATION_VALUE && !replacing;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="replace-link-dialog-title"
    >
      <div
        data-testid="replace-link-dialog-backdrop"
        className="absolute inset-0 bg-black/50"
        onClick={() => { if (!replacing) onClose(); }}
      />
      <div
        ref={panelRef}
        tabIndex={-1}
        className="relative w-full max-w-sm rounded-lg bg-white p-4 shadow-xl space-y-3 focus:outline-none"
      >
        <h3 id="replace-link-dialog-title" className="text-sm font-semibold text-amber-800">
          Replace this link?
        </h3>
        <p className="text-xs text-amber-700">
          Your current public link and every previously shared link or link name — including any
          you emailed or posted — will stop working immediately. This cannot be undone.
        </p>
        <div className="space-y-1">
          <label htmlFor="replace-link-confirmation" className="text-xs font-medium text-slate-600">
            Type REPLACE to confirm
          </label>
          <input
            ref={inputRef}
            id="replace-link-confirmation"
            type="text"
            value={confirmation}
            onChange={(e) => setConfirmation(e.target.value)}
            disabled={replacing}
            autoComplete="off"
            className="w-full rounded-md border border-slate-300 px-3 py-1.5 text-sm focus:border-slate-500 focus:outline-none disabled:opacity-50"
            onKeyDown={(e) => {
              if (e.key === "Enter" && canConfirm) onConfirm(confirmation);
            }}
          />
        </div>
        {error && <p className="text-xs text-red-600">{error}</p>}
        <div className="flex gap-2 pt-1">
          <button
            onClick={() => onConfirm(confirmation)}
            disabled={!canConfirm}
            className="rounded-md bg-amber-800 px-3 py-1.5 text-xs font-medium text-white hover:bg-amber-700 disabled:opacity-50"
          >
            {replacing ? "Replacing…" : "Yes, replace link"}
          </button>
          <button
            onClick={onClose}
            disabled={replacing}
            className="rounded-md border border-slate-300 px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
          >
            Cancel
          </button>
        </div>
      </div>
    </div>
  );
}

interface PublicLinkSectionProps {
  businessName: string;
  logoUrl: string;
}

export function PublicLinkSection({ businessName, logoUrl }: PublicLinkSectionProps) {
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

  // preview logo — tracks the specific drafted URL that failed to load, so the
  // initials fallback shows for a broken URL and clears itself once the draft changes
  const [failedLogoUrl, setFailedLogoUrl] = useState<string | null>(null);

  // ensure / replace / edit state
  const [ensuring, setEnsuring] = useState(false);
  const [replacing, setReplacing] = useState(false);
  const [showReplaceDialog, setShowReplaceDialog] = useState(false);
  const [editingName, setEditingName] = useState(false);
  const [nameInput, setNameInput] = useState("");
  const [savingName, setSavingName] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [replaceError, setReplaceError] = useState<string | null>(null);
  const replaceTriggerRef = useRef<HTMLButtonElement>(null);

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

  async function handleReplace(confirmation: string) {
    setReplacing(true);
    setReplaceError(null);
    try {
      const result = await api.replaceIntake(confirmation);
      setShowReplaceDialog(false);
      setNewIntakeRawUrl(null);
      queryClient.setQueryData<IntakeStatusResult>(["intake"], {
        hasActiveLink: true,
        publicSlug: result.publicSlug,
        createdAtUtc: null,
      });
      setNewIntakeRawUrl(`${publicBaseUrl}/keep/intake/${result.rawToken}`);
    } catch (err) {
      if (err instanceof ApiError && err.code === "KeepPublicIntakeLink.ReplaceConfirmationInvalid") {
        setReplaceError("Type REPLACE to confirm this action.");
      } else {
        setReplaceError("Something went wrong. Please try again.");
      }
    } finally {
      setReplacing(false);
    }
  }

  function closeReplaceDialog() {
    setShowReplaceDialog(false);
    setReplaceError(null);
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
            <button
              ref={replaceTriggerRef}
              onClick={() => setShowReplaceDialog(true)}
              className="text-xs text-slate-400 hover:text-slate-700 underline"
            >
              Replace link (breaks old shared links)
            </button>
          </div>
        </div>
      )}

      {showReplaceDialog && (
        <ReplaceLinkDialog
          replacing={replacing}
          error={replaceError}
          onConfirm={(confirmation) => void handleReplace(confirmation)}
          onClose={closeReplaceDialog}
          triggerRef={replaceTriggerRef}
        />
      )}

      {error && <p className="text-sm text-red-600">{error}</p>}

      {/* phone-sized customer preview — reflects unsaved draft, never fetched from the live public page */}
      {intake?.hasActiveLink && (
        <div className="pt-2">
          <div className="flex items-center justify-between mb-2">
            <p className="text-xs font-medium text-slate-400 uppercase tracking-wide">Customer preview</p>
            <p className="text-[11px] text-slate-400">Unsaved changes shown live — save to publish</p>
          </div>
          <div className="mx-auto max-w-[300px] rounded-2xl border-2 border-slate-200 bg-white shadow-md overflow-hidden">
            <div className="bg-slate-800 h-5 flex items-center justify-center">
              <div className="w-10 h-1 rounded-full bg-slate-600" />
            </div>
            <div className="px-4 py-4 space-y-3">
              <div className="flex items-center gap-2">
                {logoUrl.trim() && logoUrl.trim() !== failedLogoUrl ? (
                  <img
                    src={logoUrl.trim()}
                    alt={`${businessName.trim() || "Business"} logo`}
                    className="h-8 w-8 rounded-full object-cover border border-slate-200 shrink-0"
                    onError={() => setFailedLogoUrl(logoUrl.trim())}
                  />
                ) : (
                  <div className="h-8 w-8 rounded-full bg-slate-200 text-slate-600 text-[10px] font-semibold flex items-center justify-center shrink-0">
                    {businessInitials(businessName)}
                  </div>
                )}
                <p className="text-xs font-medium text-slate-700 truncate">{businessName.trim() || "Your business"}</p>
              </div>
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
