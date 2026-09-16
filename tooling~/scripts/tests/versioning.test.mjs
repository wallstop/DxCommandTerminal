/*
    Contract tests for tooling~/scripts/release/versioning.mjs (T14, issue #85).

    Data-driven tables pin strict semver parsing and bumping, release-subject
    recognition (including GitHub's squash " (#N)" suffix), changelog
    Unreleased extraction/rotation, the release-tag.yml decision table, and
    the dry-run line diff.
*/
import test from "node:test";
import assert from "node:assert";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const modulePath = path.join(
  path.dirname(fileURLToPath(import.meta.url)),
  "../release/versioning.mjs"
);
const {
  UNRELEASED_HEADING,
  bumpVersion,
  changelogHasVersionHeading,
  diffLines,
  evaluateTagPush,
  extractSection,
  parseReleaseSubject,
  parseVersion,
  rotateUnreleased,
  sectionHeading
} = await import(pathToFileURL(modulePath).href);

test("parseVersion accepts full semver and rejects everything else", () => {
  const valid = [
    ["0.0.0", { major: 0, minor: 0, patch: 0, prerelease: null, build: null }],
    ["1.2.3", { major: 1, minor: 2, patch: 3, prerelease: null, build: null }],
    ["1.0.0-rc25.0", { major: 1, minor: 0, patch: 0, prerelease: ["rc25", "0"], build: null }],
    ["1.0.0-0", { major: 1, minor: 0, patch: 0, prerelease: ["0"], build: null }],
    ["1.0.0-rc.1", { major: 1, minor: 0, patch: 0, prerelease: ["rc", "1"], build: null }],
    ["2.0.0+build.5", { major: 2, minor: 0, patch: 0, prerelease: null, build: ["build", "5"] }],
    ["1.2.3-alpha.1+meta", { major: 1, minor: 2, patch: 3, prerelease: ["alpha", "1"], build: ["meta"] }]
  ];
  for (const [value, expected] of valid) {
    const parsed = parseVersion(value);
    assert.notStrictEqual(parsed, null, value);
    assert.strictEqual(parsed.raw, value);
    assert.strictEqual(parsed.major, expected.major, value);
    assert.strictEqual(parsed.minor, expected.minor, value);
    assert.strictEqual(parsed.patch, expected.patch, value);
    assert.deepStrictEqual(parsed.prerelease, expected.prerelease, value);
    assert.deepStrictEqual(parsed.build, expected.build, value);
  }
  const invalid = ["", "v1.2.3", "1.2", "1.2.3.4", "01.2.3", "1.02.3", "1.2.3-", "1.2.3-rc.", "1.2.3+", "latest", "1.2.3-rc#"];
  for (const value of invalid) {
    assert.strictEqual(parseVersion(value), null, value);
  }
  assert.strictEqual(parseVersion(undefined), null);
  assert.strictEqual(parseVersion(null), null);
  assert.strictEqual(parseVersion(42), null);
});

test("bumpVersion raises core numbers and drops prerelease/build", () => {
  const cases = [
    ["1.2.3", "patch", "1.2.4"],
    ["1.2.3", "minor", "1.3.0"],
    ["1.2.3", "major", "2.0.0"],
    ["1.0.0-rc25.0", "patch", "1.0.1"],
    ["1.0.0-rc25.0", "minor", "1.1.0"],
    ["1.0.0-rc25.0", "major", "2.0.0"],
    ["1.2.3+build.5", "patch", "1.2.4"],
    ["0.0.1", "major", "1.0.0"]
  ];
  for (const [value, kind, expected] of cases) {
    assert.strictEqual(bumpVersion(value, kind), expected, `${value} + ${kind}`);
  }
  assert.throws(() => bumpVersion("not-a-version", "patch"), /invalid semver version/);
  assert.throws(() => bumpVersion("1.2.3", "prerelease"), /unknown bump kind/);
  assert.throws(() => bumpVersion("1.2.3", ""), /unknown bump kind/);
});

test("parseReleaseSubject recognizes release squash subjects", () => {
  const valid = [
    ["release: v1.2.3", "1.2.3"],
    ["release: v1.2.3 (#123)", "1.2.3"],
    ["release: v1.0.0-rc26.0 (#5)", "1.0.0-rc26.0"],
    ["release: v1.0.0-rc26.0", "1.0.0-rc26.0"],
    ["release: v1.0.0-rc26.0\n\nLong squash body\nwith lines", "1.0.0-rc26.0"]
  ];
  for (const [subject, expected] of valid) {
    assert.strictEqual(parseReleaseSubject(subject), expected, subject);
  }
  const invalid = [
    "release: 1.2.3",
    "release: v1.2",
    "Release: v1.2.3",
    "release: v1.2.3 extra",
    "release:v1.2.3",
    "merge release: v1.2.3",
    "docs: readme",
    "release: vnot-a-version",
    "release: v1.2.3 (#abc)"
  ];
  for (const subject of invalid) {
    assert.strictEqual(parseReleaseSubject(subject), null, subject);
  }
  assert.strictEqual(parseReleaseSubject(undefined), null);
  assert.strictEqual(parseReleaseSubject(null), null);
});

test("sectionHeading formats the dated heading and validates inputs", () => {
  assert.strictEqual(sectionHeading("1.0.0-rc26.0", "2026-09-16"), "## [1.0.0-rc26.0] - 2026-09-16");
  assert.throws(() => sectionHeading("not-semver", "2026-09-16"), /invalid semver version/);
  assert.throws(() => sectionHeading("1.2.3", "2026-9-16"), /invalid release date/);
  assert.throws(() => sectionHeading("1.2.3", undefined), /invalid release date/);
});

test("extractSection returns the Unreleased body, empty string, or null", () => {
  const changelog = [
    "# Changelog",
    "",
    "Intro.",
    "",
    UNRELEASED_HEADING,
    "",
    "### Added",
    "",
    "- Feature one.",
    "",
    "## [1.0.0-rc25.0] - 2026-03-10",
    "",
    "- Old."
  ].join("\n");
  const body = extractSection(changelog, UNRELEASED_HEADING);
  assert.strictEqual(body, "### Added\n\n- Feature one.");
  const empty = changelog.replace("### Added\n\n- Feature one.", "");
  assert.strictEqual(extractSection(empty, UNRELEASED_HEADING), "");
  assert.strictEqual(extractSection(changelog, "## [9.9.9] - 2026-01-01"), null);
  assert.strictEqual(extractSection("", UNRELEASED_HEADING), null);
  const atEof = changelog.replace("\n\n## [1.0.0-rc25.0] - 2026-03-10\n\n- Old.", "");
  assert.strictEqual(extractSection(atEof, UNRELEASED_HEADING), "### Added\n\n- Feature one.");
  const crlf = changelog.replace(/\n/g, "\r\n");
  assert.strictEqual(extractSection(crlf, UNRELEASED_HEADING), "### Added\n\n- Feature one.");
});

test("changelogHasVersionHeading matches the dated heading", () => {
  const changelog = "## [1.0.0-rc25.0] - 2026-03-10\n\n- entry\n";
  assert.strictEqual(changelogHasVersionHeading(changelog, "1.0.0-rc25.0"), true);
  assert.strictEqual(changelogHasVersionHeading(changelog, "1.0.0-rc26.0"), false);
  assert.strictEqual(
    changelogHasVersionHeading("text [1.0.0-rc25.0] - 2026-03-10 mid-line\n", "1.0.0-rc25.0"),
    false,
    "heading must sit on its own line"
  );
  assert.throws(() => changelogHasVersionHeading(changelog, "not-semver"), /invalid semver version/);
});

test("rotateUnreleased moves the body under the dated heading", () => {
  const changelog = [
    "# Changelog",
    "",
    "Intro.",
    "",
    UNRELEASED_HEADING,
    "",
    "### Added",
    "",
    "- Feature one.",
    "",
    "### Fixed",
    "",
    "- Fix two.",
    "",
    "## [1.0.0-rc25.0] - 2026-03-10",
    "",
    "### Added",
    "",
    "- Old feature."
  ].join("\n");
  const rotated = rotateUnreleased(changelog, "1.0.0-rc26.0", "2026-09-16");
  assert.strictEqual(
    rotated,
    [
      "# Changelog",
      "",
      "Intro.",
      "",
      UNRELEASED_HEADING,
      "",
      "## [1.0.0-rc26.0] - 2026-09-16",
      "",
      "### Added",
      "",
      "- Feature one.",
      "",
      "### Fixed",
      "",
      "- Fix two.",
      "",
      "## [1.0.0-rc25.0] - 2026-03-10",
      "",
      "### Added",
      "",
      "- Old feature."
    ].join("\n")
  );
});

test("rotateUnreleased fails closed on missing or empty Unreleased content", () => {
  const changelog = `${UNRELEASED_HEADING}\n\n- Feature one.\n`;
  assert.throws(() => rotateUnreleased("no unreleased section", "1.2.3", "2026-09-16"), /no "## Unreleased" section/);
  assert.throws(() => rotateUnreleased(changelog, "1.2.3", "2026-09-16"), /last section/);
  assert.throws(
    () => rotateUnreleased(`${UNRELEASED_HEADING}\n\n## [1.2.2] - 2026-01-01\n`, "1.2.3", "2026-09-16"),
    /is empty; nothing to release/
  );
  assert.throws(
    () => rotateUnreleased(`${UNRELEASED_HEADING}\n\n   \n\n## [1.2.2] - 2026-01-01\n`, "1.2.3", "2026-09-16"),
    /is empty; nothing to release/
  );
  assert.throws(() => rotateUnreleased(changelog, "not-semver", "2026-09-16"), /invalid semver version/);
  assert.throws(() => rotateUnreleased(changelog, "1.2.3", "2026-13-01"), /invalid release date/);
});

test("evaluateTagPush covers the release-tag decision table", () => {
  const expectDecision = (actual, action, tag, reason) => {
    assert.strictEqual(actual.action, action);
    assert.strictEqual(actual.tag, tag);
    if (reason instanceof RegExp) {
      assert.match(actual.reason, reason);
    } else {
      assert.strictEqual(actual.reason, reason);
    }
  };
  const tag = (version, subject, tagExists, changelogHasVersion) =>
    evaluateTagPush({ version, subject, tagExists, changelogHasVersion });

  expectDecision(tag("1.0.0-rc26.0", "release: v1.0.0-rc26.0 (#84)", false, true), "tag", "v1.0.0-rc26.0", /release subject names v1\.0\.0-rc26\.0/);
  expectDecision(tag("1.0.0-rc26.0", "release: v1.0.0-rc26.0", false, true), "tag", "v1.0.0-rc26.0", /release subject names v1\.0\.0-rc26\.0/);
  expectDecision(
    tag("1.0.0-rc26.0", "release: v1.0.0-rc26.0 (#84)", false, false),
    "fail",
    "v1.0.0-rc26.0",
    /CHANGELOG has no "## \[1\.0\.0-rc26\.0\] - date" heading/
  );
  expectDecision(tag("1.0.0-rc26.0", "release: v1.0.0-rc26.0 (#84)", true, true), "noop", "v1.0.0-rc26.0", /already exists/);
  expectDecision(tag("1.0.0-rc26.0", "docs: readme tweak", false, true), "warn", "v1.0.0-rc26.0", /documents 1\.0\.0-rc26\.0 but the push subject is not/);
  expectDecision(
    tag("1.0.0-rc26.0", "release: v1.0.0-rc27.0 (#84)", false, true),
    "warn",
    "v1.0.0-rc26.0",
    /names release v1\.0\.0-rc27\.0 but package\.json carries 1\.0\.0-rc26\.0/
  );
  expectDecision(
    tag("1.0.0-rc26.0", "release: v1.0.0-rc27.0 (#84)", false, false),
    "noop",
    "v1.0.0-rc26.0",
    /names release v1\.0\.0-rc27\.0/
  );
  expectDecision(tag("1.0.0-rc26.0", "chore: bump something", false, false), "noop", "v1.0.0-rc26.0", /ordinary package\.json push/);
  expectDecision(tag("garbage", "release: vgarbage", false, false), "fail", null, /invalid version/);
});

test("diffLines emits an exact LCS edit script", () => {
  const cases = [
    ["", "", []],
    ["a\nb\nc", "a\nb\nc", ["  a", "  b", "  c"]],
    ["a\nb", "a\nc", ["  a", "- b", "+ c"]],
    ["x", "", ["- x"]],
    ["", "x", ["+ x"]],
    ["1.0.0-rc25.0", "1.0.0-rc26.0", ["- 1.0.0-rc25.0", "+ 1.0.0-rc26.0"]]
  ];
  for (const [before, after, expected] of cases) {
    assert.deepStrictEqual(diffLines(before, after), expected);
  }
  const removals = diffLines("one\ntwo\nthree", "three");
  assert.deepStrictEqual(removals, ["- one", "- two", "  three"]);
  const additions = diffLines("three", "one\ntwo\nthree");
  assert.deepStrictEqual(additions, ["+ one", "+ two", "  three"]);
});
