/*
    Contract tests for the typed-python entry points (issue #220). The
    pre-commit hooks and CI invoke the linters as
    `uv run --project tooling~ python tooling~/scripts/<script>.py`;
    these tests pin that wiring: the entry scripts exist, uv resolves the
    project interpreter, payload output passes through, and the payload's
    exit code is the process exit code (green on this repo, red on a
    fixture that violates the size lint).
 */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import test from "node:test";

const toolingRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const entries = [
  "scripts/lint_llm_instructions.py",
  "scripts/lint_skill_sizes.py",
  "scripts/lint_unity_meta.py",
  "scripts/generate_skills_index.py",
  "scripts/verify_analyzer_payload.py"
];

const hasUv = spawnSync("uv", ["--version"], { encoding: "utf8" }).status === 0;
const needsUv = { skip: hasUv ? false : "uv is not installed" };

function runEntry(args) {
  return spawnSync("uv", ["run", "--project", toolingRoot, "python", ...args], {
    encoding: "utf8",
    timeout: 120_000
  });
}

test("python entry points exist next to the uv project", () => {
  for (const entry of entries) {
    assert.ok(fs.existsSync(path.join(toolingRoot, entry)), entry);
  }
});

test("uv entry runs the size linter green on this repo", needsUv, () => {
  const result = runEntry([path.join(toolingRoot, "scripts", "lint_skill_sizes.py")]);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  assert.match(result.stdout, /Summary: /u);
});

test("uv entry propagates a red payload and its report", needsUv, () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "python-entries-"));
  try {
    fs.mkdirSync(path.join(temp, ".llm"), { recursive: true });
    fs.writeFileSync(path.join(temp, ".llm", "context.md"), "# oversize\n".repeat(301));
    const result = runEntry([
      path.join(toolingRoot, "scripts", "lint_skill_sizes.py"),
      "--repo-root",
      temp
    ]);
    assert.equal(result.status, 1, result.stdout + result.stderr);
    assert.match(result.stdout, /context\.md: 301 lines/u);
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});
