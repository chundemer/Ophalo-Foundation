# ADR-437 — Default Queue Excludes Calm Work Completed Rows

**Status:** Locked  
**Session:** S24 request workbench screenshot review  
**Supersedes:** ADR-175 / ADR-177 / ADR-189 quiet-Resolved default-list behavior  
**Next free ADR after this:** ADR-438

---

## Context

Earlier B5 request-list decisions treated `Resolved` as pre-terminal active work, so quiet resolved
rows remained in the Default Queue. That was technically consistent with the lifecycle model, but the
S24 request-list workbench makes the operational cost visible: calm **Work completed** rows can
clutter the daily desk queue even though they belong to closeout administration.

ADR-434 now labels backend/API `resolved` as staff-facing **Work completed** and keeps closeout as a
separate Owner/Admin action. ADR-384 already defines `ready_to_close` as the closeout queue, and
ready-to-close eligibility excludes active attention.

The remaining question is where a calm work-completed row should live before it is closed.

---

## Decision

Default Queue is the live operational triage queue. It must exclude calm **Work completed** rows.

In server terms, the Default Queue must exclude:

```text
Status == Resolved
AttentionLevel == None
```

Those rows belong in `ready_to_close` for Owner/Admin users.

The Default Queue must still include work-completed rows with real active attention:

```text
Status == Resolved
AttentionLevel != None
```

Active attention means the request still has unresolved operational/customer work, so the attention
state overrides the completed workflow state.

Customer intake urgency, contact preference, or other customer-reported/badge metadata alone must not
keep a **Work completed** row in the Default Queue. The controlling signal is the server attention
state, not scary-looking copy.

Closed unresolved feedback remains the existing Owner/Admin exception in Default Queue until feedback
review moves it out.

---

## Rationale

Default Queue should answer:

```text
What needs live operational attention now?
```

Ready to Close should answer:

```text
What completed work needs final administrative closeout?
```

Putting calm completed work in both places makes `ready_to_close` redundant and inflates the primary
queue with administrative backlog. That weakens the promise-loop surface for pilot business owners.

This does not change the lifecycle model. `Resolved` remains pre-terminal, supports normal permitted
customer/business writes where policy allows, and becomes `Closed` only through the explicit closeout
workflow.

---

## Implementation Notes

- Update the Default active-view query and default view count together.
- Keep `ready_to_close` eligibility as `Resolved` plus no active attention.
- Add focused tests proving:
  - calm `Resolved` rows are absent from Default Queue;
  - calm `Resolved` rows are present in `ready_to_close`;
  - `Resolved` rows with active attention remain in Default Queue and Needs Attention;
  - `ready_to_close` excludes active-attention `Resolved` rows.
- Do not implement this as a client-only filter. Counts, pagination candidates, and row membership
  must come from the server.

---

## Consequences

Older language that says quiet `Resolved` rows remain in the default list is superseded for the PWA
command-center queue. Quiet `Resolved` rows are still active/pre-terminal in the lifecycle, but their
primary list home is now `ready_to_close`.
