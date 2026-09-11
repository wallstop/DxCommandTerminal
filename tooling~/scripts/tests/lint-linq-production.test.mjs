/*
    Contract tests for tooling~/scripts/lint-linq-production.mjs.

    The rule: production code (Runtime/, Editor/) bans LINQ outright - the using
    directive, fully-qualified System.Linq calls, and static Enumerable calls all
    fail, because each shape invites the allocation-heavy operator chain the ban
    exists to prevent. The negative cases carry the weight: comment-only lines that
    mention the ban's vocabulary, List<T>.ToArray()/CopyTo (instance methods, not
    LINQ), Tests and Generator~ directories, and fixture-shaped code must all stay
    silent, or a sweep buries real production LINQ in noise. The positive cases pin
    every detection shape.
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
const linterPath = path.join(repoRoot, "scripts", "lint-linq-production.mjs");
const { linqViolations } = await import(pathToFileURL(linterPath).href);

function violationsIn(source) {
  return linqViolations(source);
}

/** Shapes that mention LINQ vocabulary and must stay silent. */
const EXEMPT = [
  ["a clean production file", "namespace N\n{\n    public sealed class C\n    {\n    }\n}"],

  [
    "List<T>.ToArray() instance method",
    "List<string> list = new();\nstring[] copy = list.ToArray();",
  ],

  [
    "List<T>.CopyTo instance method",
    "string[] result = new string[list.Count];\nlist.CopyTo(result, 0);",
  ],

  [
    "Dictionary.Keys.CopyTo instance method",
    "string[] keys = new string[map.Count];\nmap.Keys.CopyTo(keys, 0);",
  ],

  [
    "a comment-only line mentioning the ban",
    "// using System.Linq is banned in production code",
  ],

  [
    "a comment-only line mentioning Enumerable",
    "/* Enumerable.Empty would allocate here, so use Array.Empty */",
  ],

  [
    "a doc comment mentioning LINQ",
    "/// <summary>Does not use LINQ.</summary>",
  ],

  [
    "a trailing comment after clean code",
    "string[] copy = list.ToArray(); // copied without LINQ",
  ],

  [
    "a type whose name merely contains Enumerable",
    "sealed class EnumerableFactory\n{\n    void Make() { }\n}",
  ],

  [
    "an identifier containing the word linq",
    "int linqFreeCount = 0;",
  ],
];

/** Shapes that violate the ban. Every one is caught. */
const VIOLATIONS = [
  ["the using directive", "using System.Linq;"],
  ["the indented using directive", "    using System.Linq;"],
  ["the qualified static call", "System.Linq.Enumerable.Empty<string>()"],
  ["the static Enumerable call", "Enumerable.Range(0, 10)"],
  ["the static Enumerable call with a space", "Enumerable .Empty<string>()"],
  ["the using with odd spacing", "using   System.Linq;"],
];

test("exempt shapes stay silent", () => {
  for (const [name, source] of EXEMPT) {
    assert.deepStrictEqual(violationsIn(source), [], name);
  }
});

test("violation shapes are caught", () => {
  for (const [name, source] of VIOLATIONS) {
    assert.strictEqual(violationsIn(source).length, 1, name);
  }
});

test("line numbers are one-based and count every violating line", () => {
  const source = [
    "namespace N",
    "{",
    "    using System.Linq;",
    "    int[] values = Enumerable.Range(0, 3).ToArray();",
    "}",
  ].join("\n");
  const violations = violationsIn(source);
  assert.deepStrictEqual(
    violations.map((violation) => violation.line),
    [3, 4]
  );
});

function runLinter(fixtureRoot) {
  return spawnSync(process.execPath, [linterPath], {
    cwd: repoRoot,
    env: { ...process.env, LINQ_PRODUCTION_ROOTS: fixtureRoot },
    encoding: "utf8",
  });
}

test("a fixture tree with production LINQ fails with a report", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "linq-lint-"));
  const production = path.join(fixtureRoot, "Production");
  fs.mkdirSync(production, { recursive: true });
  fs.writeFileSync(
    path.join(production, "Bad.cs"),
    "using System.Linq;\npublic sealed class C { int[] V() => Enumerable.Range(0, 2).ToArray(); }\n"
  );
  try {
    const result = runLinter(fixtureRoot);
    assert.strictEqual(result.status, 1, result.stderr);
    assert.ok(result.stderr.includes("Bad.cs:1"), result.stderr);
    assert.ok(result.stderr.includes("Bad.cs:2"), result.stderr);
    assert.ok(result.stdout.includes("1 file(s) scanned"), result.stdout);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});

test("a fixture tree with clean production code passes", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "linq-lint-"));
  const production = path.join(fixtureRoot, "Production");
  fs.mkdirSync(production, { recursive: true });
  fs.writeFileSync(
    path.join(production, "Clean.cs"),
    "List<string> list = new();\nstring[] copy = list.ToArray();\n// using System.Linq is banned here\n"
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
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "linq-lint-"));
  try {
    const result = runLinter(fixtureRoot);
    assert.strictEqual(result.status, 1, result.stderr);
    assert.ok(result.stderr.includes("checked nothing"), result.stderr);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});
