import test from "node:test";
import assert from "node:assert/strict";
import {
  resolveOptions,
  captureScriptSourcePath,
  captureInstallTarget,
  captureArtifactRoot,
  captureOutputDir,
  CAPTURE_PACKAGE_NAME
} from "../unity-mcp.mjs";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");

test("github token aliases resolve in documented order with env beating file", () => {
  const env = (token) => (token ? { GITHUB_TOKEN: token } : {});
  const local = { GH_TOKEN: "from-file" };
  assert.equal(resolveOptions({}, env("from-env"), local, "/tmp").githubToken, "from-env");
  assert.equal(resolveOptions({}, {}, local, "/tmp").githubToken, "from-file");

  const localFirst = resolveOptions(
    {},
    {},
    { GITHUB_PAT: "pat", GH_TOKEN: "gh" },
    "/tmp"
  );
  assert.equal(localFirst.githubToken, "gh", "alias order applies within .env.local too");
});

test("z.ai key aliases resolve with env beating file", () => {
  assert.equal(
    resolveOptions({}, { ZAI_API_KEY: "e1" }, { Z_AI_API_KEY: "f1" }, "/tmp").zaiToken,
    "e1"
  );
  assert.equal(
    resolveOptions({}, {}, { Z_AI_API_KEY: "f2" }, "/tmp").zaiToken,
    "f2"
  );
});

test("capture script source lives outside Unity compilation", () => {
  const source = captureScriptSourcePath(REPO_ROOT);
  assert.equal(path.basename(source), "DxTerminalStateCapture.cs.txt");
  assert.ok(fs.existsSync(source), `${source} must exist`);
});

test("capture install target follows the DxMessaging Assets/Editor convention", () => {
  const project = path.resolve("/host/UnityProject");
  assert.equal(
    captureInstallTarget(project),
    path.join(project, "Assets", "Editor", "DxTerminalStateCapture.cs")
  );
});

test("capture artifacts prefer the package tree and fall back to Library", () => {
  const project = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-cap-"));
  try {
    // No package directory: falls back to Library (never imported by Unity).
    assert.equal(
      captureArtifactRoot(project),
      path.join(project, "Library", "DxTerminalStateCapture")
    );

    const packageRoot = path.join(project, "Packages", CAPTURE_PACKAGE_NAME);
    fs.mkdirSync(packageRoot, { recursive: true });
    assert.equal(
      captureArtifactRoot(project),
      path.join(packageRoot, ".artifacts", "unity-state")
    );

    const stamp = "2026-09-07T00-00-00-000Z";
    assert.equal(
      captureOutputDir(project, stamp),
      path.join(packageRoot, ".artifacts", "unity-state", stamp)
    );
  } finally {
    fs.rmSync(project, { recursive: true, force: true });
  }
});
