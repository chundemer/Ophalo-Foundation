/**
 * Copies WOFF2 variable font files from @fontsource-variable packages to public/fonts/
 * so they can be served from the app origin (ADR-379) and preloaded from index.html.
 *
 * Runs automatically via postinstall. Re-run manually with: pnpm copy-fonts
 */
import { copyFileSync, mkdirSync, existsSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("..", import.meta.url));
const dest = join(root, "public", "fonts");

if (!existsSync(dest)) mkdirSync(dest, { recursive: true });

function findWoff2(dir, fragment) {
  try {
    const files = readdirSync(dir);
    return files.find((f) => f.includes(fragment) && f.endsWith(".woff2"));
  } catch {
    return undefined;
  }
}

const fonts = [
  {
    pkg: "@fontsource-variable/inter",
    fragment: "latin-wght-normal",
    dest: "inter-variable.woff2",
  },
  {
    pkg: "@fontsource-variable/source-serif-4",
    fragment: "latin-wght-normal",
    dest: "source-serif-4-variable.woff2",
  },
];

let ok = true;
for (const { pkg, fragment, dest: destFile } of fonts) {
  const dir = join(root, "node_modules", pkg, "files");
  const file = findWoff2(dir, fragment);
  if (!file) {
    console.error(`[copy-fonts] NOT FOUND: ${pkg}/files/*${fragment}*.woff2`);
    ok = false;
    continue;
  }
  copyFileSync(join(dir, file), join(dest, destFile));
  console.log(`[copy-fonts] ${file} → public/fonts/${destFile}`);
}

if (!ok) process.exit(1);
