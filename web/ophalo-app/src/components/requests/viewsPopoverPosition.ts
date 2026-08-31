// GAP-060: the Request-list Views menu is an overlay anchored to its trigger. It previously
// used `position: absolute` inside the queue-panel header, so an `overflow-hidden` /
// `h-dvh` ancestor clipped it — option labels and part of the menu rendered outside the
// visible bounds. The menu now renders with `position: fixed` (still a DOM child of the
// trigger's container, so outside-click detection and Tab order are unchanged) and this
// pure function computes a viewport-bounded placement that reacts to edge collisions,
// narrow widths, and browser zoom (zoom changes the CSS-pixel viewport size).

export interface AnchorRect {
  top: number;
  bottom: number;
  left: number;
  right: number;
}

export interface AnchoredPositionInput {
  trigger: AnchorRect;
  viewportWidth: number;
  viewportHeight: number;
  /** Preferred menu width when the viewport has room for it. */
  preferredWidth: number;
  /** Never render narrower than this unless the viewport itself is narrower. */
  minWidth: number;
  /** Gap between trigger and menu, and the minimum inset from any viewport edge. */
  margin: number;
}

export interface AnchoredPosition {
  left: number;
  top: number;
  width: number;
  maxHeight: number;
  /** Which side of the trigger the menu was placed on. */
  placement: "below" | "above";
}

function clamp(value: number, min: number, max: number): number {
  if (max < min) return min;
  return Math.min(Math.max(value, min), max);
}

/**
 * Compute a fixed-position placement for the Views menu that always stays within
 * `margin` of every viewport edge. The menu is right-aligned to the trigger when
 * possible and flips above the trigger when there is materially more room there.
 */
export function computeAnchoredPosition(input: AnchoredPositionInput): AnchoredPosition {
  const { trigger, viewportWidth, viewportHeight, preferredWidth, minWidth, margin } = input;

  const available = Math.max(viewportWidth - margin * 2, 0);
  // Prefer the requested width, never exceed the viewport, and only drop below
  // minWidth when the viewport genuinely cannot fit it.
  const width = Math.min(Math.max(preferredWidth, Math.min(minWidth, available)), available || preferredWidth);

  // Right-align to the trigger, then pull fully inside the viewport.
  const rightAlignedLeft = trigger.right - width;
  const left = clamp(rightAlignedLeft, margin, Math.max(viewportWidth - margin - width, margin));

  const spaceBelow = viewportHeight - margin - (trigger.bottom + margin);
  const spaceAbove = trigger.top - margin - margin;

  let placement: "below" | "above" = "below";
  let top: number;
  let maxHeight: number;

  if (spaceBelow >= spaceAbove || spaceBelow >= 240) {
    placement = "below";
    top = trigger.bottom + margin;
    maxHeight = Math.max(viewportHeight - margin - top, 0);
  } else {
    placement = "above";
    maxHeight = Math.max(spaceAbove, 0);
    top = Math.max(trigger.top - margin - maxHeight, margin);
  }

  return {
    left: Math.round(left),
    top: Math.round(top),
    width: Math.round(width),
    maxHeight: Math.round(maxHeight),
    placement,
  };
}
