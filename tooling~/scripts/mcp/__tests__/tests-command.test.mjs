import test from "node:test";
import assert from "node:assert/strict";
import {
  DEFAULT_TEST_RUN_TIMEOUT,
  TEST_MODES,
  parseTestStatus,
  resolveTestRunOptions,
  testSummaryLine
} from "../unity-mcp.mjs";

test("test run options default to the full suite with the standard deadline", () => {
  assert.deepEqual(resolveTestRunOptions({}), {
    mode: "all",
    filter: "",
    runTimeout: DEFAULT_TEST_RUN_TIMEOUT
  });
  assert.ok(TEST_MODES.includes("playmode"));
});

test("test run options validate mode and timeout before any endpoint contact", () => {
  assert.throws(() => resolveTestRunOptions({ mode: "smoke" }), /Unknown --mode/);
  assert.throws(() => resolveTestRunOptions({ runTimeout: "abc" }), /--run-timeout/);
  assert.throws(() => resolveTestRunOptions({ runTimeout: "0" }), /--run-timeout/);
});

test("test run options accept explicit mode, filter, and timeout", () => {
  assert.deepEqual(resolveTestRunOptions({ mode: "editmode", filter: "CommandArg", runTimeout: "45000" }), {
    mode: "editmode",
    filter: "CommandArg",
    runTimeout: 45_000
  });
});

test("run_tests status decoding is data-driven across bridge generations", () => {
  const cases = [
    // [payload, seenRunning, finished, running, total]
    ['{"Summary":{"total":5,"passed":5,"failed":0,"skipped":0}}', false, true, false, 5],
    ['{"summary":{"total":3,"passed":2,"failed":1,"skipped":0},"status":"Completed"}', false, true, false, 3],
    ['{"status":"in_progress"}', false, false, true, null],
    ['{"status":"Running"}', false, false, true, null],
    // Idle before the run starts must not end the poll even with a summary object.
    ['{"summary":{"total":0},"status":"idle"}', false, false, false, 0],
    // Idle after the run was seen in flight is the zero-total completion shape.
    ['{"summary":{"total":0},"status":"idle"}', true, true, false, 0],
    ['{"status":"completed"}', true, false, false, null],
    ['{"status":"completed","summary":{"total":0,"passed":0,"failed":0,"skipped":0}}', true, true, false, 0],
    ["not json at all", false, false, false, null]
  ];
  for (const [payload, seenRunning, finished, running, total] of cases) {
    const status = parseTestStatus(payload, seenRunning);
    assert.equal(status.finished, finished, `finished for ${payload}`);
    assert.equal(status.running, running, `running for ${payload}`);
    const observed = status.summary === null ? null : Number(status.summary.total ?? 0);
    assert.equal(observed, total, `total for ${payload}`);
  }
});

test("summary line reports the four counters in order", () => {
  assert.equal(
    testSummaryLine({ total: 9, passed: 7, failed: 1, skipped: 1 }),
    "Tests: 9 total, 7 passed, 1 failed, 1 skipped."
  );
});
