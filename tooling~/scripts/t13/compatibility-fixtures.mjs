import { execFileSync } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  importUnityArgs,
  listArtifact,
  probeEditorVersion,
  runUnityProcess,
  scaffoldProject,
  scanLogForErrors,
  SETTLE_PHASE_ENV,
  settleUnityArgs,
  validateImportedProject
} from "../release/import-drill.mjs";
import { exportUnityPackage } from "../release/export-unitypackage.mjs";
const MODULE_DIR = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(MODULE_DIR, "../../..");
const DEFAULT_MATRIX = path.join(REPO_ROOT, "compat", "matrix.json");
const DEFAULT_FIXTURE_ROOT = path.join(REPO_ROOT, "compat", "fixtures");
const TEST_FILTER = "DxCommandTerminal.T13.Compatibility.Tests";
const INPUT_PROFILES = new Map([
  ["legacy", 0],
  ["both", 2],
  ["input-system", 1]
]);
export function loadMatrix(matrixPath = DEFAULT_MATRIX) {
  const matrix = JSON.parse(fs.readFileSync(matrixPath, "utf8"));
  validateMatrix(matrix);
  return matrix;
}
export function validateMatrix(matrix) {
  if (matrix === null || typeof matrix !== "object") throw new Error("compatibility matrix must be an object");
  if (matrix.schemaVersion !== 1 || typeof matrix.fixtureRevision !== "string") throw new Error("compatibility matrix has an unsupported schema");
  validateInputProfile(matrix.inputProfile, "compatibility matrix");
  if (typeof matrix.domainReloadEnabled !== "boolean") throw new Error("compatibility matrix domainReloadEnabled must be boolean");
  if (!Array.isArray(matrix.coverage) || matrix.coverage.length === 0 || matrix.coverage.some((item) => typeof item !== "string" || item.length === 0)) {
    throw new Error("compatibility matrix coverage must name the covered contracts");
  }
  if (!Array.isArray(matrix.editors) || matrix.editors.length === 0) throw new Error("compatibility matrix has no editors");
  const ids = new Set();
  const families = new Set();
  for (const editor of matrix.editors) {
    if (editor === null || typeof editor !== "object") throw new Error("compatibility matrix contains an invalid editor");
    for (const field of ["id", "family", "versionPattern"]) {
      if (typeof editor[field] !== "string" || editor[field].length === 0) throw new Error(`compatibility matrix editor is missing ${field}`);
    }
    if (ids.has(editor.id)) throw new Error(`duplicate compatibility matrix editor: ${editor.id}`);
    validateInputProfile(editor.inputProfile ?? matrix.inputProfile, `editor ${editor.id}`);
    if (editor.domainReloadEnabled !== undefined && typeof editor.domainReloadEnabled !== "boolean") {
      throw new Error(`editor ${editor.id} domainReloadEnabled must be boolean`);
    }
    ids.add(editor.id);
    families.add(editor.family);
  }
  for (const required of ["2021.3", "2022.3", "6000.0"]) {
    if (!families.has(required)) throw new Error(`compatibility matrix is missing the ${required} editor family`);
  }
}
function validateInputProfile(value, owner) {
  if (!INPUT_PROFILES.has(value)) {
    throw new Error(`${owner} inputProfile must be legacy, both, or input-system`);
  }
}
export function legSettings(matrix, leg) { return { inputProfile: leg.inputProfile ?? matrix.inputProfile, domainReloadEnabled: leg.domainReloadEnabled ?? matrix.domainReloadEnabled }; }
export function editorMatches(editor, version) {
  if (typeof version !== "string" || version.length === 0) return false;
  if (editor.expectedVersion !== undefined && version !== editor.expectedVersion) return false;
  return new RegExp(editor.versionPattern, "u").test(version);
}
export function parseArgs(argv) {
  const options = {
    artifact: "",
    matrix: DEFAULT_MATRIX,
    fixtureRoot: DEFAULT_FIXTURE_ROOT,
    out: "",
    only: "",
    unity: "",
    unityById: new Map(),
    keep: false,
    timeoutMinutes: 20
  };
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    const equals = token.indexOf("=");
    const name = token.startsWith("--") && equals !== -1 ? token.slice(2, equals) : token.slice(2);
    const inlineValue = token.startsWith("--") && equals !== -1 ? token.slice(equals + 1) : null;
    const next = () => {
      if (inlineValue !== null) {
        return inlineValue;
      }
      index += 1;
      if (index >= argv.length) {
        throw new Error(`missing value for ${token}`);
      }
      return argv[index];
    };
    if (name === "artifact") {
      options.artifact = path.resolve(next());
    } else if (name === "matrix") {
      options.matrix = path.resolve(next());
    } else if (name === "fixture-root") {
      options.fixtureRoot = path.resolve(next());
    } else if (name === "out") {
      options.out = path.resolve(next());
    } else if (name === "only") {
      options.only = next();
    } else if (name === "unity") {
      options.unity = next();
    } else if (name.startsWith("unity-")) {
      options.unityById.set(name, next());
    } else if (name === "keep") {
      if (inlineValue !== null) {
        throw new Error("--keep does not take a value");
      }
      options.keep = true;
    } else if (name === "timeout-minutes") {
      const value = Number(next());
      if (!Number.isInteger(value) || value < 1) {
        throw new Error("--timeout-minutes must be a positive integer");
      }
      options.timeoutMinutes = value;
    } else {
      throw new Error(`unknown option: ${token}`);
    }
  }
  if (options.artifact === "") {
    throw new Error("missing --artifact <path>");
  }
  return options;
}
export function copyFixtureFiles(fixtureRoot, project) {
  const source = path.join(fixtureRoot, "Assets");
  if (!fs.existsSync(source)) throw new Error(`fixture root has no Assets directory: ${fixtureRoot}`);
  fs.cpSync(source, path.join(project, "Assets"), { recursive: true });
}
export function writeProjectSettings(project, matrix, leg) {
  const settingsDirectory = path.join(project, "ProjectSettings");
  const settings = legSettings(matrix, leg);
  fs.mkdirSync(settingsDirectory, { recursive: true });
  fs.writeFileSync(
    path.join(settingsDirectory, "EditorSettings.asset"),
    `%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!159 &15900000\nEditorSettings:\n  m_ObjectHideFlags: 0\n  m_Enabled: 1\n  m_EnterPlayModeOptionsEnabled: ${settings.domainReloadEnabled ? 0 : 1}\n  m_EnterPlayModeOptions: ${settings.domainReloadEnabled ? 0 : 1}\n`
  );
  fs.writeFileSync(
    path.join(settingsDirectory, "ProjectSettings.asset"),
    `%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &12900000\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  activeInputHandler: ${INPUT_PROFILES.get(settings.inputProfile)}\n`
  );
}
function testUnityArgs(project, resultsPath, logPath) {
  return [
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath",
    project,
    "-runTests",
    "-testPlatform",
    "playmode",
    "-testFilter",
    TEST_FILTER,
    "-testResults",
    resultsPath,
    "-logFile",
    logPath
  ];
}
export function parseTestResults(resultsPath) {
  if (!fs.existsSync(resultsPath)) return { passed: false, result: null, reason: "test results file was not written" };
  const text = fs.readFileSync(resultsPath, "utf8");
  const paired = /^(?:\s*<\?xml\s+[^?]*\?>\s*)?<test-run\b([^>]*)>([\s\S]*)<\/test-run>\s*$/u.exec(text);
  const selfClosing = /^(?:\s*<\?xml\s+[^?]*\?>\s*)?<test-run\b([^>]*)\/>\s*$/u.exec(text);
  const match = paired ?? selfClosing;
  if (match === null || (paired !== null && (!validXmlBody(match[2]) || /<\/?test-run\b/u.test(match[2])))) {
    return { passed: false, result: null, reason: "test results have a malformed test-run root" };
  }
  const attributes = parseTestRunAttributes(match[1].trim());
  if (attributes === null) return { passed: false, result: null, reason: "test results have malformed test-run attributes" };
  const result = attributes.result ?? null;
  if (result !== "Passed") return { passed: false, result, reason: "test-run result is not Passed" };
  const counts = {};
  for (const name of ["total", "passed", "failed", "skipped", "inconclusive"]) {
    const value = attributes[name];
    if (typeof value !== "string" || !/^(0|[1-9][0-9]*)$/u.test(value)) return { passed: false, result, reason: `test-run ${name} count is invalid` };
    counts[name] = Number(value);
    if (!Number.isSafeInteger(counts[name])) return { passed: false, result, reason: `test-run ${name} count is invalid` };
  }
  if (counts.total === 0 || counts.passed === 0) return { passed: false, result, reason: "test-run contains no passed tests" };
  if (counts.passed !== counts.total) return { passed: false, result, reason: "test-run passed count does not equal total" };
  if (counts.failed !== 0 || counts.skipped !== 0 || counts.inconclusive !== 0) {
    return { passed: false, result, reason: "test-run contains failed, skipped, or inconclusive tests" };
  }
  return { passed: true, result, reason: null };
}
function validXmlBody(body) {
  const stack = [];
  let offset = 0;
  while (offset < body.length) {
    const start = body.indexOf("<", offset);
    if (start === -1) return true;
    if (body.startsWith("<!--", start)) {
      const end = body.indexOf("-->", start + 4);
      if (end === -1) return false;
      offset = end + 3;
      continue;
    }
    if (body.startsWith("<![CDATA[", start)) {
      const end = body.indexOf("]]>", start + 9);
      if (end === -1) return false;
      offset = end + 3;
      continue;
    }
    if (body.startsWith("<?", start)) {
      const end = body.indexOf("?>", start + 2);
      if (end === -1) return false;
      offset = end + 2;
      continue;
    }
    const end = findXmlTagEnd(body, start);
    if (end === -1) return false;
    const tag = body.slice(start, end + 1);
    const name = /^<\/?([A-Za-z_][A-Za-z0-9_.:-]*)/u.exec(tag)?.[1];
    if (name === undefined) return false;
    if (tag.startsWith("</")) {
      if (stack.pop() !== name) return false;
    } else if (!tag.endsWith("/>")) {
      stack.push(name);
    }
    offset = end + 1;
  }
  return stack.length === 0;
}
function findXmlTagEnd(body, start) {
  let quote = null;
  for (let index = start + 1; index < body.length; index += 1) {
    const character = body[index];
    if (quote !== null) {
      if (character === quote) quote = null;
    } else if (character === "\"" || character === "'") {
      quote = character;
    } else if (character === ">") {
      return index;
    }
  }
  return -1;
}
function parseTestRunAttributes(source) {
  const attributes = {};
  const whitespace = /\s+/y;
  const attribute = /([A-Za-z_][A-Za-z0-9_.-]*)\s*=\s*"([^"]*)"/y;
  let offset = 0;
  while (offset < source.length) {
    if (offset > 0) {
      whitespace.lastIndex = offset;
      if (whitespace.exec(source) === null) return null;
      offset = whitespace.lastIndex;
    }
    attribute.lastIndex = offset;
    const match = attribute.exec(source);
    if (match === null || attributes[match[1]] !== undefined) return null;
    attributes[match[1]] = match[2];
    offset = attribute.lastIndex;
  }
  return attributes;
}
function phaseOutcome(run, phase) {
  if (run.timedOut === true) {
    return `${phase} timed out`;
  }
  if (run.spawnError !== undefined) {
    return `${phase} could not launch Unity: ${run.spawnError}`;
  }
  if (run.signal !== null && run.signal !== undefined) {
    return `${phase} was killed by ${run.signal}`;
  }
  if (run.code !== 0) {
    return `${phase} exited ${run.code}`;
  }
  return null;
}
function phaseRecord(run, logPath) {
  return {
    exitCode: run.code ?? null,
    signal: run.signal ?? null,
    timedOut: run.timedOut === true,
    log: logPath,
    logErrors: scanLogForErrors(logPath)
  };
}
function sha256File(filePath) {
  return crypto.createHash("sha256").update(fs.readFileSync(filePath)).digest("hex");
}
function fixtureHashes(fixtureRoot) {
  const root = path.join(fixtureRoot, "Assets");
  const result = {};
  const visit = (directory) => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true }).sort((left, right) => left.name.localeCompare(right.name))) {
      const filePath = path.join(directory, entry.name);
      if (entry.isDirectory()) {
        visit(filePath);
      } else if (entry.isFile()) {
        result[path.relative(fixtureRoot, filePath).split(path.sep).join("/")] = sha256File(filePath);
      }
    }
  };
  visit(root);
  return result;
}
export function requireFreshDirectory(directory) {
  fs.mkdirSync(path.dirname(directory), { recursive: true });
  try {
    fs.mkdirSync(directory);
  } catch (error) {
    if (error.code === "EEXIST") {
      throw new Error(`compatibility report directory must be fresh: ${directory}`);
    }
    throw error;
  }
}
function sourcePackageIdentity() {
  const source = JSON.parse(fs.readFileSync(path.join(REPO_ROOT, "package.json"), "utf8"));
  return { name: source.name, version: source.version };
}
function packageIdentityRecord(artifactIdentity) {
  const source = sourcePackageIdentity();
  const matches = artifactIdentity === null ? null : artifactIdentity.name === source.name && artifactIdentity.version === source.version;
  return { artifact: artifactIdentity, source, matches };
}
function matrixHash(matrix) { return sha256Buffer(Buffer.from(JSON.stringify(matrix))); }
function sha256Buffer(buffer) { return crypto.createHash("sha256").update(buffer).digest("hex"); }
function currentArtifactSha256() {
  return sha256Buffer(exportUnityPackage({ packageRoot: REPO_ROOT, out: "" }).buffer);
}
function gitState() {
  try {
    const revision = execFileSync("git", ["rev-parse", "HEAD"], {
      cwd: REPO_ROOT,
      encoding: "utf8"
    }).trim();
    const status = execFileSync("git", ["status", "--porcelain"], {
      cwd: REPO_ROOT,
      encoding: "utf8"
    });
    return { revision, dirty: status.length > 0 };
  } catch {
    return { revision: "(unavailable)", dirty: null };
  }
}
function stamp() {
  return `${Date.now()}-${process.pid}`;
}
function writeManifest(pathname, manifest) { manifest.complete = true; fs.writeFileSync(pathname, `${JSON.stringify(manifest, null, 2)}\n`); }
export async function runLeg(options, matrix, leg, runtime = {}) {
  const reportDirectory = path.join(options.out, leg.id);
  const project = path.join(reportDirectory, "project");
  const manifestPath = path.join(reportDirectory, "manifest.json");
  requireFreshDirectory(reportDirectory);
  const unity = options.unityById.get(leg.id) ?? (options.only === leg.id ? options.unity : "");
  if (unity === "") {
    throw new Error(`missing Unity editor path for ${leg.id}`);
  }
  const artifactBuffer = fs.readFileSync(options.artifact);
  const artifact = listArtifact(artifactBuffer);
  const artifactSha256 = sha256Buffer(artifactBuffer);
  const expectedArtifactSha256 = options.expectedArtifactSha256 ?? currentArtifactSha256();
  const probeVersion = runtime.probeEditorVersion ?? probeEditorVersion;
  const runUnity = runtime.runUnity ?? runUnityProcess;
  const validateProject = runtime.validateImportedProject ?? validateImportedProject;
  const editorVersion = probeVersion(unity);
  const settings = legSettings(matrix, leg);
  const requestedLegs = options.requestedLegs ?? [leg.id];
  const selectedLegs = options.selectedLegs ?? [leg.id];
  const manifest = {
    fixtureRevision: matrix.fixtureRevision,
    coverage: matrix.coverage,
    matrixSha256: matrixHash(matrix),
    git: gitState(),
    requestedLegs,
    selectedLegs,
    requestedFamily: leg.family,
    editor: leg.id,
    editorVersion,
    unity,
    inputProfile: settings.inputProfile,
    domainReloadEnabled: settings.domainReloadEnabled,
    artifact: options.artifact,
    artifactSha256,
    expectedArtifactSha256,
    artifactHashMatches: artifactSha256 === expectedArtifactSha256,
    packageIdentity: packageIdentityRecord(artifact.packageIdentity),
    project,
    fixtureHashes: fixtureHashes(options.fixtureRoot),
    phases: [],
    checks: [],
    testResults: null,
    failed: true,
    failure: null,
    complete: false
  };
  if (manifest.artifactHashMatches !== true) {
    manifest.failure = `artifact hash does not match the current checkout: ${artifactSha256} != ${expectedArtifactSha256}`;
    writeManifest(manifestPath, manifest);
    return manifest;
  }
  if (manifest.packageIdentity.matches !== true) {
    manifest.failure = `artifact package identity does not match the checkout: ${JSON.stringify(manifest.packageIdentity)}`;
    writeManifest(manifestPath, manifest);
    return manifest;
  }
  if (!editorMatches(leg, editorVersion)) {
    manifest.failure = `Unity ${editorVersion} does not match ${leg.family} (${leg.versionPattern})`;
    writeManifest(manifestPath, manifest);
    return manifest;
  }
  try {
    scaffoldProject(project, editorVersion, artifact.scratchDependencies);
    writeProjectSettings(project, matrix, leg);
    copyFixtureFiles(options.fixtureRoot, project);
  } catch (error) {
    manifest.failure = `fixture scaffold failed: ${error.message}`;
    writeManifest(manifestPath, manifest);
    return manifest;
  }
  const logsDirectory = path.join(reportDirectory, "logs");
  fs.mkdirSync(logsDirectory, { recursive: true });
  const resultsPath = path.join(reportDirectory, "results.xml");
  const phases = [
    { name: "import", args: importUnityArgs(project, options.artifact, path.join(logsDirectory, "import.log")), env: undefined },
    { name: "settle", args: settleUnityArgs(project, path.join(logsDirectory, "settle.log")), env: SETTLE_PHASE_ENV },
    { name: "tests", args: testUnityArgs(project, resultsPath, path.join(logsDirectory, "tests.log")), env: { DX_T13_EXPECT_DOMAIN_RELOAD: settings.domainReloadEnabled ? "0" : "1", DX_T13_PERSISTENCE_FILE: path.join(reportDirectory, "fixture-persistence.json") } }
  ];
  const timeoutMs = options.timeoutMinutes * 60_000;
  for (const phase of phases) {
    const logPath = phase.args[phase.args.indexOf("-logFile") + 1];
    const run = await runUnity(unity, phase.args, logPath, timeoutMs, phase.env);
    const record = phaseRecord(run, logPath);
    manifest.phases.push({ name: phase.name, ...record });
    manifest.failure = phaseOutcome(run, phase.name);
    if (manifest.failure === null && 0 < record.logErrors.length) {
      manifest.failure = `${phase.name} log validation failed:\n  - ${record.logErrors.join("\n  - ")}`;
    }
    if (manifest.failure !== null) {
      break;
    }
    if (phase.name === "settle") {
      const validation = validateProject(project, artifact);
      manifest.checks = validation.checks;
      if (0 < validation.failures.length) {
        manifest.failure = `import validation failed:\n  - ${validation.failures.join("\n  - ")}`;
        break;
      }
    }
    if (phase.name === "tests") {
      manifest.testResults = parseTestResults(resultsPath);
      if (!manifest.testResults.passed) {
        manifest.failure = `compatibility tests did not pass: ${manifest.testResults.reason ?? manifest.testResults.result}`;
        break;
      }
    }
  }
  manifest.failed = manifest.failure !== null;
  writeManifest(manifestPath, manifest);
  if (!manifest.failed && !options.keep) {
    fs.rmSync(project, { recursive: true, force: true });
  }
  return manifest;
}
export function resolveLegs(options, matrix) {
  if (options.only !== "") {
    const leg = matrix.editors.find((editor) => editor.id === options.only);
    if (leg === undefined) {
      throw new Error(`unknown --only editor: ${options.only}`);
    }
    if (options.unity === "" && !options.unityById.has(leg.id)) {
      throw new Error(`missing --unity or --${leg.id} for ${leg.id}`);
    }
    return [leg];
  }
  for (const leg of matrix.editors) {
    if (!options.unityById.has(leg.id)) {
      throw new Error(`missing --unity-${leg.id} for matrix leg ${leg.id}`);
    }
  }
  return matrix.editors;
}
export async function runMatrix(options, matrix = loadMatrix(options.matrix), runtime = {}) {
  const output = options.out === "" ? path.join(REPO_ROOT, ".artifacts", "t13", stamp()) : options.out;
  requireFreshDirectory(output);
  const requestedLegs = options.only === "" ? matrix.editors.map((leg) => leg.id) : [options.only];
  const selectedLegs = resolveLegs(options, matrix).map((leg) => leg.id);
  const expectedArtifactSha256 = options.expectedArtifactSha256 ?? currentArtifactSha256();
  const resolved = { ...options, out: output, requestedLegs, selectedLegs, expectedArtifactSha256 };
  const artifactBuffer = fs.readFileSync(options.artifact);
  const artifact = listArtifact(artifactBuffer);
  const manifests = [];
  for (const legId of selectedLegs) {
    manifests.push(
      await runLeg(resolved, matrix, matrix.editors.find((leg) => leg.id === legId), runtime)
    );
  }
  const report = {
    fixtureRevision: matrix.fixtureRevision,
    coverage: matrix.coverage,
    matrixSha256: matrixHash(matrix),
    git: gitState(),
    requestedLegs,
    selectedLegs,
    artifact: options.artifact,
    artifactSha256: sha256Buffer(artifactBuffer),
    expectedArtifactSha256,
    artifactHashMatches: sha256Buffer(artifactBuffer) === expectedArtifactSha256,
    packageIdentity: packageIdentityRecord(artifact.packageIdentity),
    failed: manifests.some((manifest) => manifest.failed),
    legs: manifests,
    complete: false
  };
  writeManifest(path.join(output, "matrix-manifest.json"), report);
  return report;
}
const isMain = process.argv[1] !== undefined && fileURLToPath(import.meta.url) === path.resolve(process.argv[1]);
if (isMain) {
  try {
    const options = parseArgs(process.argv.slice(2));
    const matrix = loadMatrix(options.matrix);
    const report = await runMatrix(options, matrix);
    for (const leg of report.legs) {
      console.log(`[t13] ${leg.editor}: ${leg.failed ? "FAILED" : "PASSED"} (${leg.editorVersion})`);
    }
    if (report.failed) {
      process.exitCode = 1;
    }
  } catch (error) {
    console.error(`[t13] ERROR: ${error.message}`);
    process.exitCode = 1;
  }
}
