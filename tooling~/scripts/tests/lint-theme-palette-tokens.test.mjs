/*
    Contract tests for tooling~/scripts/lint-theme-palette-tokens.mjs.

    The rule: the command palette styles itself only through theme custom properties,
    so (1) every shipped theme block defines the full required-variable list and
    (2) every var() inside a .palette-* rule names a required token and carries a
    fallback, because a bare var() silently renders engine defaults when no theme
    sheet is attached. The negative cases carry the weight: non-palette rules may
    use bare var()s (the terminal rules do), a file with a broken sibling theme
    block still fails per-block, comments stay silent, and BaseStyles-shaped files
    without theme classes must not demand the required-token list. The positive
    cases pin every detection shape against a real, recurring styling bug.
*/
import test from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath, pathToFileURL } from "node:url";

const repoRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../.."
);
const linterPath = path.join(repoRoot, "scripts", "lint-theme-palette-tokens.mjs");
const { ussViolations, extractBlocks, stripComments, REQUIRED_TOKENS } = await import(
  pathToFileURL(linterPath).href
);

/** A complete theme block: every required token, exactly as shipped themes define them. */
const FULL_THEME = [
  ".dark-theme {",
  "    --terminal-bg: rgba(28, 28, 30, 0.9);",
  "    --button-bg: rgba(58, 58, 60, 1);",
  "    --input-field-bg: rgba(44, 44, 46, 0.8);",
  "    --button-selected-bg: rgba(0, 122, 255, 0.85);",
  "    --button-hover-bg: rgba(72, 72, 74, 1);",
  "    --scroll-bg: rgba(58, 58, 60, 1);",
  "    --scroll-inverse-bg: rgba(90, 90, 90, 1);",
  "    --scroll-active-bg: rgba(110, 110, 110, 1);",
  "    --button-text: rgba(242, 242, 247, 0.9);",
  "    --button-selected-text: rgba(255, 255, 255, 1);",
  "    --button-hover-text: rgba(242, 242, 247, 1);",
  "    --input-text-color: rgba(242, 242, 247, 1.0);",
  "    --text-message: rgba(242, 242, 247, 1);",
  "    --text-warning: rgba(255, 204, 0, 1);",
  "    --text-input-echo: rgba(50, 173, 230, 1);",
  "    --text-shell: rgba(142, 142, 147, 1);",
  "    --text-error: rgba(255, 69, 58, 1);",
  "    --scroll-color: rgba(242, 242, 247, 1);",
  "    --caret-color: rgba(242, 242, 247, 1.0);",
  "}",
].join("\n");

/** A .palette-* rule that follows both contracts. */
const CLEAN_PALETTE = [
  ".palette-panel {",
  "    background-color: var(--terminal-bg, rgba(31, 31, 31, 0.96));",
  "}",
  ".palette-input > #unity-text-input {",
  "    color: var(--input-text-color, rgba(235, 235, 235, 1));",
  "}",
].join("\n");

test("stripComments blanks comment contents but keeps newlines", () => {
  const stripped = stripComments("a {\n/* --button-bg: hidden */\n--x: 1;\n}");
  assert.ok(!stripped.includes("--button-bg"), stripped);
  assert.strictEqual(stripped.split("\n").length, 4, "newlines survive");
});

test("extractBlocks mirrors the Editor helper's brace scan", () => {
  const text = ".a { x: 1; }\n.b, .c-theme { y: 2; }";
  const blocks = extractBlocks(text);
  assert.deepStrictEqual(
    blocks.map((block) => block.selector),
    [".a", ".b, .c-theme"]
  );
  assert.ok(blocks[1].contents.includes("y: 2;"), "body is captured");
});

test("a complete theme block and clean palette rules stay silent", () => {
  assert.deepStrictEqual(ussViolations(`${FULL_THEME}\n${CLEAN_PALETTE}`), []);
});

test("a non-theme, non-palette rule is untouched", () => {
  const source = [
    ".terminal-button {",
    "    background-color: var(--button-bg);",
    "}",
    "",
  ].join("\n");
  assert.deepStrictEqual(ussViolations(source), []);
});

test("a file without theme classes owes no required tokens", () => {
  assert.deepStrictEqual(ussViolations(CLEAN_PALETTE), []);
});

test("a theme block missing one token fails naming that token", () => {
  const source = FULL_THEME.replace("--scroll-color: rgba(242, 242, 247, 1);", "");
  const violations = ussViolations(source);
  assert.strictEqual(violations.length, 1, "exactly the removed token");
  assert.strictEqual(violations[0].token, "--scroll-color");
});

test("a broken sibling theme block fails even when the file is otherwise complete", () => {
  const source = `${FULL_THEME}\n\n.broken-theme { --terminal-bg: red; }`;
  const violations = ussViolations(source);
  assert.ok(1 < violations.length, "the sibling block reports its missing tokens");
  assert.ok(
    violations.every((violation) => 1 < violation.line),
    "violations land on the broken block"
  );
});

test("a bare var() inside a palette rule fails", () => {
  const source = `.palette-feedback {\n    color: var(--text-error);\n}`;
  const violations = ussViolations(source);
  assert.deepStrictEqual(
    violations.map((violation) => [violation.token, violation.reason]),
    [["--text-error", "missing var() fallback"]]
  );
});

test("an empty var() fallback fails like a missing one", () => {
  const source = `.palette-feedback {\n    color: var(--text-error, );\n}`;
  const violations = ussViolations(source);
  assert.deepStrictEqual(
    violations.map((violation) => [violation.token, violation.reason]),
    [["--text-error", "missing var() fallback"]]
  );
});

test("a fallback-less nested var() fails", () => {
  const source = `.palette-feedback {\n    color: var(--text-error, var(--button-text));\n}`;
  const violations = ussViolations(source);
  assert.deepStrictEqual(
    violations.map((violation) => [violation.token, violation.reason]),
    [["--button-text", "missing var() fallback"]]
  );
});

test("an unknown token nested inside a fallback fails", () => {
  const source = `.palette-feedback {\n    color: var(--text-error, var(--made-up, red));\n}`;
  const violations = ussViolations(source);
  assert.deepStrictEqual(
    violations.map((violation) => [violation.token, violation.reason]),
    [["--made-up", "token is not guaranteed in every shipped theme"]]
  );
});

test("line numbers anchor at the selector line across CRLF and blank lines", () => {
  const source = [
    ".palette-a {",
    "    color: var(--text-error, red);",
    "}",
    "",
    "",
    ".palette-b {",
    "    color: var(--made-up, red);",
    "}",
  ].join("\r\n");
  const violations = ussViolations(source);
  assert.strictEqual(violations.length, 1);
  assert.strictEqual(violations[0].line, 6, "the violation lands on the violating rule");
});

test("an rgba() fallback with commas parses as one present fallback", () => {
  const source = `.palette-panel {\n    background-color: var(--terminal-bg, rgba(31, 31, 31, 0.96));\n}`;
  assert.deepStrictEqual(ussViolations(source), []);
});

test("REQUIRED_TOKENS never drifts from the Editor helper's required list", () => {
  /*
      The lint's token list must stay a mirror of
      TerminalThemeStyleSheetHelper.RequiredVariables: the Editor inspector
      validates theme packs against that list, so a token added there only
      would make this lint green while themes fail in the inspector, and vice
      versa. Greping the C# literal keeps both lists honest without a Unity
      compile in CI.
   */
  const helperPath = path.join(
    repoRoot,
    "..",
    "Editor",
    "Helper",
    "TerminalThemeStyleSheetHelper.cs"
  );
  const source = fs.readFileSync(helperPath, "utf8");
  const listStart = source.indexOf("RequiredVariables = new()");
  assert.ok(0 <= listStart, "the helper declares RequiredVariables");
  const listEnd = source.indexOf("};", listStart);
  assert.ok(0 <= listEnd, "the RequiredVariables list is closed");
  const declared = [
    ...source.slice(listStart, listEnd).matchAll(/"(--[a-zA-Z0-9-]+)"/g),
  ].map((match) => match[1]);
  assert.ok(0 < declared.length, "at least one required token is declared");
  assert.deepStrictEqual(REQUIRED_TOKENS, declared);
});

test("an unknown token inside a palette rule fails even with a fallback", () => {
  const source = `.palette-input {\n    color: var(--made-up-token, red);\n}`;
  const violations = ussViolations(source);
  assert.ok(
    violations.some(
      (violation) =>
        violation.token === "--made-up-token"
        && violation.reason.includes("not guaranteed")
    )
  );
});

test("palette violations report inside a multi-selector rule", () => {
  const source = [
    ".palette-row:hover, .palette-row-selected {",
    "    background-color: var(--made-up-token, red);",
    "}",
  ].join("\n");
  assert.strictEqual(ussViolations(source).length, 1);
});

test("comments never trip the scan", () => {
  const source = [
    "/*",
    "    .palette-input { color: var(--unmentioned); }",
    "*/",
    CLEAN_PALETTE,
  ].join("\n");
  assert.deepStrictEqual(ussViolations(source), []);
});

function runLinter(fixtureRoot) {
  const env = { ...process.env };
  if (fixtureRoot !== undefined) {
    env.THEME_TOKEN_ROOTS = fixtureRoot;
  }

  return spawnSync(process.execPath, [linterPath], {
    cwd: repoRoot,
    env,
    encoding: "utf8",
  });
}

test("the shipped tree passes", () => {
  const result = runLinter();
  assert.strictEqual(result.status, 0, result.stderr);
  assert.ok(result.stdout.includes("file(s) scanned"), result.stdout);
});

test("a fixture with violations fails with a report", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "theme-token-lint-"));
  fs.writeFileSync(
    path.join(fixtureRoot, "BrokenTheme.uss"),
    `.dark-theme { --terminal-bg: red; }\n`
  );
  fs.writeFileSync(
    path.join(fixtureRoot, "Palette.uss"),
    `.palette-panel { background-color: var(--terminal-bg); }\n`
  );
  try {
    const result = runLinter(fixtureRoot);
    assert.strictEqual(result.status, 1, result.stderr);
    assert.ok(result.stderr.includes("BrokenTheme.uss"), result.stderr);
    assert.ok(
      result.stderr.includes("missing var() fallback '--terminal-bg'"),
      result.stderr
    );
    assert.ok(result.stdout.includes("2 file(s) scanned"), result.stdout);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});

test("a missing root fails loudly instead of scanning nothing", () => {
  const missing = path.join(os.tmpdir(), "theme-token-lint-missing-root");
  fs.rmSync(missing, { recursive: true, force: true });
  try {
    const result = runLinter(missing);
    assert.strictEqual(result.status, 1, result.stderr);
    assert.ok(result.stderr.includes("checked nothing"), result.stderr);
  } finally {
    fs.rmSync(missing, { recursive: true, force: true });
  }
});

test("extractBlocks tolerates an unterminated block", () => {
  const blocks = extractBlocks(".a { x: 1;");
  assert.strictEqual(blocks.length, 1);
  assert.strictEqual(blocks[0].selector, ".a");
});