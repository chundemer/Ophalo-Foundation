import { useCallback, useEffect, useRef, useState } from "react";
import type { FocusEvent } from "react";

// RD-019A: centralizes the two intentionally different width measurements Request Detail relies on,
// plus the mobile action-rail focus state. Extracted verbatim from `RequestDetailContent` — no
// behavior change. The two predicates are deliberately not interchangeable (BL137):
//   - `isViewportWide` (matchMedia, 1001px): selects the dedicated Actual Work workspace *route*,
//     matching `ActualWorkWorkspacePage`'s own guard.
//   - `isWide` (ResizeObserver on the detail container, 1001px): selects Request Detail's own
//     internal composition (mobile anchor / action rail / module order / in-page composer chrome).
export interface RequestDetailLayout {
  rootRef: React.RefObject<HTMLDivElement | null>;
  isViewportWide: boolean;
  isWide: boolean;
  isTextEditing: boolean;
  handleCanvasFocus: (e: FocusEvent<HTMLDivElement>) => void;
  handleCanvasBlur: (e: FocusEvent<HTMLDivElement>) => void;
}

export function useRequestDetailLayout(): RequestDetailLayout {
  // BL136 4f-i: the route-vs-modal decision must use the *viewport* 1001px predicate, matching
  // `ActualWorkWorkspacePage`. In Workbench two-pane mode the detail container is < 1001px at
  // viewports up to ~1360px (a 360px queue pane sits beside it), but a direct workspace deep-link
  // renders the desktop workspace at those same viewports — so the container width (`isWide` below)
  // would wrongly keep the in-page modal there. `matchMedia` mirrors the workspace page's own guard.
  const [isViewportWide, setIsViewportWide] = useState(
    () => typeof window?.matchMedia === "function" && window.matchMedia("(min-width: 1001px)").matches,
  );
  useEffect(() => {
    if (typeof window?.matchMedia !== "function") return;
    const mq = window.matchMedia("(min-width: 1001px)");
    const sync = () => setIsViewportWide(mq.matches);
    mq.addEventListener("change", sync);
    return () => mq.removeEventListener("change", sync);
  }, []);

  // Locked in keep-ui-design-model-v2.md §13 (build-log 133); duplicated rather than imported —
  // same rule `RequestWorkbenchShell.tsx`'s `PROTECTED_WORKSPACE_MIN_PX` measures. This is the
  // *container* width, used for Request Detail's own internal layout (mobile anchor / action
  // rail / module order / the in-page composer's own `isWide` chrome).
  const rootRef = useRef<HTMLDivElement | null>(null);
  const [isWide, setIsWide] = useState(false);
  useEffect(() => {
    const el = rootRef.current;
    if (!el) return;
    const observer = new ResizeObserver((entries) => {
      const width = entries[0]?.contentRect.width ?? 0;
      setIsWide(width >= 1001);
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  // Mobile action-rail hide/unpin while text is being entered (Slice 2, locked spec §4.2).
  // Scoped `focus`/`blur` on the canvas root rather than document, and rather than threading a
  // prop through every sheet/composer — React's `onFocus`/`onBlur` bubble via `focusin`/
  // `focusout` under the hood, so one pair of handlers on the outer wrapper covers every
  // descendant field with no cleanup/effect needed. `relatedTarget` guards the field-to-field
  // flicker case (e.g. tabbing straight from one text field into another).
  const [isTextEditing, setIsTextEditing] = useState(false);
  const isTextEntryElement = useCallback((el: EventTarget | null): boolean => {
    if (!(el instanceof HTMLElement)) return false;
    const tag = el.tagName;
    return tag === "INPUT" || tag === "TEXTAREA" || el.isContentEditable;
  }, []);
  const handleCanvasFocus = useCallback(
    (e: FocusEvent<HTMLDivElement>) => {
      if (isTextEntryElement(e.target)) setIsTextEditing(true);
    },
    [isTextEntryElement],
  );
  const handleCanvasBlur = useCallback(
    (e: FocusEvent<HTMLDivElement>) => {
      if (!isTextEntryElement(e.target)) return;
      if (isTextEntryElement(e.relatedTarget)) return;
      setIsTextEditing(false);
    },
    [isTextEntryElement],
  );

  return { rootRef, isViewportWide, isWide, isTextEditing, handleCanvasFocus, handleCanvasBlur };
}
