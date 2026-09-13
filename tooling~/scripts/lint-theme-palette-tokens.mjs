/*
    Command palette theme tokens must exist in every shipped theme (issue #17 T10).

    The command palette styles itself exclusively through theme custom properties
    (`Styles/BaseStyles.uss` `.palette-*` rules), so its look derives from each
    theme in `Styles/Themes/*.uss`. Two contracts keep that derivation intact:

    1. Every theme block (`.something-theme` / `.theme-something` class) defines the
       full required-variable list. The Editor helper
       (TerminalThemeStyleSheetHelper.RequiredVariables) already rejects incomplete
       theme packs in the inspector; this lint adds Unity-independent CI coverage so
       a broken theme cannot land unnoticed.
    2. Every `var(--token)` inside a `.palette-*` rule names a required token and
       carries a fallback value. A palette rule without a fallback renders with
       engine defaults (invisible text, transparent panel) whenever no theme sheet
       is attached, so a bare `var()` in a palette rule is a silent styling bug.

    Detection is block-aware: USS selector blocks are split the same way the Editor
    helper splits them, so a theme class that is missing tokens fails even when a
    sibling class in the same file is complete. Comment contents are stripped before
    scanning.

    Exit codes: 0 = clean, 1 = at least one violation (or nothing was scanned).
*/
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../.."
);
// Overridable so the contract tests can point the scan at a fixture tree. Nothing in CI sets it.
const SCAN_ROOTS = process.env.THEME_TOKEN_ROOTS
  ? process.env.THEME_TOKEN_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Styles/Themes", "Styles/BaseStyles.uss"];

const REQUIRED_TOKENS = [
  "--terminal-bg",
  "--button-bg",
  "--input-field-bg",
  "--button-selected-bg",
  "--button-hover-bg",
  "--scroll-bg",
  "--scroll-inverse-bg",
  "--scroll-active-bg",
  "--button-text",
  "--button-selected-text",
  "--button-hover-text",
  "--input-text-color",
  "--text-message",
  "--text-warning",
  "--text-input-echo",
  "--text-shell",
  "--text-error",
  "--scroll-color",
  "--caret-color",
];

export { REQUIRED_TOKENS };

/** Replaces `/* ... *​/` comment contents with spaces, keeping newlines. */
export function stripComments(text) {
  return text.replace(/\/\*[\s\S]*?\*\//g, (match) =>
    match.replace(/[^\n]/g, " ")
  );
}

/**
 * Splits stripped USS text into selector blocks, mirroring the Editor helper's
 * brace scan: the selector is the text between the previous rule's `}` and the
 * `{`, and the body runs to the next `}`. `start` is the block's offset in the
 * stripped text, for one-based line reporting.
 */
export function extractBlocks(text) {
  const blocks = [];
  let lastIndex = 0;
  while (lastIndex < text.length) {
    const braceIndex = text.indexOf("{", lastIndex);
    if (braceIndex < 0) {
      break;
    }

    const previousRuleEnd = text.lastIndexOf("}", braceIndex - 1);
    let start = previousRuleEnd < 0 ? lastIndex : previousRuleEnd + 1;
    while (start < text.length && /\s/.test(text[start])) {
      start++;
    }

    const selector = text.slice(start, braceIndex).trim();
    let endIndex = text.indexOf("}", braceIndex + 1);
    if (endIndex < 0) {
      endIndex = text.length;
    }
    const contents = text.slice(start, endIndex);
    blocks.push({ selector, contents, start });
    lastIndex = endIndex + 1;
  }

  return blocks;
}

function selectorParts(selector) {
  return selector
    .split(",")
    .map((part) => part.trim())
    .filter(Boolean);
}

function isThemeSelector(selector) {
  return selectorParts(selector).some(
    (part) => part.startsWith(".") && (/theme-/i.test(part) || /-theme/i.test(part))
  );
}

function isPaletteSelector(selector) {
  return selectorParts(selector).some(
    (part) => part.startsWith(".palette") && 8 < part.length
  );
}

/** Returns the required tokens a theme block fails to define. */
export function themeViolations(block) {
  const missing = [];
  for (const token of REQUIRED_TOKENS) {
    if (!new RegExp(`${token}\\s*:`, "i").test(block.contents)) {
      missing.push(token);
    }
  }

  return missing;
}

const VAR_PATTERN = /var\(\s*(--[a-zA-Z0-9-]+)\s*(,)?/g;

/**
 * Returns var() problems inside a block: tokens no shipped theme defines, and
 * bare or empty var() references without a usable fallback value. Nested
 * var()s inside a fallback are scanned recursively: `var(--a, var(--b))`
 * must report `--b` even though the outer call has a fallback.
 */
export function paletteTokenViolations(block) {
  const violations = [];
  const known = new Set(REQUIRED_TOKENS);
  collectVarViolations(block.contents, known, violations);
  return violations;
}

function collectVarViolations(source, known, violations) {
  VAR_PATTERN.lastIndex = 0;
  let match;
  while ((match = VAR_PATTERN.exec(source)) !== null) {
    const [, token, comma] = match;
    if (!comma) {
      violations.push({ token, reason: "missing var() fallback" });
      continue;
    }

    /*
        Walk to the outer var()'s matching close paren to delimit the
        fallback; nested var()s inside it are scanned recursively, and the
        outer scan resumes after the close so inner tokens report once.
     */
    const fallbackStart = match.index + match[0].length;
    let depth = 1;
    let fallbackEnd = fallbackStart;
    while (fallbackEnd < source.length && 0 < depth) {
      const ch = source[fallbackEnd];
      if (ch === "(") {
        depth++;
      } else if (ch === ")") {
        depth--;
      }

      fallbackEnd++;
    }

    if (depth !== 0) {
      break;
    }

    const fallback = source.slice(fallbackStart, fallbackEnd - 1);
    if (fallback.trim().length === 0) {
      violations.push({ token, reason: "missing var() fallback" });
      VAR_PATTERN.lastIndex = fallbackEnd;
      continue;
    }

    if (!known.has(token)) {
      violations.push({
        token,
        reason: "token is not guaranteed in every shipped theme",
      });
    }

    if (fallback.includes("var(")) {
      collectVarViolations(fallback, known, violations);
    }

    VAR_PATTERN.lastIndex = fallbackEnd;
  }
}

function lineAt(text, index) {
  let line = 1;
  for (let i = 0; i < index && i < text.length; i++) {
    if (text[i] === "\n") {
      line++;
    }
  }

  return line;
}

/** Returns violations for one stripped USS file: theme blocks missing tokens and palette rules with bad var()s. */
export function ussViolations(rawText) {
  const text = stripComments(rawText);
  const violations = [];
  for (const block of extractBlocks(text)) {
    if (isThemeSelector(block.selector)) {
      for (const token of themeViolations(block)) {
        violations.push({
          line: lineAt(text, block.start),
          token,
          reason: "theme block does not define",
        });
      }
    }

    if (isPaletteSelector(block.selector)) {
      for (const violation of paletteTokenViolations(block)) {
        violations.push({
          line: lineAt(text, block.start),
          token: violation.token,
          reason: violation.reason,
        });
      }
    }
  }

  return violations;
}

function listFiles(root) {
  const absolute = path.resolve(REPO_ROOT, root);
  if (!fs.existsSync(absolute)) {
    return [];
  }

  const stat = fs.statSync(absolute);
  if (stat.isFile()) {
    return absolute.endsWith(".uss") ? [absolute] : [];
  }

  const found = [];
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const child = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (entry.name === "bin" || entry.name === "obj" || entry.name === "node_modules") {
          continue;
        }
        walk(child);
      } else if (entry.name.endsWith(".uss")) {
        found.push(child);
      }
    }
  };
  walk(absolute);
  return found;
}

function main() {
  const files = SCAN_ROOTS.flatMap((root) => listFiles(root));
  if (files.length === 0) {
    console.error(
      `[theme-palette-tokens] ERROR: no .uss files were found under ${SCAN_ROOTS.join(", ")}, so this run checked nothing. A scan that matched nothing is the absence of a measurement, not a pass.`
    );
    return 1;
  }

  let violations = 0;
  for (const file of files) {
    const fileViolations = ussViolations(fs.readFileSync(file, "utf8"));
    if (fileViolations.length === 0) {
      continue;
    }

    const relative = path.relative(REPO_ROOT, file);
    for (const violation of fileViolations) {
      violations++;
      console.error(
        `${relative}:${violation.line}: ${violation.reason} '${violation.token}'`
      );
    }
  }

  console.log(`[theme-palette-tokens] ${files.length} file(s) scanned`);
  return 0 < violations ? 1 : 0;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  /*
      exitCode, not exit: on Windows, writes to a piped stderr are asynchronous, and a
      process.exit() would truncate the violation report this process is still flushing.
      Letting the loop drain keeps the report whole and the exit code identical.
   */
  process.exitCode = main(process.argv.slice(2));
}