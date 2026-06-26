# OpHalo — Brand Guide

_Quiet Intelligence. Clear Decisions._

This is the canonical reference for the OpHalo and Keep visual identity. Every
asset in this kit is generated from `tools/generate_brand.py` — do not hand-edit
the SVGs; change the script and re-run (see `README.md`).

---

## 1. Brand architecture

**OpHalo** is the parent brand. **Keep** is a product within OpHalo. They share
one logo family — the same mark and the same wordmark typography — and are
distinguished only by an accent color and, for Keep, the appended word.

| | Wordmark | Accent | Used for |
|---|---|---|---|
| **OpHalo** | `ophalo` | Terracotta `#BF6B43` | Marketing site, company-level |
| **Keep** | `ophalo keep` | Teal `#168A9A` | The operator app |

This is deliberate: Keep reads as a product *of* OpHalo, not a separate company.

---

## 2. The logo

The logo has three forms. Pick the smallest one that carries the meaning the
context needs.

- **Wordmark** (`wordmarks/`) — the outlined word alone. Use in running text,
  email signatures, or where the mark already appears nearby.
- **Mark** (`marks/`) — the symbol alone (the calligraphic **O** with the tilted
  halo woven through it). Use for avatars, favicons, app tiles, watermarks.
- **Lockup** (`logos/`) — mark + wordmark together. **This is the primary logo.**
  Use it as the default wherever space allows.

The wordmark is **Poppins SemiBold (600)**, outlined to vector paths — there is
no font dependency, so it renders identically in email, PDF, and print. The
letter-spacing (`-0.015em`) is baked in.

### App icon

`app-icon/keep-app-icon.svg` is the Keep **tile** — the navy rounded square with
the mark inside. It is the only place the navy tile is used: installed-PWA icon,
favicon, app-store style avatars. Do **not** use the tile inline in a lockup.

---

## 3. Variants

Every wordmark, mark, and lockup ships in four variants:

| Variant | Ink | Halo | Use on |
|---|---|---|---|
| **color** | Navy `#10243E` | Accent (terra/teal) | Light backgrounds (canvas, white) |
| **reversed** | Warm-white `#F8F6F1` | Accent (terra/teal) | Dark / navy backgrounds, photos |
| **mono-black** | Navy `#10243E` | Navy | One-color light (fax, stamp, engraving) |
| **mono-white** | Warm-white `#F8F6F1` | Warm-white | One-color dark, knockout |

`reversed` keeps the brand accent on the halo for color-on-dark use; `mono-white`
is a flat single-color knockout. The `reversed` and `mono-white` SVGs are
**transparent** — they carry no background, so they drop onto any surface.

---

## 4. Color

| Name | Hex | Role |
|---|---|---|
| **Navy** | `#10243E` | Primary. The O, the wordmark, mono ink. |
| **Terracotta** | `#BF6B43` | OpHalo parent accent — the halo. |
| **Teal** | `#168A9A` | Keep product accent — the halo and the word "keep". |
| **Canvas (warm-white)** | `#F8F6F1` | Background; reversed/knockout ink. |
| **Ink** | `#172033` | Body text (not the logo — document text). |

Never recolor the halo to anything outside terracotta (OpHalo) or teal (Keep).
Never set the OpHalo wordmark in terracotta or the Keep wordmark fully in teal —
only the word "keep" takes the accent.

---

## 5. Typography

- **Wordmark / logo:** Poppins SemiBold (600), outlined. Do not retype it in a
  live font — always use the supplied vector wordmark.
- **Headings & UI:** Poppins.
- **Body:** the app's system stack (see `globals.css`); ink color `#172033`.

---

## 6. Clear space & minimum size

- **Clear space:** keep free space equal to the height of the lowercase **o** in
  the wordmark on all four sides of any logo form. Nothing — text, image edge,
  or fold — intrudes into it.
- **Minimum size (lockup):** 120 px wide on screen / 30 mm in print.
- **Minimum size (mark):** 24 px. Below that use the app-icon tile.
- **Favicon:** the app-icon tile is legible to 16 px (verified).

---

## 7. Do / Don't

**Do**
- Default to the full-color lockup.
- Use `reversed` on navy and over photography.
- Give the logo room (see clear space).

**Don't**
- Stretch, rotate, or re-proportion any form.
- Recolor the halo or wordmark outside the palette above.
- Re-typeset the wordmark in a live font (kerning/weight will drift).
- Add effects — shadows, gradients, outlines, glows.
- Place the color logo on a busy or low-contrast background — switch to
  `reversed` or `mono-white`.
- Use the navy tile inline; it is an app icon only.

---

_OpHalo LLC — Tennessee — ophalo.com_
