/*
    Contract tests for the release.mjs publish-flow subcommands (T14 phase 2,
    issue #85): verify-release (tag/package/changelog agreement + dist-tag),
    notes (shared changelog extractor, fail-closed), and publish-gate (the
    already-published skip check, with an injectable registry probe).
*/
import test from "node:test";
import assert from "node:assert";
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { runInNewContext } from "node:vm";

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

const workflowRoot = path.resolve(toolingRoot, "../.github/workflows");
const publishWorkflow = fs.readFileSync(path.join(workflowRoot, "release.yml"), "utf8");
const tagWorkflow = fs.readFileSync(path.join(workflowRoot, "release-tag.yml"), "utf8");

function job(text, name) {
  const start = text.indexOf(`\n  ${name}:\n`);
  assert.notStrictEqual(start, -1, `missing job ${name}`);
  return text.slice(start + 1).split(/\n(?=  [a-z][a-z-]*:\n)/)[0];
}

function condition(text) {
  return text.match(/^    if: (.+)$/m)?.[1];
}

function enabled(text, context) {
  return runInNewContext(condition(text).replace(/^\$\{\{ | \}\}$/g, ""), context, { timeout: 100 });
}

test("workflow structure: no tag-push trigger and both entry points default to rehearsal", () => {
  const triggers = publishWorkflow.split("\nconcurrency:")[0];
  assert.doesNotMatch(triggers, /^  push:/m);
  for (const entry of ["workflow_call", "workflow_dispatch"]) {
    const block = triggers.split(`  ${entry}:`)[1].split(/\n  \w+:/)[0];
    assert.match(block, /dry_run:[\s\S]*?default: true/);
  }
});

test("workflow structure: rehearsal excludes all Release, npm, and attestation writer jobs", () => {
  for (const name of ["attest", "publish-npm", "github-release"]) {
    const writer = job(publishWorkflow, name);
    assert.strictEqual(condition(writer), "${{ !inputs.dry_run }}");
    assert.strictEqual(enabled(writer, { inputs: { dry_run: true } }), false);
    assert.strictEqual(enabled(writer, { inputs: { dry_run: false } }), true);
  }
  for (const name of ["verify", "validate", "unitypackage"]) {
    const build = job(publishWorkflow, name);
    assert.match(build, /contents: read/);
    assert.doesNotMatch(build, /(?:contents|id-token|attestations): write|gh release|npm publish|attest-build-provenance/);
  }
  assert.doesNotMatch(job(publishWorkflow, "github-release"), /always\(\)|cancelled\(\)|result == 'skipped'/);
});

test("workflow structure: publishing depends on npm success and references the release environment", () => {
  assert.match(job(publishWorkflow, "publish-npm"), /needs: \[verify, validate, unitypackage, attest\]/);
  assert.match(job(publishWorkflow, "github-release"), /needs: \[verify, validate, unitypackage, publish-npm\]/);
  for (const name of ["publish-npm", "github-release"]) {
    assert.match(job(publishWorkflow, name), /environment: release/);
  }
});

test("workflow structure: automatic tag handoff depends on successful tagging", () => {
  const handoff = job(tagWorkflow, "publish");
  assert.match(handoff, /needs: tag/);
  assert.strictEqual(condition(handoff), "${{ needs.tag.outputs.action == 'tag' }}");
  assert.match(handoff, /uses: \.\/\.github\/workflows\/release.yml/);
  assert.match(handoff, /tag: \$\{\{ needs.tag.outputs.tag \}\}/);
  assert.match(handoff, /expected_sha: \$\{\{ github.sha \}\}/);
  assert.match(handoff, /dry_run: false/);
  for (const action of ["tag", "noop", "warn", "fail", ""]) {
    assert.strictEqual(enabled(handoff, { needs: { tag: { outputs: { action } } } }), action === "tag");
  }
  assert.doesNotMatch(handoff, /always\(\)|continue-on-error|secrets: inherit/);
  for (const permission of ["contents", "id-token", "attestations"]) {
    assert.match(handoff, new RegExp(`${permission}: write`));
  }
  assert.match(job(tagWorkflow, "tag"), /action: \$\{\{ steps.decide.outputs.action \}\}/);
  assert.match(job(tagWorkflow, "tag"), /tag: \$\{\{ steps.decide.outputs.tag \}\}/);
});

test("workflow structure: default-branch guard and immutable checkouts", () => {
  const verify = job(publishWorkflow, "verify");
  assert.strictEqual(condition(verify), "github.repository == 'wallstop/DxCommandTerminal' && github.ref == 'refs/heads/master'");
  assert.match(verify, /ref: refs\/tags\/\$\{\{ env.TAG \}\}/);
  assert.match(verify, /EXPECTED_SHA: \$\{\{ inputs.expected_sha \}\}/);
  assert.match(verify, /test -z "\$EXPECTED_SHA" \|\| test "\$sha" = "\$EXPECTED_SHA"/);
  for (const name of ["validate", "unitypackage", "publish-npm", "github-release"]) {
    assert.match(job(publishWorkflow, name), /ref: \$\{\{ needs.verify.outputs.sha \}\}/);
  }
});

test("workflow structure: remote checks precede npm and all Release mutations", () => {
  assert.strictEqual(publishWorkflow.match(/verify-remote-tag --tag "\$TAG" --sha "\$GITHUB_SHA"/g)?.length, 4);
  assert.match(job(publishWorkflow, "publish-npm"), /verify-remote-tag[\s\S]*npm publish/);
  assert.match(job(publishWorkflow, "publish-npm"), /--artifact "com.wallstop-studios.dxcommandterminal-\$\{version\}.tgz"/);
  assert.match(job(publishWorkflow, "github-release"), /args=\(--verify-tag --draft/);
  const ci = fs.readFileSync(path.join(workflowRoot, "tooling-tests.yml"), "utf8");
  assert.strictEqual(ci.match(/"\.github\/workflows\/release\*\.yml"/g)?.length, 2);
});

test("workflow shell prerequisite fails closed without opt-in or matching provenance SHA", { skip: process.platform === "win32" }, () => {
  const verify = job(publishWorkflow, "verify");
  assert.match(verify, /RELEASE_ENABLED: \$\{\{ vars.RELEASE_PUBLISH_ENABLED \}\}/);
  assert.match(verify, /DRY_RUN: \$\{\{ inputs.dry_run \}\}/);
  const script = verify.split("        run: |\n")[1].split("\n\n")[0].replace(/^          /gm, "");
  const sha = "a".repeat(40);
  for (const [dryRun, enabled, eventSha, expected, succeeds] of [
    ["true", "", "b".repeat(40), "", true],
    ["false", "", sha, "", false],
    ["false", "false", sha, "", false],
    ["false", "TRUE", sha, "", false],
    ["false", "true", "b".repeat(40), "", false],
    ["false", "true", sha, "b".repeat(40), false],
    ["false", "true", sha, sha, true]
  ]) {
    const out = path.join(tempRoot("prerequisite"), "output");
    const invoke = () => execFileSync("bash", ["-e", "-c", `git() { printf '%s\\n' "$BUILD_SHA"; };\n${script}`], {
      encoding: "utf8", stdio: "pipe",
      env: { ...process.env, BUILD_SHA: sha, GITHUB_SHA: eventSha, EXPECTED_SHA: expected,
        DRY_RUN: dryRun, RELEASE_ENABLED: enabled, GITHUB_OUTPUT: out }
    });
    if (succeeds) {
      invoke();
      assert.strictEqual(fs.readFileSync(out, "utf8"), `sha=${sha}\n`);
    } else {
      assert.throws(invoke, (error) => error.status === 1);
      assert.strictEqual(fs.existsSync(out), false);
    }
  }
});

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

for (const version of [RC, STABLE]) {
  test(`registry boundary accepts identical bytes only: ${version}`, () => {
    const artifact = path.join(tempRoot("integrity"), "package.tgz");
    fs.writeFileSync(artifact, "release bytes");
    const integrity = `sha512-${createHash("sha512").update("release bytes").digest("base64")}`;
    const options = { name: "com.wallstop-studios.dxcommandterminal", version, artifact };
    const exec = (command, args) => {
      assert.match(command, /^npm(?:\.cmd)?$/);
      assert.deepStrictEqual(args, ["view", `${options.name}@${version}`, "dist.integrity", "--json", "--registry=https://registry.npmjs.org"]);
      return JSON.stringify(integrity);
    };
    assert.strictEqual(evaluatePublishGate(options, { exec }).publish, false);
    fs.writeFileSync(artifact, "different bytes");
    assert.throws(() => evaluatePublishGate(options, { exec }), /integrity does not match/);
    for (const output of ["", "null", '"sha1-old"', "{}", "[]"]) {
      assert.throws(() => evaluatePublishGate(options, { exec: () => output }));
    }
    for (const code of ["E404", "E401", "E503", undefined]) {
      const probe = () => { throw { status: 1, stdout: JSON.stringify({ error: { code } }) }; };
      if (code === "E404") {
        const result = evaluatePublishGate(options, { exec: probe });
        assert.strictEqual(result.publish, true);
        assert.strictEqual(result.distTag, version === RC ? "next" : "latest");
      } else {
        assert.throws(() => evaluatePublishGate(options, { exec: probe }), /registry probe failed/);
      }
    }
  });
}

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

function mockedCli(args, output, expectedCommand, expectedArgs) {
  const preload = `import cp from "node:child_process";
    import {syncBuiltinESMExports} from "node:module";
    import assert from "node:assert/strict";
    cp.execFileSync = (command, args) => {
      assert.equal(command, ${JSON.stringify(expectedCommand)});
      assert.deepEqual(args, ${JSON.stringify(expectedArgs)});
      const output = ${JSON.stringify(output)};
      if (output === null) throw Error("remote unavailable");
      return output;
    };
    syncBuiltinESMExports();`;
  return execFileSync(process.execPath, ["--import", `data:text/javascript,${encodeURIComponent(preload)}`, releaseCli, ...args],
    { encoding: "utf8", stdio: "pipe" });
}

for (const tag of ["v1.0.1", "v1.0.0-rc26.0", "v1.0.1-candidate.1"]) {
  test(`CLI remote tag boundary: ${tag}, annotated/lightweight/moved/deleted/unavailable`, () => {
    const sha = "a".repeat(40);
    const ref = `refs/tags/${tag}`;
    const args = ["verify-remote-tag", "--tag", tag, "--sha", sha];
    const gitArgs = ["ls-remote", "--exit-code", "origin", ref, `${ref}^{}`];
    for (const output of [`${sha}\t${ref}\n`, `${"b".repeat(40)}\t${ref}\n${sha}\t${ref}^{}\n`]) {
      mockedCli(args, output, "git", gitArgs);
    }
    for (const output of [`${"b".repeat(40)}\t${ref}\n`, `${sha}\t${ref}\n${"b".repeat(40)}\t${ref}^{}\n`, "", null]) {
      assert.throws(() => mockedCli(args, output, "git", gitArgs), (error) => error.status === 1);
    }
  });
}

test("CLI publish-gate compares downloaded tarball bytes before emitting skip output", () => {
  const dir = tempRoot("cli-integrity");
  const artifact = path.join(dir, "package.tgz");
  const out = path.join(dir, "output.txt");
  fs.writeFileSync(artifact, "release bytes");
  const integrity = `sha512-${createHash("sha512").update("release bytes").digest("base64")}`;
  const args = ["publish-gate", "--name", "pkg", "--version", RC, "--artifact", artifact, "--github-output", out];
  const npmArgs = ["view", `pkg@${RC}`, "dist.integrity", "--json", "--registry=https://registry.npmjs.org"];
  mockedCli(args, JSON.stringify(integrity), process.platform === "win32" ? "npm.cmd" : "npm", npmArgs);
  assert.strictEqual(fs.readFileSync(out, "utf8"), "publish=false\ndist-tag=next\n");
  fs.writeFileSync(artifact, "different bytes");
  fs.unlinkSync(out);
  assert.throws(() => mockedCli(args, JSON.stringify(integrity), process.platform === "win32" ? "npm.cmd" : "npm", npmArgs));
  assert.strictEqual(fs.existsSync(out), false);
});
