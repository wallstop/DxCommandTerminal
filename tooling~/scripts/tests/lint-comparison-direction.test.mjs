/*
    Contract tests for tooling~/scripts/lint-comparison-direction.mjs. Adapted
    from unity-helpers' scripts/tests/test-lint-comparison-direction.js (MIT,
    Ambiguous-Interactive).

    The linter's whole value is telling a comparison from the four other things `>` means in C#, so
    the negative cases here are the point: a generic closer, a lambda arrow, a shift, a relational
    pattern and an `operator >` declaration must all stay quiet, or the sweep it drives corrupts
    source. The positive cases pin the shapes it must never stop seeing.
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
const linterPath = path.join(repoRoot, "scripts", "lint-comparison-direction.mjs");
const { tokenize, markGenerics, findViolations, planFix } = await import(
  pathToFileURL(linterPath).href
);


function violationsIn(source) {
  const tokens = tokenize(source);
  return findViolations(tokens, markGenerics(tokens));
}

function fixed(source) {
  const tokens = tokenize(source);
  const generic = markGenerics(tokens);
  const violations = findViolations(tokens, generic);
  let text = source;
  const edits = violations
    .map((violation) => planFix(source, tokens, generic, violation))
    .filter((plan) => plan.replacement !== undefined)
    .sort((a, b) => b.start - a.start);
  for (const edit of edits) {
    text = text.slice(0, edit.start) + edit.replacement + text.slice(edit.end);
  }
  return text;
}

/** Shapes that contain `>` and are NOT comparisons. Every one of these must stay silent. */
const EXEMPT = [
  ["a generic closer", "Dictionary<string, List<int>> map = null;"],
  ["a nested generic closer", "Func<int, Func<int, bool>> f = null;"],
  ["a tuple inside a generic", "Dictionary<string, (int Line, string Text)> d = new();"],
  ["a tuple type argument", "Buffers<(string, Func<object, object>)>.List.Get();"],
  ["a lambda arrow", "Func<int, int> f = x => x;"],
  ["a right shift", "int y = value >> 2;"],
  ["a shift assignment", "value >>= 2;"],
  ["a relational pattern", "bool ok = c is >= (char)65 and <= (char)90;"],
  ["a property pattern", "bool ok = obj is { Count: > 0 };"],
  ["a switch-expression arm", "int r = v switch { > 5 => 1, _ => 0 };"],
  ["a switch-statement label", "switch (v) { case > 1: break; }"],
  ["an or-pattern", "bool ok = value is < 0 or > 100;"],
  ["an operator declaration", "public static bool operator >(A a, A b) => false;"],
  ["a line comment", "// count > 0"],
  ["a block comment", "/* count > 0 */"],
  ["a doc comment", "/// <summary>count > 0</summary>"],
  ["a string literal", 'string s = "a > b";'],
  ["a verbatim string literal", 'string s = @"a > b";'],
  ["a character literal", "char c = '>';"],
  ["a generic constraint", "void M<T>() where T : IComparable<T> { }"],
  ["a pointer dereference", "unsafe { int v = p->field; }"],
  ["a raw string literal", 'string s = """a > b""";'],
];

/** Shapes that ARE comparisons. Every one of these must be reported. */
const FLAGGED = [
  ["a plain greater-than", "if (a > b) { }", 1],
  ["a greater-or-equal", "if (a >= b) { }", 1],
  ["a comparison beside a generic", "if (Comparer<int>.Default.Compare(a, b) > 0) { }", 1],
  ["a comparison after a less-than", "if (a < b && c > d) { }", 1],
  ["a loop condition", "for (int i = n; i > 0; i--) { }", 1],
  ["an indexer comparison", "if (arr[i] >= arr[j]) { }", 1],
  ["a comparison inside a lambda body", "Func<int, bool> f = x => x > 0;", 1],
  ["a comparison in an interpolation hole", 'string s = $"{x > y}";', 1],
  [
    "a comparison in an interpolation hole beside an escaped quote",
    'string s = $"{x > \\"y\\"}";',
    1,
  ],
  [
    "a comparison after an interpolated string with an escaped quote in its hole",
    'string s = $"a {\\"b\\"}";\nif (i > 0) { }',
    1,
  ],
  ["a comparison after an increment", "if (i++ > 0) { }", 1],
  ["both operators on one line", "if (a > b && c >= d) { }", 2],
  ["a property pattern beside a comparison", "if (o is { Count: > 0 } && n > 0) { }", 1],
];

test("exempt shapes stay silent", () => {
  for (const [name, source] of EXEMPT) {
    const found = violationsIn(source);
    assert.strictEqual(
      found.length,
      0,
      `${name}: expected no violation, got ${found.length}: ${JSON.stringify(
        found.map((v) => v.operator)
      )}`
    );
  }
});

test("comparison shapes are flagged", () => {
  for (const [name, source, expected] of FLAGGED) {
    const found = violationsIn(source);
    assert.strictEqual(found.length, expected, `${name}: expected ${expected}, got ${found.length}`);
  }
});

test("fix swaps operands and flips the operator", () => {
  assert.strictEqual(fixed("if (a > b) { }"), "if (b < a) { }");
  assert.strictEqual(fixed("if (index >= 0) { }"), "if (0 <= index) { }");
  assert.strictEqual(fixed("if (list.Count > 0) { }"), "if (0 < list.Count) { }");
});

test("fix keeps operand precedence", () => {
  assert.strictEqual(fixed("if (a + b > c * d) { }"), "if (c * d < a + b) { }");
  assert.strictEqual(fixed("if (a > b && c > d) { }"), "if (b < a && d < c) { }");
  assert.strictEqual(fixed("int r = a > b ? 1 : 2;"), "int r = b < a ? 1 : 2;");
});

test("fix refuses when both operands can have a side effect", () => {
  // `Next() > Peek()` and `Peek() < Next()` call them in the opposite order.
  assert.strictEqual(fixed("if (Next() > Peek()) { }"), "if (Next() > Peek()) { }");
  // One impure side is safe: the other cannot observe the order.
  assert.strictEqual(fixed("if (GetCount() > 0) { }"), "if (0 < GetCount()) { }");
});

test("fix keeps a rewrite inside its interpolation hole", () => {
  // The operand scan used to walk out of the hole and swallow the literal, producing
  // `Write($"...{b}" < a)`. It compiled nowhere, which is the only reason it was caught.
  assert.strictEqual(fixed('Write($"x: {a > b}");'), 'Write($"x: {b < a}");');
  assert.strictEqual(fixed('Write($"{a > b} and {c >= d}");'), 'Write($"{b < a} and {d <= c}");');
});

test("fix refuses a comparison that spans more than one line", () => {
  const source = "if (\n    someVeryLongName\n    > other) { }";
  assert.strictEqual(fixed(source), source);
});

test("a corpus with no C# files is rejected rather than reported clean", () => {
  // A walk that matched nothing is the absence of a measurement, not a pass. Reachable
  // with nothing looking wrong: a renamed source root, a moved tree, a walk that stops descending.
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "comparison-direction-empty-"));
  try {
    const result = spawnSync(process.execPath, [linterPath], {
      env: { ...process.env, COMPARISON_DIRECTION_ROOTS: directory },
      encoding: "utf8",
    });

    assert.strictEqual(result.status, 1, "an empty walk must not report a clean run");
    assert.ok(
      result.stderr.includes("checked nothing"),
      `the report must say what was empty, got: ${result.stderr}`
    );
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

test("linter exits non-zero on a violation and zero once fixed", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "comparison-direction-"));
  try {
    const root = path.join(directory, "Fixture");
    fs.mkdirSync(root);
    const file = path.join(root, "Sample.cs");
    fs.writeFileSync(file, "class Sample { bool M(int a, int b) => a > b; }\n");
    const environment = { ...process.env, COMPARISON_DIRECTION_ROOTS: root };
    const red = spawnSync(process.execPath, [linterPath], { env: environment, encoding: "utf8" });
    assert.strictEqual(red.status, 1, "expected a violation to fail the linter");
    assert.ok(
      red.stderr.includes("Sample.cs:1:"),
      `expected the file and line in the report, got: ${red.stderr}`
    );

    const fix = spawnSync(process.execPath, [linterPath, "--fix"], {
      env: environment,
      encoding: "utf8",
    });
    assert.strictEqual(fix.status, 0, `--fix should succeed, got: ${fix.stderr}`);
    assert.strictEqual(
      fs.readFileSync(file, "utf8"),
      "class Sample { bool M(int a, int b) => b < a; }\n"
    );

    const green = spawnSync(process.execPath, [linterPath], { env: environment, encoding: "utf8" });
    assert.strictEqual(green.status, 0, `expected clean after --fix, got: ${green.stderr}`);
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});
