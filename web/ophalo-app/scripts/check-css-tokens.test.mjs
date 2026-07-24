import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  extractDefinedTokens,
  extractUsedTokens,
  findUndefinedTokenUsages,
  findTokenSyncDrift,
} from "./check-css-tokens.mjs";

const APP_ROOT = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const SRC_DIR = path.join(APP_ROOT, "src");
const APP_TOKENS_CSS = path.join(SRC_DIR, "styles", "app.css");
const SHARED_TOKENS_CSS = path.join(APP_ROOT, "..", "shared", "styles", "ophalo-tokens.css");

describe("extractDefinedTokens", () => {
  it("collects custom property names from a :root block", () => {
    const css = `:root {\n  --a: #fff;\n  --b: 1px;\n}`;
    expect(extractDefinedTokens(css)).toEqual(new Set(["--a", "--b"]));
  });
});

describe("extractUsedTokens", () => {
  it("collects var(--token) references from source text", () => {
    const source = `className="text-[var(--a)] bg-[var(--b)] text-[var(--a)]"`;
    expect(extractUsedTokens(source)).toEqual(new Set(["--a", "--b"]));
  });
});

describe("findUndefinedTokenUsages", () => {
  it("flags every var(--...) reference in ophalo-app/src not defined in app.css", () => {
    const definedTokens = extractDefinedTokens(readFileSync(APP_TOKENS_CSS, "utf8"));
    const undefinedUsages = findUndefinedTokenUsages(SRC_DIR, definedTokens);
    expect(undefinedUsages).toEqual([]);
  });

  it("detects a sentinel undefined token when no tokens are treated as defined", () => {
    const undefinedUsages = findUndefinedTokenUsages(SRC_DIR, new Set());
    expect(undefinedUsages.length).toBeGreaterThan(0);
  });
});

describe("findTokenSyncDrift", () => {
  it("reports no drift between app.css and the shared token source of truth", () => {
    const appTokens = extractDefinedTokens(readFileSync(APP_TOKENS_CSS, "utf8"));
    const sharedTokens = extractDefinedTokens(readFileSync(SHARED_TOKENS_CSS, "utf8"));
    expect(findTokenSyncDrift(appTokens, sharedTokens)).toEqual({
      missingFromApp: [],
      missingFromShared: [],
    });
  });

  it("detects tokens missing on either side", () => {
    const appTokens = new Set(["--a", "--b"]);
    const sharedTokens = new Set(["--a", "--c"]);
    expect(findTokenSyncDrift(appTokens, sharedTokens)).toEqual({
      missingFromApp: ["--c"],
      missingFromShared: ["--b"],
    });
  });
});
