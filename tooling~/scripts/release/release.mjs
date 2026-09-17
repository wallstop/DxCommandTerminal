/*
    Release CLI (T14 phase 2, issue #85). One entry point, four subcommands:

    prepare        - rewrites package.json + CHANGELOG.md for the next release
                     (bump or explicit version), refusing when the version tag or
                     the release branch already exists. --dry-run prints the
                     prepared diff and writes nothing.
    tag-gate       - decides whether a default-branch push that touched
                     package.json should push its release tag; release-tag.yml
                     wraps this (tag/noop/warn/fail) and executes the push.
    verify-release - the release.yml verify job: confirms the tag, package.json
                     version, and changelog heading agree, and reports the npm
                     dist-tag for the version shape (prerelease -> next).
    notes          - the shared changelog extractor: prints (or writes) the
                     "## [version] - date" section body for the GitHub Release
                     notes, failing closed on a missing or empty section.
    publish-gate   - the release.yml npm-publish skip check: reports whether
                     name@version is already on the registry (re-run safety)
                     plus the dist-tag, via an injectable registry probe.

    All git, file, npm, and network side effects live here; the pure decision
    and rewrite logic lives in versioning.mjs.
*/
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  bumpVersion,
  changelogHasVersionHeading,
  diffLines,
  evaluateTagPush,
  extractSection,
  parseVersion,
  rotateUnreleased
} from "./versioning.mjs";

const REPO_ROOT = path.resolve(fileURLToPath(new URL("../../..", import.meta.url)));
const VERSION_FIELD_PATTERN = /("version"\s*:\s*")([^"]+)(")/g;
const BUMPS = new Set(["patch", "minor", "major"]);
const REGEX_ESCAPE_PATTERN = /[.*+?^${}()|[\]\\]/g;

// Node refuses to spawn .cmd/.bat without a shell on Windows (CVE-2024-27980
// hardening). The npm view arguments are literals with no spaces, so shell
// joining stays safe.
const NPM = process.platform === "win32" ? "npm.cmd" : "npm";
const NPM_SPAWN_OPTIONS = process.platform === "win32" ? { shell: true } : {};

function defaultRefExists(ref) {
  try {
    execFileSync("git", ["rev-parse", "--verify", "-q", ref], {
      stdio: ["ignore", "ignore", "ignore"]
    });
    return true;
  } catch (error) {
    if (error.code === "ENOENT") {
      throw new Error("git is unavailable; cannot check for existing release refs");
    }
    return false;
  }
}

function parseOptionArgs(argv, optionMap, flags = {}) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    const next = () => {
      index += 1;
      if (index >= argv.length) {
        throw new Error(`missing value for ${argument}`);
      }
      return argv[index];
    };
    const flag = flags[argument];
    if (flag !== undefined) {
      options[flag] = true;
      continue;
    }
    const option = optionMap[argument];
    if (option === undefined) {
      throw new Error(`unknown argument: ${argument}`);
    }
    options[option] = next();
  }
  return options;
}

/*
    Computes the prepared release rewrite without touching the filesystem.
    deps.refExists is injectable so contract tests never need a git
    checkout. Returns { current, next, tag, branch, date, changes } where
    each change is { path, label, before, after }.
*/
function prepareRelease(options, deps = {}) {
  const refExists = deps.refExists ?? defaultRefExists;
  const packageText = fs.readFileSync(options.packageJsonPath, "utf8");
  const fieldMatches = [...packageText.matchAll(VERSION_FIELD_PATTERN)];
  if (fieldMatches.length !== 1) {
    throw new Error(`expected exactly one "version" field in package.json, found ${fieldMatches.length}`);
  }
  const current = fieldMatches[0][2];
  if (parseVersion(current) === null) {
    throw new Error(`package.json carries an invalid version: ${current}`);
  }
  let next;
  if (options.version !== undefined) {
    if (parseVersion(options.version) === null) {
      throw new Error(`invalid --version (expected semver): ${options.version}`);
    }
    next = options.version;
  } else if (BUMPS.has(options.bump)) {
    next = bumpVersion(current, options.bump);
  } else {
    throw new Error(`unknown --bump kind: ${options.bump} (expected patch, minor, or major; or pass --version)`);
  }
  if (next === current) {
    throw new Error(`version is already ${next}; nothing to release`);
  }
  if (parseVersion(next).build !== null) {
    throw new Error(`--version ${next} carries build metadata; build metadata is not supported for release versions`);
  }
  const tag = `v${next}`;
  const branch = `release/v${next}`;
  const tagRef = `refs/tags/${tag}`;
  if (refExists(tagRef)) {
    throw new Error(`${tagRef} already exists; delete it or pick another version`);
  }
  const branchRef = `refs/heads/${branch}`;
  const remoteBranchRef = `refs/remotes/origin/${branch}`;
  if (refExists(branchRef) || refExists(remoteBranchRef)) {
    throw new Error(
      `${branch} already exists (checked ${branchRef} and ${remoteBranchRef}); delete it or pick another version`
    );
  }
  const date = new Date().toISOString().slice(0, 10);
  const changelogText = fs.readFileSync(options.changelogPath, "utf8");
  const nextChangelogText = rotateUnreleased(changelogText, next, date);
  const nextPackageText = packageText.replace(
    VERSION_FIELD_PATTERN,
    (match, open, version, close) => (version === current ? `${open}${next}${close}` : match)
  );
  const changes = [];
  if (nextPackageText !== packageText) {
    changes.push({ path: options.packageJsonPath, label: "package.json", before: packageText, after: nextPackageText });
  }
  changes.push({ path: options.changelogPath, label: "CHANGELOG.md", before: changelogText, after: nextChangelogText });
  return { current, next, tag, branch, date, changes };
}

/*
    Evaluates one tag-gate invocation. Returns the decision; the CLI wrapper
    prints, writes github-output, and sets the exit code.
*/
function evaluateTagGate(options) {
  if (parseVersion(options.version) === null) {
    throw new Error(`package.json carries an invalid version: ${options.version}`);
  }
  const changelogText = fs.readFileSync(options.changelogPath, "utf8");
  return evaluateTagPush({
    version: options.version,
    subject: options.subject,
    tagExists: options.tagExists === true,
    changelogHasVersion: changelogHasVersionHeading(changelogText, options.version)
  });
}

/*
    The npm dist-tag for a version shape: prerelease versions (1.0.0-rc26.0)
    publish under "next", stable versions (1.0.1) under "latest". Locked in
    PLAN.md T14.
*/
function distTagFor(version) {
  const parsed = parseVersion(version);
  if (parsed === null) {
    throw new Error(`invalid semver version: ${version}`);
  }
  return parsed.prerelease !== null ? "next" : "latest";
}

/*
    release.yml verify job: the tag being published, the version in
    package.json, and the changelog heading must all agree before anything
    is built or published. Throws (fail closed) on any disagreement.
    Returns { version, tag, distTag } for the CLI wrapper to emit.
*/
function verifyRelease(options) {
  const parsed = parseVersion(options.version);
  if (parsed === null) {
    throw new Error(`package.json carries an invalid version: ${options.version}`);
  }
  if (options.tag !== `v${options.version}`) {
    throw new Error(
      `tag ${options.tag} does not match package.json version ${options.version} (expected v${options.version})`
    );
  }
  extractReleaseNotes(options);
  return { version: options.version, tag: options.tag, distTag: distTagFor(options.version) };
}

/*
    Shared changelog extractor: the "## [version] - date" section body for
    the GitHub Release notes. Fails closed when the heading is missing or
    the section is empty - publishing an empty notes body would mean a
    release the changelog never documented.
*/
function extractReleaseNotes(options) {
  if (parseVersion(options.version) === null) {
    throw new Error(`invalid semver version: ${options.version}`);
  }
  const changelogText = fs.readFileSync(options.changelogPath, "utf8");
  if (!changelogHasVersionHeading(changelogText, options.version)) {
    throw new Error(
      `CHANGELOG has no "## [${options.version}] - date" heading; release notes cannot be extracted`
    );
  }
  const escaped = options.version.replace(REGEX_ESCAPE_PATTERN, "\\$&");
  // [ \t] not \s before $: with the m flag, \s* would run across newlines and
  // the matched heading would swallow the section's leading blank lines,
  // breaking extractSection's exact-line lookup.
  const heading = new RegExp(`^## \\[${escaped}\\] - (\\d{4}-\\d{2}-\\d{2})[ \\t]*$`, "m").exec(changelogText)?.[0];
  const body = extractSection(changelogText, heading);
  if (body === null || body.length === 0) {
    throw new Error(`CHANGELOG section for ${options.version} is empty; nothing to publish as release notes`);
  }
  return body;
}

function evaluatePublishGate(options, deps = {}) {
  const parsed = parseVersion(options.version);
  if (parsed === null) {
    throw new Error(`invalid semver version: ${options.version}`);
  }
  if (typeof options.name !== "string" || options.name.length === 0) {
    throw new Error("missing package name");
  }
  const distTag = distTagFor(options.version);
  const integrity = registryIntegrity(options.name, options.version, deps.exec ?? execFileSync);
  if (integrity !== null) {
    const actual = `sha512-${createHash("sha512").update(fs.readFileSync(options.artifact)).digest("base64")}`;
    if (integrity !== actual) {
      throw new Error("registry integrity does not match the release tarball; refusing to skip publish");
    }
    return {
      publish: false,
      distTag,
      reason: `${options.name}@${options.version} has identical registry integrity; skipping publish`
    };
  }
  return { publish: true, distTag, reason: `${options.name}@${options.version} is not on the registry yet` };
}

function registryIntegrity(name, version, exec) {
  let output;
  try {
    output = exec(NPM, ["view", `${name}@${version}`, "dist.integrity", "--json", "--registry=https://registry.npmjs.org"], {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
      ...NPM_SPAWN_OPTIONS
    });
  } catch (error) {
    let code;
    try {
      code = JSON.parse(String(error.stdout)).error?.code;
    } catch {}
    if (error.status === 1 && code === "E404") {
      return null;
    }
    throw new Error("registry probe failed; cannot prove whether the version exists");
  }
  const integrity = JSON.parse(output);
  if (typeof integrity !== "string" || !/^sha512-[A-Za-z0-9+/]{86}==$/.test(integrity)) {
    throw new Error("registry returned missing or unsupported integrity");
  }
  return integrity;
}

function verifyRemoteTag(options, deps = {}) {
  if (!/^v/.test(options.tag ?? "") || parseVersion(options.tag.slice(1)) === null ||
      !/^[a-f0-9]{40}$/.test(options.sha ?? "")) {
    throw new Error("expected a version tag and full commit SHA");
  }
  const ref = `refs/tags/${options.tag}`;
  const output = (deps.exec ?? execFileSync)("git", ["ls-remote", "--exit-code", "origin", ref, `${ref}^{}`], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"]
  });
  const byName = new Map(output.trim().split(/\r?\n/).map((line) => {
    const [sha, name] = line.split(/\s+/);
    return [name, sha];
  }));
  if (!byName.has(ref) || (byName.get(`${ref}^{}`) ?? byName.get(ref)) !== options.sha) {
    throw new Error("remote tag is missing or does not point to the verified commit");
  }
}

const PREPARE_OPTIONS = {
  "--bump": "bump",
  "--version": "version",
  "--package-json": "packageJsonPath",
  "--changelog": "changelogPath"
};
const GATE_OPTIONS = {
  "--version": "version",
  "--subject": "subject",
  "--changelog": "changelogPath",
  "--github-output": "githubOutput"
};
const PREPARE_FLAGS = { "--dry-run": "dryRun" };
const GATE_FLAGS = { "--tag-exists": "tagExists" };
const VERIFY_OPTIONS = {
  "--version": "version",
  "--tag": "tag",
  "--changelog": "changelogPath",
  "--github-output": "githubOutput"
};
const NOTES_OPTIONS = {
  "--version": "version",
  "--changelog": "changelogPath",
  "--output": "outputPath",
  "--github-output": "githubOutput"
};
const PUBLISH_GATE_OPTIONS = {
  "--name": "name",
  "--version": "version",
  "--artifact": "artifact",
  "--github-output": "githubOutput"
};
const FALLBACK_COMMANDS = (tag, version) => [
  `git tag -a ${tag} -m "DxCommandTerminal ${version}"`,
  `git push origin ${tag}`
];

const VERIFY_FLAGS = {};
const NOTES_FLAGS = {};
const PUBLISH_GATE_FLAGS = {};

function writeGithubOutput(path, decision) {
  if (path === undefined) {
    return;
  }
  const lines = [`action=${decision.action}`];
  if (decision.tag !== null) {
    lines.push(`version=${decision.tag.slice(1)}`, `tag=${decision.tag}`);
  }
  fs.appendFileSync(path, `${lines.join("\n")}\n`);
}

function appendGithubOutput(githubOutput, lines) {
  if (githubOutput === undefined) {
    return;
  }
  fs.appendFileSync(githubOutput, `${lines.join("\n")}\n`);
}

function printDiff(changes) {
  for (const change of changes) {
    console.log(`--- ${change.label}`);
    console.log(`+++ ${change.label} (proposed)`);
    console.log(diffLines(change.before, change.after).join("\n"));
  }
}

function runPrepare(argv) {
  const options = parseOptionArgs(argv, PREPARE_OPTIONS, PREPARE_FLAGS);
  options.packageJsonPath = path.resolve(options.packageJsonPath ?? path.join(REPO_ROOT, "package.json"));
  options.changelogPath = path.resolve(options.changelogPath ?? path.join(REPO_ROOT, "CHANGELOG.md"));
  const result = prepareRelease(options);
  if (options.dryRun) {
    console.log(`[release] dry-run: ${result.current} -> ${result.next} (${result.date}); nothing written, nothing pushed`);
    printDiff(result.changes);
    return;
  }
  for (const change of result.changes) {
    fs.writeFileSync(change.path, change.after);
  }
  console.log(`[release] prepared ${result.current} -> ${result.next} (${result.date})`);
  console.log(`[release] tag: ${result.tag}; branch: ${result.branch}`);
  console.log(
    `[release] wrote ${result.changes.map((change) => change.label).join(", ")}; ` +
      "open the release branch and squash-merge with subject " +
      `"release: ${result.tag}" to trigger auto-tagging`
  );
}

function runTagGate(argv) {
  const options = parseOptionArgs(argv, GATE_OPTIONS, GATE_FLAGS);
  if (options.version === undefined) {
    throw new Error("missing --version");
  }
  if (options.subject === undefined) {
    throw new Error("missing --subject");
  }
  options.changelogPath = path.resolve(options.changelogPath ?? path.join(REPO_ROOT, "CHANGELOG.md"));
  const decision = evaluateTagGate(options);
  writeGithubOutput(options.githubOutput, decision);
  switch (decision.action) {
    case "tag": {
      console.log(`[release-tag] decision: tag ${decision.tag} (${decision.reason})`);
      return;
    }
    case "noop": {
      console.log(`[release-tag] decision: no-op (${decision.reason})`);
      return;
    }
    case "warn": {
      console.log(`::warning::[release-tag] ${decision.reason}`);
      console.log(`[release-tag] refusing auto-tag; manual fallback for ${decision.tag}:`);
      for (const command of FALLBACK_COMMANDS(decision.tag, decision.tag.slice(1))) {
        console.log(`[release-tag]   ${command}`);
      }
      return;
    }
    default: {
      throw new Error(decision.reason);
    }
  }
}

const USAGE =
  "usage: node release.mjs prepare (--bump patch|minor|major | --version X.Y.Z[-pre]) [--dry-run] " +
  "[--package-json <path>] [--changelog <path>]\n" +
  "       node release.mjs tag-gate --version X.Y.Z --subject <commit subject> --changelog <path> " +
  "[--tag-exists] [--github-output <path>]\n" +
  "       node release.mjs verify-release --version X.Y.Z --tag vX.Y.Z --changelog <path> " +
  "[--github-output <path>]\n" +
  "       node release.mjs verify-candidate --dry-run --version X.Y.Z --changelog <path> [--github-output <path>]\n" +
  "       node release.mjs notes --version X.Y.Z --changelog <path> [--output <path>] " +
  "[--github-output <path>]\n" +
  "       node release.mjs publish-gate --name <package> --version X.Y.Z --artifact <tgz> [--github-output <path>]\n" +
  "       node release.mjs verify-remote-tag --tag vX.Y.Z --sha <commit>";

function runVerifyRelease(argv) {
  const options = parseOptionArgs(argv, VERIFY_OPTIONS, VERIFY_FLAGS);
  if (options.version === undefined) {
    throw new Error("missing --version");
  }
  if (options.tag === undefined) {
    throw new Error("missing --tag");
  }
  options.changelogPath = path.resolve(options.changelogPath ?? path.join(REPO_ROOT, "CHANGELOG.md"));
  const result = verifyRelease(options);
  console.log(
    `[release-verify] ok: tag ${result.tag} == package.json ${result.version}, ` +
      `changelog heading present, dist-tag: ${result.distTag}`
  );
  appendGithubOutput(options.githubOutput, [
    `version=${result.version}`,
    `tag=${result.tag}`,
    `dist-tag=${result.distTag}`
  ]);
}

function runVerifyCandidate(argv) {
  const options = parseOptionArgs(argv, {
    "--version": "version",
    "--changelog": "changelogPath",
    "--github-output": "githubOutput"
  }, { "--dry-run": "dryRun" });
  if (options.dryRun !== true) {
    throw new Error("verify-candidate requires --dry-run; publishing requires verify-release and a version tag");
  }
  options.changelogPath = path.resolve(options.changelogPath ?? path.join(REPO_ROOT, "CHANGELOG.md"));
  extractReleaseNotes(options);
  const distTag = distTagFor(options.version);
  console.log(`[release-candidate] artifact-only: package.json ${options.version}, dated notes verified; not publish authorization`);
  appendGithubOutput(options.githubOutput, [`version=${options.version}`, `dist-tag=${distTag}`]);
}

function runNotes(argv) {
  const options = parseOptionArgs(argv, NOTES_OPTIONS, NOTES_FLAGS);
  if (options.version === undefined) {
    throw new Error("missing --version");
  }
  options.changelogPath = path.resolve(options.changelogPath ?? path.join(REPO_ROOT, "CHANGELOG.md"));
  const body = extractReleaseNotes(options);
  if (options.outputPath !== undefined) {
    fs.mkdirSync(path.dirname(path.resolve(options.outputPath)), { recursive: true });
    fs.writeFileSync(options.outputPath, `${body}\n`);
    console.log(`[release-notes] wrote ${body.split("\n").length} lines to ${options.outputPath}`);
  } else {
    console.log(body);
  }
  appendGithubOutput(options.githubOutput, [`lines=${body.split("\n").length}`]);
}

function runPublishGate(argv) {
  const options = parseOptionArgs(argv, PUBLISH_GATE_OPTIONS, PUBLISH_GATE_FLAGS);
  if (options.version === undefined) {
    throw new Error("missing --version");
  }
  if (options.name === undefined) {
    throw new Error("missing --name");
  }
  const decision = evaluatePublishGate(options);
  console.log(`[release-publish] ${decision.reason}`);
  appendGithubOutput(options.githubOutput, [
    `publish=${decision.publish}`,
    `dist-tag=${decision.distTag}`
  ]);
}

function main() {
  const [command, ...rest] = process.argv.slice(2);
  try {
    if (command === "prepare") {
      runPrepare(rest);
    } else if (command === "tag-gate") {
      runTagGate(rest);
    } else if (command === "verify-release") {
      runVerifyRelease(rest);
    } else if (command === "verify-candidate") {
      runVerifyCandidate(rest);
    } else if (command === "notes") {
      runNotes(rest);
    } else if (command === "verify-remote-tag") {
      verifyRemoteTag(parseOptionArgs(rest, { "--tag": "tag", "--sha": "sha" }));
    } else if (command === "publish-gate") {
      runPublishGate(rest);
    } else {
      throw new Error(command === undefined ? "missing subcommand" : `unknown subcommand: ${command}`);
    }
  } catch (error) {
    console.error(`[release] ERROR: ${error.message}`);
    console.error(USAGE);
    process.exitCode = 1;
  }
}

const isMain =
  process.argv[1] !== undefined && fileURLToPath(import.meta.url) === path.resolve(process.argv[1]);

if (isMain) {
  main();
}

export {
  FALLBACK_COMMANDS,
  distTagFor,
  evaluatePublishGate,
  evaluateTagGate,
  extractReleaseNotes,
  prepareRelease,
  verifyRelease,
  verifyRemoteTag
};
