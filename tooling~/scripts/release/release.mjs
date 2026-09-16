/*
    Release CLI (T14 phase 2, issue #85). One entry point, two subcommands:

    prepare   - rewrites package.json + CHANGELOG.md for the next release
                (bump or explicit version), refusing when the version tag or
                the release branch already exists. --dry-run prints the
                prepared diff and writes nothing.
    tag-gate  - decides whether a default-branch push that touched
                package.json should push its release tag; release-tag.yml
                wraps this (tag/noop/warn/fail) and executes the push.

    All git and file side effects live here; the pure decision and rewrite
    logic lives in versioning.mjs.
*/
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  bumpVersion,
  changelogHasVersionHeading,
  diffLines,
  evaluateTagPush,
  parseVersion,
  rotateUnreleased
} from "./versioning.mjs";

const REPO_ROOT = path.resolve(fileURLToPath(new URL("../../..", import.meta.url)));
const VERSION_FIELD_PATTERN = /("version"\s*:\s*")([^"]+)(")/g;
const BUMPS = new Set(["patch", "minor", "major"]);

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
const FALLBACK_COMMANDS = (tag, version) => [
  `git tag -a ${tag} -m "DxCommandTerminal ${version}"`,
  `git push origin ${tag}`
];

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
  "[--tag-exists] [--github-output <path>]";

function main() {
  const [command, ...rest] = process.argv.slice(2);
  try {
    if (command === "prepare") {
      runPrepare(rest);
    } else if (command === "tag-gate") {
      runTagGate(rest);
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

export { FALLBACK_COMMANDS, evaluateTagGate, prepareRelease };
