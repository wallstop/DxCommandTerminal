/*
    Contract tests for tooling~/scripts/release/release.mjs (T14, issue #85).

    prepareRelease computes the rewrite against temp fixtures with an
    injected refExists (no git checkout needed); the CLI end-to-end cases
    spawn the script to pin that --dry-run writes nothing and a real prepare
    rewrites exactly package.json + CHANGELOG.md. tag-gate cases pin the
    warn/fail printouts and the github-output file the workflow reads.
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
const { FALLBACK_COMMANDS, evaluateTagGate, prepareRelease } = await import(pathToFileURL(releaseCli).href);

const tempDirs = [];
test.after(() => {
  for (const dir of tempDirs) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

function tempRoot(label) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), `dxt-release-${label}-`));
  tempDirs.push(dir);
  return dir;
}

const FIXTURE_PACKAGE = `{
  "name": "com.wallstop-studios.dxcommandterminal",
  "version": "1.0.0-rc25.0",
  "displayName": "DxCommandTerminal"
}
`;
const FIXTURE_CHANGELOG = [
  "# Changelog",
  "",
  "Intro.",
  "",
  "## Unreleased",
  "",
  "### Added",
  "",
  "- Feature one.",
  "",
  "## [1.0.0-rc25.0] - 2026-03-10",
  "",
  "- Old feature."
].join("\n");

function writeFixture(label, overrides = {}) {
  const root = tempRoot(label);
  const packageJsonPath = path.join(root, "package.json");
  const changelogPath = path.join(root, "CHANGELOG.md");
  fs.writeFileSync(packageJsonPath, overrides.package ?? FIXTURE_PACKAGE);
  fs.writeFileSync(changelogPath, overrides.changelog ?? `${FIXTURE_CHANGELOG}\n`);
  return { root, packageJsonPath, changelogPath };
}

function options(fixture, extra = {}) {
  return { packageJsonPath: fixture.packageJsonPath, changelogPath: fixture.changelogPath, ...extra };
}

test("prepareRelease bumps the version and rotates the changelog", () => {
  const fixture = writeFixture("bump");
  const result = prepareRelease(options(fixture, { bump: "patch" }), { refExists: () => false });
  assert.strictEqual(result.current, "1.0.0-rc25.0");
  assert.strictEqual(result.next, "1.0.1");
  assert.strictEqual(result.tag, "v1.0.1");
  assert.strictEqual(result.branch, "release/v1.0.1");
  assert.match(result.date, /^\d{4}-\d{2}-\d{2}$/);
  assert.strictEqual(result.changes.length, 2);
  const packageChange = result.changes.find((change) => change.label === "package.json");
  assert.match(packageChange.after, /"version": "1\.0\.1"/);
  assert.strictEqual(packageChange.before, FIXTURE_PACKAGE);
  const changelogChange = result.changes.find((change) => change.label === "CHANGELOG.md");
  assert.match(changelogChange.after, /## \[1\.0\.1\] - \d{4}-\d{2}-\d{2}/);
  assert.match(changelogChange.after, /- Feature one\./);
  // Nothing is written by prepareRelease itself; the CLI owns file writes.
  assert.strictEqual(fs.readFileSync(fixture.packageJsonPath, "utf8"), FIXTURE_PACKAGE);
});

test("prepareRelease accepts an explicit version overriding the bump", () => {
  const fixture = writeFixture("explicit");
  const result = prepareRelease(options(fixture, { version: "1.0.0-rc26.0" }), { refExists: () => false });
  assert.strictEqual(result.next, "1.0.0-rc26.0");
  assert.strictEqual(result.tag, "v1.0.0-rc26.0");
  assert.match(result.changes[1].after, /## \[1\.0\.0-rc26\.0\] - \d{4}-\d{2}-\d{2}/);
});

test("prepareRelease fails closed on conflicts and bad input", () => {
  const fixture = writeFixture("fail-closed");
  const base = options(fixture, { bump: "patch" });
  assert.throws(
    () => prepareRelease(base, { refExists: (ref) => ref === "refs/tags/v1.0.1" }),
    /refs\/tags\/v1\.0\.1 already exists/
  );
  assert.throws(
    () => prepareRelease(base, { refExists: (ref) => ref === "refs/remotes/origin/release/v1.0.1" }),
    /release\/v1\.0\.1 already exists \(checked refs\/heads\/release\/v1\.0\.1 and refs\/remotes\/origin\/release\/v1\.0\.1\)/  );
  assert.throws(
    () => prepareRelease(options(fixture, { version: "1.0.0-rc25.0+build.2" }), { refExists: () => false }),
    /carries build metadata; build metadata is not supported for release versions/
  );
  assert.throws(
    () => prepareRelease(base, { refExists: (ref) => ref === "refs/heads/release/v1.0.1" }),
    /release\/v1\.0\.1 already exists \(checked refs\/heads\/release\/v1\.0\.1 and refs\/remotes\/origin\/release\/v1\.0\.1\)/
  );
  assert.throws(
    () => prepareRelease(options(fixture, { bump: "patch", version: "1.0.0-rc25.0" }), { refExists: () => false }),
    /already 1\.0\.0-rc25\.0/
  );
  assert.throws(() => prepareRelease(options(fixture, { bump: "prerelease" }), { refExists: () => false }), /unknown --bump kind/);
  assert.throws(() => prepareRelease(options(fixture, { version: "not-semver" }), { refExists: () => false }), /invalid --version/);
  assert.throws(
    () =>
      prepareRelease(options(fixture, { version: "1.2.3", changelogPath: path.join(tempRoot("empty"), "missing.md") }), {
        refExists: () => false
      }),
    /ENOENT/
  );
  const emptyUnreleased = writeFixture("empty-unreleased", {
    changelog: "# Changelog\n\n## Unreleased\n\n## [1.0.0-rc25.0] - 2026-03-10\n"
  });
  assert.throws(
    () => prepareRelease(options(emptyUnreleased, { bump: "patch" }), { refExists: () => false }),
    /is empty; nothing to release/
  );
  const missingVersionField = writeFixture("no-version", { package: '{"name": "x"}\n' });
  assert.throws(
    () => prepareRelease(options(missingVersionField, { bump: "patch" }), { refExists: () => false }),
    /expected exactly one "version" field/
  );
});

test("CLI dry-run prints the prepared diff and writes nothing", () => {
  const fixture = writeFixture("cli-dry");
  const before = {
    package: fs.readFileSync(fixture.packageJsonPath),
    changelog: fs.readFileSync(fixture.changelogPath)
  };
  const stdout = execFileSync(
    process.execPath,
    [
      releaseCli,
      "prepare",
      "--bump",
      "patch",
      "--package-json",
      fixture.packageJsonPath,
      "--changelog",
      fixture.changelogPath,
      "--dry-run"
    ],
    { cwd: fixture.root }
  ).toString();
  assert.match(stdout, /dry-run: 1\.0\.0-rc25\.0 -> 1\.0\.1/);
  assert.match(stdout, /\+   "version": "1\.0\.1"/);
  assert.match(stdout, /-   "version": "1\.0\.0-rc25\.0"/);
  assert.match(stdout, /\+\+\+ CHANGELOG\.md \(proposed\)/);
  assert.match(stdout, /\+ ## \[1\.0\.1\] - \d{4}-\d{2}-\d{2}/);
  assert.deepStrictEqual(
    { package: fs.readFileSync(fixture.packageJsonPath), changelog: fs.readFileSync(fixture.changelogPath) },
    before
  );
});

test("CLI prepare rewrites exactly package.json and CHANGELOG.md", () => {
  const fixture = writeFixture("cli-apply");
  const marker = path.join(fixture.root, "unrelated.txt");
  fs.writeFileSync(marker, "keep me");
  execFileSync(
    process.execPath,
    [
      releaseCli,
      "prepare",
      "--bump",
      "patch",
      "--package-json",
      fixture.packageJsonPath,
      "--changelog",
      fixture.changelogPath
    ],
    { cwd: fixture.root }
  );
  const manifest = JSON.parse(fs.readFileSync(fixture.packageJsonPath, "utf8"));
  assert.strictEqual(manifest.version, "1.0.1");
  const changelog = fs.readFileSync(fixture.changelogPath, "utf8");
  assert.match(changelog, /## \[1\.0\.1\] - \d{4}-\d{2}-\d{2}/);
  assert.match(changelog, /## Unreleased\n\n## \[1\.0\.1\]/);
  assert.strictEqual(fs.readFileSync(marker, "utf8"), "keep me");
});

/*
    Red drill for the fail-fast invariant: the CLI consults the real git
    refs of its working tree, so a pre-existing v-tag (or release branch)
    refuses the prepare before any file is written.
*/
test("CLI prepare fails closed when the version tag already exists", () => {
  const fixture = writeFixture("cli-tag-conflict");
  execFileSync("git", ["init", "-q"], { cwd: fixture.root });
  execFileSync(
    "git",
    ["-c", "user.email=drill@example.com", "-c", "user.name=drill", "commit", "-q", "--allow-empty", "-m", "init"],
    { cwd: fixture.root }
  );
  execFileSync("git", ["tag", "v1.0.1"], { cwd: fixture.root });
  const before = fs.readFileSync(fixture.packageJsonPath, "utf8");
  let stderr = "";
  try {
    execFileSync(
      process.execPath,
      [
        releaseCli,
        "prepare",
        "--bump",
        "patch",
        "--package-json",
        fixture.packageJsonPath,
        "--changelog",
        fixture.changelogPath
      ],
      { cwd: fixture.root }
    );
    assert.fail("expected non-zero exit");
  } catch (error) {
    stderr = error.stderr.toString();
    assert.notStrictEqual(error.status, 0);
  }
  assert.match(stderr, /refs\/tags\/v1\.0\.1 already exists/);
  assert.strictEqual(fs.readFileSync(fixture.packageJsonPath, "utf8"), before);
});

test("tag-gate warns with fallback commands and emits no tag output", () => {
  const fixture = writeFixture("gate-warn");
  const output = path.join(fixture.root, "github-output.txt");
  const stdout = execFileSync(process.execPath, [
    releaseCli,
    "tag-gate",
    "--version",
    "1.0.0-rc25.0",
    "--subject",
    "docs: readme tweak",
    "--changelog",
    fixture.changelogPath,
    "--github-output",
    output
  ]).toString();
  assert.match(stdout, /::warning::.*documents 1\.0\.0-rc25\.0 but the push subject is not/);
  assert.strictEqual(FALLBACK_COMMANDS("v1.0.0-rc25.0", "1.0.0-rc25.0")[0], 'git tag -a v1.0.0-rc25.0 -m "DxCommandTerminal 1.0.0-rc25.0"');
  assert.strictEqual(fs.readFileSync(output, "utf8"), "action=warn\nversion=1.0.0-rc25.0\ntag=v1.0.0-rc25.0\n");
});

test("tag-gate emits the tag action for a documented release subject", () => {
  const fixture = writeFixture("gate-tag");
  const output = path.join(fixture.root, "github-output.txt");
  const stdout = execFileSync(process.execPath, [
    releaseCli,
    "tag-gate",
    "--version",
    "1.0.0-rc25.0",
    "--subject",
    "release: v1.0.0-rc25.0 (#84)",
    "--changelog",
    fixture.changelogPath,
    "--github-output",
    output
  ]).toString();
  assert.match(stdout, /decision: tag v1\.0\.0-rc25\.0/);
  assert.strictEqual(fs.readFileSync(output, "utf8"), "action=tag\nversion=1.0.0-rc25.0\ntag=v1.0.0-rc25.0\n");
});

test("tag-gate no-ops on an existing tag and on an ordinary push", () => {
  const fixture = writeFixture("gate-noop");
  const output = path.join(fixture.root, "github-output.txt");
  const decision = evaluateTagGate({
    version: "1.0.0-rc25.0",
    subject: "release: v1.0.0-rc25.0 (#84)",
    changelogPath: fixture.changelogPath,
    tagExists: true
  });
  assert.strictEqual(decision.action, "noop");
  const existingTag = execFileSync(
    process.execPath,
    [
      releaseCli,
      "tag-gate",
      "--version",
      "1.0.0-rc25.0",
      "--subject",
      "release: v1.0.0-rc25.0 (#84)",
      "--changelog",
      fixture.changelogPath,
      "--tag-exists",
      "--github-output",
      output
    ],
    { cwd: fixture.root }
  ).toString();
  assert.match(existingTag, /decision: no-op \(tag v1\.0\.0-rc25\.0 already exists/);
  assert.strictEqual(fs.readFileSync(output, "utf8"), "action=noop\nversion=1.0.0-rc25.0\ntag=v1.0.0-rc25.0\n");
  const noHeading = path.join(fixture.root, "NOHEADING.md");
  fs.writeFileSync(noHeading, "# Changelog\n\nno released sections\n");
  const ordinary = execFileSync(
    process.execPath,
    [releaseCli, "tag-gate", "--version", "1.0.0-rc25.0", "--subject", "docs: readme tweak", "--changelog", noHeading],
    { cwd: fixture.root }
  ).toString();
  assert.match(ordinary, /decision: no-op \(ordinary package\.json push/);
});

test("tag-gate fails closed on a release subject without a changelog heading", () => {
  const fixture = writeFixture("gate-fail");
  const noHeading = path.join(fixture.root, "NOHEADING.md");
  fs.writeFileSync(noHeading, "# Changelog\n\nno released sections\n");
  const decision = evaluateTagGate({
    version: "1.0.0-rc25.0",
    subject: "release: v1.0.0-rc25.0 (#84)",
    changelogPath: noHeading,
    tagExists: false
  });
  assert.strictEqual(decision.action, "fail");
  let stderr = "";
  try {
    execFileSync(
      process.execPath,
      [releaseCli, "tag-gate", "--version", "1.0.0-rc25.0", "--subject", "release: v1.0.0-rc25.0", "--changelog", noHeading],
      { cwd: fixture.root }
    );
    assert.fail("expected non-zero exit");
  } catch (error) {
    stderr = error.stderr.toString();
    assert.notStrictEqual(error.status, 0);
  }
  assert.match(stderr, /CHANGELOG has no "## \[1\.0\.0-rc25\.0\] - date" heading/);
});

test("tag-gate rejects an invalid version and cross-subcommand flags", () => {
  const fixture = writeFixture("gate-bad-input");
  let stderr = "";
  try {
    execFileSync(
      process.execPath,
      [releaseCli, "tag-gate", "--version", "garbage", "--subject", "release: vgarbage", "--changelog", fixture.changelogPath],
      { cwd: fixture.root }
    );
    assert.fail("expected non-zero exit");
  } catch (error) {
    stderr = error.stderr.toString();
    assert.notStrictEqual(error.status, 0);
  }
  assert.match(stderr, /package\.json carries an invalid version: garbage/);
  assert.throws(
    () =>
      execFileSync(
        process.execPath,
        [releaseCli, "tag-gate", "--version", "1.0.0-rc25.0", "--subject", "x", "--changelog", fixture.changelogPath, "--dry-run"],
        { cwd: fixture.root }
      ),
    /unknown argument: --dry-run/
  );
  assert.throws(
    () =>
      execFileSync(
        process.execPath,
        [releaseCli, "prepare", "--bump", "patch", "--package-json", fixture.packageJsonPath, "--changelog", fixture.changelogPath, "--tag-exists"],
        { cwd: fixture.root }
      ),
    /unknown argument: --tag-exists/
  );
});
