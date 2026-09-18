/*
    Contract tests for tooling~/scripts/lint-unity-null-patterns.mjs.

    The rule: null assertions read through Unity's `==`/`!=` operators
    (`Assert.That(x == null)`), never `Assert.IsNull`/`Assert.IsNotNull`, which bypass
    the fake-null operator (rule 25, issue #98). The negative cases carry the weight:
    the banned call text inside any literal (strings, verbatim fixture sources,
    interpolated holes, chars) and comments must stay silent, or a sweep corrupts
    fixture data. The positive cases pin detection and the exact mechanical conversion.
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
const linterPath = path.join(repoRoot, "scripts", "lint-unity-null-patterns.mjs");
const { violations, planFix, applyFixes } = await import(
  pathToFileURL(linterPath).href
);

function foundIn(source) {
  return violations(source);
}

function fixed(source) {
  return applyFixes(source, foundIn(source));
}

/** Shapes that contain `Assert.IsNull` text and are NOT real calls. Every one stays silent. */
const EXEMPT = [
  ["a call inside a plain string", 'var hint = "call Assert.IsNull(x) first";'],

  [
    "a call inside a verbatim string",
    'var source = @"\n    Assert.IsNull(fixture);\n";',
  ],

  ["a call inside an interpolated hole message", 'var text = $"see Assert.IsNotNull(x)";'],

  ["a call inside a char literal", "char c = '(';"],

  ["a call inside a line comment", "// Assert.IsNull(x);"],

  ["a call inside a block comment", "/*\nAssert.IsNull(x);\n*/"],

  ["a call inside a preprocessor directive", "#region Assert.IsNull calls"],
];

/** Shapes that ARE banned calls. Every one is caught with the right kind. */
const CAUGHT = [
  ["a plain single-line IsNull", "Assert.IsNull(value);", "IsNull"],
  ["a plain single-line IsNotNull", "Assert.IsNotNull(value);", "IsNotNull"],
  [
    "a message-carrying IsNotNull",
    'Assert.IsNotNull(error, "queues an error");',
    "IsNotNull",
  ],
  [
    "a qualified NUnit call",
    "NUnit.Framework.Assert.IsNull(x);",
    "IsNull",
  ],
  [
    "a multi-line call",
    'Assert.IsNull(\n    shell.X,\n    "message"\n);',
    "IsNull",
  ],
  [
    "a call inside an interpolated message stays one violation with the hole as data",
    'Assert.IsNull(error, $"ran {name}");',
    "IsNull",
  ],
];

test("exempt shapes stay silent", () => {
  for (const [name, source] of EXEMPT) {
    assert.deepStrictEqual(foundIn(source), [], name);
  }
});

test("banned call shapes are caught with the right kind", () => {
  for (const [name, source, kind] of CAUGHT) {
    const found = foundIn(source);
    assert.strictEqual(found.length, 1, name);
    assert.strictEqual(found[0].kind, kind, name);
  }
});

test("one-based line and column are reported", () => {
  const source = "int a;\n    Assert.IsNull(x);";
  const found = foundIn(source);
  assert.strictEqual(found[0].line, 2);
  assert.strictEqual(found[0].column, 5);
});

test("a nested subject call's top-level comma splits the subject, not the message", () => {
  const source = 'Assert.IsNull(Pair(a, b), "message");';
  const found = foundIn(source);
  assert.strictEqual(found[0].subject, "Pair(a, b)");
});

test("fix converts a plain single-line IsNull", () => {
  assert.strictEqual(
    fixed("Assert.IsNull(value);"),
    "Assert.That(value == null);"
  );
});

test("fix converts a plain single-line IsNotNull", () => {
  assert.strictEqual(
    fixed("Assert.IsNotNull(value);"),
    "Assert.That(value != null);"
  );
});

test("fix keeps the message arguments untouched", () => {
  assert.strictEqual(
    fixed('Assert.IsNotNull(error, "queues an error");'),
    'Assert.That(error != null, "queues an error");'
  );
});

test("fix preserves interior line breaks and indentation", () => {
  const source = 'Assert.IsNull(\n    shell.X,\n    "message"\n);';
  assert.strictEqual(
    fixed(source),
    'Assert.That(\n    shell.X == null,\n    "message"\n);'
  );
});

test("fix keeps surrounding code untouched", () => {
  const source = "int a = 1;\nAssert.IsNull(a);\nint b = 2;\n";
  assert.strictEqual(
    fixed(source),
    "int a = 1;\nAssert.That(a == null);\nint b = 2;\n"
  );
});

test("fix handles several calls in one pass", () => {
  const source = "Assert.IsNull(a);\nAssert.IsNotNull(b);\n";
  assert.strictEqual(
    fixed(source),
    "Assert.That(a == null);\nAssert.That(b != null);\n"
  );
});

test("fix splices at the token offset when trivia separates the call tokens", () => {
  /*
      Whitespace, comments, and line breaks between `Assert`, `.`, the method
      name, and `(` are all legal C#. The splice point must come from the
      token walk's open-paren offset, or the cut lands inside a token and the
      rewrite emits invalid C# (Bugbot, PR #102).
   */
  assert.strictEqual(fixed("Assert . IsNull (x);"), "Assert.That(x == null);");
  assert.strictEqual(
    fixed("Assert./*c*/IsNull(x);"),
    "Assert.That(x == null);"
  );
  assert.strictEqual(
    fixed("Assert.\n  IsNotNull(x);"),
    "Assert.That(x != null);"
  );
  assert.strictEqual(
    fixed('Assert . IsNull ( x , "msg" );'),
    'Assert.That( x == null , "msg" );'
  );
});

test("fix handles CRLF sources without mixing line endings", () => {
  const source = 'Assert.IsNull(\r\n    shell.X,\r\n    "message"\r\n);';
  const fixedText = fixed(source);
  assert.strictEqual(/[^\r]\n/.test(fixedText), false);
  assert.strictEqual(
    fixedText,
    'Assert.That(\r\n    shell.X == null,\r\n    "message"\r\n);'
  );
});

test("fix refuses a subject whose precedence would change under the appended operator", () => {
  const source = "Assert.IsNull(c ? a : b);";
  const found = foundIn(source);
  assert.strictEqual(planFix(source, found[0]), undefined);
  assert.strictEqual(fixed(source), source);
});

test("an unbalanced call is reported but never rewritten", () => {
  const source = "Assert.IsNull(x;";
  const found = foundIn(source);
  assert.strictEqual(found.length, 1);
  assert.strictEqual(planFix(source, found[0]), undefined);
  assert.strictEqual(fixed(source), source);
});

test("cli: a dirty fixture tree fails, --fix converts, a second scan passes", () => {
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), "unity-null-patterns-"));
  try {
    const tests = path.join(workspace, "Tests");
    fs.mkdirSync(tests, { recursive: true });
    fs.writeFileSync(
      path.join(tests, "Sample.cs"),
      "Assert.IsNull(x);\nAssert.IsNotNull(y, \"msg\");\n"
    );
    const environment = {
      ...process.env,
      UNITY_NULL_ROOTS: tests,
    };
    const red = spawnSync(process.execPath, [linterPath], {
      env: environment,
      encoding: "utf8",
    });
    assert.strictEqual(red.status, 1);
    assert.match(red.stderr, /Assert\.IsNull/);
    assert.match(red.stderr, /Assert\.IsNotNull/);

    const fixedRun = spawnSync(process.execPath, [linterPath, "--fix"], {
      env: environment,
      encoding: "utf8",
    });
    assert.strictEqual(fixedRun.status, 0);
    assert.match(fixedRun.stdout, /converted 2 assert call/);

    const green = spawnSync(process.execPath, [linterPath], {
      env: environment,
      encoding: "utf8",
    });
    assert.strictEqual(green.status, 0);
    assert.strictEqual(
      fs.readFileSync(path.join(tests, "Sample.cs"), "utf8"),
      'Assert.That(x == null);\nAssert.That(y != null, "msg");\n'
    );
  } finally {
    fs.rmSync(workspace, { recursive: true, force: true });
  }
});

test("cli: scanning nothing fails instead of reading as green", () => {
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), "unity-null-empty-"));
  try {
    const red = spawnSync(process.execPath, [linterPath], {
      env: { ...process.env, UNITY_NULL_ROOTS: path.join(workspace, "Missing") },
      encoding: "utf8",
    });
    assert.strictEqual(red.status, 1);
    assert.match(red.stderr, /checked nothing/);
  } finally {
    fs.rmSync(workspace, { recursive: true, force: true });
  }
});
