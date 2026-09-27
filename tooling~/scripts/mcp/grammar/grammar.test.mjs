import test from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { RUN_CLAIM_FILE, RUN_REQUEST_FILE, parseRunClaim } from "../unity-mcp.mjs";

const GRAMMAR_DIR = path.dirname(fileURLToPath(import.meta.url));
const GRAMMAR_PROJECT = path.join(GRAMMAR_DIR, "grammar.csproj");

/*
    The reporter's grammar, both halves (issue #167).

    DxTerminalTestRunReporter is C# that only ever compiled inside a Unity
    project, so nothing in CI ran it, and the contract it shares with this module
    was held up by pins read out of the reporter's source text. It is now linked
    into a Unity-free project and driven through the callbacks a real run uses:
    a claim it wrote is a claim this file decodes.

    The pins this replaced each cost a behavior-preserving refactor its test
    suite, and two of them - the suite skip and the guarded walk - were C#
    behavior that no text pin could see. A mutation probe measured the pins at 11
    of 13 tried mutations; this gate catches the behavior ones too, because the
    change under test is a claim, not a line of source.

    Invariant globalization is on for the harness process, so a formatting culture
    cannot be observed here. The reporter's own InvariantCulture calls are the
    guarantee, and a locale that is real belongs to a real editor.
 */
let cached;
function runReporter() {
  if (cached !== undefined) return cached;
  const output = path.join(
    fs.mkdtempSync(path.join(os.tmpdir(), "dxt-grammar-")),
    "claims.json"
  );
  const result = spawnSync(
    "dotnet",
    ["run", "--project", GRAMMAR_PROJECT, "-c", "Release", "--", output],
    { encoding: "utf8" }
  );
  assert.equal(
    result.status,
    0,
    `the reporter must compile and run:\n${result.stdout}\n${result.stderr}`
  );
  cached = JSON.parse(fs.readFileSync(output, "utf8"));
  return cached;
}

test("the reader and the reporter agree on the claim files they share", () => {
  // A renamed file on either side is invisible to every other test: the reader
  // would wait out the start grace and then fail with a false diagnosis. The
  // reporter's own accessor answers for the reporter, so nothing here reads its
  // source. The reporter is run once for this file; two runs would build it
  // twice to learn the same thing.
  const { layout } = runReporter();
  assert.equal(layout.claim, RUN_CLAIM_FILE);
  assert.equal(layout.request, RUN_REQUEST_FILE);
});

test("every claim the reporter wrote decodes to the result the run describes", () => {
  const { claims } = runReporter();
  assert.ok(Array.isArray(claims) && 0 < claims.length, "the harness must write claims");

  const corpus = new Set();
  for (const expected of claims) {
    const { label, claim } = expected;
    /*
        The result the run described. The decoded claim is the reader's whole
        shape, so this is one equality rather than a search for each field: a
        field written twice, a field in the wrong place, and a counter the line
        does not lead with all fail here.
     */
    const decoded = parseRunClaim(claim);
    assert.deepEqual(
      decoded,
      expected.state === "refused"
        ? {
            state: expected.state,
            token: expected.token,
            mode: expected.mode,
            reason: expected.reason
          }
        : {
            state: expected.state,
            token: expected.token,
            mode: expected.mode,
            summary: {
              total:
                expected.passed + expected.failed + expected.skipped + expected.inconclusive,
              passed: expected.passed,
              failed: expected.failed,
              skipped: expected.skipped,
              inconclusive: expected.inconclusive,
              failedNames: expected.names,
              failedMore: expected.failedMore
            }
          },
      `${label}: ${claim}`
    );

    /*
        The line itself, which the decoded result cannot show. It stays ASCII, so
        nothing a name holds can break the line from the inside; the counters
        lead it; the duration is a number this side can parse, so its separator
        is part of the grammar; and a names field is written only when there are
        names to put in it.
     */
    assert.deepEqual(
      [...claim].filter((character) => character.codePointAt(0) > 0x7f),
      [],
      `${label}: the claim must stay ASCII, so no name can break the line`
    );
    if (expected.state !== "refused") {
      assert.ok(
        claim.startsWith(
          `pass=${expected.passed} fail=${expected.failed} `
            + `skipped=${expected.skipped} inconclusive=${expected.inconclusive} `
            + `duration=0.5 token=${expected.token} mode=${expected.mode} finished=`
        ),
        `${label}: the claim must lead with its counters, duration, token, mode and stamp: ${claim}`
      );
      const named = claim.includes("failed-names=");
      assert.equal(
        named,
        0 < expected.names.length,
        `${label}: a names field is written only when there are names to put in it`
      );
    }
    for (const name of expected.names ?? []) {
      corpus.add(name);
    }
  }

  /*
      The generated corpus, checked for what it was generated to cover: every
      printable ASCII character, because the line is space separated, its fields
      are key=value and the names are split on a comma, and a name may hold all
      three. A sweep that quietly stopped at A would leave the rest untested with
      every other assertion still green.
   */
  const names = [...corpus];
  for (let code = 0x20; code <= 0x7f; code += 1) {
    const hex = code.toString(16).toUpperCase().padStart(2, "0");
    assert.ok(
      names.some((name) => name.includes(`Char${hex}`)),
      `a decoded name must carry the character U+${hex}`
    );
  }
  // The cases a character-wise encoder gets wrong, by value: they must be in the
  // decoded set, so the corpus cannot shrink to ASCII and still pass.
  for (const name of [
    "Wallstop.Fixture.NoBreakSpace(\u00A0)",
    "Wallstop.Fixture.EmSpace(\u2003)",
    "Wallstop.Fixture.LineSeparator(\u2028)",
    "Wallstop.Fixture.ParagraphSeparator(\u2029)",
    "Wallstop.Fixture.Percent(100%)",
    "Wallstop.Fixture.Newline(a\nb)",
    "Wallstop.Fixture.Tab(a\tb)",
    "Wallstop.Fixture.Emoji(\u{1F600})"
  ]) {
    assert.ok(corpus.has(name), `the decoded names must carry ${JSON.stringify(name)}`);
  }
});
