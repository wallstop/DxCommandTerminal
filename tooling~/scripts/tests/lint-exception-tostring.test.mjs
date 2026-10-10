/*
    Contract tests for tooling~/scripts/lint-exception-tostring.mjs.

    The rule: a Debug.Log-family call logs the exception itself, not its
    .Message - interpolation calls ToString() implicitly and keeps the type,
    the message, and the throw-site stack, while .Message keeps only the
    sentence. The negative cases carry the weight: a .Message in a comment
    or a plain string is a mention, not a call; the funnel-side shapes
    (Terminal.Log, ReportCommandFailure's one-line error record plus
    Debug.LogException) keep .Message on purpose and must stay silent; and
    an interpolation hole that holds a quoted string must still be read, so
    a wrapped call cannot hide the violation next door. The positive cases
    pin every sink (Log, LogWarning, LogError) and every delivery shape
    (hole, concatenation, nested call).
*/
import test from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath, pathToFileURL } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const linterPath = path.join(repoRoot, "scripts", "lint-exception-tostring.mjs");
const { exceptionMessageViolations } = await import(pathToFileURL(linterPath).href);

function violationsIn(source) {
  return exceptionMessageViolations(source);
}

/** Shapes that name .Message and must stay silent. */
const EXEMPT = [
  ["a clean production file", "namespace N\n{\n    public sealed class C\n    {\n    }\n}"],

  [
    "the full exception interpolated (the migrated shape)",
    'catch (Exception e)\n{\n    Debug.LogWarning(LogTextSanitizer.Sanitize($"[Dx] discovery failed: {e}"));\n}',
  ],

  [
    ".Message inside a line comment",
    '// Debug.LogWarning($"raw {e.Message} in a comment");',
  ],

  [
    ".Message inside a block comment",
    '/* Debug.LogWarning($"raw {e.Message} in a block comment"); */',
  ],

  [
    ".Message inside a plain string literal",
    'const string example = "Debug.Log($\\"{e.Message}\\");";',
  ],

  [
    ".Message inside a nested string in a hole is data, and the hole's own code is code",
    'Debug.Log(LogTextSanitizer.Sanitize($"wrapped {LogTextSanitizer.Sanitize(e)} tail"));',
  ],

  [
    "the in-game funnel renders the message by design (Terminal.Log)",
    'catch (Exception e)\n{\n    Terminal.Log(TerminalLogType.Error, $"failed: {e.Message}");\n}',
  ],

  [
    "the split-channel failure record keeps its one line; LogException takes the exception",
    [
      "private void ReportCommandFailure(string commandName, Exception exception)",
      "{",
      "    IssueErrorMessage($\"Command '{commandName}' threw {exception.GetType().Name}: {exception.Message}\");",
      "    Debug.LogException(exception);",
      "}"
    ].join("\n"),
  ],

  [
    ".Message on a non-log API is out of the rule's subject",
    'var copy = new CommandConfigurationException(e.Failure, e.Message, name, innerException: e);',
  ]
];

/** Shapes that must fail. */
const VIOLATIONS = [
  [
    "Debug.Log with a hole holding .Message",
    'catch (Exception e)\n{\n    Debug.LogWarning(LogTextSanitizer.Sanitize($"[Dx] discovery failed: {e.Message}"));\n}',
    "e.Message"
  ],
  [
    "Debug.LogError with a hole holding .Message",
    'Debug.LogError(LogTextSanitizer.Sanitize($"bind failed for {name}: {e.Message}"));',
    "e.Message"
  ],
  [
    ".Message delivered by concatenation",
    'Debug.LogWarning(LogTextSanitizer.Sanitize($"[Dx] bake failed: " + $"{e.Message}"));',
    "e.Message"
  ],
  [
    ".Message inside a hole that also holds a quoted string (the #213 shape)",
    'Debug.Log($"values [{string.Join("; ", errors.Select(e => e.Message))}]");',
    "e.Message"
  ],
  [
    "an unwrapped log call is not an escape",
    'Debug.Log($"provider failed: {provider.Message}");',
    "provider.Message"
  ]
];

test("display-only mentions of .Message stay silent", () => {
  for (const [name, source] of EXEMPT) {
    assert.deepEqual(violationsIn(source), [], name);
  }
});

test("a .Message in a Debug.Log-family argument fails, with its line", () => {
  for (const [name, source, member] of VIOLATIONS) {
    const violations = violationsIn(source);
    assert.equal(violations.length, 1, name);
    assert.equal(violations[0].text, member, name);
    assert.ok(0 < violations[0].line, name);
  }
});

test("the scan refuses an empty tree instead of reporting a hollow pass", () => {
  const missing = path.join(repoRoot, "scripts", "tests", "fixtures", "no-such-root");
  const run = spawnSync(process.execPath, [linterPath], {
    encoding: "utf8",
    env: { ...process.env, EXCEPTION_TOSTRING_ROOTS: missing },
  });
  assert.equal(run.status, 1);
  assert.match(run.stderr, /no C# files were found/u);
});

test("the linter fails the process on a violating tree", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "exception-tostring-"));
  const directory = path.join(fixtureRoot, "Runtime");
  fs.mkdirSync(directory, { recursive: true });
  fs.writeFileSync(
    path.join(directory, "Probe.cs"),
    'public sealed class Probe\n{\n    public void Log(Exception e)\n    {\n        UnityEngine.Debug.Log($"failed: {e.Message}");\n    }\n}\n'
  );
  try {
    const run = spawnSync(process.execPath, [linterPath], {
      encoding: "utf8",
      env: { ...process.env, EXCEPTION_TOSTRING_ROOTS: fixtureRoot },
    });
    assert.equal(run.status, 1);
    assert.match(run.stderr, /e\.Message/u);
    assert.match(run.stdout, /1 file\(s\) scanned/u);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});

test("the shipped tree is clean", () => {
  const run = spawnSync(process.execPath, [linterPath], { encoding: "utf8", env: { ...process.env } });
  assert.equal(run.status, 0, run.stderr);
});
