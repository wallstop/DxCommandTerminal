import test from "node:test";
import assert from "node:assert/strict";
import {
  DEFAULT_TEST_RUN_TIMEOUT,
  RUN_CLAIM_FILE,
  RUN_REQUEST_FILE,
  TEST_MODES,
  awaitRunClaim,
  awaitRunResult,
  isSessionCapError,
  newRunToken,
  parseRunClaim,
  parseTestStatus,
  readSummaryKey,
  resolveTestRunOptions,
  runClaimPaths,
  sessionSignalBudgetMs,
  summaryIsFresh,
  summaryKey,
  testRunLegs,
  testSummaryLine,
  waitForTestIdle
} from "../unity-mcp.mjs";
import path from "node:path";

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

test("the summary line reports the four counters in order", () => {
  assert.equal(
    testSummaryLine({ total: 9, passed: 9, failed: 0, skipped: 0, failedNames: [] }),
    "Tests: 9 total, 9 passed, 0 failed, 0 skipped."
  );
});

/*
    A red run has to name itself. The counters alone leave the reader to dig
    through the editor console for the test names, which is the whole cost of a
    red run. Nothing downstream repeats the names, so this block is the only
    place they appear.
 */
test("a result block names its failures, and says so when it cannot", () => {
  const cases = [
    // [summary, expected]
    // A green leg: the counters, and nothing else.
    [
      { total: 9, passed: 9, failed: 0, skipped: 0, failedNames: [] },
      "Tests: 9 total, 9 passed, 0 failed, 0 skipped."
    ],
    [
      {
        total: 363,
        passed: 361,
        failed: 2,
        skipped: 0,
        failedNames: ["Wallstop.A.One", "Wallstop.B.Two(System.String)"]
      },
      "Tests: 363 total, 361 passed, 2 failed, 0 skipped.\n"
        + "  failed: Wallstop.A.One\n"
        + "  failed: Wallstop.B.Two(System.String)"
    ],
    // The editor's cap is its own count, and it is printed as its own line, so
    // the cap can never be read as a test name.
    [
      {
        total: 12,
        passed: 0,
        failed: 12,
        skipped: 0,
        failedNames: ["Ns.A.One", "Ns.A.Two"],
        failedMore: 10
      },
      "Tests: 12 total, 0 passed, 12 failed, 0 skipped.\n"
        + "  failed: Ns.A.One\n"
        + "  failed: Ns.A.Two\n"
        + "  ... and 10 more the cap did not list"
    ],
    // A control character, a bidi override, or a zero-width joiner in a name
    // must not split it across two lines, reorder one, or forge a result line -
    // and the escape has to be visible, or a reader cannot tell a name changed.
    // U+2028 and U+2029 are here because they are line separators: a name
    // holding one splits its line in a log viewer, which is where a red run is
    // read. An enumerated range missed both.
    [
      {
        total: 1,
        passed: 0,
        failed: 1,
        skipped: 0,
        failedNames: [
          "Ns.A.One\n  Tests: 1 total, 1 passed, 0 failed, 0 skipped.",
          "Ns.A\u202E﻿B",
          "Ns.A\u2028  Tests: 9 total, 9 passed, 0 failed, 0 skipped."
        ]
      },
      "Tests: 1 total, 0 passed, 1 failed, 0 skipped.\n"
        + "  failed: Ns.A.One\\u000A  Tests: 1 total, 1 passed, 0 failed, 0 skipped.\n"
        + "  failed: Ns.A\\u202E\\uFEFFB\n"
        + "  failed: Ns.A\\u2028  Tests: 9 total, 9 passed, 0 failed, 0 skipped."
    ],
    // A red run the editor named nothing says so, instead of printing the
    // counters alone - which is the output this whole change replaced.
    [
      { total: 4, passed: 3, failed: 1, skipped: 0, failedNames: [] },
      "Tests: 4 total, 3 passed, 1 failed, 0 skipped.\n"
        + "  no failed test names were reported (bridge fallback, or a reporter older than this change)"
    ],
    // One name per failing case, even when the cases share a method: a
    // parameterized test that failed twice failed twice, and the reader's only
    // other count is the `fail=` counter.
    [
      {
        total: 3,
        passed: 0,
        failed: 3,
        skipped: 0,
        failedNames: ['Ns.M.Case("a b")', 'Ns.M.Case("a,b")', 'Ns.M.Case("a=b")']
      },
      "Tests: 3 total, 0 passed, 3 failed, 0 skipped.\n"
        + '  failed: Ns.M.Case("a b")\n'
        + '  failed: Ns.M.Case("a,b")\n'
        + '  failed: Ns.M.Case("a=b")'
    ]
  ];
  for (const [summary, expected] of cases) {
    assert.equal(testSummaryLine(summary), expected, expected);
  }
});

/*
    The oracle for "a printed name cannot break a line", in both directions.

    The hostile set is derived from the Unicode categories and then enumerated,
    not read off the production literal: a test that compares the two strings can
    only catch a narrowing, never a widening. The first version of the filter
    enumerated its own range and missed 24 characters below the BMP - two of them
    line separators - and 127 more above it, so an enumerated expectation here
    would drift exactly the way the code did.

    The whole scalar range, not the BMP: the reporter percent-encodes UTF-8
    bytes, so a tag character like U+E0001 arrives intact, and it needs the
    eight-digit escape form. A BMP-only walk would never have reached it.

    The other direction matters too. A filter that escapes a comma, a space, or a
    letter is its own defect: the reader loses the name it was given.

    One limit, stated: widening the production class to a category this oracle
    does not name is a semantic decision this test will not second-guess. What it
    does catch is a narrow, a malformed escape, a dropped character, an
    over-filter, and any enumerating of the range.
 */
const NAME_LINE = "\n  failed: ";

function printedName(name) {
  const block = testSummaryLine({
    total: 1,
    passed: 0,
    failed: 1,
    skipped: 0,
    failedNames: [name]
  });
  const at = block.indexOf(NAME_LINE);
  assert.notEqual(at, -1, `the block must name the test: ${JSON.stringify(block)}`);
  return block.slice(at + NAME_LINE.length);
}

test("every control, format, or line-separator scalar is escaped, and nothing else is", () => {
  const hostile = /[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]/u;
  const problems = [];
  let checked = 0;
  for (let code = 0; code <= 0x10ffff; ++code) {
    if (code >= 0xd800 && code <= 0xdfff) continue; // a lone surrogate, not a scalar
    const character = String.fromCodePoint(code);
    const name = "Ns.A" + character + "B";
    if (hostile.test(character)) {
      checked += 1;
      // The exact escape, so a malformed one is caught: `\u` takes four hex
      // digits and `\U` takes eight.
      const hex = code.toString(16).toUpperCase();
      const expected =
        code <= 0xffff ? "\\u" + hex.padStart(4, "0") : "\\U" + hex.padStart(8, "0");
      const actual = printedName(name);
      if (actual !== "Ns.A" + expected + "B") {
        problems.push(
          "U+" + hex.padStart(4, "0")
            + " printed as "
            + JSON.stringify(actual)
            + ", expected "
            + JSON.stringify("Ns.A" + expected + "B")
        );
      }
      continue;
    }
    // Not hostile: it must reach the reader. Sampled rather than exhaustive -
    // every scalar in the plane is the same assertion 1.1M times over - but
    // spread across the categories a wrong class would grab.
    if (code % 4093 !== 0) continue;
    const actual = printedName(name);
    if (actual !== name) {
      problems.push(
        "U+" + hex4(code) + " is not hostile but was rewritten to " + JSON.stringify(actual)
      );
    }
  }
  assert.ok(0 < checked, "the hostile set must not be empty");
  assert.deepEqual(problems, [], problems.slice(0, 8).join("\n"));

  // A name a real suite produces survives untouched. A backslash is not here: the
  // filter escapes it, which is what keeps a literal `\u000A` in a name from
  // reading as an escape of a real one.
  for (const name of [
    'Ns.M.Case("a b")',
    "Ns.M.Case(a,b)",
    "Ns.M.Case(a=b)",
    "Ns.M.中文",
    "Ns.M.😀"
  ]) {
    assert.equal(printedName(name), name, "must survive untouched: " + name);
  }
  assert.equal(
    printedName("Ns.A\\u000AB"),
    "Ns.A\\u005Cu000AB",
    "a literal backslash-u sequence must not read as an escape of a real one"
  );
});

function hex4(code) {
  return code.toString(16).toUpperCase().padStart(4, "0");
}

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

/*
    The claim file is the editor's own report of one run (issue #162), so the
    node side never polls a main thread a Play Mode run occupies. The first
    token is the state; the owner token is the attribution, and a claim the
    editor wrote by a torn write has neither and can never be a result.
 */
test("claim decoding is data-driven over the three states and the torn cases", () => {
  const cases = [
    // [line, state, token, extra]
    [
      "running token=t-1 mode=PlayMode started=2026-09-27T02:28:00.0Z",
      "running",
      "t-1",
      { mode: "PlayMode" }
    ],
    [
      "pass=18 fail=0 skipped=0 inconclusive=0 duration=0.071 token=t-2 mode=PlayMode finished=x",
      "finished",
      "t-2",
      {
        summary: {
          total: 18,
          passed: 18,
          failed: 0,
          skipped: 0,
          inconclusive: 0,
          failedNames: [],
          failedMore: 0
        }
      }
    ],
    [
      "pass=3 fail=2 skipped=1 inconclusive=4 token=t-3",
      "finished",
      "t-3",
      {
        summary: {
          total: 10,
          passed: 3,
          failed: 2,
          skipped: 1,
          inconclusive: 4,
          failedNames: [],
          failedMore: 0
        }
      }
    ],
    // A red run names its failures. The editor percent-encodes every character
    // the claim grammar reserves, so a name with a space, a comma, or an equals
    // survives the round trip instead of collapsing into its neighbours.
    [
      "pass=361 fail=2 skipped=0 inconclusive=0 token=t-9 mode=EditMode "
        + "failed-names=Ns.A.One,Ns.B.Two(System.Int32)",
      "finished",
      "t-9",
      {
        summary: {
          total: 363,
          passed: 361,
          failed: 2,
          skipped: 0,
          inconclusive: 0,
          failedNames: ["Ns.A.One", "Ns.B.Two(System.Int32)"],
          failedMore: 0
        }
      }
    ],
    // The three cases of one parameterized test that a replace-with-underscore
    // encoder would have merged into one name. `a b` is %20, `a,b` is %2C, and
    // `a=b` is %3D - all reserved, all distinct once decoded.
    [
      "pass=0 fail=3 token=t-12 failed-names="
        + 'Ns.M.Case(%22a%20b%22),Ns.M.Case(%22a%2Cb%22),Ns.M.Case(%22a%3Db%22)',
      "finished",
      "t-12",
      {
        summary: {
          total: 3,
          passed: 0,
          failed: 3,
          skipped: 0,
          inconclusive: 0,
          failedNames: ['Ns.M.Case("a b")', 'Ns.M.Case("a,b")', 'Ns.M.Case("a=b")'],
          failedMore: 0
        }
      }
    ],
    // A '%' is escaped as %25, so a name that already reads as an escape comes
    // back as itself. A truncated escape - which no reporter writes, but a hand
    // edited claim can - decodes to itself instead of taking the run with it.
    [
      "pass=0 fail=2 token=t-13 failed-names=Ns.M.Case(100%25),Ns.M.Case(%ZZ)",
      "finished",
      "t-13",
      {
        summary: {
          total: 2,
          passed: 0,
          failed: 2,
          skipped: 0,
          inconclusive: 0,
          failedNames: ["Ns.M.Case(100%)", "Ns.M.Case(%ZZ)"],
          failedMore: 0
        }
      }
    ],
    // An escape is over UTF-8 bytes, so a name outside ASCII arrives as two to
    // four escapes. Encoding the character instead would write %2003, which
    // decodes as a space and "03": a real case in this repository, whose
    // ChoiceFormattingPreservesQuotedWhitespace cases include U+2003.
    [
      "pass=0 fail=2 token=t-14 failed-names=Ns.M.Case(%E2%80%83),Ns.M.Case(%C3%A9)",
      "finished",
      "t-14",
      {
        summary: {
          total: 2,
          passed: 0,
          failed: 2,
          skipped: 0,
          inconclusive: 0,
          failedNames: ["Ns.M.Case(\u2003)", "Ns.M.Case(é)"],
          failedMore: 0
        }
      }
    ],
    // The cap is a sibling count, not a name, so a 12-failure run reports ten
    // names and two unnamed. Ten is the reporter's cap.
    [
      "pass=1 fail=12 token=t-10 failed-more=2 failed-names="
        + "Ns.C.One,Ns.C.Two,Ns.C.Three,Ns.C.Four,Ns.C.Five,Ns.C.Six,Ns.C.Seven,Ns.C.Eight,"
        + "Ns.C.Nine,Ns.C.Ten",
      "finished",
      "t-10",
      {
        summary: {
          total: 13,
          passed: 1,
          failed: 12,
          skipped: 0,
          inconclusive: 0,
          failedNames: [
            "Ns.C.One",
            "Ns.C.Two",
            "Ns.C.Three",
            "Ns.C.Four",
            "Ns.C.Five",
            "Ns.C.Six",
            "Ns.C.Seven",
            "Ns.C.Eight",
            "Ns.C.Nine",
            "Ns.C.Ten"
          ],
          failedMore: 2
        }
      }
    ],
    // failed-more is a count, not a gate, and it is still bounded by the
    // failures this same line reports: a malformed, oversized, or
    // editor-impossible value must not print as if the editor had written it.
    [
      "pass=1 fail=2 token=t-15 failed-more=lots failed-names=Ns.C.One",
      "finished",
      "t-15",
      {
        summary: {
          total: 3,
          passed: 1,
          failed: 2,
          skipped: 0,
          inconclusive: 0,
          failedNames: ["Ns.C.One"],
          failedMore: 0
        }
      }
    ],
    [
      "pass=0 fail=1 token=t-17 failed-more=1e1 failed-names=Ns.C.One",
      "finished",
      "t-17",
      {
        summary: {
          total: 1,
          passed: 0,
          failed: 1,
          skipped: 0,
          inconclusive: 0,
          failedNames: ["Ns.C.One"],
          failedMore: 0
        }
      }
    ],
    [
      "pass=0 fail=1 token=t-18 failed-more=9 failed-names=Ns.C.One",
      "finished",
      "t-18",
      {
        summary: {
          total: 1,
          passed: 0,
          failed: 1,
          skipped: 0,
          inconclusive: 0,
          failedNames: ["Ns.C.One"],
          failedMore: 0
        }
      }
    ],
    // An empty part between separators is a claim the editor never writes, and
    // dropping it cannot lose a name the editor reported.
    [
      "pass=1 fail=2 token=t-16 failed-names=Ns.C.One,,Ns.C.Two",
      "finished",
      "t-16",
      {
        summary: {
          total: 3,
          passed: 1,
          failed: 2,
          skipped: 0,
          inconclusive: 0,
          failedNames: ["Ns.C.One", "Ns.C.Two"],
          failedMore: 0
        }
      }
    ],
    // An empty field, and an absent one, both decode to no names. The editor
    // writes neither on a green run; the reader cannot tell them apart, so the
    // summary must not claim that nothing failed.
    [
      "pass=2 fail=1 token=t-11 failed-names=",
      "finished",
      "t-11",
      {
        summary: {
          total: 3,
          passed: 2,
          failed: 1,
          skipped: 0,
          inconclusive: 0,
          failedNames: [],
          failedMore: 0
        }
      }
    ],
    [
      "did-not-run token=t-4 mode=EditMode reason=the EditMode run executed no tests",
      "refused",
      "t-4",
      { reason: "the EditMode run executed no tests" }
    ],
    // A run started by something other than the MCP tooling still reports, and
    // the reason says so.
    [
      "did-not-run token=none mode=PlayMode reason=a run was started by something other than the MCP tooling",
      "refused",
      "none",
      { reason: "a run was started by something other than the MCP tooling" }
    ],
    // A refusal with no reason text still refuses.
    ["did-not-run token=t-5", "refused", "t-5", { reason: "unspecified" }],
    // Torn or foreign writes carry no token: unreadable, never a result.
    ["pass=581 fail=0 skipped=0 inconcl", "unreadable", null, {}],
    ["running mode=PlayMode", "unreadable", null, {}],
    // An empty token names nothing. Reading it as "another run's claim" would
    // suppress the start grace and hide a run that never started.
    ["pass=54 fail=0 skipped=0 inconclusive=0 token=", "unreadable", null, {}],
    // A non-numeric or non-finite counter would make the summary NaN, and NaN
    // passes both exit guards: a green run out of a malformed line.
    ["pass=3 fail=oops skipped=0 inconclusive=0 token=t-6", "unreadable", null, {}],
    ["pass=1e999 fail=0 skipped=0 inconclusive=0 token=t-7", "unreadable", null, {}],
    ["pass=-1 fail=0 skipped=0 inconclusive=0 token=t-8", "unreadable", null, {}],
    ["totally unrelated text", "unreadable", null, {}],
    ["", "unreadable", null, {}],
    [undefined, "unreadable", null, {}]
  ];
  for (const [line, state, token, extra] of cases) {
    const claim = parseRunClaim(line);
    assert.equal(claim.state, state, `state for: ${line}`);
    assert.equal(claim.token, token, `token for: ${line}`);
    for (const [key, value] of Object.entries(extra)) {
      assert.deepEqual(claim[key], value, `${key} for: ${line}`);
    }
  }
});

test("claim paths sit beside each other and the owner token is unique per run", () => {
  const root = path.join(path.sep, "project", "Packages", "pkg", ".artifacts", "unity-state");
  const paths = runClaimPaths(root);
  assert.equal(paths.request, path.join(root, RUN_REQUEST_FILE));
  assert.equal(paths.claim, path.join(root, RUN_CLAIM_FILE));
  assert.notEqual(paths.request, paths.claim);

  const tokens = new Set([newRunToken("editmode", 42), newRunToken("editmode", 42)]);
  assert.equal(tokens.size, 2, "two requests at the same instant must not share a token");
  for (const token of tokens) {
    assert.match(token, /^editmode-[0-9a-z]+-[0-9a-f]{8}$/u, `token shape: ${token}`);
  }
});

test("a claim read after the deadline is still this run's result", async () => {
  // The same rule the bridge wait follows: inspect before re-testing the
  // deadline, or the read the last poll made is dropped.
  let reads = 0;
  let clock = 0;
  const outcome = await awaitRunClaim({
    claim: "claim",
    token: "t-1",
    requestedAt: 0,
    deadline: 1_000,
    read: () => {
      reads += 1;
      // A read that overruns the deadline still has to be inspected.
      clock += 500;
      return reads === 1 ? "running token=t-1" : "pass=3 fail=0 skipped=0 inconclusive=0 token=t-1";
    },
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    }
  });

  assert.equal(outcome.state, "finished");
  assert.equal(outcome.summary.total, 3);
  assert.ok(clock > 1_000, "the clock was past the deadline when the claim arrived");
  assert.equal(reads, 2, "the late claim is the one that ends the wait");
});

test("another run's claim is never this run's result", async () => {
  // The previous run's line is the trap the token removes: its counters look
  // exactly like a result, and it can be the only claim on disk.
  let clock = 0;
  const outcome = await awaitRunClaim({
    claim: "claim",
    token: "t-2",
    requestedAt: 0,
    deadline: 20_000,
    read: () => "pass=363 fail=0 skipped=0 inconclusive=0 token=t-1",
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    }
  });

  assert.equal(outcome.state, "no-claim");
  assert.match(outcome.detail, /belongs to another run/u, outcome.detail);
});

test("a foreign claim that disappears does not disable the start grace", async () => {
  // A second session's request clears the claim for a moment. Latching its
  // token would make the "the editor never started this run" failure
  // unreachable for the rest of the wait.
  let clock = 0;
  let reads = 0;
  const outcome = await awaitRunClaim({
    claim: "claim",
    token: "t-2",
    requestedAt: 0,
    deadline: 600_000,
    startGraceMs: 500,
    pollIntervalMs: 100,
    read: () => {
      reads += 1;
      return reads === 1 ? "pass=363 fail=0 skipped=0 inconclusive=0 token=t-1" : "";
    },
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    }
  });

  assert.equal(outcome.state, "no-start");
  assert.match(outcome.detail, /no claim for this run within 500 ms/u, outcome.detail);
  assert.ok(clock <= 700, `waited ${clock} ms, grace 500 ms plus one 100 ms poll`);
});

test("a run that never reports a start fails on the grace, not the whole timeout", async () => {
  let clock = 0;
  const outcome = await awaitRunClaim({
    claim: "claim",
    token: "t-1",
    requestedAt: 0,
    deadline: 600_000,
    startGraceMs: 500,
    pollIntervalMs: 100,
    read: () => "",
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    }
  });

  assert.equal(outcome.state, "no-start");
  assert.match(outcome.detail, /no claim for this run within 500 ms/u, outcome.detail);
  // At most one poll interval past the grace: a refusal must not sit out the
  // whole run timeout.
  assert.ok(clock <= 700, `waited ${clock} ms, grace 500 ms plus one 100 ms poll`);
});

test("the start grace stops the moment the editor acknowledges our run", async () => {
  // The regression the first cut had: the grace was gated on "no foreign claim"
  // only, so a run that reported `running` and then took longer than the grace
  // failed as if it had never started - a T04 capture leg budgets far more than
  // that.
  let clock = 0;
  const outcome = await awaitRunClaim({
    claim: "claim",
    token: "t-1",
    requestedAt: 0,
    deadline: 600_000,
    startGraceMs: 500,
    read: () => (clock <= 20_000 ? "running token=t-1 mode=PlayMode" : "pass=1 fail=0 skipped=0 inconclusive=0 token=t-1"),
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    }
  });

  assert.equal(outcome.state, "finished");
  assert.equal(outcome.summary.total, 1);
  assert.ok(clock > 20_000, `the wait must not end on the grace (waited ${clock} ms)`);
});

test("did-not-run ends the wait with its reason, and an in-flight claim does not", async () => {
  let clock = 0;
  const refused = await awaitRunClaim({
    claim: "claim",
    token: "t-1",
    requestedAt: 0,
    deadline: 60_000,
    read: () => "did-not-run token=t-1 mode=PlayMode reason=the playmode run executed no tests",
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    }
  });
  assert.deepEqual(refused, {
    state: "refused",
    reason: "the playmode run executed no tests"
  });
  assert.equal(clock, 0, "a refusal needs no further read");

  clock = 0;
  const inFlight = await awaitRunClaim({
    claim: "claim",
    token: "t-1",
    requestedAt: 0,
    deadline: 4_000,
    read: () => "running token=t-1 mode=PlayMode",
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    }
  });
  assert.equal(inFlight.state, "no-claim", "a run in flight waits out the deadline");
  assert.match(inFlight.detail, /wrote no claim for this run/u, inFlight.detail);
  assert.equal(clock, 4_000);
});

test("an unattributed refusal is the editor's diagnosis, not a wait", async () => {
  // The editor writes token=none in exactly one case: it started a run it has
  // no request for. That is the answer to "why did my request not arrive", and
  // waiting out the deadline would throw it away.
  let clock = 0;
  const outcome = await awaitRunClaim({
    claim: "claim",
    token: "t-1",
    requestedAt: 0,
    deadline: 600_000,
    read: () =>
      "did-not-run token=none mode=EditMode reason=not requested by the MCP tooling",
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    }
  });

  assert.deepEqual(outcome, { state: "refused", reason: "not requested by the MCP tooling" });
  assert.equal(clock, 0);
});

test("the request is consumed once the editor acknowledges it, and the mode is checked", async () => {
  // Consuming is what stops a run nobody asked for (a person in the Test Runner
  // window, a second agent) from stamping itself with our token later.
  const consumed = [];
  let clock = 0;
  const outcome = await awaitRunClaim({
    claim: "claim",
    request: "request",
    token: "t-1",
    expectMode: "playmode",
    requestedAt: 0,
    deadline: 60_000,
    read: () => (clock <= 4_000 ? "running token=t-1 mode=PlayMode" : "pass=2 fail=0 skipped=0 inconclusive=0 token=t-1 mode=PlayMode"),
    consume: (file) => consumed.push(file),
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    }
  });

  assert.equal(outcome.state, "finished");
  assert.deepEqual(consumed, ["request"], "the request is consumed once, not per poll");
});

test("a claim from another mode is refused, not reported as this leg", async () => {
  // The token is the attribution, so a mode that disagrees with the leg means
  // the attribution is wrong. Silent acceptance would report another run's
  // counters as this leg's result.
  await assert.rejects(
    awaitRunClaim({
      claim: "claim",
      token: "t-1",
      expectMode: "playmode",
      requestedAt: 0,
      deadline: 60_000,
      read: () => "pass=363 fail=0 skipped=0 inconclusive=0 token=t-1 mode=EditMode",
      consume: () => {},
      now: () => 0,
      sleep: async () => {}
    }),
    /reported a EditMode run while the playmode leg was requested/u
  );
});

test("a busy editor that refuses the idle probe is waited out, not failed", async () => {
  // Observed live: a main-thread timeout right at the start of `unity:tests`
  // killed the command with "No backend tool variant answered", even though the
  // editor became idle seconds later. Busy is the state this wait exists for.
  let clock = 0;
  let probes = 0;
  const idleCall = (text) => () => ({ call: { content: [{ text }] } });
  const answers = [
    () => {
      throw new Error("Main thread operation timed out after 5000ms");
    },
    idleCall("true"),
    idleCall("false")
  ];

  await waitForTestIdle(
    null,
    () => answers[Math.min(probes++, answers.length - 1)](),
    60_000,
    () => clock
  );
  assert.equal(probes, 3, "the wait must keep asking through a refused call");

  await assert.rejects(
    waitForTestIdle(
      null,
      () => {
        throw new Error("Main thread operation timed out after 5000ms");
      },
      2_000,
      () => {
        clock = 3_000;
        return clock;
      }
    ),
    /stayed busy/u
  );
});

test("a claim wait with no path or token is refused, not polled to the deadline", async () => {
  // The wiring mistake that shipped once: the reporter object was spread into
  // the wait, so the claim path never arrived and every read returned nothing
  // for the whole run timeout.
  for (const broken of [{ token: "t-1" }, { claim: "claim" }, { claim: "", token: "t-1" }]) {
    await assert.rejects(
      awaitRunClaim({ ...broken, deadline: 600_000, read: () => "" }),
      /needs a claim path and an owner token/u
    );
  }
});

test("a claim poll interval of zero cannot become a spin", async () => {
  let sleeps = 0;
  let clock = 0;
  await awaitRunClaim({
    claim: "claim",
    token: "t-1",
    requestedAt: 0,
    deadline: 40,
    read: () => "running token=t-1",
    pollIntervalMs: 0,
    now: () => clock,
    sleep: async (ms) => {
      sleeps += 1;
      assert.ok(ms >= 1, "every sleep is at least a millisecond");
      clock += ms;
    }
  });
  assert.ok(sleeps <= 40, `a zero interval must not spin (slept ${sleeps} times)`);
});
