import test from "node:test";
import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { runUnityProcess } from "../release/import-drill.mjs";
import { exportUnityPackage } from "../release/export-unitypackage.mjs";
import { makeFixturePackage } from "./support/fixture-package.mjs";
import {
  applyCanonicalInputProfile,
  readInputHandler,
  loadMatrix,
  parseArgs,
  parseTestResults,
  requireFreshDirectory,
  resolveLegs,
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

test("canonical input settings preserve Unity fields and fail closed on ambiguous keys", () => {
  const project = temporaryRoot("canonical-profile");
  fs.mkdirSync(path.join(project, "ProjectSettings"));
  const file = path.join(project, "ProjectSettings", "ProjectSettings.asset");
  const canonical = "PlayerSettings:\n  serializedVersion: 28\n  companyName: Kept\n  activeInputHandler: 0\n  productName: Kept\n";
  for (const newline of ["\n", "\r\n"]) {
    for (const [profile, value] of [["legacy", 0], ["input-system", 1], ["both", 2]]) {
      const original = canonical.replaceAll("\n", newline);
      fs.writeFileSync(file, original);
      assert.equal(applyCanonicalInputProfile(project, profile), value);
      assert.equal(readInputHandler(project), value);
      assert.equal(fs.readFileSync(file, "utf8"), original.replace("activeInputHandler: 0", `activeInputHandler: ${value}`));
    }
  }
  for (const bad of [
    canonical.replace("  activeInputHandler: 0\n", ""),
    canonical + "  activeInputHandler: 1\n",
    canonical + "  activeInputHandler: invalid\n",
    canonical.replace("activeInputHandler: 0", "activeInputHandler: 3"),
    canonical.replace("activeInputHandler: 0", "activeInputHandler:")
  ]) {
    fs.writeFileSync(file, bad);
    assert.throws(() => applyCanonicalInputProfile(project, "both"), /exactly one activeInputHandler|unsupported activeInputHandler/);
    assert.equal(fs.readFileSync(file, "utf8"), bad);
  }
});

test("default paths load the shipped tooling compatibility fixtures", () => {
  const options = parseArgs(["--artifact", "artifact.unitypackage"]);
  assert.equal(options.matrix, MATRIX_PATH);
  assert.equal(options.fixtureRoot, FIXTURE_ROOT);
  assert.doesNotThrow(() => loadMatrix(options.matrix));
});

test("matrix leg errors name the exact editor flag", () => {
  const matrix = loadMatrix(MATRIX_PATH);
  const missing = { only: "", unity: "", unityById: new Map() };
  assert.throws(() => resolveLegs(missing, matrix), /missing --unity-2021/);
  assert.throws(
    () => resolveLegs({ ...missing, only: "unity-6" }, matrix),
    /missing --unity or --unity-6/
  );
});

test("test results require a strict all-passed test-run", () => {
  const directory = temporaryRoot("results");
  const valid = [
    '<test-run id="1" result="Passed" total="2" passed="2" failed="0" skipped="0" inconclusive="0" />',
    '<?xml version="1.0"?><test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0"><test-suite /></test-run>',
    '<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0"><test-suite name="Fixture" result="Passed"><test-case name="Lifecycle" result="Passed"><output><![CDATA[early log <test-run>ready</test-run>]]></output><!-- literal </test-run> text --></test-case></test-suite></test-run>'
  ];
  const invalid = [
    ["missing", /not written/],
    ["malformed", /malformed test-run root/],
    ["malformed body", /malformed test-run root/],
    ["unclosed child", /malformed test-run root/],
    ["nested test run", /malformed test-run root/],
    ["invalid child close", /malformed test-run root/],
    ["invalid child attributes", /malformed test-run root/],
    ["unseparated child attributes", /malformed test-run root/],
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
    if (name === "nested test run") {
      fs.writeFileSync(file, '<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0"><test-run /></test-run>');
    }
    if (name === "unclosed child" || name === "invalid child close") {
      const body = name === "unclosed child" ? "<test-suite>tail" : "<test-suite></test-suite garbage>";
      fs.writeFileSync(file, `<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0">${body}</test-run>`);
    }
    if (name === "invalid child attributes" || name === "unseparated child attributes") {
      const body = name === "invalid child attributes" ? "<test-suite nonsense></test-suite>" : '<test-suite name="a"name="b"/>';
      fs.writeFileSync(file, `<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0">${body}</test-run>`);
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
  /*
      The fixture carries the real checkout's package identity, so the
      runner's source-vs-artifact compare holds without the real tree's
      whole-package export. Cache freshness is pinned where it is
      decidable: the fixture cache test (a packaged edit re-exports, a
      non-shipped edit does not) and the digest miss; no test compares
      two builds of the real tree.
   */
  const source = JSON.parse(fs.readFileSync(path.resolve(TOOLING_ROOT, "..", "package.json"), "utf8"));
  const packageRoot = makeFixturePackage(temporaryRoot("package"), { name: source.name, version: source.version });
  const artifact = exportUnityPackage({ packageRoot, out: "", cache: true });
  fs.writeFileSync(artifactPath, artifact.buffer);
  const out = path.join(root, "reports");
  const options = { artifact: artifactPath, out, only: "unity-6", unity: "unity", unityById: new Map(), fixtureRoot: FIXTURE_ROOT, keep: true, timeoutMinutes: 5, expectedArtifactSha256: crypto.createHash("sha256").update(artifact.buffer).digest("hex") };
  const matrix = loadMatrix(MATRIX_PATH);
  const testEnvironments = [];
  const testPlatforms = [];
  let compileFailure = null;
  let playModeXml = PASSED_XML;
  let revertPhase = null;
  const requestedHandlers = new Map();
  const runtime = {
    probeEditorVersion: () => "6000.4.6f1",
    validateImportedProject: () => ({ failures: [], checks: [] }),
    runUnity: async (unity, args, logPath, timeoutMs, environment) => {
      const project = args[args.indexOf("-projectPath") + 1];
      const settingsFile = path.join(project, "ProjectSettings", "ProjectSettings.asset");
      if (args.includes("-importPackage")) {
        requestedHandlers.set(project, readInputHandler(project));
        fs.writeFileSync(settingsFile, "PlayerSettings:\n  serializedVersion: 28\n  productName: Preserved\n  activeInputHandler: 0\n");
      } else {
        assert.equal(readInputHandler(project), requestedHandlers.get(project));
        assert.match(fs.readFileSync(settingsFile, "utf8"), /productName: Preserved/);
      }
      assert.equal(
        fs.existsSync(path.join(project, "Assets", "Compatibility", "CompatibilityProbe.cs")),
        !args.includes("-importPackage")
      );
      if (!args.includes("-importPackage")) {
        for (const name of ["FixtureThemePersister.cs", "FixtureThemePersister.cs.meta"]) {
          assert.equal(fs.existsSync(path.join(project, "Assets", "Compatibility", name)), false);
          assert.deepStrictEqual(
            fs.readFileSync(path.join(project, "Assets", name)),
            fs.readFileSync(path.join(FIXTURE_ROOT, "Assets", "Compatibility", name))
          );
        }
      }
      if (!args.includes("-importPackage")) {
        const fixture = path.join(project, "Assets", "Compatibility");
        assert.deepStrictEqual(JSON.parse(fs.readFileSync(path.join(fixture, "Compatibility.Tests.asmdef"))).includePlatforms, []);
        const editorAssembly = JSON.parse(fs.readFileSync(path.join(fixture, "Editor", "Compatibility.Editor.Tests.asmdef")));
        assert.deepStrictEqual(editorAssembly.includePlatforms, ["Editor"]);
        assert.ok(editorAssembly.references.includes("DxCommandTerminal.T13.Compatibility.Tests"));
        assert.equal(fs.existsSync(path.join(fixture, "Editor", "EditorCompatibilityProbe.cs")), true);
        assert.equal(fs.existsSync(path.join(fixture, "EditorCompatibilityProbe.cs")), false);
      }
      const manifest = JSON.parse(fs.readFileSync(path.join(project, "Packages", "manifest.json"), "utf8"));
      assert.equal(manifest.dependencies["com.unity.test-framework"], "1.1.33");
      for (const name of ["DxTerminalStateCapture", "DxTerminalTestRunReporter"]) {
        assert.deepStrictEqual(
          fs.readFileSync(path.join(project, "Assets", "Editor", `${name}.cs`)),
          fs.readFileSync(path.join(TOOLING_ROOT, "scripts", "mcp", `${name}.cs.txt`))
        );
      }
      if (args.includes("-runTests")) {
        assert.equal(args.includes("-quit"), false);
        assert.ok(["editmode", "playmode"].includes(args[args.indexOf("-testPlatform") + 1]));
        testPlatforms.push(args[args.indexOf("-testPlatform") + 1]);
        testEnvironments.push(environment);
        assert.equal(environment.DX_T13_INPUT_PROFILE, { 0: "legacy", 1: "input-system", 2: "both" }[requestedHandlers.get(project)]);
      }
      const phase = args.includes("-runTests") ? args[args.indexOf("-testPlatform") + 1] : args.includes("-importPackage") ? "import" : "settle";
      if (phase === revertPhase) fs.writeFileSync(settingsFile, fs.readFileSync(settingsFile, "utf8").replace(/activeInputHandler: [012]/u, "activeInputHandler: 0"));
      fs.writeFileSync(logPath, compileFailure === null ? "phase complete\n" : `Assets/Editor/${compileFailure}.cs(1,1): error CS1002: ; expected\n`);
      if (args.includes("-runTests")) {
        const resultsPath = args[args.indexOf("-testResults") + 1];
        const platform = args[args.indexOf("-testPlatform") + 1];
        if (platform !== "playmode" || playModeXml !== null) {
          fs.writeFileSync(resultsPath, platform === "playmode" ? playModeXml : PASSED_XML);
        }
      }
      return { code: 0, signal: null, timedOut: false };
    }
  };
  const report = await runMatrix(options, matrix, runtime);
  assert.deepStrictEqual(report.requestedLegs, ["unity-6"]);
  assert.deepStrictEqual(report.selectedLegs, ["unity-6"]);
  assert.equal(testEnvironments[0].DX_T13_DOMAIN_RELOAD_ENABLED, "1");
  assert.deepStrictEqual(testPlatforms, ["editmode", "playmode"]);
  assert.ok(fs.existsSync(path.join(out, "unity-6", "results.xml")));
  assert.ok(fs.existsSync(path.join(out, "unity-6", "playmode-results.xml")));
  const noReloadReport = await runMatrix(
    { ...options, out: path.join(root, "no-reload"), only: "unity-6-no-reload" },
    matrix,
    runtime
  );
  assert.deepStrictEqual(testEnvironments.map((environment) => environment.DX_T13_DOMAIN_RELOAD_ENABLED), ["1", "1", "0", "0"]);
  assert.equal(noReloadReport.failed, false);
  for (const profile of ["input-system", "both"]) {
    for (const domainReloadEnabled of [true, false]) {
      const selected = domainReloadEnabled ? "unity-6" : "unity-6-no-reload";
      const profiled = await runMatrix({ ...options, out: path.join(root, `${profile}-${selected}`), only: selected }, { ...matrix, inputProfile: profile }, runtime);
      assert.equal(profiled.failed, false);
      const value = profile === "input-system" ? 1 : 2;
      assert.equal(profiled.legs[0].requestedInputHandler, value);
      assert.deepStrictEqual(profiled.legs[0].effectiveInputHandlers, ["import", "settle", "tests", "playmode-tests"].map(phase => ({ phase, value })));
    }
  }
  for (const phase of ["settle", "editmode", "playmode"]) {
    revertPhase = phase;
    const reverted = await runMatrix({ ...options, out: path.join(root, `reverted-${phase}`), keep: false }, { ...matrix, inputProfile: "input-system" }, runtime);
    assert.equal(reverted.failed, true);
    assert.match(reverted.legs[0].failure, /input profile verification failed/);
    assert.equal(reverted.legs[0].effectiveInputHandlers.at(-1).value, 0);
    assert.ok(fs.existsSync(reverted.legs[0].project));
    if (phase === "playmode") assert.equal(reverted.legs[0].playModeTestResults.passed, true);
  }
  revertPhase = null;
  assert.match(report.matrixSha256, /^[0-9a-f]{64}$/);
  assert.match(report.git.revision, /^[0-9a-f]{40}$/);
  assert.equal(typeof report.git.dirty, "boolean");
  assert.equal(report.packageIdentity.matches, true);
  assert.equal(report.complete, true);
  assert.equal(report.failed, false);
  assert.deepStrictEqual(report.legs[0].phases.map((phase) => phase.name), ["import", "settle", "tests", "playmode-tests"]);
  assert.equal(report.legs[0].testResults.passed, true);
  assert.equal(report.legs[0].playModeTestResults.passed, true);
  for (const [name, xml] of [
    ["missing", null],
    ["malformed", "malformed"],
    ["zero", '<test-run result="Passed" total="0" passed="0" failed="0" skipped="0" inconclusive="0" />'],
    ["failed", '<test-run result="Failed" total="1" passed="0" failed="1" skipped="0" inconclusive="0" />'],
    ["skipped", '<test-run result="Passed" total="2" passed="1" failed="0" skipped="1" inconclusive="0" />'],
    ["inconclusive", '<test-run result="Passed" total="2" passed="1" failed="0" skipped="0" inconclusive="1" />']
  ]) {
    playModeXml = xml;
    const failed = await runMatrix({ ...options, out: path.join(root, `playmode-${name}`), keep: false }, matrix, runtime);
    assert.equal(failed.failed, true, name);
    assert.equal(failed.legs[0].testResults.passed, true, name);
    assert.equal(failed.legs[0].playModeTestResults.passed, false, name);
    assert.match(failed.legs[0].failure, /playmode-tests compatibility tests did not pass/, name);
    assert.ok(fs.existsSync(failed.legs[0].project), name);
  }
  playModeXml = PASSED_XML;
  for (const name of ["DxTerminalStateCapture", "DxTerminalTestRunReporter"]) {
    assert.equal(
      report.legs[0].editorDevToolHashes[`Assets/Editor/${name}.cs`],
      crypto.createHash("sha256").update(fs.readFileSync(path.join(TOOLING_ROOT, "scripts", "mcp", `${name}.cs.txt`))).digest("hex")
    );
    compileFailure = name;
    const failed = await runMatrix({ ...options, out: path.join(root, name) }, matrix, runtime);
    assert.equal(failed.failed, true);
    assert.equal(failed.legs[0].editorVersion, "6000.4.6f1");
    assert.match(failed.legs[0].failure, /import log validation failed/);
    assert.ok(failed.legs[0].failure.includes(`${name}.cs`));
    assert.equal(failed.legs[0].phases.length, 1);
    assert.ok(fs.existsSync(failed.legs[0].project));
    assert.equal(fs.existsSync(path.join(failed.legs[0].project, "Assets", "Compatibility")), false);
  }
  compileFailure = null;
  const disappearingFixture = path.join(root, "disappearing-fixture");
  fs.cpSync(FIXTURE_ROOT, disappearingFixture, { recursive: true });
  const installationFailure = await runMatrix(
    { ...options, out: path.join(root, "installation-failure"), fixtureRoot: disappearingFixture, keep: false },
    matrix,
    {
      ...runtime,
      runUnity: async (...args) => {
        assert.ok(args[1].includes("-importPackage"), "fixture installation failure must prevent settle and tests");
        const result = await runtime.runUnity(...args);
        fs.renameSync(path.join(disappearingFixture, "Assets"), path.join(disappearingFixture, "UnavailableAssets"));
        return result;
      }
    }
  );
  const failedInstallation = installationFailure.legs[0];
  assert.equal(installationFailure.failed, true);
  assert.equal(installationFailure.complete, true);
  assert.equal(failedInstallation.complete, true);
  assert.match(failedInstallation.failure, /fixture or input profile setup failed: fixture root has no Assets directory/);
  assert.deepStrictEqual(failedInstallation.phases.map((phase) => phase.name), ["import"]);
  assert.ok(fs.existsSync(failedInstallation.project));
  assert.deepStrictEqual(
    JSON.parse(fs.readFileSync(path.join(path.dirname(failedInstallation.project), "manifest.json"), "utf8")),
    failedInstallation
  );
  const mismatch = await runMatrix(
    { ...options, out: path.join(root, "mismatch"), expectedArtifactSha256: "0".repeat(64) },
    matrix,
    runtime
  );
  assert.equal(mismatch.failed, true);
  assert.match(mismatch.legs[0].failure, /artifact hash does not match/);
  await assert.rejects(() => runMatrix(options, matrix, runtime), /must be fresh/);
});
