// Validates that every `var(--token)` reference in ophalo-app source resolves to a token
// defined in the app's inlined :root block, and that the inlined block stays in sync with
// the shared source of truth (web/shared/styles/ophalo-tokens.css). See GAP-028.
//
// Usage: node scripts/check-css-tokens.mjs
import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const APP_ROOT = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const SRC_DIR = path.join(APP_ROOT, "src");
const APP_TOKENS_CSS = path.join(SRC_DIR, "styles", "app.css");
const SHARED_TOKENS_CSS = path.join(APP_ROOT, "..", "shared", "styles", "ophalo-tokens.css");

const SOURCE_EXTENSIONS = new Set([".ts", ".tsx", ".css"]);

export function extractDefinedTokens(cssText) {
  const rootBlockMatch = cssText.match(/:root\s*{([^}]*)}/);
  if (!rootBlockMatch) return new Set();
  const tokens = new Set();
  for (const match of rootBlockMatch[1].matchAll(/(--[a-zA-Z0-9-]+)\s*:/g)) {
    tokens.add(match[1]);
  }
  return tokens;
}

export function extractUsedTokens(sourceText) {
  const tokens = new Set();
  for (const match of sourceText.matchAll(/var\((--[a-zA-Z0-9-]+)/g)) {
    tokens.add(match[1]);
  }
  return tokens;
}

function collectSourceFiles(dir) {
  const files = [];
  for (const entry of readdirSync(dir)) {
    const fullPath = path.join(dir, entry);
    const stats = statSync(fullPath);
    if (stats.isDirectory()) {
      files.push(...collectSourceFiles(fullPath));
    } else if (SOURCE_EXTENSIONS.has(path.extname(entry))) {
      files.push(fullPath);
    }
  }
  return files;
}

export function findUndefinedTokenUsages(srcDir, definedTokens) {
  const undefinedUsages = [];
  for (const filePath of collectSourceFiles(srcDir)) {
    const usedTokens = extractUsedTokens(readFileSync(filePath, "utf8"));
    for (const token of usedTokens) {
      if (!definedTokens.has(token)) {
        undefinedUsages.push({ filePath, token });
      }
    }
  }
  return undefinedUsages;
}

export function findTokenSyncDrift(appTokens, sharedTokens) {
  const missingFromApp = [...sharedTokens].filter((t) => !appTokens.has(t));
  const missingFromShared = [...appTokens].filter((t) => !sharedTokens.has(t));
  return { missingFromApp, missingFromShared };
}

function main() {
  const appCss = readFileSync(APP_TOKENS_CSS, "utf8");
  const sharedCss = readFileSync(SHARED_TOKENS_CSS, "utf8");
  const appTokens = extractDefinedTokens(appCss);
  const sharedTokens = extractDefinedTokens(sharedCss);

  const undefinedUsages = findUndefinedTokenUsages(SRC_DIR, appTokens);
  const { missingFromApp, missingFromShared } = findTokenSyncDrift(appTokens, sharedTokens);

  let failed = false;

  if (undefinedUsages.length > 0) {
    failed = true;
    console.error("Undefined CSS token references found:");
    for (const { filePath, token } of undefinedUsages) {
      console.error(`  ${path.relative(APP_ROOT, filePath)}: var(${token}) is not defined in styles/app.css`);
    }
  }

  if (missingFromApp.length > 0) {
    failed = true;
    console.error(`Tokens defined in ophalo-tokens.css but missing from app.css: ${missingFromApp.join(", ")}`);
  }

  if (missingFromShared.length > 0) {
    failed = true;
    console.error(`Tokens defined in app.css but missing from ophalo-tokens.css: ${missingFromShared.join(", ")}`);
  }

  if (failed) {
    process.exit(1);
  }

  console.log("CSS token usage and sync check passed.");
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  main();
}
