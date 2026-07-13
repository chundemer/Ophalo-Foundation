# ADR-438 — Assigned to Me excludes calm Work completed rows

**Status:** Locked  
**Date:** 2026-07-12  
**Related:** ADR-434, ADR-437, build-log/082

## Decision

`assigned_to_me` is the current user's active-promise queue. It excludes calm **Work completed**
rows where:

- backend status is `Resolved`;
- `AttentionLevel == None`.

Resolved rows with active attention remain in `assigned_to_me` when the current user is responsible.

## Rationale

`Assigned to Me` should answer, "What am I still responsible for doing?" Calm completed work has
left the active promise loop and belongs in `ready_to_close`, where Owner/Admin closeout happens.
Keeping calm resolved rows in `assigned_to_me` mixes active work with administrative closeout and
makes the tab feel stale.

Resolved rows with active attention are different: the customer promise is not calm, so the
responsible user still needs to see and act on them.

## Implementation Notes

The backend list query and `AssignedToMe` count both apply the same exclusion:

```text
Status != Resolved OR AttentionLevel != None
```

Local PWA mock filtering mirrors the same rule using the mock fixture's `normal` no-attention value.
