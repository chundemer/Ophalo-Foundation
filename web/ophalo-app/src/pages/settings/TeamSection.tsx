import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type AccountRole, type MemberItem, ApiError } from "../../lib/apiClient";

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

// ─── Member row ───────────────────────────────────────────────────────────────

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
          <div className="flex items-start justify-between gap-2">
            <p className="text-xs text-amber-800 mb-1">
              Use this only if the invite email was not received. Anyone with this link can accept the invite.
            </p>
            <button
              type="button"
              onClick={() => setManualShareUrl(null)}
              className="text-amber-600 hover:text-amber-900 shrink-0"
              aria-label="Dismiss"
            >
              ×
            </button>
          </div>
          <p className="text-xs font-mono text-amber-900 break-all">{manualShareUrl}</p>
        </div>
      )}
    </div>
  );
}

// ─── Invite form ──────────────────────────────────────────────────────────────

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

export function TeamSection({ callerRole }: { callerRole: AccountRole }) {
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
      <h2 className="text-base font-semibold text-slate-900 mb-1.5">Team</h2>
      <p className="text-sm text-slate-500 mb-4">
        Keep works great for solo businesses — no team required. When you're ready, invite the people who help answer customers or handle work.
      </p>

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
              <p className="py-4 text-sm text-slate-400">Just you for now — use the form above to invite someone when you're ready.</p>
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
