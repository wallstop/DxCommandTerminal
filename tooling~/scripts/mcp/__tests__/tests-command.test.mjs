import test from "node:test";
import assert from "node:assert/strict";
import {
  DEFAULT_TEST_RUN_TIMEOUT,
  TEST_MODES,
  awaitRunResult,
  isSessionCapError,
  parseTestStatus,
  readSummaryKey,
  resolveTestRunOptions,
  sessionSignalBudgetMs,
  summaryIsFresh,
  summaryKey,
  testRunLegs,
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

test("mode all runs both concrete legs, because the bridge does not start an all-mode run", () => {
  assert.deepEqual(testRunLegs("all"), ["editmode", "playmode"]);
  assert.deepEqual(testRunLegs("playmode"), ["playmode"]);
  assert.deepEqual(testRunLegs("editmode"), ["editmode"]);
});

test("test run options validate mode and timeout before any endpoint contact", () => {
  assert.throws(() => resolveTestRunOptions({ mode: "smoke" }), /Unknown --mode/);
  assert.throws(() => resolveTestRunOptions({ runTimeout: "abc" }), /--run-timeout/);
  assert.throws(() => resolveTestRunOptions({ runTimeout: "0" }), /--run-timeout/);
  assert.throws(() => resolveTestRunOptions({ runTimeout: "1000" }), /at least 30000ms/);
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
    // Idle before the run starts must never end the poll - the summary may
    // be a stale previous run (the false-green trap).
    ['{"summary":{"total":0},"status":"idle"}', false, false, false, 0],
    ['{"summary":{"total":7,"passed":7,"failed":0,"skipped":0},"status":"idle"}', false, false, false, 7],
    // Idle after the run was seen in flight is the completion shape.
    ['{"summary":{"total":0},"status":"idle"}', true, true, false, 0],
    ['{"summary":{"total":7,"passed":7,"failed":0,"skipped":0},"status":"idle"}', true, true, false, 7],
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

test("a result identical to the pre-request run is the previous run's", () => {
  const before =
    '{"status":"completed","duration":8.5,"summary":{"total":21,"passed":21,"failed":0}}';
  const previousKey = readSummaryKey(before);
  assert.equal(
    previousKey,
    "duration=8.5;total=21,passed=21,failed=0,skipped=null,inconclusive=null"
  );

  // The false-green trap: the bridge echoes the last run instead of starting one.
  assert.equal(summaryIsFresh(before, previousKey, false), false);
  // A run seen in flight makes any later result attributable, counters or not.
  assert.equal(summaryIsFresh(before, previousKey, true), true);
  // Different counters can only be a new run.
  assert.equal(
    summaryIsFresh(
      '{"status":"completed","summary":{"total":1,"passed":1,"failed":0}}',
      previousKey,
      false
    ),
    true
  );
  // So is the same suite re-run: the counters repeat, the duration does not.
  assert.equal(
    summaryIsFresh(
      '{"status":"completed","duration":8.7,"summary":{"total":21,"passed":21,"failed":0}}',
      previousKey,
      false
    ),
    true
  );
  // Nothing to compare against (no prior run) or nothing to read.
  assert.equal(summaryIsFresh(before, null, false), true);
  assert.equal(summaryIsFresh("not json at all", previousKey, false), false);
  assert.equal(summaryIsFresh('{"status":"in_progress"}', previousKey, false), false);
});

test("summary keys normalize the payload spelling, missing counters, and junk numbers", () => {
  assert.equal(
    readSummaryKey('{"Summary":{"total":3,"passed":3}}'),
    summaryKey({ total: 3, passed: 3 })
  );
  assert.equal(readSummaryKey('{"status":"idle"}'), null);
  assert.equal(readSummaryKey("not json at all"), null);
  // Two malformed payloads must not collide into one key.
  assert.notEqual(
    readSummaryKey('{"summary":{"total":"x"}}'),
    readSummaryKey('{"summary":{"total":"y"}}')
  );
});

test("an in-flight payload is never finished, even with the previous run's counts", () => {
  const stale = '{"status":"in_progress","summary":{"total":21,"passed":21,"failed":0}}';
  assert.equal(parseTestStatus(stale, false).finished, false);
  assert.equal(parseTestStatus(stale, false).running, true);
  // Idle before the run starts carries the same trap, and only in-flight
  // observation unlocks it.
  const idle = '{"status":"idle","summary":{"total":21,"passed":21,"failed":0}}';
  assert.equal(parseTestStatus(idle, false).finished, false);
  assert.equal(parseTestStatus(idle, true).finished, true);
});

test("the editor's full session table is recognized as transient, not a dead bridge", () => {
  const cap =
    'Streamable HTTP error: Error POSTing to endpoint: {"jsonrpc":"2.0","id":null,' +
    '"error":{"code":-32000,"message":"Too many concurrent MCP sessions (limit 8); ' +
    'close one or raise --max-sessions"}}';
  assert.equal(isSessionCapError(cap), true);
  assert.equal(isSessionCapError("HTTP 503: Service Unavailable"), false);
  assert.equal(isSessionCapError(undefined), false);
});

/*
    The deadline must not decide the fate of a result fetched after it passed:
    shaped as `while (now < deadline) { inspect; sleep; fetch }` the loop drops
    that payload, so a run that finished during the last poll reads as missing
    (Cursor Bugbot, PR #161). A clock that jumps past the deadline during the
    final read is the shape that used to fail.
 */
test("a run result fetched after the deadline still counts", async () => {
  const previous = readSummaryKey(
    '{"status":"completed","duration":9,"summary":{"total":1,"passed":1,"failed":0}}'
  );
  const finished = { total: 3, passed: 3, failed: 0, skipped: 0 };
  let polls = 0;
  let clock = 0;

  const summary = await awaitRunResult({
    initial: '{"status":"in_progress"}',
    previousKey: previous,
    deadline: 1_000,
    read: async () => {
      polls += 1;
      // A poll that overruns the deadline still has to be inspected.
      clock += 500;
      return '{"status":"completed","duration":2,"summary":{"total":3,"passed":3,"failed":0,"skipped":0}}';
    },
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    }
  });

  assert.deepEqual(summary, finished);
  assert.equal(polls, 1, "the late payload is the one that ends the wait");
  assert.ok(clock > 1_000, "the clock was past the deadline when the result arrived");
});

/*
    Not a regression test for the deadline defect - it passes under the old loop
    too. It is the over-correction guard: a wait that accepted anything to avoid
    a false "missing" would pass here as well.
 */
test("the wait still gives up when the last payload is not attributable", async () => {
  const stale = '{"status":"completed","duration":9,"summary":{"total":1,"passed":1,"failed":0}}';
  const previous = readSummaryKey(stale);
  let polls = 0;
  let clock = 0;

  const summary = await awaitRunResult({
    initial: stale,
    previousKey: previous,
    deadline: 1_000,
    read: async () => {
      polls += 1;
      return stale;
    },
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    }
  });

  assert.equal(summary, null, "a run that never starts must not report a result");
  assert.equal(polls, 1, "one poll fits before the deadline, and the retry is bounded by it");
});


test("the session signal budget covers every leg", () => {
  // Sized for one leg the signal fires first, so the per-leg deadline is
  // unreachable and a slow first suite still starves the second one.
  assert.equal(sessionSignalBudgetMs(600_000, 1), 600_000);
  assert.equal(sessionSignalBudgetMs(600_000, 2), 1_200_000);
  assert.equal(sessionSignalBudgetMs(600_000, 0), 600_000, "never below one leg");
  assert.equal(
    sessionSignalBudgetMs(30_000, testRunLegs("all").length),
    60_000,
    "the minimum flag value must still cover both legs"
  );
});

test("a poll interval of zero cannot become a spin", async () => {
  const stale = '{"status":"completed","duration":9,"summary":{"total":1,"passed":1,"failed":0}}';
  let polls = 0;
  let clock = 0;
  await awaitRunResult({
    initial: stale,
    previousKey: readSummaryKey(stale),
    deadline: 40,
    read: async () => {
      polls += 1;
      return stale;
    },
    pollIntervalMs: 0,
    now: () => clock,
    sleep: async (ms) => {
      assert.ok(ms >= 1, "every sleep is at least a millisecond");
      clock += ms;
    }
  });
  assert.ok(polls <= 40, `a zero interval must not spin (polled ${polls} times)`);
});
