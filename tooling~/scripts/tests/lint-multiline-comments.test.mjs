/*
    Contract tests for tooling~/scripts/lint-multiline-comments.mjs.

    The rule: two or more consecutive comment-only `//` lines are one comment with fake
    structure and must be a block comment instead. The negative cases carry the weight:
    `///` doc runs, single `//` lines, `//` inside any literal (strings, verbatim fixture
    sources, interpolated holes, chars), and `//` inside block comments must all stay
    silent, or a sweep corrupts fixture data. The positive cases pin detection and the
    exact conversion shape the reviewer asked for.

    Second rule: two adjacent block comments that restate each other are one comment
    written twice, which is what happens when a comment is revised by adding a second
    block instead of editing the first. Its negative cases carry the weight too - two
    different comments, two `///` doc blocks, and a block comment inside a string
    literal are all legitimately two things.
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
const linterPath = path.join(repoRoot, "scripts", "lint-multiline-comments.mjs");
const { commentRuns, planFix, applyFixes, duplicateBlockComments } = await import(
  pathToFileURL(linterPath).href
);

function runsIn(source) {
  return commentRuns(source);
}

function fixed(source) {
  return applyFixes(source, commentRuns(source));
}

/** Shapes that contain `//` lines and are NOT stacked comments. Every one stays silent. */
const EXEMPT = [
  ["a single comment line", "// one"],

  ["doc comment runs", "/// <summary>\n/// Text.\n/// </summary>"],

  [
    "a blank line between comments",
    "// first\n\n// second",
  ],

  [
    "a doc comment breaking the run",
    "// first\n/// doc\n// second",
  ],

  [
    "code between comments",
    "// first\nint x = 1;\n// second",
  ],

  [
    "comments inside a normal string",
    'var url = "https://example.test/a//b";',
  ],

  [
    "comments inside a verbatim string",
    "var source = @\"\n// looks like a comment\n// and more\n\";",
  ],

  [
    "comments inside an interpolated string",
    'var text = $"a {x} // not a comment";',
  ],

  [
    "a quote inside a char literal",
    "char quote = '\"';\n// single comment",
  ],

  [
    "comments inside a block comment",
    "/*\n// inside a block\n// still inside\n*/",
  ],

  [
    "a preprocessor directive breaking the run",
    "// first\n#if UNITY_EDITOR\n// second",
  ],

  [
    "trailing comments after code are outside the rule",
    "int a = 1; // one\nint b = 2; // two",
  ],

  [
    "a trailing comment between comment-only lines stays outside",
    "// one\nint a = 1; // trailing\n// two",
  ],
];

/** Shapes that ARE stacked comments. Every one is caught. */
const CAUGHT = [
  ["a two-line run", "// first\n// second"],
  ["a three-line run", "// first\n// second\n// third"],
  ["an indented run", "        // first\n        // second"],
  [
    "a run with deeper indentation preserved",
    "    // outer\n    //   inner detail",
  ],
];

test("exempt shapes stay silent", () => {
  for (const [name, source] of EXEMPT) {
    assert.deepStrictEqual(runsIn(source), [], name);
  }
});

test("stacked comment runs are caught", () => {
  for (const [name, source] of CAUGHT) {
    const runs = runsIn(source);
    assert.strictEqual(runs.length, 1, name);
    assert.strictEqual(1 < runs[0].entries.length, true, name);
  }
});

test("runs report zero-based line and one-based reporting line", () => {
  const source = "int a;\n// one\n// two";
  const runs = runsIn(source);
  assert.strictEqual(runs[0].entries[0].line, 1);
});

test("fix converts a plain run to the block shape", () => {
  const fixedText = fixed("// first\n// second");
  assert.strictEqual(fixedText, "/*\n   first\n   second\n*/");
});

test("fix preserves the run's indent and deeper content indentation", () => {
  const source = "    // outer\n    //   inner detail\n";
  assert.strictEqual(
    fixed(source),
    "    /*\n       outer\n         inner detail\n    */\n"
  );
});

test("fix keeps surrounding code and blank lines untouched", () => {
  const source = "int a = 1;\n// one\n// two\nint b = 2;\n";
  assert.strictEqual(
    fixed(source),
    "int a = 1;\n/*\n   one\n   two\n*/\nint b = 2;\n"
  );
});

test("fix strips trailing whitespace from converted content", () => {
  const fixedText = fixed("// one   \n// two\t\n");
  assert.strictEqual(fixedText, "/*\n   one\n   two\n*/\n");
});

test("fix emits CRLF when the file uses CRLF", () => {
  const source = "int a;\r\n// one\r\n// two\r\nint b;\r\n";
  const fixedText = fixed(source);
  assert.strictEqual(
    fixedText,
    "int a;\r\n/*\r\n   one\r\n   two\r\n*/\r\nint b;\r\n"
  );
  assert.strictEqual(fixedText.includes("\n   one\n"), false);
});

test("fix refuses a run whose content contains the block-comment close", () => {
  const source = "// keep */ going\n// second\n";
  assert.strictEqual(planFix(source, runsIn(source)[0]), undefined);
  assert.strictEqual(fixed(source), source);
});

test("verbatim fixture sources with stacked comments are data, not violations", () => {
  const source = [
    "var fixture = @\"",
    "namespace Fixtures",
    "{",
    "    // stacked inside",
    "    // the fixture string",
    "}\";",
  ].join("\n");
  assert.deepStrictEqual(runsIn(source), []);
});

test("empty lines inside a run convert to empty block lines", () => {
  const fixedText = fixed("// one\n//\n// two");
  assert.strictEqual(fixedText, "/*\n   one\n\n   two\n*/");
});

test("cli: a dirty fixture tree fails, --fix converts, a second scan passes", () => {
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), "multiline-comments-"));
  try {
    const runtime = path.join(workspace, "Runtime");
    fs.mkdirSync(runtime, { recursive: true });
    fs.writeFileSync(path.join(runtime, "Sample.cs"), "// one\n// two\n");
    const environment = {
      ...process.env,
      MULTILINE_COMMENT_ROOTS: runtime,
    };
    const red = spawnSync(process.execPath, [linterPath], {
      env: environment,
      encoding: "utf8",
    });
    assert.strictEqual(red.status, 1);
    assert.match(red.stderr, /2 stacked \/\/ comment lines/);

    const fixedRun = spawnSync(process.execPath, [linterPath, "--fix"], {
      env: environment,
      encoding: "utf8",
    });
    assert.strictEqual(fixedRun.status, 0);
    assert.match(fixedRun.stdout, /converted 1 comment run/);

    const green = spawnSync(process.execPath, [linterPath], {
      env: environment,
      encoding: "utf8",
    });
    assert.strictEqual(green.status, 0);
    assert.strictEqual(
      fs.readFileSync(path.join(runtime, "Sample.cs"), "utf8"),
      "/*\n   one\n   two\n*/\n"
    );
  } finally {
    fs.rmSync(workspace, { recursive: true, force: true });
  }
});

test("cli: scanning nothing fails instead of reading as green", () => {
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), "multiline-empty-"));
  try {
    const red = spawnSync(process.execPath, [linterPath], {
      env: { ...process.env, MULTILINE_COMMENT_ROOTS: path.join(workspace, "Missing") },
      encoding: "utf8",
    });
    assert.strictEqual(red.status, 1);
    assert.match(red.stderr, /checked nothing/);
  } finally {
    fs.rmSync(workspace, { recursive: true, force: true });
  }
});

/* ------------------------------------------------------------------ *
 * Duplicate block comments
 * ------------------------------------------------------------------ */

/** Two block comments that are genuinely one comment, written twice. Each must be caught. */
const RESTATED = [
  [
    "an identical block",
    `class A
{
    /*
        Counting loop over a string, which is the one case rule 11 does not cover and
        never covers: string.GetEnumerator returns a class, so foreach allocates one
        enumerator per call and the measurement lives in hot-path-allocations.
     */
    /*
        Counting loop over a string, which is the one case rule 11 does not cover and
        never covers: string.GetEnumerator returns a class, so foreach allocates one
        enumerator per call and the measurement lives in hot-path-allocations.
     */
    void M() { }
}`,
  ],
  [
    "a stale block under a corrected one",
    `class A
{
    /*
        a rule that the older comment states in the old words, about the sweep being
        limited to hot paths only, and the numbers that came with the old statement
     */
    /*
        a rule that the older comment states in the new words, about the sweep being
        unlimited, and the numbers that came with the old statement
     */
    void M() { }
}`,
  ],
];

/** Two block comments that are two comments. Every one must stay silent. */
const DISTINCT = [
  ["two different comments on one member", `class A
{
    /* Why the caret is clamped: the panel re-clamps a programmatic write. */
    /* How the clamp is derived: Math.Clamp against the current value length. */
    void M() { }
}`],

  ["two doc comment blocks", `class A
{
    /// <summary>First member.</summary>
    void A() { }

    /// <summary>Second member, documented the same way.</summary>
    void B() { }
}`],

  ["two identical blocks separated by code", `class A
{
    /* alpha beta gamma delta epsilon */
    void A() { }

    /* alpha beta gamma delta epsilon */
    void B() { }
}`],

  ["two blocks in a string literal", `class A
{
    string s = "/* alpha beta gamma delta epsilon */ /* alpha beta gamma delta epsilon */";
    void M() { }
}`],

  ["one block, no pair", `class A
{
    /* alpha beta gamma delta epsilon zeta eta theta */
    void M() { }
}`],

  ["a block that is only punctuation and short words", `class A
{
    /* a b c */
    /* a b c */
    void M() { }
}`],
];

test("restated block comments are caught", () => {
  for (const [name, source] of RESTATED) {
    const pairs = duplicateBlockComments(source);
    assert.equal(pairs.length, 1, `${name} should report one pair`);
    assert.ok(
      pairs[0].overlap > 0.5,
      `${name} should exceed the 0.5 threshold, got ${pairs[0].overlap}`
    );
    assert.ok(pairs[0].first.line < pairs[0].second.line, "the pair is in source order");
  }
});

test("two distinct block comments stay silent", () => {
  for (const [name, source] of DISTINCT) {
    assert.equal(
      duplicateBlockComments(source).length,
      0,
      `${name} should not be reported`
    );
  }
});

test("duplicate detection does not change the `//` run contract", () => {
  const source = "// one\n// two\nclass A { }\n";
  assert.equal(commentRuns(source).length, 1, "`//` runs are still detected");
  assert.equal(
    duplicateBlockComments(source).length,
    0,
    "`//` runs are not block comments"
  );
});

test("the CLI reports duplicates and exits non-zero", () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "dup-comments-"));
  try {
    fs.writeFileSync(
      path.join(dir, "A.cs"),
      `class A
{
    /*
        the same words appear in both of these blocks, which is what the rule is for and
        why a sweep would have to read the overlap before reporting anything at all
     */
    /*
        the same words appear in both of these blocks, which is what the rule is for and
        why a sweep would have to read the overlap before reporting anything at all
     */
    void M() { }
}`
    );
    const result = spawnSync(process.execPath, [linterPath], {
      env: { ...process.env, MULTILINE_COMMENT_ROOTS: dir },
      encoding: "utf8",
    });
    assert.equal(result.status, 1, "a duplicate fails the linter");
    assert.match(result.stderr, /restate each other/);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test("an empty scan root fails rather than passing silently", () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "dup-empty-"));
  try {
    const result = spawnSync(process.execPath, [linterPath], {
      env: { ...process.env, MULTILINE_COMMENT_ROOTS: dir },
      encoding: "utf8",
    });
    assert.equal(result.status, 1, "an empty root is an error, not a pass");
    assert.match(result.stderr, /checked nothing/);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
