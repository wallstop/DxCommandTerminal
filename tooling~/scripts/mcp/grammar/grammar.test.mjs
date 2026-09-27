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
    behavior that no text pin could see. A mutation probe measured the pins at
    11 of 13 tried mutations; this gate catches the behavior ones too, because
    the change under test is a claim, not a line of source.

    Invariant globalization is on for the harness process, so a formatting
    culture cannot be observed here. The reporter's own InvariantCulture calls
    are the guarantee, and a locale that is real belongs to a real editor.
 */
let cached;
function readReporterClaims() {
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
  // reporter's own accessor answers for the reporter, so nothing here reads
  // its source.
  const { layout } = readReporterClaims();
  assert.equal(layout.claim, RUN_CLAIM_FILE);
  assert.equal(layout.request, RUN_REQUEST_FILE);
});

test("every claim the reporter wrote decodes back to the names that went in", () => {
  const { claims } = readReporterClaims();
  assert.ok(Array.isArray(claims) && 0 < claims.length, "the harness must write claims");

  const decoded = new Map();
  for (const { label, claim, names } of claims) {
    const parsed = parseRunClaim(claim);
    if (claim.startsWith("did-not-run")) {
      assert.equal(parsed.state, "refused", `${label}: a refusal must decode as one`);
      assert.deepEqual(names, [], `${label}: a refusal names nothing`);
      continue;
    }
    assert.equal(parsed.state, "finished", `${label}: ${claim}`);
    assert.deepEqual(
      parsed.summary.failedNames,
      names,
      `${label}: the names must survive the line intact`
    );
    // The remainder is the editor's own count, bounded by the failures this
    // same line reports: a count the editor could not have written must never be
    // printed as if it were one.
    assert.ok(
      parsed.summary.failedMore <=
        Math.max(0, parsed.summary.failed - parsed.summary.failedNames.length),
      `${label}: failed-more must be bounded by the failures on the line`
    );
    for (const name of names) {
      decoded.set(name, true);
    }
  }

  /*
      The generated corpus, checked for what it was generated to cover: every
      printable ASCII character, because the line is space separated, its fields
      are key=value and the names are split on a comma, and a name may hold all
      three. A sweep that quietly stopped at A would leave the rest untested with
      every other assertion still green.
   */
  const corpus = [...decoded.keys()];
  for (let code = 0x20; code <= 0x7f; code += 1) {
    const hex = code.toString(16).toUpperCase().padStart(2, "0");
    assert.ok(
      corpus.some((name) => name.includes(`Char${hex}`)),
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
    assert.ok(decoded.has(name), `the decoded names must carry ${JSON.stringify(name)}`);
  }
});
