/*
    Contract tests for the release.mjs publish-flow subcommands (T14 phase 2,
    issue #85): verify-release (tag/package/changelog agreement + dist-tag),
    notes (shared changelog extractor, fail-closed), and publish-gate (the
    already-published skip check, with an injectable registry probe).
*/
import test from "node:test";
import assert from "node:assert";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const toolingRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const releaseCli = path.join(toolingRoot, "scripts", "release", "release.mjs");
const { distTagFor, evaluatePublishGate, extractReleaseNotes, verifyRelease } = await import(
  pathToFileURL(releaseCli).href
);

const tempDirs = [];
test.after(() => {
  for (const dir of tempDirs) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

function tempRoot(label) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), `dxt-publish-${label}-`));
  tempDirs.push(dir);
  return dir;
}

function changelogFor(version, body = ["### Added", "", "- Something shipped."]) {
  const dir = tempRoot(version.replaceAll(".", "-"));
  const changelogPath = path.join(dir, "CHANGELOG.md");
  const text = ["# Changelog", "", `## [${version}] - 2026-09-16`, "", ...body, ""].join("\n");
  fs.writeFileSync(changelogPath, text);
  return changelogPath;
}

const RC = "1.0.0-rc26.0";
const STABLE = "1.0.1";

test("verifyRelease accepts agreeing tag, version, and changelog heading", () => {
  const result = verifyRelease({
    version: RC,
    tag: `v${RC}`,
    changelogPath: changelogFor(RC)
  });
  assert.deepStrictEqual(result, { version: RC, tag: `v${RC}`, distTag: "next" });
});

test("verifyRelease reports latest for a stable version", () => {
  const result = verifyRelease({
    version: STABLE,
    tag: `v${STABLE}`,
    changelogPath: changelogFor(STABLE)
  });
  assert.strictEqual(result.distTag, "latest");
});

const verifyFailures = [
  case_("tag mismatch", { version: RC, tag: "v1.0.1" }, /does not match package.json version/),
  case_("unprefixed tag", { version: RC, tag: RC }, /does not match package.json version/),
  case_("invalid version", { version: "not-semver", tag: "vnot-semver" }, /invalid version/),
  case_("missing heading", { version: "2.0.0", tag: "v2.0.0" }, /no "## \[2\.0\.0\] - date" heading/)
];

function case_(name, overrides, expected) {
  return { name, overrides, expected };
}

for (const testCase of verifyFailures) {
  test(`verifyRelease fails closed: ${testCase.name}`, () => {
    assert.throws(
      () =>
        verifyRelease({
          version: testCase.overrides.version,
          tag: testCase.overrides.tag,
          changelogPath: changelogFor(testCase.overrides.version === "2.0.0" ? RC : testCase.overrides.version)
        }),
      testCase.expected
    );
  });
}

for (const [label, body] of [
  ["empty at EOF", []],
  ["whitespace only", ["", " \t", ""]],
  ["empty before an older release", ["## [0.9.0] - 2026-01-01", "", "- Older release notes."]]
]) {
  test(`verify-release rejects ${label} notes before emitting workflow outputs`, () => {
    const changelogPath = changelogFor(RC, body);
    const before = fs.readFileSync(changelogPath);
    const outputPath = path.join(tempRoot("verify-empty"), "github-output.txt");
    assert.throws(
      () => verifyRelease({ version: RC, tag: `v${RC}`, changelogPath }),
      /is empty; nothing to publish as release notes/
    );
    assert.throws(
      () => execFileSync(
        process.execPath,
        [releaseCli, "verify-release", "--version", RC, "--tag", `v${RC}`,
          "--changelog", changelogPath, "--github-output", outputPath],
        { encoding: "utf8", stdio: "pipe" }
      ),
      (error) => {
        assert.strictEqual(error.status, 1);
        assert.match(error.stderr, /is empty; nothing to publish as release notes/);
        return true;
      }
    );
    assert.strictEqual(fs.existsSync(outputPath), false);
    assert.deepStrictEqual(fs.readFileSync(changelogPath), before);
  });
}

test("distTagFor branches on prerelease shape", () => {
  assert.strictEqual(distTagFor(RC), "next");
  assert.strictEqual(distTagFor(STABLE), "latest");
  assert.strictEqual(distTagFor("2.0.0-beta.1+build"), "next");
  assert.throws(() => distTagFor("nope"), /invalid semver version/);
});

test("extractReleaseNotes returns the dated section body", () => {
  const body = extractReleaseNotes({ version: RC, changelogPath: changelogFor(RC) });
  assert.strictEqual(body, "### Added\n\n- Something shipped.");
});

test("extractReleaseNotes picks the requested section, not the newest", () => {
  const dir = tempRoot("two-sections");
  const changelogPath = path.join(dir, "CHANGELOG.md");
  fs.writeFileSync(
    changelogPath,
    [
      "# Changelog",
      "",
      "## [1.0.1] - 2026-10-01",
      "",
      "- Newer entry.",
      "",
      `## [${RC}] - 2026-09-16`,
      "",
      "- Older entry.",
      ""
    ].join("\n")
  );
  assert.strictEqual(
    extractReleaseNotes({ version: RC, changelogPath }),
    "- Older entry."
  );
});

test("extractReleaseNotes fails closed on a missing or empty section", () => {
  const missing = changelogFor(RC);
  assert.throws(
    () => extractReleaseNotes({ version: "2.0.0", changelogPath: missing }),
    /no "## \[2\.0\.0\] - date" heading/
  );
  const dir = tempRoot("empty-section");
  const changelogPath = path.join(dir, "CHANGELOG.md");
  fs.writeFileSync(changelogPath, `# Changelog\n\n## [${RC}] - 2026-09-16\n\n## [1.0.0] - 2026-01-01\n\n- Old.\n`);
  assert.throws(
    () => extractReleaseNotes({ version: RC, changelogPath }),
    /is empty; nothing to publish as release notes/
  );
});

test("evaluatePublishGate skips a version already on the registry", () => {
  const decision = evaluatePublishGate(
    { name: "com.wallstop-studios.dxcommandterminal", version: RC },
    { registryHasVersion: () => true }
  );
  assert.deepStrictEqual(decision, {
    publish: false,
    distTag: "next",
    reason: "com.wallstop-studios.dxcommandterminal@1.0.0-rc26.0 is already on the registry; skipping publish"
  });
});

test("evaluatePublishGate publishes an unseen version with the shape's dist-tag", () => {
  const seen = new Set([`${"com.wallstop-studios.dxcommandterminal"}@1.0.0`]);
  const decision = evaluatePublishGate(
    { name: "com.wallstop-studios.dxcommandterminal", version: RC },
    { registryHasVersion: (name, version) => seen.has(`${name}@${version}`) }
  );
  assert.deepStrictEqual(decision.publish, true);
  assert.strictEqual(decision.distTag, "next");
});

test("evaluatePublishGate validates its inputs", () => {
  assert.throws(
    () => evaluatePublishGate({ name: "pkg", version: "bad" }, { registryHasVersion: () => false }),
    /invalid semver version/
  );
  assert.throws(
    () => evaluatePublishGate({ name: "", version: RC }, { registryHasVersion: () => false }),
    /missing package name/
  );
  assert.throws(() => evaluatePublishGate({ version: RC }, { registryHasVersion: () => false }), /missing package name/);
});

test("CLI verify-release emits github-output; notes writes the notes file", () => {
  const changelogPath = changelogFor(RC);
  const out = path.join(tempRoot("cli"), "output.txt");
  const stdout = execFileSync(
    process.execPath,
    [
      releaseCli,
      "verify-release",
      "--version",
      RC,
      "--tag",
      `v${RC}`,
      "--changelog",
      changelogPath,
      "--github-output",
      out
    ],
    { encoding: "utf8" }
  );
  assert.match(stdout, /dist-tag: next/);
  assert.strictEqual(fs.readFileSync(out, "utf8"), `version=${RC}\ntag=v${RC}\ndist-tag=next\n`);

  const notesPath = path.join(tempRoot("cli-notes"), "notes.md");
  execFileSync(
    process.execPath,
    [releaseCli, "notes", "--version", RC, "--changelog", changelogPath, "--output", notesPath],
    { encoding: "utf8" }
  );
  assert.strictEqual(fs.readFileSync(notesPath, "utf8"), "### Added\n\n- Something shipped.\n");
});

test("CLI publish-gate reports publish=true for an unseen version", () => {
  const out = path.join(tempRoot("gate"), "output.txt");
  const stdout = execFileSync(
    process.execPath,
    [
      releaseCli,
      "publish-gate",
      "--name",
      "com.wallstop-studios.dxcommandterminal",
      "--version",
      RC,
      "--github-output",
      out
    ],
    { encoding: "utf8" }
  );
  assert.match(stdout, /is not on the registry yet/);
  assert.strictEqual(fs.readFileSync(out, "utf8"), "publish=true\ndist-tag=next\n");
});
