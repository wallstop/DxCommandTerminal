/*
    Contract tests for tooling~/scripts/preflight.mjs.

    Data-driven over the runner contract: the default check set is non-empty
    with unique names, --skip filters by exact name and refuses unknown names,
    runCollect reports per-check pass/fail with captured output, and main
    refuses to scan nothing.
*/
import test from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const toolingRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const preflightPath = path.join(toolingRoot, "scripts", "preflight.mjs");
const { buildChecks, parseSkip, runChecks, main } = await import(
  pathToFileURL(preflightPath).href
);

function writeTempScript(body) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-test-"));
  const file = path.join(dir, "subject.mjs");
  fs.writeFileSync(file, body);
  return file;
}

function case_(name, overrides) {
  return { name, ...overrides };
}

test("buildChecks: unique names, non-empty commands, expected canaries", () => {
  const checks = buildChecks();
  assert.ok(checks.length > 0, "check set must not be empty");
  const names = checks.map((check) => check.name);
  assert.deepEqual(new Set(names).size, names.length, "check names must be unique");
  for (const check of checks) {
    assert.equal(typeof check.command, "string");
    assert.ok(check.command.trim().length > 0, `${check.name} needs a command`);
  }
  const expected = [
    "node-tests",
    "t11-check",
    "package-validate",
    "docs-guides",
    "compat-check",
    "lint-linq-production",
    "lint-docs-catalog"
  ];
  for (const name of expected) {
    assert.ok(names.includes(name), `expected check '${name}' in the default set`);
  }
});

const skipCases = [
  case_("inline form filters by exact name", {
    argv: ["--skip=node-tests,docs-guides"],
    skipped: ["node-tests", "docs-guides"]
  }),
  case_("space form filters identically", {
    argv: ["--skip", "node-tests"],
    skipped: ["node-tests"]
  }),
  case_("whitespace and empty segments are ignored", {
    argv: ["--skip=node-tests, , docs-guides ,"],
    skipped: ["node-tests", "docs-guides"]
  }),
  case_("no flag skips nothing", { argv: [], skipped: [] })
];

for (const { name, argv, skipped } of skipCases) {
  test(`parseSkip: ${name}`, () => {
    const known = ["node-tests", "docs-guides", "t11-check"];
    const remaining = parseSkip(argv, known);
    assert.deepEqual(
      remaining.sort(),
      [...skipped].sort(),
      "skip must list exactly the skipped names"
    );
  });
}

test("parseSkip: unknown name fails loudly with the known set", () => {
  assert.throws(() => parseSkip(["--skip=nope"], ["node-tests"]), /unknown --skip name 'nope'/);
});

const runCases = [
  case_("passing command", {
    body: "process.exit(0);",
    expectOk: true,
    outputFragment: null
  }),
  case_("failing command exits non-zero", {
    body: "console.error('boom-marker'); process.exit(3);",
    expectOk: false,
    outputFragment: "boom-marker"
  })
];

for (const { name, body, expectOk, outputFragment } of runCases) {
  test(`runChecks: ${name}`, async () => {
    const subject = writeTempScript(body);
    const results = await runChecks([{ name: "subject", command: `node "${subject}"` }]);
    assert.equal(results.length, 1);
    assert.equal(results[0].ok, expectOk, `subject should ${expectOk ? "pass" : "fail"}`);
    assert.ok(results[0].durationMs >= 0, "duration must be recorded");
    if (outputFragment !== null) {
      assert.ok(results[0].output.includes(outputFragment), "failing output must be captured");
    }
  });
}

test("runChecks: settles once per check and preserves results", async () => {
  const subjects = ["a", "b"].map((label) =>
    writeTempScript(`console.log('${label}'); process.exit(0);`)
  );
  const settled = [];
  const results = await runChecks(
    subjects.map((subject, index) => ({
      name: `check-${index}`,
      command: `node "${subject}"`
    })),
    { onSettled: (result) => settled.push(result.name) }
  );
  assert.equal(results.length, subjects.length);
  assert.deepEqual(settled.sort(), ["check-0", "check-1"], "one onSettled per check");
  assert.ok(results.every((result) => result.ok), "all subjects pass");
});

test("main: skipping every check refuses to scan nothing", async () => {
  const all = buildChecks().map((check) => check.name);
  const exitCode = await main([`--skip=${all.join(",")}`]);
  assert.equal(exitCode, 1, "empty check set must exit 1");
});

test("main: unknown --skip name throws before spawning", async () => {
  await assert.rejects(() => main(["--skip=definitely-not-a-check"]), /unknown --skip name/);
});
