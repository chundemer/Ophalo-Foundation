// Rasterize the brand-kit SVGs to PNG (for email / social / office docs that
// can't use SVG). Depends on `sharp`, which lives in the web apps' node_modules.
//
// Run from repo root:
//   node --experimental-default-type=module \
//     --import=./web/ophalo-web/node_modules/.../  (not needed)
// Simplest: run with sharp resolvable, e.g. from inside web/ophalo-web:
//   node ../../brand-kit/tools/rasterize.mjs
//
// Color + reversed lockups/marks are exported at retina-friendly sizes.
// Reversed assets are flattened onto navy so they're previewable; the SVGs
// themselves stay transparent.
import { readFileSync, mkdirSync, existsSync } from "fs";
import { dirname, resolve } from "path";
import { fileURLToPath } from "url";
import { createRequire } from "module";

const KIT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const REPO = resolve(KIT, "..");

// sharp lives in the web apps' node_modules, not in brand-kit/. Resolve it from
// whichever app has it installed.
const appWithSharp = ["web/ophalo-web", "web/ophalo-app"]
  .map((a) => resolve(REPO, a, "node_modules/sharp/package.json"))
  .find(existsSync);
if (!appWithSharp) {
  console.error("sharp not found. Run `npm ci` in web/ophalo-web first.");
  process.exit(1);
}
const sharp = createRequire(appWithSharp)("sharp");
const NAVY = "#10243E";
const WHITE = "#F8F6F1";

// [svg path (relative to kit), output basename, [widths], previewBg|null]
const jobs = [
  ["logos/ophalo-lockup-color.svg",        "ophalo-lockup-color",        [400, 800, 1600], WHITE],
  ["logos/ophalo-lockup-reversed.svg",     "ophalo-lockup-reversed",     [400, 800, 1600], NAVY],
  ["logos/ophalo-keep-lockup-color.svg",   "ophalo-keep-lockup-color",   [400, 800, 1600], WHITE],
  ["logos/ophalo-keep-lockup-reversed.svg","ophalo-keep-lockup-reversed",[400, 800, 1600], NAVY],
  ["marks/ophalo-mark-color.svg",          "ophalo-mark-color",          [256, 512, 1024], null],
  ["marks/ophalo-keep-mark-color.svg",     "ophalo-keep-mark-color",     [256, 512, 1024], null],
  ["app-icon/keep-app-icon.svg",           "keep-app-icon",              [192, 512, 1024], null],
];

mkdirSync(`${KIT}/png`, { recursive: true });
for (const [src, base, widths, bg] of jobs) {
  const buf = readFileSync(`${KIT}/${src}`);
  for (const w of widths) {
    let img = sharp(buf, { density: 384 }).resize({ width: w });
    if (bg) img = img.flatten({ background: bg });
    const out = `${KIT}/png/${base}@${w}.png`;
    await img.png().toFile(out);
    console.log("  png/" + `${base}@${w}.png`);
  }
}
console.log("Done.");
