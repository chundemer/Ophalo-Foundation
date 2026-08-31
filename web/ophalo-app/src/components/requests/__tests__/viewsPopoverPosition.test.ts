import { describe, it, expect } from "vitest";
import { computeAnchoredPosition } from "../viewsPopoverPosition";

// GAP-060: the Views menu was clipped outside the viewport/queue bounds. jsdom has no
// layout engine, so the viewport-containment guarantee is verified here on the pure
// placement math at each width we claim coverage for.

const BASE = {
  preferredWidth: 288,
  minWidth: 224,
  margin: 8,
};

function withinViewport(
  pos: { left: number; top: number; width: number; maxHeight: number },
  viewportWidth: number,
  viewportHeight: number,
  margin = BASE.margin,
) {
  expect(pos.left).toBeGreaterThanOrEqual(margin);
  expect(pos.left + pos.width).toBeLessThanOrEqual(viewportWidth - margin);
  expect(pos.top).toBeGreaterThanOrEqual(margin);
  expect(pos.top + pos.maxHeight).toBeLessThanOrEqual(viewportHeight - margin);
}

describe("computeAnchoredPosition", () => {
  it("stays within a standard desktop viewport (1280x800), right-aligned to the trigger", () => {
    const pos = computeAnchoredPosition({
      ...BASE,
      trigger: { top: 120, bottom: 148, left: 1100, right: 1160 },
      viewportWidth: 1280,
      viewportHeight: 800,
    });
    expect(pos.placement).toBe("below");
    expect(pos.width).toBe(288);
    expect(pos.left + pos.width).toBe(1160); // right edge tracks the trigger
    withinViewport(pos, 1280, 800);
  });

  it("pulls back inside the right edge when the trigger sits against it", () => {
    const pos = computeAnchoredPosition({
      ...BASE,
      trigger: { top: 60, bottom: 88, left: 1270, right: 1278 },
      viewportWidth: 1280,
      viewportHeight: 800,
    });
    withinViewport(pos, 1280, 800);
  });

  it("clamps width and horizontal offset at a narrow PWA width (360x740)", () => {
    const pos = computeAnchoredPosition({
      ...BASE,
      trigger: { top: 150, bottom: 178, left: 300, right: 344 },
      viewportWidth: 360,
      viewportHeight: 740,
    });
    expect(pos.width).toBeLessThanOrEqual(360 - BASE.margin * 2);
    withinViewport(pos, 360, 740);
  });

  it("stays inside a very narrow / high-zoom viewport (280x600)", () => {
    const pos = computeAnchoredPosition({
      ...BASE,
      trigger: { top: 90, bottom: 118, left: 240, right: 272 },
      viewportWidth: 280,
      viewportHeight: 600,
    });
    expect(pos.width).toBeLessThanOrEqual(280 - BASE.margin * 2);
    withinViewport(pos, 280, 600);
  });

  it("flips above the trigger and stays bounded when there is little room below", () => {
    const pos = computeAnchoredPosition({
      ...BASE,
      trigger: { top: 560, bottom: 588, left: 900, right: 960 },
      viewportWidth: 1280,
      viewportHeight: 620,
    });
    expect(pos.placement).toBe("above");
    withinViewport(pos, 1280, 620);
  });

  it("never reports a negative or overflowing maxHeight", () => {
    const pos = computeAnchoredPosition({
      ...BASE,
      trigger: { top: 4, bottom: 8, left: 100, right: 160 },
      viewportWidth: 320,
      viewportHeight: 200,
    });
    expect(pos.maxHeight).toBeGreaterThanOrEqual(0);
    withinViewport(pos, 320, 200);
  });
});
