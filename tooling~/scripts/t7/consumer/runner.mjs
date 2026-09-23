/*
    T07 consumer-project drill (PLAN.md "T07 - compile/import/reload",
    "Remaining scenarios: consumer-project edit and clean import").

    Measures, in a throwaway consumer project, what the stock in-editor
    probes cannot see:

    - clean import: the exported release artifact imported through the
      documented CLI argument into an empty project, then a settle pass
      waiting out the import-triggered compilation;
    - consumer edit->ready: paired batch invocations of the same project,
      control (no edit) vs one appended comment line on a shipped Runtime
      source; the paired duration delta isolates the package recompile
      window from the fixed batch startup cost.

    Measurement only. The compile-overhead gate question is settled
    (#124 decision, session-053): this drill never claims a gate, and its
    small-sample output is evidence, not a gate run. Batch invocations run
    while the live host editor stays open, so control/edit pairs share the
    same background contention by construction.

    Runs on the host (a Unity binary and license are required), launched
    detached so the driving editor never blocks:

        node runner.mjs --unity <Unity binary> --artifact <.unitypackage> \
            --out <artifacts dir> [--pairs 4] [--timeout-minutes 30]

    Fail-closed style follows import-drill.mjs: an existing non-empty
    project directory aborts, every phase records its exit code, and the
    manifest reports failed=true unless every phase exited 0. Unity side
    effects are injectable through `runtime.runPhase`; contract tests
    never launch Unity.
*/
import { execFileSync, spawn } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const MODULE_DIR = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(MODULE_DIR, "../../../..");
const IMPORT_ROOT = "Packages/com.wallstop-studios.dxcommandterminal";
export const EDIT_TARGET = `${IMPORT_ROOT}/Runtime/CommandTerminal/Backend/Terminal.cs`;
const DEFAULT_PAIRS = 4;
const DEFAULT_TIMEOUT_MINUTES = 30;

const OPTION_NAMES = new Set(["unity", "artifact", "out", "pairs", "timeout-minutes", "keep"]);

export function parseDrillArgs(argv) {
  const options = {
    unity: "",
    artifact: "",
    out: "",
    pairs: DEFAULT_PAIRS,
    timeoutMinutes: DEFAULT_TIMEOUT_MINUTES,
    keep: false
  };
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (!token.startsWith("--")) {
      throw new Error(`unexpected argument: ${token}`);
    }
    const separator = token.indexOf("=");
    const name = token.slice(2, separator === -1 ? undefined : separator);
    if (!OPTION_NAMES.has(name)) {
      throw new Error(`unknown option: --${name}`);
    }
    if (name === "keep") {
      if (separator !== -1) throw new Error(`--keep does not take a value`);
      options.keep = true;
      continue;
    }
    const value = separator === -1 ? argv[++index] : token.slice(separator + 1);
    if (value === undefined || value === "") {
      throw new Error(`missing value for --${name}`);
    }
    if (name === "pairs" || name === "timeout-minutes") {
      const parsed = Number(value);
      if (!Number.isInteger(parsed) || parsed < 1) {
        throw new Error(`--${name} must be a positive integer`);
      }
      options[name === "pairs" ? "pairs" : "timeoutMinutes"] = parsed;
    } else {
      options[name] = value;
    }
  }
  if (options.unity === "") throw new Error("--unity is required (host Unity binary)");
  if (options.artifact === "") throw new Error("--artifact is required (.unitypackage path)");
  if (options.out === "") throw new Error("--out is required (artifacts directory)");
  return options;
}

/*
    One batch invocation per entry. Import and the paired runs quit
    themselves; settle self-exits through the scaffolded driver. Every
    phase gets its own log so failures stay attributable.
*/
export function planPhases(options) {
  const phases = [];
  const batch = ["-batchmode", "-nographics"];
  const logs = path.join(options.out, "logs");
  const project = path.join(options.out, "project");
  phases.push({
    name: "import",
    kind: "import",
    pair: null,
    args: [
      ...batch,
      "-quit",
      "-projectPath",
      project,
      "-importPackage",
      options.artifact,
      "-logFile",
      path.join(logs, "import.log")
    ]
  });
  phases.push({
    name: "settle",
    kind: "settle",
    pair: null,
    env: { DX_T7_CONSUMER_SETTLE: "1" },
    args: [
      ...batch,
      "-projectPath",
      project,
      "-executeMethod",
      "DxT7ConsumerSettle.WaitForIdle",
      "-logFile",
      path.join(logs, "settle.log")
    ]
  });
  for (let pair = 1; pair <= options.pairs; pair += 1) {
    for (const kind of ["control", "edit"]) {
      phases.push({
        name: `${kind}-${pair}`,
        kind,
        pair,
        args: [
          ...batch,
          "-quit",
          "-projectPath",
          project,
          "-logFile",
          path.join(logs, `${kind}-${pair}.log`)
        ]
      });
    }
  }
  return phases;
}

/*
    Scratch consumer project: pins the probed editor version, resolves the
    production Input System dependency, and deliberately installs the Test
    Framework to reproduce the #149 warm-cache headless condition. Fails
    closed on an existing non-empty directory or an implausible version.
*/
export function scaffoldProject(projectDir, editorVersion, driverSource) {
  if (!/^[0-9a-zA-Z][0-9a-zA-Z.\-_]*$/u.test(editorVersion)) {
    throw new Error(`implausible editor version: ${JSON.stringify(editorVersion)}`);
  }
  if (fs.existsSync(projectDir) && fs.readdirSync(projectDir).length > 0) {
    throw new Error(`project directory must be empty or missing: ${projectDir}`);
  }
  fs.mkdirSync(path.join(projectDir, "Assets", "Editor"), { recursive: true });
  fs.mkdirSync(path.join(projectDir, "Packages"), { recursive: true });
  fs.mkdirSync(path.join(projectDir, "ProjectSettings"), { recursive: true });
  fs.writeFileSync(
    path.join(projectDir, "ProjectSettings", "ProjectVersion.txt"),
    `m_EditorVersion: ${editorVersion}\n`
  );
  fs.writeFileSync(
    path.join(projectDir, "Packages", "manifest.json"),
    `${JSON.stringify(
      {
        dependencies: {
          "com.unity.inputsystem": "1.7.0",
          "com.unity.test-framework": "1.1.33"
        }
      },
      null,
      2
    )}\n`
  );
  fs.writeFileSync(path.join(projectDir, "Assets", "Editor", "DxT7ConsumerSettle.cs"), driverSource);
}

export const SETTLE_DRIVER_SOURCE = `using System;
using UnityEditor;
using UnityEngine;

/*
    Generated by tooling~/scripts/t7/consumer/runner.mjs - do not edit.
    Batch-mode settle driver in the shape proven by the import drill: the
    [InitializeOnLoad] static ctor re-subscribes after every domain reload
    (the settle run's own compilation reloads the domain), and the env guard
    keeps the import phase inert. Exits 0 settled, 3 idle past the deadline;
    the deadline is checked only while the pipeline is idle, so a long
    compile is never aborted by it.
*/
[InitializeOnLoad]
public static class DxT7ConsumerSettle
{
    static DxT7ConsumerSettle()
    {
        if (Environment.GetEnvironmentVariable("DX_T7_CONSUMER_SETTLE") == "1")
        {
            EditorApplication.update += WaitForIdle;
        }
    }

    public static void WaitForIdle()
    {
        if (_start == default)
        {
            _start = DateTime.UtcNow;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }
        double elapsedSeconds = (DateTime.UtcNow - _start).TotalSeconds;
        if (elapsedSeconds > 240.0)
        {
            Debug.Log("[dx-t7-consumer] idle past the settle deadline, exiting");
            EditorApplication.Exit(3);
            return;
        }
        if (elapsedSeconds < 3.0)
        {
            return;
        }
        Debug.Log("[dx-t7-consumer] pipeline idle");
        EditorApplication.update -= WaitForIdle;
        EditorApplication.Exit(0);
    }

    private static DateTime _start;
}
`;

/*
    Log-based per-phase compile attribution. A batch invocation always
    reloads its domain once and shows a ~500 ms script-check baseline; only
    a real recompile carries the "Requested script compilation because"
    line. DLL mtimes are useless here: a comment-only edit produces
    byte-identical IL, and the build system then never rewrites the output.
*/
export function parseScriptingAttribution(logPath) {
  const text = fs.existsSync(logPath) ? fs.readFileSync(logPath, "utf8") : "";
  const scripting =
    /Scripting: domain reloads=(\d+), domain reload time=([\d.]+) ms, compile time=([\d.]+) ms/u.exec(
      text
    );
  return {
    requestedRecompile: text.includes("Requested script compilation because"),
    domainReloads: scripting === null ? null : Number(scripting[1]),
    compileTimeMs: scripting === null ? null : Number(scripting[3])
  };
}

function median(values) {
  const sorted = [...values].sort((a, b) => a - b);
  const middle = sorted.length >> 1;
  return sorted.length % 2 === 1
    ? sorted[middle]
    : (sorted[middle - 1] + sorted[middle]) / 2;
}

export function summarizePairs(pairRecords) {
  const compileDeltas = pairRecords
    .filter((pair) => pair.compileTimeDeltaMs !== null)
    .map((pair) => pair.compileTimeDeltaMs);
  if (pairRecords.length === 0) {
    return {
      pairs: 0,
      controlMedianMs: null,
      editMedianMs: null,
      deltaMedianMs: null,
      deltaMinMs: null,
      deltaMaxMs: null,
      recompilePairs: 0,
      compileTimeDeltaMedianMs: null
    };
  }
  return {
    pairs: pairRecords.length,
    controlMedianMs: median(pairRecords.map((pair) => pair.controlMs)),
    editMedianMs: median(pairRecords.map((pair) => pair.editMs)),
    deltaMedianMs: median(pairRecords.map((pair) => pair.deltaMs)),
    deltaMinMs: Math.min(...pairRecords.map((pair) => pair.deltaMs)),
    deltaMaxMs: Math.max(...pairRecords.map((pair) => pair.deltaMs)),
    recompilePairs: pairRecords.filter((pair) => pair.editRequestedRecompile).length,
    compileTimeDeltaMedianMs: compileDeltas.length === 0 ? null : median(compileDeltas)
  };
}

function sha256File(filePath) {
  return crypto.createHash("sha256").update(fs.readFileSync(filePath)).digest("hex");
}

function logPathOf(phase) {
  const index = phase.args.indexOf("-logFile");
  return index === -1 ? null : phase.args[index + 1];
}

function probeEditorVersion(unity) {
  return execFileSync(unity, ["-version"], { encoding: "utf8", timeout: 60_000 }).trim();
}

function gitRevision() {
  try {
    return execFileSync("git", ["rev-parse", "HEAD"], { encoding: "utf8", cwd: REPO_ROOT }).trim();
  } catch {
    return "(unavailable)";
  }
}

/*
    Default phase runner: spawn detached from the caller's event loop and
    poll for exit. Spawn failures resolve (never crash the drill); the
    watchdog escalates SIGTERM -> SIGKILL and waits for close before the
    next phase touches the same Library. Injectable so contract tests
    never launch Unity.
*/
export function defaultRunPhase(unity, phase, deadlineAt) {
  return new Promise((resolve) => {
    let settled = false;
    let timedOut = false;
    let escalated = false;
    let sigkill = null;
    const env = phase.env === undefined ? undefined : { ...process.env, ...phase.env };
    const child = spawn(unity, phase.args, { stdio: "ignore", env });
    const finish = (outcome) => {
      if (settled) return;
      settled = true;
      clearInterval(watch);
      clearTimeout(sigkill);
      resolve(outcome);
    };
    const watch = setInterval(() => {
      if (settled || escalated || Date.now() <= deadlineAt) return;
      // Escalate exactly once: SIGTERM, then SIGKILL if still alive, and
      // only the close event resolves - the next phase must not race a
      // dying editor for Library/.
      escalated = true;
      timedOut = true;
      child.kill("SIGTERM");
      sigkill = setTimeout(() => {
        if (!settled) child.kill("SIGKILL");
      }, 10_000);
      sigkill.unref?.();
    }, 500);
    child.on("error", (error) => finish({ exitCode: null, timedOut: false, spawnError: error.message }));
    child.on("close", (code, signal) =>
      finish({ exitCode: code ?? (signal === null ? 0 : -1), timedOut })
    );
  });
}

export async function runDrill(options, runtime = {}) {
  const runPhase = runtime.runPhase ?? defaultRunPhase;
  const probeVersion = runtime.probeEditorVersion ?? probeEditorVersion;
  const notify = runtime.notify ?? (() => {});
  if (!fs.existsSync(options.unity)) throw new Error(`unity binary not found: ${options.unity}`);
  if (!fs.existsSync(options.artifact)) throw new Error(`artifact not found: ${options.artifact}`);

  fs.mkdirSync(path.join(options.out, "logs"), { recursive: true });
  const progressPath = path.join(options.out, "progress.txt");
  const progress = (line) => {
    fs.appendFileSync(progressPath, line);
    notify(line);
  };
  const project = path.join(options.out, "project");
  const editorVersion = probeVersion(options.unity);
  scaffoldProject(project, editorVersion, SETTLE_DRIVER_SOURCE);

  const startedAt = new Date().toISOString();
  const deadlineAt = Date.now() + options.timeoutMinutes * 60_000;
  // Hash and size the artifact once up front: a mid-run move or delete must
  // not throw past the manifest write.
  const artifact = {
    path: options.artifact,
    sha256: sha256File(options.artifact),
    bytes: fs.statSync(options.artifact).size
  };
  const phaseRecords = [];
  const pairRecords = [];
  let failure = null;

  for (const phase of planPhases(options)) {
    if (phase.kind === "edit") {
      fs.appendFileSync(
        path.join(project, EDIT_TARGET),
        `// t7 consumer drill sample ${phase.pair} (${new Date().toISOString()})\n`
      );
    }
    const started = Date.now();
    const outcome = await runPhase(options.unity, phase, deadlineAt);
    const record = {
      name: phase.name,
      kind: phase.kind,
      pair: phase.pair,
      exitCode: outcome.exitCode,
      timedOut: outcome.timedOut === true,
      durationMs: Date.now() - started
    };
    if (outcome.spawnError !== undefined) {
      record.spawnError = outcome.spawnError;
    }
    if (phase.kind === "control" || phase.kind === "edit") {
      record.attribution = parseScriptingAttribution(logPathOf(phase));
    }
    if (phase.kind === "edit") {
      const control = phaseRecords.findLast((entry) => entry.kind === "control" && entry.pair === phase.pair);
      const controlCompile =
        control === undefined || control.attribution.compileTimeMs === null
          ? null
          : control.attribution.compileTimeMs;
      const editCompile = record.attribution.compileTimeMs;
      pairRecords.push({
        pair: phase.pair,
        controlMs: control === undefined ? null : control.durationMs,
        editMs: record.durationMs,
        deltaMs: control === undefined ? null : record.durationMs - control.durationMs,
        editRequestedRecompile: record.attribution.requestedRecompile,
        editCompileTimeMs: editCompile,
        controlCompileTimeMs: controlCompile,
        compileTimeDeltaMs:
          editCompile === null || controlCompile === null ? null : editCompile - controlCompile
      });
    }
    phaseRecords.push(record);
    progress(`phase ${record.name}: exit ${record.exitCode} in ${record.durationMs}ms\n`);
    if (record.timedOut) {
      failure = `phase ${record.name} outlived the drill deadline and was killed`;
      break;
    }
    if (record.spawnError !== undefined) {
      failure = `phase ${record.name} could not launch unity: ${record.spawnError}`;
      break;
    }
    if (record.exitCode !== 0) {
      failure = `phase ${record.name} exited ${record.exitCode}`;
      break;
    }
    if (phase.kind === "settle" && !fs.existsSync(path.join(project, EDIT_TARGET))) {
      // Batch imports can silently drop the whole package with exit 0
      // (import-drill.mjs header note); fail closed instead of measuring
      // a project that never received it.
      failure = `import did not land the package (${EDIT_TARGET} missing after settle)`;
      break;
    }
  }

  const manifest = {
    timestamp: startedAt,
    finishedAt: new Date().toISOString(),
    failed: failure !== null,
    failure,
    editorVersion,
    unity: options.unity,
    artifact,
    importRoot: IMPORT_ROOT,
    editTarget: EDIT_TARGET,
    project,
    revision: gitRevision(),
    pairs: summarizePairs(pairRecords),
    pairRecords,
    phases: phaseRecords,
    timeoutMinutes: options.timeoutMinutes
  };
  fs.writeFileSync(path.join(options.out, "manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`);
  if (failure === null && options.keep !== true) {
    fs.rmSync(project, { recursive: true, force: true });
  }
  progress(failure === null ? "drill complete\n" : `drill failed: ${failure}\n`);
  return manifest;
}

function usage() {
  return [
    "Usage: node runner.mjs --unity <Unity binary> --artifact <.unitypackage> --out <dir>",
    "       [--pairs 4] [--timeout-minutes 30] [--keep]",
    "",
    "Host-side T07 consumer drill: clean import + paired control/edit batch",
    "invocations. Writes manifest.json, progress.txt, and logs/<phase>.log",
    "under --out. The scratch project is deleted on success unless --keep.",
    "Launch detached from the driving editor."
  ].join("\n");
}

const isMain =
  process.argv[1] !== undefined && fileURLToPath(import.meta.url) === path.resolve(process.argv[1]);

if (isMain) {
  try {
    const options = parseDrillArgs(process.argv.slice(2));
    const manifest = await runDrill(options);
    if (manifest.failed) {
      console.error(`[t7-consumer] FAILED: ${manifest.failure}`);
      process.exitCode = 1;
    }
  } catch (error) {
    console.error(`[t7-consumer] ERROR: ${error.message}`);
    console.error(usage());
    process.exitCode = 1;
  }
}
