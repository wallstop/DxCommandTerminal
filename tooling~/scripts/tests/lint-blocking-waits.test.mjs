/*
    Contract tests for tooling~/scripts/lint-blocking-waits.mjs.

    The rule: production code (Runtime/, Editor/) never blocks the calling
    thread - every copy command and UI key handler runs on Unity's main
    thread, and a sleep, spin, or blocking await freezes the whole game or
    editor for the wait's duration (wallstop, PR #254: "blocking the main
    thread for 450ms is a no-go for anything we do, ever"). The positive
    cases pin every wait shape the ban covers; the negative cases carry the
    weight: comment-only mentions of the banned vocabulary, string literals,
    same-name identifiers that are not the banned APIs, instance Join calls
    with arguments, and the reason-carrying allow marker must all stay
    silent, or a sweep buries real blocking waits in noise.
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
const linterPath = path.join(repoRoot, "scripts", "lint-blocking-waits.mjs");
const { blockingWaitViolations } = await import(pathToFileURL(linterPath).href);

function violationsIn(source) {
  return blockingWaitViolations(source);
}

/** Shapes that mention the banned vocabulary and must stay silent. */
const EXEMPT = [
  ["a clean production file", "namespace N\n{\n    public sealed class C\n    {\n    }\n}"],

  [
    "a comment-only line mentioning the ban",
    "// Thread.Sleep would block the main thread here",
  ],

  [
    "a comment-only line mentioning SpinWait",
    "/* SpinWait.SpinUntil is banned; sleep is a no-go */",
  ],

  [
    "a doc comment mentioning the ban",
    "/// <summary>Does not block the thread.</summary>",
  ],

  [
    "banned vocabulary inside a string literal",
    'string note = "Thread.Sleep is banned in production code";',
  ],

  [
    "an identifier containing the word Sleep",
    "int sleepCount = 0;",
  ],

  [
    "an identifier containing the word Wait",
    "int waitHandleCount = 0;",
  ],

  [
    "Task.Delay awaited, not blocked on",
    "async System.Threading.Tasks.Task Run() { await System.Threading.Tasks.Task.Delay(10); }",
  ],

  [
    "string.Join with arguments is not Thread.Join",
    'string line = string.Join(", ", parts);',
  ],

  [
    "String.Join is the same concatenation",
    "string line = String.Join(separator, parts);",
  ],

  [
    "Path.Join is path concatenation",
    'string p = Path.Join(a, b);',
  ],

  [
    "a wait vocabulary mention inside a literal segment stays data",
    'string msg = $"Thread.Sleep is banned, use {nameof(task)} later";'
  ],
];

/** Shapes that block the thread. Every one is caught. */
const VIOLATIONS = [
  ["Thread.Sleep", "System.Threading.Thread.Sleep(100);"],
  ["the imported Thread.Sleep", "using System.Threading;\nSleep(450);"],
  ["SpinWait.SpinUntil", "System.Threading.SpinWait.SpinUntil(() => done, 450);"],
  ["a bare SpinWait type mention in code", "SpinWait spin = new SpinWait();"],
  ["Task.Wait", "task.Wait();"],
  ["Task.Result", "string json = readerTask.Result;"],
  ["GetAwaiter().GetResult()", "string json = task.GetAwaiter().GetResult();"],
  ["WaitOne", "doneHandle.WaitOne();"],
  ["Thread.Join", "worker.Join();"],
  ["Monitor.Wait", "System.Threading.Monitor.Wait(lockObject);"],
  [
    "a marker with no reason does not exempt",
    "string x = task.Result; // blocking-waits:allow:"
  ],
  [
    "a wait inside an interpolated string's hole is code (Bugbot, PR #254)",
    'string msg = $"{task.Wait()} wait done";'
  ],
  ["Task.Wait with a timeout (Bugbot, PR #254)", "task.Wait(450);"],
  ["Task.Wait with a TimeSpan", "task.Wait(TimeSpan.FromSeconds(1));"],
  ["Thread.Join with a timeout", "worker.Join(450);"],
  ["Task.Join with a timeout", "task.Join(450);"],
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

test("a reason-carrying marker exempts its own line and the next", () => {
  const trailing = "string json = readerTask.Result; // blocking-waits:allow: guarded, never blocks";
  assert.deepStrictEqual(violationsIn(trailing), [], "trailing marker");

  const above = "// blocking-waits:allow: guarded by the pump above; .Result on a completed task never blocks\nstring json = readerTask.Result;";
  assert.deepStrictEqual(violationsIn(above), [], "marker on the line above");
});

test("a marker only exempts the two lines it names", () => {
  const source = [
    "// blocking-waits:allow: the guard is the pump",
    "string a = first.Result;",
    "string b = second.Result;",
  ].join("\n");
  const violations = violationsIn(source);
  assert.deepStrictEqual(violations.map((violation) => violation.line), [3]);
});

test("line numbers are one-based and count every violating line", () => {
  const source = [
    "namespace N",
    "{",
    "    void Wait()",
    "    {",
    "        System.Threading.Thread.Sleep(10);",
    "        task.Wait();",
    "    }",
    "}",
  ].join("\n");
  const violations = violationsIn(source);
  assert.deepStrictEqual(
    violations.map((violation) => violation.line),
    [5, 6]
  );
});

function runLinter(fixtureRoot) {
  return spawnSync(process.execPath, [linterPath], {
    cwd: repoRoot,
    env: { ...process.env, BLOCKING_WAIT_ROOTS: fixtureRoot },
    encoding: "utf8",
  });
}

test("a fixture tree with a blocking wait fails with a report", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "blocking-lint-"));
  const production = path.join(fixtureRoot, "Production");
  fs.mkdirSync(production, { recursive: true });
  fs.writeFileSync(
    path.join(production, "Bad.cs"),
    "using System.Threading;\npublic sealed class C { void W() { Thread.Sleep(450); } }\n"
  );
  try {
    const result = runLinter(fixtureRoot);
    assert.strictEqual(result.status, 1, result.stderr);
    assert.ok(result.stderr.includes("Bad.cs:2"), result.stderr);
    assert.ok(result.stdout.includes("1 file(s) scanned"), result.stdout);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});

test("an allowed site passes and the scan stays green", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "blocking-lint-"));
  const production = path.join(fixtureRoot, "Production");
  fs.mkdirSync(production, { recursive: true });
  fs.writeFileSync(
    path.join(production, "Guarded.cs"),
    "public sealed class C\n{\n    string Read()\n    {\n        // blocking-waits:allow: guarded by the pump above\n        return task.Result;\n    }\n}\n"
  );
  try {
    const result = runLinter(fixtureRoot);
    assert.strictEqual(result.status, 0, result.stderr);
    assert.ok(result.stdout.includes("1 file(s) scanned"), result.stdout);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});

test("a fixture tree with clean production code passes", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "blocking-lint-"));
  const production = path.join(fixtureRoot, "Production");
  fs.mkdirSync(production, { recursive: true });
  fs.writeFileSync(
    path.join(production, "Clean.cs"),
    "public sealed class C { async void R() { await System.Threading.Tasks.Task.Delay(10); } }\n// Thread.Sleep is banned here\n"
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
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "blocking-lint-"));
  try {
    const result = runLinter(fixtureRoot);
    assert.strictEqual(result.status, 1, result.stderr);
    assert.ok(result.stderr.includes("checked nothing"), result.stderr);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});
