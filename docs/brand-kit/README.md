# OpHalo brand kit

The canonical, handoff-ready logo assets for **OpHalo** and **Keep**. Self-contained:
every wordmark is outlined to vector paths, so nothing here depends on a font being
installed.

👉 Brand rules — colors, clear space, do/don't — are in **[`BRAND.md`](./BRAND.md)**.

## What's here

```
brand-kit/
├── logos/      Primary logo — mark + wordmark lockup (use this by default)
├── wordmarks/  The outlined word alone (ophalo / ophalo keep)
├── marks/      The symbol alone (O + halo)
├── app-icon/   Keep navy tile — installed-app icon / favicon / avatar only
├── png/        Raster exports of the common assets (email, social, Office)
└── tools/      Generator scripts (source of truth) + requirements
```

Each of `logos/`, `wordmarks/`, `marks/` ships four variants per brand:
`-color` · `-reversed` (light ink, accent halo, transparent) · `-mono-black` ·
`-mono-white`. See `BRAND.md` §3 for which to use where.

**Prefer SVG.** Use the PNGs in `png/` only where SVG isn't supported (some email
clients, Office docs). PNGs are exported at `@400/@800/@1600` (lockups) and
`@256/@512/@1024` (marks).

## Regenerating

The SVGs are generated from the in-repo Poppins SemiBold (`poppins-600.woff2`),
so they always match what the apps render. Don't hand-edit the SVGs — edit the
script and re-run.

```bash
# from repo root — one-time setup
python3 -m venv brand-kit/tools/venv
brand-kit/tools/venv/bin/pip install -r brand-kit/tools/requirements.txt

# regenerate SVGs
brand-kit/tools/venv/bin/python brand-kit/tools/generate_brand.py

# regenerate PNGs (uses sharp from web/ophalo-web/node_modules)
node brand-kit/tools/rasterize.mjs
```

> `brand-kit/tools/venv/` is git-ignored — recreate it with the commands above.
