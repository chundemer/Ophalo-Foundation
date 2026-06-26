#!/usr/bin/env python3
"""
OpHalo / Keep brand-kit generator.

Reads the canonical in-repo Poppins SemiBold (poppins-600.woff2), outlines the
wordmarks to vector paths (no font dependency), and emits the full logo family:
wordmarks, marks, and lockups, each in color / reversed / mono-black / mono-white.

Run from repo root:  ./brand-kit/tools/.venv/bin/python brand-kit/tools/generate_brand.py
(or any python with `fonttools` + `brotli` installed)

This is the single source of truth for every SVG in brand-kit/. Do not hand-edit
the generated SVGs — change this script and re-run.
"""

from __future__ import annotations
import os
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.boundsPen import BoundsPen

REPO = Path(__file__).resolve().parents[2]
FONT = REPO / "web/ophalo-web/src/app/fonts/poppins-600.woff2"
OUT = REPO / "brand-kit"

# ---- Brand palette (resolved from the apps' globals.css + marks) -------------
NAVY = "#10243E"   # primary ink / O / wordmark
TERRA = "#BF6B43"  # OpHalo parent accent (halo)
TEAL = "#168A9A"   # Keep product accent (halo + "keep")
WHITE = "#F8F6F1"  # warm-white canvas / reversed ink

LETTER_SPACING_EM = -0.015  # matches .site-logo-word in globals.css

# ---- Font load --------------------------------------------------------------
_font = TTFont(str(FONT))
UPM = _font["head"].unitsPerEm
_cmap = _font.getBestCmap()
_glyphs = _font.getGlyphSet()
_hmtx = _font["hmtx"]


def _glyph(ch: str):
    gn = _cmap[ord(ch)]
    pen = SVGPathPen(_glyphs)
    _glyphs[gn].draw(pen)
    bpen = BoundsPen(_glyphs)
    _glyphs[gn].draw(bpen)
    adv = _hmtx[gn][0]
    return pen.getCommands(), bpen.bounds, adv  # d, (xmin,ymin,xmax,ymax)|None, advance


def layout_word(text: str):
    """Return list of (penX, d, bounds, advance) in font units (y-up) plus word bbox."""
    spacing = LETTER_SPACING_EM * UPM
    placed = []
    penX = 0.0
    xmin = ymin = float("inf")
    xmax = ymax = float("-inf")
    for ch in text:
        d, b, adv = _glyph(ch)
        placed.append((penX, d, b, adv))
        if b:  # space has no outline
            x0, y0, x1, y1 = b
            xmin = min(xmin, penX + x0)
            xmax = max(xmax, penX + x1)
            ymin = min(ymin, y0)
            ymax = max(ymax, y1)
        penX += adv + spacing
    return placed, (xmin, ymin, xmax, ymax)


def wordmark_group(text: str, color_for, scale: float = 0.1):
    """
    Build an SVG <g> for the outlined word. Font units are scaled by `scale`
    (default 0.1 -> 1000upm becomes 100 user units of cap-square).
    `color_for(index)` returns the fill for the glyph at that string index.
    Returns (group_svg, width, height) in scaled user units.
    """
    placed, (xmin, ymin, xmax, ymax) = layout_word(text)
    w = (xmax - xmin) * scale
    h = (ymax - ymin) * scale
    # Flip y-up -> y-down, shift so the word's top-left sits at (0,0).
    inner = "".join(
        f'<g transform="translate({penX:.2f} 0)"><path d="{d}" fill="{color_for(i)}"/></g>'
        for i, (penX, d, b, adv) in enumerate(placed) if d
    )
    g = (
        f'<g transform="scale({scale}) translate({-xmin:.2f} {ymax:.2f}) scale(1 -1)">'
        f'{inner}</g>'
    )
    return g, w, h


# ---- Mark (unified open mark: calligraphic O + tilted halo) ------------------
# Geometry lifted verbatim from web/ophalo-web/public/brand/ophalo-mark.svg.
_mark_counter = [0]


def mark_inner(halo: str, o: str) -> str:
    """Inner markup of the open mark, viewBox 0 0 240 240, parametric colors."""
    _mark_counter[0] += 1
    n = _mark_counter[0]
    return f"""<defs>
  <clipPath id="gapClip{n}"><circle cx="138" cy="96" r="24"/></clipPath>
  <mask id="gap{n}">
    <rect x="0" y="0" width="240" height="240" fill="white"/>
    <g clip-path="url(#gapClip{n})">
      <ellipse cx="148" cy="86" rx="50" ry="19" fill="none" stroke="black" stroke-width="20" transform="rotate(-24 148 86)"/>
    </g>
  </mask>
</defs>
<ellipse cx="148" cy="86" rx="50" ry="19" fill="none" stroke="{halo}" stroke-width="12" transform="rotate(-24 148 86)"/>
<path mask="url(#gap{n})" fill-rule="evenodd" fill="{o}" d="M 110 70 A 64 64 0 1 0 110 198 A 64 64 0 1 0 110 70 Z M 110 88 A 38 46 0 1 1 110 180 A 38 46 0 1 1 110 88 Z"/>"""


# ---- Variants ---------------------------------------------------------------
# Each variant defines: halo, o (mark) + ophalo-word color + keep-word color.
def variants(accent: str):
    return {
        "color":      dict(halo=accent, o=NAVY,  oph=NAVY,  keep=accent, bg=None),
        "reversed":   dict(halo=accent, o=WHITE, oph=WHITE, keep=WHITE,  bg=None),
        "mono-black": dict(halo=NAVY,   o=NAVY,  oph=NAVY,  keep=NAVY,   bg=None),
        "mono-white": dict(halo=WHITE,  o=WHITE, oph=WHITE, keep=WHITE,  bg=None),
    }


def svg(viewbox_w, viewbox_h, body, bg=None) -> str:
    rect = f'<rect width="{viewbox_w:.2f}" height="{viewbox_h:.2f}" fill="{bg}"/>' if bg else ""
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" '
        f'viewBox="0 0 {viewbox_w:.2f} {viewbox_h:.2f}" '
        f'width="{viewbox_w:.2f}" height="{viewbox_h:.2f}" role="img">\n'
        f'{rect}{body}\n</svg>\n'
    )


def write(path: Path, content: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content)
    print(f"  {path.relative_to(REPO)}")


PAD = 0.08  # 8% padding around content for breathing room


def gen_wordmark(brand: str, text: str, accent: str):
    for name, v in variants(accent).items():
        def color_for(i, v=v, text=text):
            # color the "keep" portion with the keep color
            return v["keep"] if (brand == "keep" and i >= text.index("keep")) else v["oph"]
        g, w, h = wordmark_group(text, color_for)
        pad = h * PAD
        vw, vh = w + pad * 2, h + pad * 2
        body = f'<g transform="translate({pad:.2f} {pad:.2f})">{g}</g>'
        write(OUT / "wordmarks" / f"{brand_slug(brand)}-wordmark-{name}.svg", svg(vw, vh, body, v["bg"]))


def gen_mark(brand: str, accent: str):
    for name, v in variants(accent).items():
        size = 240.0
        body = mark_inner(v["halo"], v["o"])
        write(OUT / "marks" / f"{brand_slug(brand)}-mark-{name}.svg",
              svg(size, size, body, v["bg"]))


def gen_lockup(brand: str, text: str, accent: str):
    for name, v in variants(accent).items():
        def color_for(i, v=v, text=text):
            return v["keep"] if (brand == "keep" and i >= text.index("keep")) else v["oph"]
        wg, ww, wh = wordmark_group(text, color_for)

        mark_h = wh * 1.62                 # mark a touch taller than the word
        gap = mark_h * 0.34                # space between mark and word
        word_y = (mark_h - wh) / 2.0       # vertically center word to mark
        content_w = mark_h + gap + ww
        content_h = mark_h
        pad = mark_h * PAD
        vw, vh = content_w + pad * 2, content_h + pad * 2

        mark = (
            f'<svg x="{pad:.2f}" y="{pad:.2f}" width="{mark_h:.2f}" height="{mark_h:.2f}" '
            f'viewBox="0 0 240 240">{mark_inner(v["halo"], v["o"])}</svg>'
        )
        word = (
            f'<g transform="translate({pad + mark_h + gap:.2f} {pad + word_y:.2f})">{wg}</g>'
        )
        write(OUT / "logos" / f"{brand_slug(brand)}-lockup-{name}.svg",
              svg(vw, vh, mark + word, v["bg"]))


def brand_slug(brand: str) -> str:
    return "ophalo" if brand == "ophalo" else "ophalo-keep"


def gen_app_icon():
    # The established Keep tile (navy rounded square) — copied verbatim as the
    # canonical app-icon / avatar asset.
    src = REPO / "web/ophalo-app/public/brand/keep-mark.svg"
    write(OUT / "app-icon" / "keep-app-icon.svg", src.read_text())


if __name__ == "__main__":
    print("Generating brand-kit SVGs:")
    gen_wordmark("ophalo", "ophalo", TERRA)
    gen_wordmark("keep", "ophalo keep", TEAL)
    gen_mark("ophalo", TERRA)
    gen_mark("keep", TEAL)
    gen_lockup("ophalo", "ophalo", TERRA)
    gen_lockup("keep", "ophalo keep", TEAL)
    gen_app_icon()
    print("Done.")
