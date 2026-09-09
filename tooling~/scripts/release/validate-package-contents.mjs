import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(fileURLToPath(new URL("../../..", import.meta.url)));

const REQUIRED_FILES = [
  "README.md",
  "README.md.meta",
  "LICENSE",
  "LICENSE.meta",
  "CHANGELOG.md",
  "CHANGELOG.md.meta",
  "package.json.meta",
  "Runtime/WallstopStudios.DxCommandTerminal.asmdef",
  "Runtime/WallstopStudios.DxCommandTerminal.asmdef.meta",
  "Editor/WallstopStudios.DxCommandTerminal.Editor.asmdef",
  "Editor/WallstopStudios.DxCommandTerminal.Editor.asmdef.meta",
  "Tests/Runtime/WallstopStudios.DxCommandTerminal.Tests.Runtime.asmdef",
  "Tests/Runtime/WallstopStudios.DxCommandTerminal.Tests.Runtime.asmdef.meta"
];

const FORBIDDEN_EXACT = new Set([
  "AGENTS.md",
  "CLAUDE.md",
  "doc.md",
  ".cursorrules",
  ".editorconfig",
  ".gitattributes",
  ".gitignore",
  ".dockerignore",
  ".pre-commit-config.yaml",
  ".env.example",
  "package-lock.json",
  ".DS_Store"
]);

const FORBIDDEN_PREFIXES = [
  "tooling~",
  "node_modules",
  "scripts",
  ".git",
  ".github",
  ".llm",
  ".devcontainer",
  ".artifacts",
  "progress",
  ".env",
  ".vscode",
  ".codex",
  ".cursor",
  ".copilot",
  ".nanocoder"
];

const NPM = process.platform === "win32" ? "npm.cmd" : "npm";

function fail(message) {
  console.error(`[package-validate] ERROR: ${message}`);
  process.exitCode = 1;
}

function pack() {
  const destination = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-pack-"));
  const stdout = execFileSync(NPM, ["pack", "--pack-destination", destination], {
    cwd: REPO_ROOT,
    encoding: "utf8"
  });
  return path.join(destination, stdout.trim().split(/\r?\n/).at(-1));
}

function listTarball(tarball) {
  const stdout = execFileSync("tar", ["-tzf", tarball], { encoding: "utf8" });
  return stdout
    .split(/\r?\n/)
    .filter(Boolean)
    .map((entry) => entry.replace(/^package\//, ""))
    .filter((entry) => entry.length > 0 && !entry.endsWith("/"));
}

function extractFile(tarball, entry) {
  return execFileSync("tar", ["-xzf", tarball, "-O", `package/${entry}`], {
    encoding: "utf8",
    maxBuffer: 16 * 1024 * 1024
  });
}

function trackedFiles() {
  const stdout = execFileSync("git", ["ls-files"], { cwd: REPO_ROOT, encoding: "utf8" });
  return new Set(stdout.split(/\r?\n/).filter(Boolean));
}

function main() {
  const rootManifest = JSON.parse(
    fs.readFileSync(path.join(REPO_ROOT, "package.json"), "utf8")
  );
  const tarball = pack();
  const entries = listTarball(tarball);
  const entrySet = new Set(entries);
  let problems = 0;

  const check = (ok, message) => {
    if (!ok) {
      fail(message);
      problems += 1;
    }
  };

  const shippedManifest = JSON.parse(extractFile(tarball, "package.json"));
  check(
    shippedManifest.name === rootManifest.name && shippedManifest.version === rootManifest.version,
    `shipped manifest identity mismatch: expected ${rootManifest.name}@${rootManifest.version}, ` +
      `got ${shippedManifest.name}@${shippedManifest.version}`
  );

  const allowlist = rootManifest.files;
  check(
    Array.isArray(allowlist) && 0 < allowlist.length,
    "package.json must declare an npm files allowlist"
  );
  if (Array.isArray(allowlist)) {
    const tracked = trackedFiles();
    const expected = new Set(["package.json"]);
    for (const entry of tracked) {
      if (allowlist.some((root) => entry === root || entry.startsWith(`${root}/`))) {
        expected.add(entry);
      }
    }
    for (const missing of expected) {
      if (!entrySet.has(missing)) {
        fail(`tracked file missing from tarball: ${missing}`);
        problems += 1;
      }
    }
    for (const extra of entrySet) {
      if (!expected.has(extra)) {
        fail(`untracked or unallowed file shipped in tarball: ${extra}`);
        problems += 1;
      }
    }
  }

  for (const required of REQUIRED_FILES) {
    check(entrySet.has(required), `required file missing from tarball: ${required}`);
  }

  for (const entry of entries) {
    if (FORBIDDEN_EXACT.has(entry)) {
      fail(`non-shippable file in tarball: ${entry}`);
      problems += 1;
      continue;
    }
    if (FORBIDDEN_PREFIXES.some((prefix) => entry.startsWith(`${prefix}/`))) {
      fail(`non-shippable path in tarball: ${entry}`);
      problems += 1;
    }
  }

  const shippedFiles = entries.filter((entry) => !entry.endsWith("/"));
  const impliedDirectories = new Set();
  for (const file of shippedFiles) {
    let current = path.dirname(file);
    while (current && current !== "." && !impliedDirectories.has(current)) {
      impliedDirectories.add(current);
      current = path.dirname(current);
    }
  }

  for (const directory of impliedDirectories) {
    check(
      entrySet.has(`${directory}.meta`),
      `shipped directory without its folder meta: ${directory}`
    );
  }

  for (const file of shippedFiles) {
    if (file.endsWith(".meta")) {
      const target = file.slice(0, -".meta".length);
      check(
        entrySet.has(target) || impliedDirectories.has(target),
        `orphan meta in tarball: ${file}`
      );
    } else if (file !== "package.json") {
      check(entrySet.has(`${file}.meta`), `shipped file without meta: ${file}`);
    }
  }

  for (const asmdef of REQUIRED_FILES.filter((candidate) => candidate.endsWith(".asmdef"))) {
    let parsed;
    try {
      parsed = JSON.parse(extractFile(tarball, asmdef));
    } catch (error) {
      check(false, `asmdef is not valid JSON: ${asmdef} (${error.message})`);
      continue;
    }
    check(
      typeof parsed.name === "string" && 0 < parsed.name.length,
      `asmdef missing a name: ${asmdef}`
    );
  }

  console.log(
    `[package-validate] ${path.basename(tarball)}\n` +
      `[package-validate] ${entries.length} entries checked; ` +
      `${problems === 0 ? "all content checks passed" : `${problems} problem(s) found`}`
  );
  if (0 < problems) {
    process.exitCode = 1;
  }
}

main();
