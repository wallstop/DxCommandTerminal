import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { runUnityProcess } from "../release/import-drill.mjs";
import { exportUnityPackage } from "../release/export-unitypackage.mjs";
import {
  loadMatrix,
  parseTestResults,
  requireFreshDirectory,
  runMatrix,
  validateMatrix,
  writeProjectSettings
} from "../t13/compatibility-fixtures.mjs";

const TOOLING_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const MATRIX_PATH = path.join(TOOLING_ROOT, "compat", "matrix.json");
const FIXTURE_ROOT = path.join(TOOLING_ROOT, "compat", "fixtures");
const PASSED_XML = '<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0" />';
const temporaryDirectories = [];

function temporaryRoot(label) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), `dxct-t13-${label}-`));
  temporaryDirectories.push(directory);
  return directory;
}

test.after(() => {
  for (const directory of temporaryDirectories) fs.rmSync(directory, { recursive: true, force: true });
});

test("matrix validates profiles, coverage, and no-domain-reload settings", () => {
  const matrix = loadMatrix(MATRIX_PATH);
  assert.deepStrictEqual(matrix.editors.map((editor) => editor.family), ["2021.3", "2022.3", "6000.0", "6000.0"]);
  assert.equal(matrix.editors[3].domainReloadEnabled, false);
  assert.ok(matrix.coverage.includes("playmode-scene-change"));
  const invalid = [
    [{ ...matrix, inputProfile: "new" }, /inputProfile must be/],
    [{ ...matrix, domainReloadEnabled: "true" }, /domainReloadEnabled must be boolean/],
    [{ ...matrix, coverage: [] }, /coverage must name/]
  ];
  for (const [value, expected] of invalid) assert.throws(() => validateMatrix(value), expected);
  const settingsCases = [
    [matrix.editors[0], 0, 0],
    [{ ...matrix.editors[0], inputProfile: "both" }, 2, 0],
    [{ ...matrix.editors[0], inputProfile: "input-system" }, 1, 0],
    [matrix.editors[3], 0, 1]
  ];
  for (const [leg, inputHandler, enterPlayMode] of settingsCases) {
    const project = temporaryRoot(`settings-${inputHandler}-${enterPlayMode}`);
    writeProjectSettings(project, matrix, leg);
    assert.match(fs.readFileSync(path.join(project, "ProjectSettings/ProjectSettings.asset"), "utf8"), new RegExp(`activeInputHandler: ${inputHandler}\\b`));
    assert.match(fs.readFileSync(path.join(project, "ProjectSettings/EditorSettings.asset"), "utf8"), new RegExp(`m_EnterPlayModeOptionsEnabled: ${enterPlayMode}\\b`));
    assert.match(fs.readFileSync(path.join(project, "ProjectSettings/EditorSettings.asset"), "utf8"), new RegExp(`m_EnterPlayModeOptions: ${enterPlayMode}\\b`));
  }
});

test("test results require a strict all-passed test-run", () => {
  const directory = temporaryRoot("results");
  const valid = [
    '<test-run id="1" result="Passed" total="2" passed="2" failed="0" skipped="0" inconclusive="0" />',
    '<?xml version="1.0"?><test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0"><test-suite /></test-run>'
  ];
  const invalid = [
    ["missing", /not written/],
    ["malformed", /malformed test-run root/],
    ["malformed body", /malformed test-run root/],
    ["zero", /no passed tests/],
    ["negative", /total count is invalid/],
    ["failed", /failed, skipped, or inconclusive/],
    ["skipped", /failed, skipped, or inconclusive/],
    ["inconclusive", /failed, skipped, or inconclusive/],
    ["missing count", /inconclusive count is invalid/],
    ["mismatch", /does not equal total/],
    ["duplicate", /malformed test-run attributes/],
    ["result", /result is not Passed/]
  ];
  for (const [index, xml] of valid.entries()) {
    const file = path.join(directory, `valid-${index}.xml`);
    fs.writeFileSync(file, xml);
    assert.deepStrictEqual(parseTestResults(file), { passed: true, result: "Passed", reason: null });
  }
  for (const [name, expected] of invalid) {
    const file = path.join(directory, `${name}.xml`);
    if (name !== "missing") {
      const counts = name === "zero" ? "0" : "1";
      const passed = name === "zero" || name === "result" ? "0" : counts;
      const failed = ["failed", "result"].includes(name) ? "1" : "0";
      const skipped = name === "skipped" ? "1" : "0";
      const inconclusive = name === "inconclusive" ? "1" : "0";
      const result = name === "result" ? "Failed" : "Passed";
      fs.writeFileSync(file, `<test-run result="${result}" total="${counts}" passed="${passed}" failed="${failed}" skipped="${skipped}" inconclusive="${inconclusive}" />`);
    }
    if (name === "malformed" || name === "malformed body") {
      fs.writeFileSync(
        file,
        name === "malformed body"
          ? '<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0"><<<</test-run>'
          : "<test-run result=\"Passed\""
      );
    }
    if (name === "negative") fs.writeFileSync(file, '<test-run result="Passed" total="-1" passed="-1" failed="0" skipped="0" inconclusive="0" />');
    if (name === "missing count") fs.writeFileSync(file, '<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" />');
    if (name === "mismatch") fs.writeFileSync(file, '<test-run result="Passed" total="2" passed="1" failed="0" skipped="0" inconclusive="0" />');
    if (name === "duplicate") fs.writeFileSync(file, '<test-run result="Passed" result="Failed" total="1" passed="1" failed="0" skipped="0" inconclusive="0" />');
    const result = parseTestResults(file);
    assert.equal(result.passed, false, name);
    assert.match(result.reason, expected, name);
  }
});

test("report and import outputs fail closed on reuse", () => {
  const root = temporaryRoot("fresh");
  const report = path.join(root, "report");
  const project = path.join(root, "project");
  fs.mkdirSync(project);
  fs.writeFileSync(path.join(project, "stale"), "stale");
  for (const name of ["manifest.json", "unity-import.log", "unity-settle.log"]) fs.writeFileSync(path.join(root, name), "stale");
  requireFreshDirectory(report);
  fs.writeFileSync(path.join(report, "results.xml"), "stale");
  assert.throws(() => requireFreshDirectory(report), /must be fresh/);
  assert.deepStrictEqual(fs.readdirSync(root), ["manifest.json", "project", "report", "unity-import.log", "unity-settle.log"]);
});

test("Unity timeout resolves after close and spawn errors survive", async () => {
  const script = "setInterval(()=>{},1000)";
  const started = Date.now();
  const timeout = await runUnityProcess(process.execPath, ["-e", script], "", 20);
  assert.equal(timeout.timedOut, true);
  assert.ok(timeout.code !== undefined);
  assert.ok(timeout.signal !== undefined);
  assert.ok(Date.now() - started >= 20);
  const failure = await runUnityProcess(path.join(temporaryRoot("missing"), "unity"), [], "", 1000);
  assert.equal(failure.timedOut, false);
  assert.match(failure.spawnError, /ENOENT/);
});

test("matrix manifests record complete identity and reject stale reports", async () => {
  const root = temporaryRoot("run");
  const artifactPath = path.join(root, "artifact.unitypackage");
  const artifact = exportUnityPackage({ packageRoot: path.resolve(TOOLING_ROOT, ".."), out: "" });
  fs.writeFileSync(artifactPath, artifact.buffer);
  const out = path.join(root, "reports");
  const options = { artifact: artifactPath, out, only: "unity-6", unity: "unity", unityById: new Map(), fixtureRoot: FIXTURE_ROOT, keep: true, timeoutMinutes: 5 };
  const matrix = loadMatrix(MATRIX_PATH);
  const runtime = {
    probeEditorVersion: () => "6000.4.6f1",
    validateImportedProject: () => ({ failures: [], checks: [] }),
    runUnity: async (unity, args, logPath) => {
      fs.writeFileSync(logPath, "phase complete\n");
      if (args.includes("-runTests")) {
        const project = args[args.indexOf("-projectPath") + 1];
        fs.writeFileSync(path.join(path.dirname(project), "results.xml"), PASSED_XML);
      }
      return { code: 0, signal: null, timedOut: false };
    }
  };
  const report = await runMatrix(options, matrix, runtime);
  assert.deepStrictEqual(report.requestedLegs, ["unity-6"]);
  assert.deepStrictEqual(report.selectedLegs, ["unity-6"]);
  assert.match(report.matrixSha256, /^[0-9a-f]{64}$/);
  assert.match(report.git.revision, /^[0-9a-f]{40}$/);
  assert.equal(typeof report.git.dirty, "boolean");
  assert.equal(report.packageIdentity.matches, true);
  assert.equal(report.complete, true);
  assert.equal(report.failed, false);
  assert.deepStrictEqual(report.legs[0].phases.map((phase) => phase.name), ["import", "settle", "tests"]);
  const mismatch = await runMatrix(
    { ...options, out: path.join(root, "mismatch"), expectedArtifactSha256: "0".repeat(64) },
    matrix,
    runtime
  );
  assert.equal(mismatch.failed, true);
  assert.match(mismatch.legs[0].failure, /artifact hash does not match/);
  await assert.rejects(() => runMatrix(options, matrix, runtime), /must be fresh/);
});
