/*
    Contract tests for tooling~/scripts/lint-string-equality.mjs.

    The rule: shipped code (Runtime/, Editor/) never compares strings with
    `==` / `!=` when either operand is a string literal or `string.Empty`;
    such comparisons must name their rule with `string.Equals` and an explicit
    `StringComparison` (context.md rule 7). The negative cases carry the
    weight: identifier-vs-identifier equality (which the tokenizer cannot
    type-check), `== null`, char literals, numeric/enum comparisons, and
    `==` inside comments or string literals must all stay silent, or a sweep
    buries real violations in noise. The positive cases pin every detection
    shape, including verbatim, interpolated-hole, and raw-string operands.
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
const linterPath = path.join(repoRoot, "scripts", "lint-string-equality.mjs");
const { stringEqualityViolations } = await import(pathToFileURL(linterPath).href);

function linesIn(source) {
  return stringEqualityViolations(source).map((violation) => violation.line);
}

function columnsIn(source) {
  return stringEqualityViolations(source).map((violation) => violation.column);
}

/** Shapes that must stay silent. */
const EXEMPT = [
  [
    "identifier-vs-identifier equality is out of the tokenizer's reach",
    "if (font.name == defaultFontName)\n{\n    return;\n}",
  ],

  ["null comparisons", "if (font != null && _persistedFont == null)\n{\n}"],

  [
    "char literals are not string operands",
    "if (firstChar == '\"' || c != '\\\\')\n{\n}",
  ],

  ["numeric comparisons", "int index = 0;\nif (index == 0 || count != 1)\n{\n}"],

  [
    "enum and reference comparisons",
    "if (_state == TerminalState.Closed && CurrentFont == font)\n{\n}",
  ],

  [
    "the correct explicit form",
    "if (string.Equals(fieldName, \"Array\", StringComparison.Ordinal))\n{\n}",
  ],

  [
    "the correct explicit form, ignoring case",
    "if (string.Equals(font.name, defaultFontName, StringComparison.OrdinalIgnoreCase))\n{\n}",
  ],

  [
    "an operator declaration is not a comparison",
    "public static bool operator ==(TerminalThemeConfiguration left, TerminalThemeConfiguration right)\n{\n    return left.font == right.font;\n}",
  ],

  [
    "equality inside a string literal is data",
    'string note = "if (a == \\"b\\") it is data";',
  ],

  [
    "equality inside a char literal is data",
    'const string pattern = \'=\';',
  ],

  ["a comment-only line mentioning the rule", '// if (a == "b") is banned here'],

  [
    "a block comment mentioning the rule",
    '/*\n    if (a == "b") is banned; string.Equals states the comparison.\n*/\nint x = 0;',
  ],

  [
    "interpolated holes with non-string comparisons",
    'Log($"{a > b} and {c == d} stay silent");',
  ],

  [
    "attribute string arguments without equality",
    '[Tooltip("Rate from 0 to 1")]\npublic float rate = 0.5f;',
  ],

  [
    "a raw string whose contents hold an equality",
    'var text = """\n    if (a == "b") is data\n    """;',
  ],

  [
    "a verbatim identifier that is not a string",
    "if (@event != null)\n{\n}",
  ],
];

/** Shapes that violate the rule. Every one is caught once. */
const VIOLATIONS = [
  ["the right string literal", 'if (name == "value")\n{\n}'],
  ["the left string literal", 'if ("value" == name)\n{\n}'],
  ["the negated string literal", 'if (name != "value")\n{\n}'],
  ["string.Empty on the right", "if (name == string.Empty)\n{\n}"],
  ["string.Empty on the left", "if (string.Empty != name)\n{\n}"],
  ["String.Empty (alias)", "if (name == String.Empty)\n{\n}"],
  ["a verbatim literal", 'if (path == @"C:\\\\temp")\n{\n}'],
  ["an interpolated literal operand", 'if (name == $"{prefix}")\n{\n}'],
  [
    "an equality inside an interpolated hole",
    'Log($"flag: {name == \\"on\\"}");',
  ],
  ["a raw-string operand", 'if (text == """\n    body\n    """)\n{\n}'],
  ["a lambda body comparison", 'comparison = (a, b) => a == "b";'],
  ["a switch-when comparison", 'if (command is { } c when c == "run")\n{\n}'],
];

test("exempt shapes stay silent", () => {
  for (const [name, source] of EXEMPT) {
    assert.deepStrictEqual(linesIn(source), [], name);
  }
});

test("violation shapes are caught", () => {
  for (const [name, source] of VIOLATIONS) {
    assert.strictEqual(linesIn(source).length, 1, name);
  }
});

test("violations report one-based lines and columns", () => {
  const source = [
    "namespace N",
    "{",
    '    bool A(string name) => name == "a";',
    "    bool B() => string.Empty == null;",
    '    bool C(string name) => name != "c";',
    "}",
  ].join("\n");
  assert.deepStrictEqual(linesIn(source), [3, 4, 5]);
  assert.deepStrictEqual(columnsIn(source), [33, 30, 33]);
});

test("a raw-string operand is detected at the closing fence line", () => {
  const source = 'if (text == """\n    body\n    """)\n{\n}';
  assert.deepStrictEqual(linesIn(source), [1]);
});

function runLinter(fixtureRoot) {
  return spawnSync(process.execPath, [linterPath], {
    cwd: repoRoot,
    env: { ...process.env, STRING_EQUALITY_ROOTS: fixtureRoot },
    encoding: "utf8",
  });
}

test("a fixture tree with string-literal equality fails with a report", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "string-equality-lint-"));
  const production = path.join(fixtureRoot, "Production");
  fs.mkdirSync(production, { recursive: true });
  fs.writeFileSync(
    path.join(production, "Bad.cs"),
    'public sealed class C\n{\n    bool A(string name) => name == "a";\n    bool B() => string.Empty == null;\n}\n'
  );
  try {
    const result = runLinter(fixtureRoot);
    assert.strictEqual(result.status, 1, result.stderr);
    assert.ok(result.stderr.includes("Bad.cs:3"), result.stderr);
    assert.ok(result.stderr.includes("Bad.cs:4"), result.stderr);
    assert.ok(result.stderr.includes("without an explicit StringComparison"), result.stderr);
    assert.ok(result.stdout.includes("1 file(s) scanned"), result.stdout);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});

test("a fixture tree with explicit comparisons passes", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "string-equality-lint-"));
  const production = path.join(fixtureRoot, "Production");
  fs.mkdirSync(production, { recursive: true });
  fs.writeFileSync(
    path.join(production, "Clean.cs"),
    'public sealed class C\n{\n    // if (a == "b") is documentation, not code\n    bool A(string name, string other) => string.Equals(name, other, StringComparison.OrdinalIgnoreCase);\n}\n'
  );
  try {
    const result = runLinter(fixtureRoot);
    assert.strictEqual(result.status, 0, result.stderr);
    assert.ok(result.stdout.includes("1 file(s) scanned"), result.stdout);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});

test("a missing root fails loudly instead of scanning nothing", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "string-equality-lint-"));
  try {
    const result = runLinter(fixtureRoot);
    assert.strictEqual(result.status, 1, result.stderr);
    assert.ok(result.stderr.includes("checked nothing"), result.stderr);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});
