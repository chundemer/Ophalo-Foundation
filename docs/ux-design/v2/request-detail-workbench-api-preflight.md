# Request Detail / Workbench API Preflight

**Status:** Read-only preflight — no implementation authorized by this document.
**Scope:** Verify what the backend actually returns/enforces for the 24 Request Detail /
Workbench actions locked in `request-detail-workbench-signoff-spec.md`, ADR-380, ADR-434
through ADR-441, and ADR-487, before any frontend coding begins. Proposed Scope is deferred from
the controlled-pilot/go-live Workbench scope.

## Scope and authority

Authority order followed: `request-detail-workbench-signoff-spec.md` (locked 2026-08-22) as the
implementation-facing consolidation; `keep-ui-production-decision-register.md` UI-001–UI-013;
`keep-ui-design-model-v2.md`; then the named ADRs. This document adds nothing to product scope —
it only records what is verified in code today versus what the spec assumes exists.

No production code was written or modified. No service was called. No data was mutated. The only
file created is this one.

## Evidence reviewed

**Docs (read in full):** `request-detail-workbench-signoff-spec.md`, ADR-380, ADR-434, ADR-435,
ADR-436, ADR-439, ADR-440, ADR-441, ADR-487; targeted sections of
`keep-ui-production-decision-register.md` (UI-005, UI-006, register lines 220–300) and
`keep-ui-design-model-v2.md` (sections 6–9).

**Backend code (read/grepped):**
- `src/OpHalo.Keep.Application/Requests/KeepRequestDetailResult.cs` — full DTO shape,
  `AvailableActionsMetadata`, `ValidationHintsMetadata`, `ContactActionItem`,
  `KeepRequestEventItem`.
- `src/OpHalo.Keep.Application/Requests/KeepRequestActionDecision.cs` and
  `KeepRequestActionPolicy.cs` — full pure policy (`Evaluate`, `ComputeAllowedStatuses`,
  `CanMarkFeedbackReviewedCore`) that computes `AvailableActionsMetadata`.
- `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs:130-149` —
  `ToAvailableActionsMetadata` field-by-field mapping from decision to JSON.
- `src/OpHalo.Api/Keep/KeepEndpoints.cs:171-780` — every request-detail-adjacent route:
  detail GET, status PATCH, classify, share-intent, sms-handoff, call-handoff, business-updates,
  internal-notes, external-contact, notification-preparation/-confirmation, attention/acknowledge,
  feedback-review, responsible PUT/DELETE, watchers PUT/DELETE, watch PUT/DELETE, mute PUT/DELETE,
  follow-up-on PUT/DELETE, planned-for PUT/DELETE, follow-up-resolution POST, service-location PUT,
  priority PUT.
- `src/OpHalo.Keep.Application/Requests/GetKeepRequestRelatedWorkService.cs` — confirms the only
  "related work" read is other-customer-requests, not Actual Work linkage.
- `src/OpHalo.Keep.Application/Requests/UpdateServiceLocationService.cs`,
  `SetBusinessPriorityService.cs`, `ManageRequestTimingService.cs` (ResolveFollowUpAsync) —
  confirmed server-side authorization exists even where no `availableActions` flag is exposed.
- The `actual-work/*` routes in `KeepEndpoints.cs` — confirmed as a separate API surface keyed by
  `actualWorkId`, not by any field on `KeepRequestDetailResult`.
- `src/OpHalo.Foundation.Application/Accounts/Authorization/PermissionKeys.cs` —
  `RequestsView`, `RequestsOperate`, `RequestsClose`, `ActualWorkCapture`.
- `src/OpHalo.Keep.Core/Entities/Enums/AttentionReason.cs` — bounded 10-value enum; no
  server-authored guidance/copy field exists anywhere in the DTO.
- `src/OpHalo.Keep.Core/Errors/KeepRequestErrors.cs:218-219` and
  `src/OpHalo.Api/Keep/KeepRequestVersionHeader.cs` — `KeepRequest.RequestChanged` /
  `X-Keep-Request-Version` header contract.

**Frontend code — correction (2026-08-22):** an earlier version of this section checked only
`web/ophalo-web` (public surfaces — intake, customer tracker, SMS/call handoff, shared
`KeepBadge`/`KeepButton`/`KeepPublicShell`) and concluded no authenticated request-detail frontend
existed. That was wrong: the authenticated app is `web/ophalo-app`, which already contains a working
Request Detail page and Actual Work integration — `ActualWorkCard.tsx`, `ActualWorkComposer.tsx`,
`ActualWorkHistoryCard.tsx`, `useActualWorkCapture.ts`, `useActualWorkHistory.ts`, and passing tests
under `web/ophalo-app/src/pages/request-detail/__tests__/`. This preflight's contract-matrix and
gap analysis (server-side flags, `PrimaryAction`, attention-guidance metadata) still stands, but the
"nothing to audit for drift on the frontend side" framing does not: Actual Work capture/history is a
reusable existing module, not new frontend work. See `docs/session-log.md` "Active priority" for the
corrected sequencing — Request Detail UI redesign first, reusing existing Actual Work components as
one conditional module; 8B (Owner/Admin financial review card) stays deferred per its own preflight
in `docs/session-log.md`'s Direct Actual Work section.

**Tests:** `tests/OpHalo.IntegrationTests/Api/KeepRequestDetailTests.cs`,
`KeepRequestDetailB4Tests.cs`, `KeepRequestDetailB5Tests.cs`, `KeepRequestDetailRowAuthApiTests.cs`;
`tests/OpHalo.UnitTests/Keep/KeepRequestDetailServiceTests.cs`,
`KeepRequestFollowUpTests.cs`. Confirmed to exist; not read in full (out of scope for a
documentation-only preflight — their existence establishes verified backend coverage, not this
document's job to re-derive).

## Contract matrix

| Locked UI action | Evidence: endpoint/type/symbol/file | Server availability/role-state guard | Version/concurrency rule | Returned effects/UI facts | Required containment | Verdict |
|---|---|---|---|---|---|---|
| Call | `ContactActionItem(Type, Available, Target)` in `KeepRequestDetailResult.ContactActions`; desktop QR via `POST /keep/requests/{id}/call-handoff` → `CreateCallHandoffService` (`KeepEndpoints.cs:330`); public resolve `GET /keep/share-call/{token}` | `Available` flag per contact type, server-computed | Call-handoff is its own write, no request version needed; detail read is unversioned | Handoff URL + expiry; no delivery/receipt claim | Persistent secondary in Anchor phone context (spec §3) | Ready for frontend |
| Text | Same `ContactActionItem`; desktop QR via `POST /keep/requests/{id}/sms-handoff` → `CreateSmsHandoffService` (`KeepEndpoints.cs:294`) | `Available` flag per contact type | Handoff write is unversioned | Handoff URL + expiry | Persistent secondary, same rule as Call | Ready for frontend |
| Email | `ContactActionItem` with `Type="email"` | `Available` flag | N/A — launch-only `mailto:` | Target address only | Persistent secondary, contact context | Ready for frontend |
| Copy phone/email | `CustomerPhone`, `CustomerEmail` fields on `KeepRequestDetailResult` | No server gate — always present when populated | N/A — client clipboard only | N/A | Quiet utility | Ready for frontend |
| Maps/service location | `ServiceAddressLine1/2/City/State/Zip` on `KeepRequestDetailResult` | No server gate | N/A — client launch only | N/A | Contextual/Anchor context strip | Ready for frontend |
| Log external contact | `AvailableActions.CanLogExternalContact`; `POST /keep/requests/{id}/external-contact` → `LogExternalContactService` (`KeepEndpoints.cs:398`); `Validation.ExternalContactSummaryMaxLength` | `CanLogExternalContact = isNonTerminal \|\| (isOwnerAdmin && HasActiveUnresolvedFeedbackReview)` (`KeepRequestActionPolicy.cs:96`) | Versioned; returns `KeepRequestDetailResult` | May set first-response/attention-clear per event fields | Drawer (spec §3, UI-008) | Ready for frontend |
| Send customer update | `AvailableActions.CanSendBusinessUpdate`; `POST /keep/requests/{id}/business-updates` (`KeepEndpoints.cs:364`); `Validation.BusinessUpdateMaxLength` | `CanSendBusinessUpdate = isNonTerminal` | Versioned | Customer-visible event; may `SetStatus` | Inline composer (UI-008) | Ready for frontend |
| Add internal note | `AvailableActions.CanAddInternalNote`; `POST /keep/requests/{id}/internal-notes` (`KeepEndpoints.cs:381`); `Validation.InternalNoteMaxLength` | `CanAddInternalNote = true` (any non-Viewer writer) | Versioned | Internal-only event | Inline composer | Ready for frontend |
| Assign/reassign responsible owner | `AvailableActions.CanAssignResponsible` (JSON) vs. `KeepRequestActionDecision.CanSelfAssignResponsible`/`CanClearResponsible` (Application-layer only); `PUT`/`DELETE /keep/requests/{id}/responsible` (`KeepEndpoints.cs:496,513`) | Decision computes `CanAssignResponsible = isOwnerAdmin && isNonTerminal`, `CanSelfAssignResponsible = isOperator && isNonTerminal`, `CanClearResponsible = isOwnerAdmin && isNonTerminal` — but `KeepRequestDetailMapper.ToAvailableActionsMetadata` (lines 130-149) maps **only** `CanAssignResponsible` into JSON; the other two fields are silently dropped | Versioned | Returns updated `Participants`/responsible state | Inline (Anchor owner context) | Backend contract gap |
| Watch/unwatch | `AvailableActions.CanWatch`/`CanUnwatch`; `PUT`/`DELETE /keep/requests/{id}/watch` (`KeepEndpoints.cs:566,581`) | `canWatch = isNonTerminal && participation == null`; `canUnwatch = isNonTerminal && participation == Watching` | Versioned | `CurrentUserDetailParticipation` updates | Quiet utility | Ready for frontend |
| Mute/unmute | `AvailableActions.CanMute`/`CanUnmute`; `PUT`/`DELETE /keep/requests/{id}/mute` (`KeepEndpoints.cs:596,611`) | `canMute = isNonTerminal && participation != null && notifEnabled == true`; inverse for unmute | Versioned | `CurrentUserDetailParticipation.NotificationsEnabled` updates | Quiet utility, near Watch | Ready for frontend |
| Set Follow Up On | `AvailableActions.CanSetFollowUpOn`; `PUT /keep/requests/{id}/follow-up-on` (`KeepEndpoints.cs:626`); `Validation.FollowUpNoteMaxLength`, `AllowedFollowUpReasons` | `canSetTiming = actor.CanWrite && status not in {Resolved,Closed,Cancelled,Spam,Test}` | Versioned | `FollowUpOnDate/Reason/Note` fields updated | Inline (first-canvas timing) | Ready for frontend |
| Complete/move/keep-active Follow Up On | No dedicated `AvailableActions` flag; `POST /keep/requests/{id}/follow-up-resolution` (`KeepEndpoints.cs:696`) → `ManageRequestTimingService.ResolveFollowUpAsync`, outcomes `complete`/`move`/`keep_active` (ADR-440) | Verified via code read: `ResolveFollowUpAsync` calls the same shared `AuthAsync` as `SetFollowUpOnAsync`, so eligibility is reasonably inferable from `CanSetFollowUpOn`, but this is not an explicit contract guarantee — the spec's own rule (§8) requires per-action availability metadata | Versioned; commits audit + timing state atomically per ADR-440 | Returns updated `KeepRequestDetailResult` | Narrow resolution flow, contextual module (spec §3) | Backend contract gap |
| Set/change/remove Planned For | `AvailableActions.CanSetPlannedFor`; `PUT`/`DELETE /keep/requests/{id}/planned-for` (`KeepEndpoints.cs:661,680`) | Same `canSetTiming` gate as Follow Up On | Versioned | `PlannedForDate` updated | Inline (first-canvas timing) | Ready for frontend |
| Clear/acknowledge authorized attention | `AvailableActions.CanAcknowledgeAttention`; `POST /keep/requests/{id}/attention/acknowledge` (`KeepEndpoints.cs:453`); `Validation.AcknowledgeReasonMaxLength` | `hasAttention = AttentionLevel != None && AttentionReason != UnresolvedFeedback` | Versioned | `AttentionClearedAtUtc/By/Reason` fields update | Attention-guidance module, explicit Why/Resolve-by (UI-005) | Backend contract gap — no server-authored guidance exists anywhere in the DTO; only `AttentionReason` (bounded 10-value enum) and `AttentionLevel`/`WaitingDirection` strings are returned. A client-side dictionary may render factual reason copy for a bounded enum, but must not assert a recommended resolution or claim an action clears attention — that is a server-authored fact. Requires structured attention-guidance metadata from the backend (see Primary-action contract assessment) |
| View/capture Actual Work | Fully separate API: `POST /keep/pricebook/actual-work/create`, `.../lines`, `.../expand-assembly`, `.../submit`, `.../transfer-recorder`, `.../review`, `GET .../request/{requestId}/history` (`KeepEndpoints.cs`) | `ActualWorkCapture` permission (`PermissionKeys.cs:58`) + `RequestsOperate` + current recorder/draft-ownership rules (ADR-487) — enforced in application services, not previewed on `KeepRequestDetailResult` | Actual Work has its own concurrency handling per ADR-487 (Draft ownership, transfer audit) | Request detail carries no Actual Work linkage | Focused workspace, price-blind (ADR-487) | Backend contract gap — add a lightweight authorized Actual Work summary to request-detail (existence, resumable-draft, capture/view permission, relevant count/state — no financial data); full detail still loads only when the module opens |
| Mark work done | No dedicated flag. Client must derive: `AvailableActions.CanChangeStatus && AllowedStatuses.includes("resolved") && Status != "resolved" && AttentionLevel == "none"` (ADR-434); `PATCH /keep/requests/{id}/status` body `{status:"resolved"}` | `ComputeAllowedStatuses` includes `Resolved` from every active status **regardless of attention** (`KeepRequestActionPolicy.cs:127-161`) — the attention check is a separate field the client must combine itself | Versioned | Status becomes `resolved`; does not clear attention (ADR-434 explicit) | Anchor primary or demoted warning variant per ADR-434 | Backend contract gap — a shared client derivation is acceptable for narrow eligibility (this row, ADR-434), but is not sufficient to choose the one current primary for active attention across the whole surface (e.g. an overdue-response request choosing between Send update / Log contact / Assign / Acknowledge). That is a recommendation, which the client is not authorized to invent. See Primary-action contract assessment |
| Close request | `AvailableActions.CanClose`; `PATCH /keep/requests/{id}/status` body `{status:"closed"}`; `AllowedStatuses` includes `"closed"` only when `canClose` | `canClose = isOwnerAdmin && Status == Resolved && AttentionLevel == None` (`KeepRequestActionPolicy.cs:87-89`) | Versioned | Status becomes `closed`, enables one-time feedback | Owner/Admin only, red filled, confirmed (spec §3) | Ready for frontend |
| Edit service location | No `AvailableActions` flag exists; `PUT /keep/requests/{id}/service-location` (`KeepEndpoints.cs:723`) → `UpdateServiceLocationService` | Verified server-side (row/permission check inside the service), but **not previewed** anywhere in `KeepRequestDetailResult` | Versioned | `ServiceAddress*` fields update | Contextual record-details module | Backend contract gap |
| Set internal priority | No `AvailableActions` flag exists; `PUT /keep/requests/{id}/priority` (`KeepEndpoints.cs:746`) → `SetBusinessPriorityService`, requires `RequestsOperate` (verified in code) | Same pattern — server-authorized, not previewed | Versioned | `BusinessPriority` field updates | Contextual record-details module | Backend contract gap |
| View customer page | `PageToken`, `NeedsShare` on `KeepRequestDetailResult`; public route `web/ophalo-web/src/app/keep/r/[pageToken]/page.tsx` already exists | No additional gate — any user who can see detail can construct the link | N/A — read-only client navigation | N/A | Low-frequency utility (Anchor) | Ready for frontend |
| Share customer-page link | `AvailableActions.CanRecordShareIntent`; `POST /keep/requests/{id}/share-intent` (`KeepEndpoints.cs:282`) with `copy_link`/`native_share`/`manual_mark_shared` (ADR-380) | `CanRecordShareIntent = true` whenever the actor is not denied (`KeepRequestActionPolicy.cs:110`) | Unversioned (share-intent endpoint takes no `X-Keep-Request-Version`) | Clears `NeedsShare`; never proves customer receipt | Low-frequency utility, opaque token only (ADR-380) | Ready for frontend |
| Generic status change | `AvailableActions.CanChangeStatus`, `AllowedStatuses`; `PATCH /keep/requests/{id}/status`; `Validation.StatusMessageMaxLength`, `MessageRequiredForStatuses` | `CanChangeStatus = isNonTerminal` | Versioned | Status event + optional message | Contextual lifecycle module only, detail-owned (ADR-435) | Ready for frontend |
| Review unresolved feedback | `AvailableActions.CanMarkFeedbackReviewed`; `POST /keep/requests/{id}/feedback-review` (`KeepEndpoints.cs:470`); `Validation.FeedbackReviewNoteMaxLength`; `Feedback*` fields on detail | `CanMarkFeedbackReviewedCore`: Owner/Admin, `Status==Closed`, feedback submitted+negative+unreviewed, `AttentionReason==UnresolvedFeedback` (`KeepRequestActionPolicy.cs:115-122`) | Versioned | `FeedbackReviewedAtUtc/By/Note` fields update | Owner/Admin review action, detail-only (ADR-435) | Ready for frontend |

**Verdict counts:** Ready for frontend — 17. Backend contract gap — 7 (Assign/reassign
self-assign-and-clear mapping omission, Follow Up resolution missing dedicated flag, Edit service
location missing flag, Set internal priority missing flag, Clear/acknowledge attention guidance
metadata, Actual Work linkage, Mark work done / primary-action derivation —
Follow Up resolution shares the "no dedicated flag" gap already counted above so it appears once —
see note below). Frontend integration gap — 0 (the two candidates from the first pass — attention
copy and primary-action derivation — are reclassified as backend gaps below; see Primary-action
contract assessment). Intentionally unavailable by state/role — 0 (state/role availability is
expressed per-request via the flags above, not as a separate category). Needs product decision — 0.

Note: "Complete/move/keep-active Follow Up On" is counted once, under Backend contract gap, since
the missing per-action availability metadata is the same category of defect as the other three
missing-flag rows (assign/clear responsible, service location, priority) — all four are cases
where a real, authorized backend capability exists but is not previewed in `AvailableActionsMetadata`.

## Primary-action contract assessment

**Finding: `KeepRequestDetailResult` does not return one structured, server-authorized "current
primary action."** There is no `PrimaryAction`, `CurrentAction`, or similarly named field/type
anywhere in `KeepRequestDetailResult.cs`, `AvailableActionsMetadata`, or
`KeepRequestActionDecision`. Confirmed by reading the full DTO and the full policy record —
neither contains such a field.

What exists instead is a set of independent, authoritative booleans/enums that the client must
combine to reconstruct the spec's "exactly one server-authorized primary at rest" rule
(spec §1.6, §3, UI-006):

- `AvailableActions.CanAcknowledgeAttention` (attention-resolution candidate)
- `AttentionLevel` / `AttentionReason` (whether attention is active at all, and which kind)
- `AvailableActions.CanChangeStatus` + `AvailableActions.AllowedStatuses` (whether `resolved` is
  reachable — used to derive Mark work done)
- `AvailableActions.CanClose` (Close request eligibility)
- `Status` (current lifecycle state)

Per the verified `KeepRequestActionPolicy.ComputeAllowedStatuses` logic, `resolved` is an allowed
transition from every active status **independent of attention level** — meaning
`CanChangeStatus && AllowedStatuses.includes("resolved")` alone is not sufficient to decide
whether "Mark work done" should render as the calm primary or the demoted "attention remains"
variant; `AttentionLevel` must be checked separately and combined per ADR-434's exact ordering.

**Correction from advisor review: this is a required backend contract addition, not an optional
enhancement.** A shared client-side derivation is acceptable only for the narrow, already-locked
"Mark work done" eligibility rule (ADR-434: `CanChangeStatus && AllowedStatuses.includes("resolved")
&& AttentionLevel == "none"`). It is not sufficient to choose the one current primary for active
attention — for example, the client cannot safely decide whether an overdue-response request should
lead with Send update, Log contact, Assign, or Acknowledge attention. Any client-side rule that picks
among those would invent a recommendation, which is exactly the behavior ADR-380/UI-006 lock out.

**Required backend contract:** `AvailableActions.PrimaryAction` is a server-computed, extensible
structured field. It is `null` when the server has no safely recommendable action; it is not a
lifecycle-only enum. When non-null, it carries at least:

```text
key                 bounded, server-owned action key
label               server-authorized visible action label
containment         inline | drawer | dialog | workspace
customerVisible     explicit visibility/effect disclosure
clearsAttention     explicit effect, where applicable
changesStatus       explicit effect, where applicable
guidanceKey          bounded server-authored Why / Resolve-by guidance key
```

The bounded `key` set must support the approved primary candidates, including compose/send customer
update, log external contact, assign responsible owner, acknowledge attention, mark work done, mark
work done with attention remaining, close request, review unresolved feedback, and future
server-approved candidates. It encodes the exact precedence rule already implemented client-side ad
hoc across ADR-434 (attention before completion), ADR-436 (why/next), and UI-006 (one enabled
local-task primary). **This contract does not currently exist in the codebase; the shape above is a
required implementation contract, not a discovered fact.** When `PrimaryAction` is `null`, the UI
shows neutral attention guidance rather than guessing.

The same correction applies to attention guidance: the static `AttentionReason → {Why, Resolve by}`
dictionary originally proposed as client-only (item 6 below) may explain a bounded `AttentionReason`
factually, but must not assert a recommended resolution or claim an action clears attention unless
that is server-authored. The backend should return structured attention guidance alongside
`PrimaryAction` — e.g. reason copy/effect metadata or a bounded guidance key — not just the bare
enum. Until that exists, use factual reason copy and neutral review language only.

Until `PrimaryAction` and attention-guidance metadata exist, the UI must fail closed exactly as the
spec's own §8 requires: derive the narrow "Mark work done" case strictly from the verified
boolean/enum combination above, write one shared, tested derivation function for that case only (not
duplicated per surface), and when any other combination is ambiguous or metadata is missing, show
neutral review context — never invent a lifecycle or attention primary that the derivation cannot
support.

### Session 0A locked decision (2026-08-25)

Christian locked the precedence rule and the field shape below. This supersedes the "mark work
done with attention remaining" candidate key above — that composite state is no longer a
`PrimaryAction` value.

**Precedence, evaluated in this order by `KeepRequestActionPolicy`:**

1. **Effective attention active:** if a server-known attention-resolution route exists (e.g.
   `respond_to_customer`, `acknowledge_attention`, `resolve_follow_up`, `log_external_contact`),
   that is `PrimaryAction`. Mark work done / Close never take the primary slot while attention is
   active, regardless of `CanChangeStatus`/`CanClose`.
2. **Effective attention active, no actionable resolution route:** `PrimaryAction = null`. Work
   completion/closeout is never promoted past unresolved attention, even as a fallback.
3. **No effective attention, Resolved + `CanClose`:** `PrimaryAction = close_request`.
4. **No effective attention, eligible non-resolved request:** `PrimaryAction = mark_work_done`.
5. **No effective attention, none of the above:** `PrimaryAction = null`.

Mark work done remains an authorized **secondary** action whenever `CanChangeStatus &&
AllowedStatuses.includes("resolved")` is true, independent of the primary slot. When attention is
active, its secondary metadata must carry the "attention remains" consequence/warning as
server-authored data (not a distinct `PrimaryAction` key) — e.g. a `consequence`/`warning` field on
the secondary-action entry, so the client renders the warning copy without inferring it from
`AttentionLevel` itself.

**Field shape addition — `Target`:** `PrimaryAction` (and, where applicable, other structured
action entries) must carry a closed `Target` field alongside `key`/`label`, naming the concrete UI
surface the client invokes — the client performs no key-to-behavior translation of its own:

```text
Target: "mutation" | "customer_update_composer" | "attention_sheet" | "contact_sheet"
       | "follow_up_sheet"
```

`Target` is more specific than the `containment` field proposed above (`inline | drawer | dialog |
workspace`, a presentation category) — `Target` names the actual component/handler, and replaces
`containment` for `PrimaryAction` specifically. `mutation` covers direct-call actions like
`mark_work_done`/`close_request`; the sheet/composer values route to the corresponding existing UI
surface (inline composer, attention drawer, contact drawer, follow-up drawer) rather than letting
each surface reinterpret `key`.

This is scope only — no implementation is authorized by this document. Backend contract addition,
policy precedence logic, and both DTO/frontend migrations remain a separate approved batch.

## Needs Attention queue-membership vs. detail-guidance gap (added 2026-08-22)

**Discovered during Workbench visual verification, not during original preflight authoring.**
ADR-426/build-log-075 already locked the general principle that detail attention guidance must be
server-authored, but its scope and the DTO it audited only cover the persisted-attention path. This
section extends that same principle to the two queue-membership predicates it did not examine.

**List membership** (`KeepRequestListPersistence.cs:127-136`) admits a row to Needs Attention on any
of three OR'd conditions:

1. `AttentionLevel != None` (persisted attention);
2. `FollowUpOnDate.HasValue && FollowUpOnDate <= today` (ADR-439: due/overdue Follow Up On becomes
   active operational attention unless a stronger reason already owns the request);
3. `FirstRespondedAtUtc == null && FirstResponseDueAtUtc <= now` (first business response overdue —
   the Request-row **Response overdue** badge, ADR-192/ADR-178).

**Detail contract today** (`KeepRequestDetailResult.cs`, `KeepRequestDetailMapper.cs`) returns the
raw dates for all three conditions but derives an effective attention verdict for none but the
first. `AttentionGuidanceCard` (`DetailPanels.tsx:799` → `helpers.ts:179`) renders only when
`attentionLevel !== "none" && attentionReason`, so conditions 2 and 3 admit a row to Needs Attention
with no corresponding Request Detail **Why / Resolve by** explanation — violating the ADR-436
requirement that staff-facing attention signals answer why the signal is shown and what to do next,
and the signoff spec's §4 lifecycle/attention matrix, which already states due/overdue Follow Up is
active attention.

The derivation exists once today, list-side only, in `GetKeepRequestListService.cs:676-699`
(`firstResponseOverdue`, `isDueOrOverdueFollowUpOn`/`isFollowUpOverdue`, per ADR-439). Detail has no
equivalent — the fix is porting this derivation into the detail mapper as an authoritative field,
not inventing new logic, and not leaving the client to re-derive it (locked: client must not derive
the reason).

**Resolution paths already exist for all three cases — this is a read-contract gap only, no new
mutation endpoint is required:**

| Case | Queue-admission source | Existing resolution mechanism | Authorization gate |
|---|---|---|---|
| 1. Persisted attention | `AttentionLevel != None` | `AcknowledgeAttention` (ADR-112) | `AvailableActions.CanAcknowledgeAttention` (`hasAttention`) |
| 2. Follow-Up due/overdue | `FollowUpOnDate <= today` (ADR-439) | `ManageRequestTimingService.ResolveFollowUpAsync` — `complete`/`move`/`keep_active` (ADR-440) | Inferable from `CanSetFollowUpOn` only; **not an explicit contract guarantee** — already flagged as gap item 3 above (§8 requires per-action availability metadata) |
| 3. First-response overdue | `FirstRespondedAtUtc == null && FirstResponseDueAtUtc <= now` | Any customer-facing response: `ChangeStatus` with message (`KeepRequest.cs:205`), `LogOutboundExternalContact` (`:713`), `ConfirmUpdateNotification` (`:857`) — all set `FirstRespondedAtUtc` | Each already independently authorized (`CanChangeStatus`, `CanLogExternalContact`, update-notification flow) |

**Cross-checked against every doc that defines a piece of this matrix, to avoid contradicting a
locked line while closing this gap:**

- Signoff spec §4 **Attention precedence** (line 91): "Stronger attention may supersede due/overdue
  Follow Up" — matches `GetKeepRequestListService.cs:695` (`isDueOrOverdueFollowUpOn` is gated on
  `AttentionLevel == None`). Case 1 already outranks case 2 in the verified code; any
  `EffectiveAttention` derivation must preserve this, not re-derive it differently.
- **Locked decision (2026-08-22, ADR-489):** the full effective attention order is (1) persisted
  attention, then (2) due/overdue Follow Up On, then (3) first-response overdue. A due Follow Up On
  is a specific, deliberate customer promise and outranks the generic first-response SLA fallback,
  so case 2 also outranks case 3 — not only case 1's absence. Each lower-ranked condition remains a
  queue-membership condition on its own — it still admits the row to Needs Attention independently —
  but must not replace or compete with a higher-ranked reason in the detail card. If a higher-ranked
  condition later resolves and a lower-ranked one still applies, `EffectiveAttention` recomputes and
  surfaces the next-ranked reason then (e.g. persisted attention clears with no first response yet →
  surfaces `FirstResponseDue`). This mirrors and extends the case 1/case 2 precedence pattern already
  verified above and keeps the derivation entirely server-side — no client-side ranking. All pairwise
  and the triple-overlap combination require explicit backend test coverage in Slice A.
- `AttentionReason.FirstResponseDue` (`AttentionReason.cs:9`, value `6`) **already exists in the
  enum and is already mapped to `"first_response_due"` in both mappers**, but is never assigned
  anywhere in `KeepRequest.cs` — confirmed by searching every `AttentionReason =` assignment in
  Core. It is a dormant slot, not a proposal: reusing it for case 3 needs no enum change and no new
  client switch arm, only wiring the read-time derivation to populate it in the detail response.
- No equivalent dormant value exists for case 2 (Follow Up due/overdue) — the enum has no
  `FollowUpDue`/similar member. Extending the enum here is a real, first-time addition and must be
  reviewed as one (owned-enum switches are exhaustive/fail-explicit per project rules — every mapper
  switch and every client switch on `AttentionReason` needs a new arm).

**All four decisions locked (2026-08-22, ADR-489 + ADR-490):**

1. **Locked (ADR-490):** one server-computed `EffectiveAttention` block on `KeepRequestDetailResult`.
   The client consumes it; it does not combine raw conditions. **Shipped shape (Slice A,
   2026-08-22) refines the original sketch:** `dueAt` split into `dueAtUtc` (real UTC instant — case
   1/case 3 only) and `dueOnDate` (date-only — case 2/Follow Up On only, never a synthesized
   instant, so a client time zone can't shift the promised calendar date); `guidance` renamed
   `guidanceKey` — a bounded routing key (`acknowledge_attention` | `resolve_follow_up` |
   `respond_to_customer` | null), not prose, since full Why/Resolve-by copy stays client-owned per
   the ADR-426 interim rule.
2. **Locked (ADR-489):** effective attention order is case 1 (persisted attention) > case 2
   (Follow Up due/overdue) > case 3 (first-response overdue) — case 2 outranks case 3, not only
   case 1's absence, since a due Follow Up On is a deliberate customer promise.
3. **Locked (ADR-490):** case 3 reuses the dormant `AttentionReason.FirstResponseDue` (no enum
   change). Case 2 gets a new `AttentionReason.FollowUpDue` member — exhaustive-switch updates
   accepted in every mapper switch and every client switch on `AttentionReason`.
4. **Locked (ADR-490):** `CanSetFollowUpOn` is the ratified shared gate for both setting and
   resolving Follow Up On. `follow-up-resolution` endpoint authorization must enforce this same
   policy explicitly — not inherit it from a client-only assumption.

Proceeding to implementation preflight below.

**Approved split (2026-08-22):** implementation proceeds as two independently compiling vertical
slices per the batch-size gate.

- **Slice A (backend contract, approved to start):** `AttentionReason.cs` (add `FollowUpDue`),
  `KeepRequestDetailResult.cs` (add `EffectiveAttention`), `KeepRequestDetailMapper.cs` (derive
  `EffectiveAttention` with the full case 1 > case 2 > case 3 order locked in ADR-489),
  `GetKeepRequestListService.cs` (add the new enum arm to its existing switch), plus backend unit/
  contract tests covering every pairwise and the triple-overlap precedence combination explicitly.
  Additive DTO field — ships and compiles independently of the frontend.
- **Slice B (frontend consumption, follows after Slice A ships):** `apiClient.types.ts`,
  `helpers.ts`, `DetailPanels.tsx`, `mocks/fixtures.ts`/`mockApiClient.ts`, plus frontend tests and
  the end-to-end matrix proving every Needs Attention row has matching Request Detail guidance or is
  not admitted to that queue.

This is list-membership-predicate-driven detail-contract work; it does not touch Visual Slice B/C,
Actual Work, or Primary Action sequencing beyond the cross-reference in gap item 5.

**Sources checked for contradiction:** `request-detail-workbench-signoff-spec.md` §4 (lifecycle and
attention matrix, precedence line); ADR-436 (staff operational signal clarity); ADR-439 (Follow Up
On promise-protection semantics); ADR-426 / build-log-075 (detail attention guidance metadata);
ADR-178/ADR-182/ADR-192 (list-side reason/next-action copy and Response overdue badge — list-only,
not detail, and not contradicted by anything proposed here).

## State/role coverage

| State/role dimension | Verified backend behavior | Evidence |
|---|---|---|
| Received | `CanChangeStatus=true` (non-terminal); `AllowedStatuses` = Scheduled/InProgress/PendingCustomer/Resolved/Cancelled | `KeepRequestActionPolicy.cs:131-134` |
| Scheduled | Same non-terminal set minus Received, plus Planned For visible | `KeepRequestActionPolicy.cs:136-138` |
| Active/InProgress | Non-terminal; attention-first ordering is a UI rule, not enforced server-side beyond `AttentionLevel`/`CanAcknowledgeAttention` | `KeepRequestActionPolicy.cs:140-142`, `:62-63` |
| Work completed (`Resolved`), no active attention | `AllowedStatuses` includes `Closed` only in this sub-case (`canClose` true); Follow Up On/Planned For become unavailable (`canSetTiming` excludes `Resolved`) | `KeepRequestActionPolicy.cs:78-83, 87-89, 148-150` |
| Work completed (`Resolved`), active attention | `AllowedStatuses` excludes `Closed` (falls to the plain `Resolved` branch); `canClose=false` | `KeepRequestActionPolicy.cs:89, 152-154` |
| Closed | `AllowedStatuses=[]`; `CanChangeStatus` remains `isNonTerminal`-derived but `IsTerminal` makes this false in practice — verified via `isNonTerminal = !request.IsTerminal` | `KeepRequestActionPolicy.cs:61, 156-158` |
| Closed with unresolved feedback | `CanMarkFeedbackReviewed` becomes the sole action; `CanLogExternalContact` re-opens for Owner/Admin (`isOwnerAdmin && HasActiveUnresolvedFeedbackReview`) | `KeepRequestActionPolicy.cs:96, 115-122` |
| Owner/Admin | Gates `CanAssignResponsible`, `CanClearResponsible` (mapper-dropped), `CanManageWatchers` (mapper-dropped), `CanClose`, `CanClassify`, `CanMarkFeedbackReviewed` | `KeepRequestActionPolicy.cs:97-121` |
| Operator/office staff | Gates `CanSelfAssignResponsible` (mapper-dropped — see Contract matrix gap) | `KeepRequestActionPolicy.cs:98` |
| Field technician posture | Not distinguished at the `KeepRequestActionPolicy` layer — Operator role is the only server-side distinction found; any field/office split is a client presentation decision, not a server permission today | Absence confirmed by full read of `KeepRequestActionPolicy.cs` |
| Viewer/read-only | Hard `DenyAll` — every flag false, `AllowedStatuses=[]` | `KeepRequestActionPolicy.cs:42-43, 15-36` |
| Entitlement absent/present — Actual Work | Not expressed on `KeepRequestDetailResult`; resolved via `ActualWorkCapture` permission + row/draft-ownership checks inside the separate Actual Work API | `PermissionKeys.cs:58`; confirmed absent from `KeepRequestDetailResult.cs` |

## Gaps and required sequencing

Ordered by what blocks frontend coding soonest:

1. **Fix the `AvailableActionsMetadata` mapper omission** (`KeepRequestDetailMapper.cs:130-149`):
   add `CanSelfAssignResponsible`, `CanClearResponsible`, and `CanManageWatchers` to the JSON
   contract — the Application-layer decision already computes them; this is a mapping fix, not new
   policy work. Blocks: Assign/reassign responsible owner for Operators (spec §2 lists
   "self-assign where authorized" as an Operator flow).
2. **Add `AvailableActions` flags for Edit service location and Set internal priority.** Both
   endpoints are implemented and authorized server-side today but are invisible to the detail
   contract, which forces the frontend to either guess from role (explicitly forbidden by
   ADR-380/UI-006) or attempt-and-fail. Blocks: Edit service location, Set internal priority.
3. **Add an explicit availability flag for follow-up resolution** (`CanResolveFollowUp` or similar),
   or explicitly ratify that `CanSetFollowUpOn` is the intended shared gate for both
   `follow-up-on` and `follow-up-resolution`. Either answer is acceptable; the current silence is
   not. Blocks: Complete/move/keep-active Follow Up On.
4. **Add a lightweight authorized Actual Work summary field to request-detail** rather than
   accepting unconditional extra round-trips on every Request open. It should disclose only
   authorized, non-financial facts: whether Actual Work exists, whether a resumable draft exists,
   whether capture/view is permitted, and a relevant count/state. Full detail still loads only
   when the module is opened. This keeps the Workbench conditional and calm instead of turning
   every Request Detail render into an entitlement/history fan-out. Blocks: View/capture Actual Work.
5. **Add server-authored `AvailableActions.PrimaryAction` and bounded attention-guidance metadata**
   per the Primary-action contract assessment above, before any component renders an Anchor
   primary or attention-guidance module. `null` is an allowed value when no action is safely
   recommendable. Blocks: Mark work done as a lifecycle-wide primary, Clear/acknowledge authorized
   attention guidance, and indirectly every surface that must show "one enabled local-task primary
   at rest" (UI-006). The narrow ADR-434 "Mark work done" eligibility check may still be written as
   one shared, tested client derivation in the interim, but it must not be extended to choose among
   other attention-remediation actions. **This item's scope now extends to the two queue-membership
   attention sources (due/overdue Follow Up On, first-response overdue) documented in "Needs
   Attention queue-membership vs. detail-guidance gap" below — not persisted attention alone.**
6. **Add contract tests proving items 1–5 across the approved lifecycle/role matrix** before the
   frontend build guide is written.

None of items 1–6 require a new ADR or reopening a locked product decision — they are contract
completeness and mapping fixes within already-locked policy.

## Go / no-go recommendation

**No-go for all Request Detail / Workbench frontend production coding until gap items 1–5 are
complete and covered by contract tests (item 6).** Backend contract work on items 1–5 can start
immediately; the agreed Request Detail UI pause remains in force.

The backend already implements real, tested, versioned, row-authorized mutations for the large
majority of the 24 actions (17 of 24 rows in the matrix are Ready for frontend as-is). That does
not authorize piecemeal Request Detail frontend work while the shared contract is incomplete. The
Anchor ("one enabled local-task primary at rest," UI-006) and the attention-guidance module cannot
be built correctly without server-authored `PrimaryAction` and attention-guidance metadata (gap 5),
and the Work Canvas's Actual Work entry point depends on the Actual Work summary
(gap 4). Building any Request Detail surface against ad hoc client derivation now would re-create
the recommendation logic the backend is meant to own, then require rework once the contract lands.

Corrected pre-code sequence:
1. Backend: expose missing responsibility, location, priority, and follow-up-resolution
   availability flags (gaps 1–3).
2. Backend: add server-authored `PrimaryAction` and bounded attention-guidance metadata (gap 5).
3. Backend: add a lightweight authorized Actual Work summary
   (gap 4).
4. Contract tests proving those fields across the approved lifecycle/role matrix (gap 6).
5. Frontend build guide: map each approved action lane and containment to the verified contract.
6. Only then start Request Anchor and Work Canvas implementation.

## Verification checklist before frontend coding

- [ ] `KeepRequestDetailMapper.ToAvailableActionsMetadata` exposes `CanSelfAssignResponsible`,
      `CanClearResponsible`, `CanManageWatchers` in JSON (currently silently dropped).
- [ ] `AvailableActionsMetadata` gains explicit server-authored availability flags for Edit service
      location and Set internal priority. The client must not derive either permission.
- [ ] Follow-up resolution's availability gate is either an explicit flag or an explicitly ratified
      reuse of `CanSetFollowUpOn`.
- [ ] Request-detail returns a lightweight authorized Actual Work summary (existence,
      resumable-draft, capture/view permission, count/state — no financial data), so the
      Workbench does not fan out to entitlement/history reads on every render.
- [ ] `AvailableActions.PrimaryAction` is server-authored (`null` allowed when no action is safely
      recommendable) and encodes the ADR-434/ADR-436/UI-006 precedence rule; the client's shared
      "Mark work done" derivation is scoped to that one ADR-434 eligibility check only, not extended
      to choose among attention-remediation actions.
- [ ] Attention guidance is server-authored (reason copy/effect metadata or a bounded guidance key)
      alongside `PrimaryAction`; any client-side `AttentionReason`-keyed dictionary is limited to
      factual reason copy, never a recommended resolution or a claim that an action clears
      attention.
- [ ] Contract tests prove the above fields across the approved lifecycle/role matrix.
- [ ] Every versioned mutation in the frontend API client sends `X-Keep-Request-Version:
      {detail.version}` and adopts the returned `KeepRequestDetailResult` on success (ADR-380).
- [ ] `409 KeepRequest.RequestChanged` handling preserves open form input and blocks resubmission
      from stale state (ADR-380; error verified at `KeepRequestErrors.cs:218-219`).
