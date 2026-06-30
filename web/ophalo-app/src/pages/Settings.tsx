import { useState, useEffect } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  api,
  type AccountRole,
  type KeepSetupResult,
  type IntakeStatusResult,
  type MemberItem,
  ApiError,
} from "../lib/apiClient";

// ─── Timezone selector ───────────────────────────────────────────────────────

const COMMON_TIMEZONES = [
  "Australia/Sydney",
  "Australia/Melbourne",
  "Australia/Brisbane",
  "Australia/Perth",
  "Australia/Adelaide",
  "Australia/Darwin",
  "Australia/Hobart",
  "Pacific/Auckland",
  "America/New_York",
  "America/Chicago",
  "America/Denver",
  "America/Los_Angeles",
  "America/Toronto",
  "America/Vancouver",
  "Europe/London",
  "Europe/Paris",
  "Europe/Berlin",
  "Europe/Amsterdam",
  "Asia/Singapore",
  "Asia/Tokyo",
  "Asia/Hong_Kong",
  "Asia/Dubai",
];

// ─── Company section ─────────────────────────────────────────────────────────

interface CompanySectionProps {
  setup: KeepSetupResult;
}

function CompanySection({ setup }: CompanySectionProps) {
  const queryClient = useQueryClient();

  const [businessName, setBusinessName] = useState(setup.businessName);
  const [timeZone, setTimeZone] = useState(setup.timeZone);
  const [phone, setPhone] = useState(setup.customerFacingPhone ?? "");
  const [email, setEmail] = useState(setup.customerFacingEmail ?? "");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    setBusinessName(setup.businessName);
    setTimeZone(setup.timeZone);
    setPhone(setup.customerFacingPhone ?? "");
    setEmail(setup.customerFacingEmail ?? "");
  }, [setup]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (submitting) return;
    setSubmitting(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await api.updateProfile({
        businessName: businessName.trim(),
        timeZone,
        customerFacingPhone: phone.trim() || null,
        customerFacingEmail: email.trim() || null,
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

  const knownTz = COMMON_TIMEZONES.includes(timeZone);

  return (
    <section>
      <h2 className="text-base font-semibold text-slate-900 mb-4">Company</h2>
      <form onSubmit={handleSubmit} className="space-y-4 max-w-lg">
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">
            Business name
          </label>
          <input
            type="text"
            value={businessName}
            onChange={(e) => { setBusinessName(e.target.value); setSaved(false); }}
            required
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">
            Timezone
          </label>
          <select
            value={knownTz ? timeZone : ""}
            onChange={(e) => { setTimeZone(e.target.value); setSaved(false); }}
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          >
            {!knownTz && (
              <option value="" disabled>
                {timeZone} (custom)
              </option>
            )}
            {COMMON_TIMEZONES.map((tz) => (
              <option key={tz} value={tz}>
                {tz}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">
            Customer-facing phone
          </label>
          <input
            type="tel"
            value={phone}
            onChange={(e) => { setPhone(e.target.value); setSaved(false); }}
            placeholder="Optional"
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">
            Customer-facing email
          </label>
          <input
            type="email"
            value={email}
            onChange={(e) => { setEmail(e.target.value); setSaved(false); }}
            placeholder="Optional"
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
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
            {submitting ? "Saving…" : "Save company"}
          </button>
          {saved && <span className="text-sm text-green-700">Saved.</span>}
        </div>
      </form>
    </section>
  );
}

// ─── Response policy section ──────────────────────────────────────────────────

interface PolicySectionProps {
  setup: KeepSetupResult;
}

function PolicySection({ setup }: PolicySectionProps) {
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
      <h2 className="text-base font-semibold text-slate-900 mb-4">Response Policy</h2>
      <form onSubmit={handleSubmit} className="space-y-4 max-w-lg">
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">
              First response target (minutes)
            </label>
            <input
              type="number"
              min={0}
              value={firstResponse}
              onChange={(e) => { setFirstResponse(e.target.value); setSaved(false); }}
              required
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">
              Standard response target (minutes)
            </label>
            <input
              type="number"
              min={0}
              value={standardResponse}
              onChange={(e) => { setStandardResponse(e.target.value); setSaved(false); }}
              required
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">
              Priority response target (minutes)
            </label>
            <input
              type="number"
              min={0}
              value={priorityResponse}
              onChange={(e) => { setPriorityResponse(e.target.value); setSaved(false); }}
              required
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">
              Status-check threshold (days)
            </label>
            <input
              type="number"
              min={0}
              value={statusCheck}
              onChange={(e) => { setStatusCheck(e.target.value); setSaved(false); }}
              required
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
            />
          </div>
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

// ─── Shared helpers ───────────────────────────────────────────────────────────

function roleLabel(role: string): string {
  switch (role) {
    case "owner": return "Owner";
    case "admin": return "Admin";
    case "operator": return "Operator";
    case "viewer": return "Viewer";
    default: return role;
  }
}

function statusLabel(status: string): string {
  switch (status) {
    case "active": return "Active";
    case "invited": return "Invited";
    case "suspended": return "Suspended";
    case "removed": return "Removed";
    default: return status;
  }
}

function memberErrorMsg(code: string | undefined, extensions?: Record<string, unknown>): string {
  switch (code) {
    case "Invite.SeatLimitReached":
      return "Your plan's team limit has been reached. Contact support to add more seats.";
    case "Member.SeatLimitReached":
      return "Team seat limit reached. Suspend or remove another team member to free a seat.";
    case "Member.PreviouslyRemoved": {
      const sa = extensions?.["suggestedAction"] as string | undefined;
      if (sa === "reactivate")
        return "This person was previously on your team. Reactivate them from the team list to restore access.";
      if (sa === "resend_invite")
        return "This person was previously invited. Resend their invite to restore access.";
      return "This person was previously a team member.";
    }
    case "Invite.AlreadyActive":
      return "This person already has team access.";
    case "Member.CannotModifySelf":
      return "You cannot change your own team access here.";
    case "Member.PrimaryOwnerProtected":
      return "The primary owner cannot be changed from this screen.";
    case "Member.CannotModifyOwner":
      return "Only an Owner can manage another Owner.";
    case "Member.LastOwner":
      return "At least one active Owner must remain.";
    case "Member.OwnerLimitReached":
      return "This account can have at most two Owners.";
    default:
      return "Something went wrong. Please try again.";
  }
}

// ─── Intake link section ──────────────────────────────────────────────────────

function IntakeSection() {
  const queryClient = useQueryClient();
  const { data: intake, isLoading } = useQuery({
    queryKey: ["intake"],
    queryFn: api.getIntake,
    staleTime: 5 * 60 * 1000,
  });

  const [ensuring, setEnsuring] = useState(false);
  const [replacing, setReplacing] = useState(false);
  const [confirmReplace, setConfirmReplace] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleEnsure() {
    setEnsuring(true);
    setError(null);
    try {
      const result = await api.ensureIntake();
      queryClient.setQueryData<IntakeStatusResult>(["intake"], {
        hasActiveLink: true,
        publicSlug: result.publicSlug,
        createdAtUtc: null,
      });
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
    try {
      const result = await api.replaceIntake();
      queryClient.setQueryData<IntakeStatusResult>(["intake"], {
        hasActiveLink: true,
        publicSlug: result.publicSlug,
        createdAtUtc: null,
      });
    } catch {
      setError("Something went wrong. Please try again.");
    } finally {
      setReplacing(false);
    }
  }

  return (
    <section>
      <h2 className="text-base font-semibold text-slate-900 mb-4">Intake link</h2>
      {isLoading && <p className="text-sm text-slate-400">Loading…</p>}
      {intake && !intake.hasActiveLink && (
        <div className="space-y-3">
          <p className="text-sm text-slate-600">No active intake link. Create one to allow customers to submit requests.</p>
          <button
            onClick={handleEnsure}
            disabled={ensuring}
            className="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
          >
            {ensuring ? "Creating…" : "Create intake link"}
          </button>
        </div>
      )}
      {intake?.hasActiveLink && (
        <div className="space-y-3">
          <div>
            <p className="text-sm text-slate-500 mb-0.5">Active slug</p>
            <p className="text-sm font-mono text-slate-900">{intake.publicSlug}</p>
          </div>
          {!confirmReplace ? (
            <button
              onClick={() => setConfirmReplace(true)}
              className="text-sm text-slate-600 underline hover:text-slate-900"
            >
              Replace intake link
            </button>
          ) : (
            <div className="space-y-2">
              <p className="text-sm text-amber-700">
                Replacing the link will invalidate the current link. Customers using the old link will not be able to submit new requests.
              </p>
              <div className="flex gap-3">
                <button
                  onClick={handleReplace}
                  disabled={replacing}
                  className="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
                >
                  {replacing ? "Replacing…" : "Confirm replace"}
                </button>
                <button
                  onClick={() => setConfirmReplace(false)}
                  className="rounded-md border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
                >
                  Cancel
                </button>
              </div>
            </div>
          )}
        </div>
      )}
      {error && <p className="text-sm text-red-600 mt-2">{error}</p>}
    </section>
  );
}

// ─── Team section — member row ─────────────────────────────────────────────────

interface MemberRowProps {
  member: MemberItem;
  callerRole: AccountRole;
  onRefresh: () => void;
}

function MemberRow({ member, callerRole, onRefresh }: MemberRowProps) {
  const [busy, setBusy] = useState(false);
  const [rowError, setRowError] = useState<string | null>(null);
  const [confirmSuspend, setConfirmSuspend] = useState(false);
  const [confirmRemove, setConfirmRemove] = useState(false);
  const [changingRole, setChangingRole] = useState(false);
  const [selectedRole, setSelectedRole] = useState(member.role);
  const [manualShareUrl, setManualShareUrl] = useState<string | null>(null);
  const [resentEmail, setResentEmail] = useState(false);

  function clearState() {
    setConfirmSuspend(false);
    setConfirmRemove(false);
    setChangingRole(false);
    setRowError(null);
  }

  async function handleSuspend() {
    setBusy(true);
    setRowError(null);
    try {
      await api.suspendMember(member.accountUserId);
      onRefresh();
    } catch (err) {
      setRowError(err instanceof ApiError ? memberErrorMsg(err.code, err.extensions) : "Something went wrong.");
    } finally {
      setBusy(false);
      setConfirmSuspend(false);
    }
  }

  async function handleReactivate() {
    setBusy(true);
    setRowError(null);
    try {
      await api.reactivateMember(member.accountUserId);
      onRefresh();
    } catch (err) {
      setRowError(err instanceof ApiError ? memberErrorMsg(err.code, err.extensions) : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  }

  async function handleRemove() {
    setBusy(true);
    setRowError(null);
    try {
      await api.removeMember(member.accountUserId);
      onRefresh();
    } catch (err) {
      setRowError(err instanceof ApiError ? memberErrorMsg(err.code, err.extensions) : "Something went wrong.");
    } finally {
      setBusy(false);
      setConfirmRemove(false);
    }
  }

  async function handleResendEmail() {
    setBusy(true);
    setRowError(null);
    setManualShareUrl(null);
    setResentEmail(false);
    try {
      await api.resendInvite(member.accountUserId, "email");
      setResentEmail(true);
    } catch (err) {
      setRowError(err instanceof ApiError ? memberErrorMsg(err.code, err.extensions) : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  }

  async function handleManualShare() {
    setBusy(true);
    setRowError(null);
    setManualShareUrl(null);
    try {
      const result = await api.resendInvite(member.accountUserId, "manual_share");
      setManualShareUrl(result?.inviteUrl ?? null);
    } catch (err) {
      setRowError(err instanceof ApiError ? memberErrorMsg(err.code, err.extensions) : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  }

  async function handleChangeRole() {
    setBusy(true);
    setRowError(null);
    try {
      await api.changeRole(member.accountUserId, selectedRole);
      onRefresh();
    } catch (err) {
      setRowError(err instanceof ApiError ? memberErrorMsg(err.code, err.extensions) : "Something went wrong.");
      setSelectedRole(member.role);
    } finally {
      setBusy(false);
      setChangingRole(false);
    }
  }

  const isRemovedInvite = member.status === "removed" && member.inviteExpiresAtUtc !== null;
  const isRemovedMember = member.status === "removed" && member.inviteExpiresAtUtc === null;
  // Admins cannot manage Owner-role members; server enforces this too but we avoid surfacing actions that will always fail.
  const canManageTarget = !member.isCurrentUser && !member.isPrimaryOwner
    && (callerRole === "owner" || member.role !== "owner");
  const canChangeRole = member.status === "active" && canManageTarget;
  const canSuspend = member.status === "active" && canManageTarget;
  const canRemoveActive = member.status === "active" && canManageTarget;

  return (
    <div className="py-3 border-b border-slate-100 last:border-b-0">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <p className="text-sm font-medium text-slate-900 truncate">
            {member.email}
            {member.isCurrentUser && <span className="ml-1.5 text-xs font-normal text-slate-400">(you)</span>}
            {member.isPrimaryOwner && <span className="ml-1.5 text-xs font-normal text-slate-400">(primary owner)</span>}
          </p>
          <p className="text-xs text-slate-500">{roleLabel(member.role)} · {statusLabel(member.status)}</p>
        </div>

        <div className="flex flex-col items-end gap-1 shrink-0">
          {/* Active row actions */}
          {member.status === "active" && (
            <>
              {changingRole ? (
                <div className="flex items-center gap-2">
                  <select
                    value={selectedRole}
                    onChange={(e) => setSelectedRole(e.target.value)}
                    disabled={busy}
                    className="rounded border border-slate-300 px-2 py-1 text-xs focus:outline-none focus:ring-1 focus:ring-slate-400"
                  >
                    {callerRole === "owner" && <option value="owner">Owner</option>}
                    <option value="admin">Admin</option>
                    <option value="operator">Operator</option>
                    <option value="viewer">Viewer</option>
                  </select>
                  <button
                    onClick={handleChangeRole}
                    disabled={busy || selectedRole === member.role}
                    className="text-xs font-medium text-slate-900 disabled:opacity-40 hover:underline"
                  >
                    Save
                  </button>
                  <button
                    onClick={() => { setChangingRole(false); setSelectedRole(member.role); }}
                    className="text-xs text-slate-500 hover:underline"
                  >
                    Cancel
                  </button>
                </div>
              ) : canChangeRole ? (
                <button
                  onClick={() => { clearState(); setChangingRole(true); }}
                  className="text-xs text-slate-600 hover:underline"
                >
                  Change role
                </button>
              ) : null}

              {canSuspend && !changingRole && (
                <>
                  {confirmSuspend ? (
                    <div className="flex gap-2">
                      <button
                        onClick={handleSuspend}
                        disabled={busy}
                        className="text-xs font-medium text-red-700 hover:underline disabled:opacity-40"
                      >
                        Confirm suspend
                      </button>
                      <button onClick={() => setConfirmSuspend(false)} className="text-xs text-slate-500 hover:underline">
                        Cancel
                      </button>
                    </div>
                  ) : (
                    <button
                      onClick={() => { clearState(); setConfirmSuspend(true); }}
                      className="text-xs text-slate-600 hover:underline"
                    >
                      Suspend
                    </button>
                  )}
                </>
              )}

              {canRemoveActive && !changingRole && !confirmSuspend && (
                <>
                  {confirmRemove ? (
                    <div className="flex gap-2">
                      <button
                        onClick={handleRemove}
                        disabled={busy}
                        className="text-xs font-medium text-red-700 hover:underline disabled:opacity-40"
                      >
                        Confirm remove
                      </button>
                      <button onClick={() => setConfirmRemove(false)} className="text-xs text-slate-500 hover:underline">
                        Cancel
                      </button>
                    </div>
                  ) : (
                    <button
                      onClick={() => { clearState(); setConfirmRemove(true); }}
                      className="text-xs text-slate-600 hover:underline"
                    >
                      Remove
                    </button>
                  )}
                </>
              )}
            </>
          )}

          {/* Invited row actions */}
          {member.status === "invited" && (
            <>
              {resentEmail && <span className="text-xs text-green-700">Invite resent.</span>}
              <button
                onClick={handleResendEmail}
                disabled={busy}
                className="text-xs text-slate-600 hover:underline disabled:opacity-40"
              >
                Resend invite
              </button>
              <button
                onClick={handleManualShare}
                disabled={busy}
                className="text-xs text-slate-500 hover:underline disabled:opacity-40"
              >
                Manual share
              </button>
            </>
          )}

          {/* Suspended row actions */}
          {member.status === "suspended" && (
            <>
              <button
                onClick={handleReactivate}
                disabled={busy}
                className="text-xs text-slate-600 hover:underline disabled:opacity-40"
              >
                Reactivate
              </button>
              {confirmRemove ? (
                <div className="flex gap-2">
                  <button
                    onClick={handleRemove}
                    disabled={busy}
                    className="text-xs font-medium text-red-700 hover:underline disabled:opacity-40"
                  >
                    Confirm remove
                  </button>
                  <button onClick={() => setConfirmRemove(false)} className="text-xs text-slate-500 hover:underline">
                    Cancel
                  </button>
                </div>
              ) : (
                <button
                  onClick={() => { clearState(); setConfirmRemove(true); }}
                  className="text-xs text-slate-600 hover:underline"
                >
                  Remove
                </button>
              )}
            </>
          )}

          {/* Removed invite row actions */}
          {isRemovedInvite && (
            <>
              {resentEmail && <span className="text-xs text-green-700">Invite resent.</span>}
              <button
                onClick={handleResendEmail}
                disabled={busy}
                className="text-xs text-slate-600 hover:underline disabled:opacity-40"
              >
                Resend invite
              </button>
              <button
                onClick={handleManualShare}
                disabled={busy}
                className="text-xs text-slate-500 hover:underline disabled:opacity-40"
              >
                Manual share
              </button>
              {confirmRemove ? (
                <div className="flex gap-2">
                  <button
                    onClick={handleRemove}
                    disabled={busy}
                    className="text-xs font-medium text-red-700 hover:underline disabled:opacity-40"
                  >
                    Confirm remove
                  </button>
                  <button onClick={() => setConfirmRemove(false)} className="text-xs text-slate-500 hover:underline">
                    Cancel
                  </button>
                </div>
              ) : (
                <button
                  onClick={() => { clearState(); setConfirmRemove(true); }}
                  className="text-xs text-slate-600 hover:underline"
                >
                  Remove
                </button>
              )}
            </>
          )}

          {/* Removed active member row actions */}
          {isRemovedMember && (
            <>
              <button
                onClick={handleReactivate}
                disabled={busy}
                className="text-xs text-slate-600 hover:underline disabled:opacity-40"
              >
                Reactivate
              </button>
              {confirmRemove ? (
                <div className="flex gap-2">
                  <button
                    onClick={handleRemove}
                    disabled={busy}
                    className="text-xs font-medium text-red-700 hover:underline disabled:opacity-40"
                  >
                    Confirm remove
                  </button>
                  <button onClick={() => setConfirmRemove(false)} className="text-xs text-slate-500 hover:underline">
                    Cancel
                  </button>
                </div>
              ) : (
                <button
                  onClick={() => { clearState(); setConfirmRemove(true); }}
                  className="text-xs text-slate-600 hover:underline"
                >
                  Remove
                </button>
              )}
            </>
          )}
        </div>
      </div>

      {rowError && <p className="mt-1.5 text-xs text-red-600">{rowError}</p>}
      {manualShareUrl && (
        <div className="mt-2 rounded-md bg-amber-50 border border-amber-200 p-2">
          <p className="text-xs text-amber-800 mb-1">
            Use this only if the invite email was not received. Anyone with this link can accept the invite.
          </p>
          <p className="text-xs font-mono text-amber-900 break-all">{manualShareUrl}</p>
        </div>
      )}
    </div>
  );
}

// ─── Team section — invite form ───────────────────────────────────────────────

interface InviteFormProps {
  atLimit: boolean;
  maxSeats: number;
  limitApplies: boolean;
  onSuccess: (email: string) => void;
}

function InviteForm({ atLimit, maxSeats, limitApplies, onSuccess }: InviteFormProps) {
  const [email, setEmail] = useState("");
  const [role, setRole] = useState("operator");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (submitting || atLimit) return;
    setSubmitting(true);
    setError(null);
    try {
      await api.inviteMember(email.trim(), role);
      onSuccess(email.trim());
      setEmail("");
      setRole("operator");
    } catch (err) {
      setError(err instanceof ApiError ? memberErrorMsg(err.code, err.extensions) : "Something went wrong.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3">
      <div className="flex gap-2">
        <input
          type="email"
          value={email}
          onChange={(e) => { setEmail(e.target.value); setError(null); }}
          placeholder="Email address"
          required
          disabled={atLimit}
          className="flex-1 rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50"
        />
        <select
          value={role}
          onChange={(e) => setRole(e.target.value)}
          disabled={atLimit}
          className="rounded-md border border-slate-300 px-2 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50"
        >
          <option value="admin">Admin</option>
          <option value="operator">Operator</option>
          <option value="viewer">Viewer</option>
        </select>
        <button
          type="submit"
          disabled={submitting || atLimit}
          className="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50 whitespace-nowrap"
        >
          {atLimit ? "Team limit reached" : (submitting ? "Inviting…" : "Invite team member")}
        </button>
      </div>
      {atLimit && limitApplies && (
        <p className="text-xs text-slate-500">
          Your plan includes {maxSeats} team seats. Contact support to add more.
        </p>
      )}
      {error && <p className="text-sm text-red-600">{error}</p>}
    </form>
  );
}

// ─── Team section ─────────────────────────────────────────────────────────────

function TeamSection({ callerRole }: { callerRole: AccountRole }) {
  const [includeRemoved, setIncludeRemoved] = useState(false);
  const [inviteSuccess, setInviteSuccess] = useState<string | null>(null);

  const {
    data: membersData,
    isLoading,
    isError,
    refetch,
  } = useQuery({
    queryKey: ["members", includeRemoved],
    queryFn: () => api.listMembers(includeRemoved),
    staleTime: 30 * 1000,
  });

  const members = membersData?.members ?? [];
  const seatUsage = membersData?.seatUsage;

  function handleInviteSuccess(email: string) {
    setInviteSuccess(email);
    void refetch();
  }

  return (
    <section>
      <h2 className="text-base font-semibold text-slate-900 mb-4">Team</h2>

      {seatUsage?.limitApplies && (
        <p className="text-sm text-slate-600 mb-4">
          Team seats: {seatUsage.occupiedSeats} of {seatUsage.maxSeats} used
        </p>
      )}

      <div className="mb-6">
        <InviteForm
          atLimit={seatUsage?.atLimit ?? false}
          maxSeats={seatUsage?.maxSeats ?? 0}
          limitApplies={seatUsage?.limitApplies ?? false}
          onSuccess={handleInviteSuccess}
        />
        {inviteSuccess && (
          <p className="mt-2 text-sm text-green-700">
            Invite sent to {inviteSuccess}. They'll receive an email link to set up their account.
          </p>
        )}
      </div>

      {isLoading && <p className="text-sm text-slate-400">Loading…</p>}
      {isError && <p className="text-sm text-slate-500">Could not load team members.</p>}

      {!isLoading && !isError && (
        <>
          <div className="divide-y divide-slate-100 border-t border-slate-100">
            {members.map((m: MemberItem) => (
              <MemberRow key={m.accountUserId} member={m} callerRole={callerRole} onRefresh={() => void refetch()} />
            ))}
            {members.length === 0 && (
              <p className="py-4 text-sm text-slate-400">No team members.</p>
            )}
          </div>

          <div className="mt-3">
            <label className="flex items-center gap-2 text-xs text-slate-500 cursor-pointer">
              <input
                type="checkbox"
                checked={includeRemoved}
                onChange={(e) => setIncludeRemoved(e.target.checked)}
                className="rounded border-slate-300"
              />
              Show removed members
            </label>
          </div>
        </>
      )}
    </section>
  );
}

// ─── Onboarding section ───────────────────────────────────────────────────────

const ONBOARDING_ITEMS: Array<{
  key: string;
  label: string;
  markKey?: string;
  markFn?: () => Promise<void>;
}> = [
  { key: "profileAndContactSaved", label: "Profile & contact saved" },
  { key: "timezoneSaved", label: "Timezone saved" },
  { key: "policySaved", label: "Response policy saved" },
  { key: "intakeLinkActive", label: "Intake link active" },
  { key: "operatorInvited", label: "Team member invited" },
  { key: "mobileDeviceRegistered", label: "Mobile device registered" },
  { key: "firstRequestCreated", label: "First request created" },
  {
    key: "quickCaptureExerciseDone",
    label: "Quick capture exercise",
    markKey: "qc",
    markFn: () => api.markQuickCaptureExercise(),
  },
  {
    key: "trackerReviewDone",
    label: "Tracker review",
    markKey: "tr",
    markFn: () => api.markTrackerReview(),
  },
  {
    key: "spamClassificationExplained",
    label: "Spam classification explained",
    markKey: "sc",
    markFn: () => api.markSpamClassification(),
  },
];

function OnboardingSection() {
  const queryClient = useQueryClient();

  const { data: checklist, isLoading, isError } = useQuery({
    queryKey: ["onboarding"],
    queryFn: api.getOnboardingChecklist,
    staleTime: 60 * 1000,
  });

  const [marking, setMarking] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleMark(markKey: string, markFn: () => Promise<void>) {
    if (marking) return;
    setMarking(markKey);
    setError(null);
    try {
      await markFn();
      await queryClient.invalidateQueries({ queryKey: ["onboarding"] });
    } catch {
      setError("Something went wrong. Please try again.");
    } finally {
      setMarking(null);
    }
  }

  return (
    <section>
      <h2 className="text-base font-semibold text-slate-900 mb-4">Onboarding</h2>

      {isLoading && <p className="text-sm text-slate-400">Loading…</p>}
      {isError && <p className="text-sm text-slate-500">Could not load onboarding checklist.</p>}

      {checklist && (
        <ul className="space-y-3">
          {ONBOARDING_ITEMS.map(({ key, label, markKey, markFn }) => {
            const done = checklist[key as keyof typeof checklist];
            return (
              <li key={key} className="flex items-center gap-3 text-sm">
                <span className={done ? "text-green-600" : "text-slate-300"}>
                  {done ? "✓" : "○"}
                </span>
                <span className={done ? "text-slate-700" : "text-slate-500"}>{label}</span>
                {markKey && markFn && !done && (
                  <button
                    onClick={() => void handleMark(markKey, markFn)}
                    disabled={marking !== null}
                    className="ml-auto text-xs text-slate-500 hover:text-slate-900 underline disabled:opacity-50"
                  >
                    {marking === markKey ? "Marking…" : "Mark done"}
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {error && <p className="mt-3 text-sm text-red-600">{error}</p>}
    </section>
  );
}

// ─── Settings page ────────────────────────────────────────────────────────────

export function Settings({ callerRole }: { callerRole: AccountRole }) {
  const { data: setup, isLoading, isError } = useQuery({
    queryKey: ["setup"],
    queryFn: api.getSetup,
    staleTime: 2 * 60 * 1000,
  });

  if (isLoading) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <span className="text-slate-400 text-sm">Loading…</span>
      </div>
    );
  }

  if (isError || !setup) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <span className="text-slate-500 text-sm">Could not load settings.</span>
      </div>
    );
  }

  return (
    <div className="flex-1 overflow-y-auto">
      <div className="max-w-2xl mx-auto px-4 py-8 space-y-10">
        <h1 className="text-xl font-semibold text-slate-900">Settings</h1>
        <CompanySection setup={setup} />
        <hr className="border-slate-200" />
        <PolicySection setup={setup} />
        <hr className="border-slate-200" />
        <IntakeSection />
        <hr className="border-slate-200" />
        <TeamSection callerRole={callerRole} />
        <hr className="border-slate-200" />
        <OnboardingSection />
      </div>
    </div>
  );
}
