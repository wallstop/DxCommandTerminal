/*
    Clean-project .unitypackage import drill (T13/T14 gate, issues #84/#85).

    Imports the actual release artifact into a throwaway Unity project via
    `Unity -batchmode -nographics -executeMethod` and fail-closed validates the
    result on disk. This is a local, maintainer-run gate: it uses the local
    Unity license and network access to resolve the package's UPM
    dependencies, and it never runs in CI.

    The drill exists because the session-025 drill wedged the maintainer's
    editor: `AssetDatabase.ImportPackage` ran interactively through the MCP
    bridge against the live project, and the modal import dialog blocked the
    editor main thread (the bridge has been timing out since). This tool never
    touches a live editor: batch mode, a scratch project, a non-interactive
    import, a hard timeout, and a separate process.

    Validation is file-system based:

    - every artifact entry exists at `<project>/<pathname>` with a matching
      `.meta` whose `guid:` equals the artifact GUID directory;
    - analyzer payload metas keep the `RoslynAnalyzer` label;
    - every imported `.asmdef` compiled to `Library/ScriptAssemblies/<name>.dll`
      (dependency resolution and compilation in one check).

    Note the limit: the drill proves the generator payload imports with its
    label and that compilation succeeds with it present; it does not execute
    the generator. Generator behavior is gated by the Unity-free generator
    suite and the payload byte-compare in CI.

    All Unity side effects are injectable; the contract tests never launch
    Unity or touch a license.
*/
import { execFileSync, spawn } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import zlib from "node:zlib";

const REPO_ROOT = path.resolve(fileURLToPath(new URL("../../..", import.meta.url)));
const TAR_BLOCK = 512;
const GUID_PATTERN = /^[0-9a-f]{32}$/u;
const GUID_LINE_PATTERN = /^guid:\s*([0-9a-fA-F]{32})\b/m;
const LOG_ERROR_PATTERNS = [/\berror CS\d+/u, /^Aborting batchmode/u, /Scripts have compiler errors/u];
const ARTIFACT_ENV = "DX_IMPORT_DRILL_ARTIFACT";
const IMPORTED_FLAG = "DX_IMPORT_DRILL_IMPORTED";

/*
    UPM packages the scratch project needs so every shipped asmdef compiles.
    `provides` lists the assembly names those packages contribute; validation
    fails closed when an artifact asmdef references an assembly neither in the
    artifact itself nor provided by one of these packages. Versions are
    2021.3-compatible floors; the registry resolves them at drill time.
*/
const SCRATCH_UPM_PACKAGES = [
  { id: "com.unity.inputsystem", version: "1.7.0", provides: ["Unity.InputSystem"] },
  {
    id: "com.unity.test-framework",
    version: "1.1.33",
    provides: ["UnityEngine.TestRunner", "UnityEditor.TestRunner"]
  }
];

/*
    Reads a ustar+gzip archive (the exporter's format) into a Map of
    `name -> { contents, isDirectory }`. Rejects pax/GNU extension headers,
    malformed size fields, duplicate names, and any typeflag other than 0/5:
    the exporter emits plain ustar, so anything else means the input is not
    an exporter artifact.
*/
export function readTar(buffer) {
  const entries = new Map();
  const tar = zlib.gunzipSync(buffer);
  let offset = 0;
  while (offset + TAR_BLOCK <= tar.length) {
    const header = tar.subarray(offset, offset + TAR_BLOCK);
    if (header.every((byte) => byte === 0)) {
      break;
    }
    const name = readTarString(header.subarray(0, 100));
    const sizeField = readTarString(header.subarray(124, 136));
    if (sizeField !== "" && !/^[0-7]{1,11}$/u.test(sizeField.trim())) {
      throw new Error(`malformed tar size field at entry ${name}`);
    }
    const size = Number.parseInt(sizeField.trim() || "0", 8);
    const typeflag = String.fromCharCode(header[156]);
    if (typeflag !== "0" && typeflag !== "5") {
      throw new Error(`unsupported tar entry type (${typeflag}) at entry ${name}`);
    }
    const prefix = readTarString(header.subarray(345, 500));
    // The exporter emits the GUID directory itself as a "5" entry whose name
    // ends in "/"; Unity's package format carries it, and it is structural.
    const fullName = (prefix ? `${prefix}/${name}` : name).replace(/\/$/u, "");
    offset += TAR_BLOCK;
    const isDirectory = typeflag === "5";
    const dataEnd = offset + size;
    if (dataEnd > tar.length) {
      throw new Error(`truncated tar entry ${fullName}`);
    }
    if (!(isDirectory && GUID_PATTERN.test(fullName))) {
      if (entries.has(fullName)) {
        throw new Error(`duplicate tar entry ${fullName}`);
      }
      entries.set(fullName, { contents: tar.subarray(offset, dataEnd), isDirectory });
    }
    offset = Math.ceil((offset + size) / TAR_BLOCK) * TAR_BLOCK;
  }
  if (offset < tar.length && tar.subarray(offset).some((byte) => byte !== 0)) {
    throw new Error(`trailing garbage after the last tar entry (byte ${offset})`);
  }
  return entries;
}

function readTarString(bytes) {
  const end = bytes.indexOf(0);
  return bytes.subarray(0, end === -1 ? bytes.length : end).toString("utf8");
}

/*
    Unity meta labels are either inline (`labels: RoslynAnalyzer`) or a
    `- item` list of any length. Returns the label set for the first
    `labels:` block, or an empty set.
*/
export function metaLabels(metaText) {
  const lines = metaText.split(/\r?\n/);
  const labels = new Set();
  for (let index = 0; index < lines.length; index += 1) {
    if (!/^labels:\s*$/u.test(lines[index]) && !/^labels:\s+\S/u.test(lines[index])) {
      continue;
    }
    const inline = /^labels:\s+(\S.*)$/u.exec(lines[index]);
    if (inline !== null) {
      labels.add(inline[1].trim());
      break;
    }
    for (index += 1; index < lines.length; index += 1) {
      const item = /^\s*-\s*(\S.*)$/u.exec(lines[index]);
      if (item === null) {
        break;
      }
      labels.add(item[1].trim());
    }
    break;
  }
  return labels;
}

/*
    Groups raw tar entries into the artifact model: one record per GUID
    directory with its pathname, meta text, and optional asset content.
    Fails closed on unknown entry shapes, missing members, duplicate GUIDs,
    GUID/meta disagreement, and pathnames escaping or mixing import roots.
*/
export function listArtifact(buffer) {
  const entries = readTar(buffer);
  const byGuid = new Map();
  const seenPathnames = new Map();
  const violations = [];

  for (const [name, entry] of entries) {
    const segments = name.split("/");
    const guid = segments[0];
    const member = segments.slice(1).join("/");
    if (segments.length !== 2 || !GUID_PATTERN.test(guid)) {
      violations.push(`unexpected archive entry: ${name}`);
      continue;
    }
    if (member !== "asset" && member !== "asset.meta" && member !== "pathname") {
      violations.push(`unexpected member ${member} in ${guid}`);
      continue;
    }
    const record = byGuid.get(guid) ?? { guid, pathname: "", metaText: "", hasAsset: false, assetText: "" };
    byGuid.set(guid, record);
    if (member === "pathname") {
      if (record.pathname !== "") {
        violations.push(`duplicate pathname entry for guid ${guid}`);
        continue;
      }
      const pathname = entry.contents.toString("utf8").trim();
      const owner = seenPathnames.get(pathname);
      if (owner !== undefined) {
        violations.push(`pathname ${pathname} is claimed by both ${owner} and ${guid}`);
        continue;
      }
      seenPathnames.set(pathname, guid);
      record.pathname = pathname;
    } else if (member === "asset.meta") {
      record.metaText = entry.contents.toString("utf8");
    } else {
      record.hasAsset = true;
      record.assetText = entry.contents.toString("utf8");
    }
  }

  for (const record of byGuid.values()) {
    if (record.pathname === "") {
      violations.push(`guid ${record.guid} has no pathname`);
      continue;
    }
    if (record.metaText === "") {
      violations.push(`guid ${record.guid} has no asset.meta`);
      continue;
    }
    if (record.pathname.includes("\\") || path.isAbsolute(record.pathname) || record.pathname.split("/").includes("..")) {
      violations.push(`unsafe pathname for guid ${record.guid}: ${record.pathname}`);
    }
    const metaGuid = GUID_LINE_PATTERN.exec(record.metaText)?.[1]?.toLowerCase();
    if (metaGuid !== record.guid) {
      violations.push(`meta guid ${metaGuid ?? "(none)"} disagrees with directory ${record.guid}`);
    }
  }

  const assets = [...byGuid.values()].sort((left, right) =>
    left.pathname < right.pathname ? -1 : left.pathname > right.pathname ? 1 : 0
  );

  let root = null;
  if (assets.length === 0) {
    violations.push("archive carries no GUID directories");
  } else {
    root = commonRoot(assets.map((asset) => asset.pathname));
    if (root === null) {
      violations.push("pathnames do not share one import root");
    }
  }

  if (violations.length > 0) {
    throw new Error(`artifact invariant violations:\n  - ${violations.join("\n  - ")}`);
  }
  const model = {
    root,
    assets: assets.map((asset) => ({
      guid: asset.guid,
      pathname: asset.pathname,
      isFolder: !asset.hasAsset,
      metaText: asset.metaText,
      asmdefSource: asset.pathname.endsWith(".asmdef") ? asset.assetText : undefined
    }))
  };
  model.scratchDependencies = scratchDependencies(model);
  return model;
}

function commonRoot(pathnames) {
  if (pathnames.length === 0) {
    return null;
  }
  const first = pathnames[0].split("/");
  if (first.length < 2) {
    return null;
  }
  const root = `${first[0]}/${first[1]}`;
  for (const pathname of pathnames) {
    if (!pathname.startsWith(`${root}/`) && pathname !== root) {
      return null;
    }
  }
  return root;
}

/*
    Derives the UPM packages the scratch manifest needs: every assembly an
    artifact asmdef references must either be another artifact asmdef or be
    provided by a SCRATCH_UPM_PACKAGES entry; defineConstraints count
    UNITY_INCLUDE_TESTS against the test framework. Fail-closed: an
    unsatisfiable reference makes the drill refuse to scaffold.
*/
function scratchDependencies(artifact) {
  const violations = [];
  const asmdefs = [];
  for (const asset of artifact.assets) {
    if (asset.pathname.endsWith(".asmdef")) {
      try {
        asmdefs.push(JSON.parse(asset.asmdefSource ?? ""));
      } catch {
        violations.push(`unreadable asmdef: ${asset.pathname}`);
      }
    }
  }
  const localAssemblies = new Set(asmdefs.map((asmdef) => asmdef.name).filter((name) => typeof name === "string"));
  const dependencies = {};
  const providerFor = (assemblyName) =>
    SCRATCH_UPM_PACKAGES.find((entry) => entry.provides.includes(assemblyName));
  for (const asmdef of asmdefs) {
    if (!Array.isArray(asmdef.references)) {
      if (asmdef.references !== undefined) {
        violations.push(`asmdef ${asmdef.name ?? "?"} has malformed references`);
      }
      continue;
    }
    for (const reference of asmdef.references) {
      if (localAssemblies.has(reference)) {
        continue;
      }
      const provider = providerFor(reference);
      if (provider === undefined) {
        violations.push(`asmdef ${asmdef.name ?? "?"} references unknown assembly ${reference}`);
        continue;
      }
      dependencies[provider.id] = provider.version;
    }
    if ((asmdef.defineConstraints ?? []).includes("UNITY_INCLUDE_TESTS")) {
      const provider = SCRATCH_UPM_PACKAGES.find((entry) => entry.id === "com.unity.test-framework");
      dependencies[provider.id] = provider.version;
    }
  }
  if (violations.length > 0) {
    throw new Error(`artifact asmdef dependencies cannot be satisfied:\n  - ${violations.join("\n  - ")}`);
  }
  return dependencies;
}

/*
    Builds the batch-mode import driver written into the scratch project.
    Deterministic content: the artifact path travels through the environment,
    never through generated source. Compilation triggered by the import
    completes (domain reload included) before the exit: [InitializeOnLoad]
    re-subscribes after the reload, SessionState survives it, and the exit
    waits for isCompiling/isUpdating to clear.
*/
export function buildImportDriver() {
  return `/*
    Generated by tooling~/scripts/release/import-drill.mjs - do not edit.
    Batch-mode import driver: imports the drill artifact non-interactively and
    exits with a code the drill gates on (0 imported, 1 exception, 2 missing
    artifact). Runs in a scratch project under -batchmode, so no dialog can
    block an editor main thread.
*/
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class DxTerminalImportDrill
{
    public static void Run()
    {
        string artifact = Environment.GetEnvironmentVariable("${ARTIFACT_ENV}");
        if (string.IsNullOrEmpty(artifact) || !File.Exists(artifact))
        {
            Debug.LogError($"[import-drill] artifact not found: {artifact}");
            EditorApplication.Exit(2);
            return;
        }
        try
        {
            // Set before the import: a synchronous domain reload inside
            // ImportPackage would abandon Run() before an after-import write.
            SessionState.SetBool("${IMPORTED_FLAG}", true);
            AssetDatabase.ImportPackage(artifact, false);
            EditorApplication.update += WaitForImportAndCompile;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void WaitForImportAndCompile()
    {
        if (!SessionState.GetBool("${IMPORTED_FLAG}", false))
        {
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }
        EditorApplication.update -= WaitForImportAndCompile;
        SessionState.SetBool("${IMPORTED_FLAG}", false);
        Debug.Log("[import-drill] import completed");
        EditorApplication.Exit(0);
    }

    static DxTerminalImportDrill()
    {
        EditorApplication.update += WaitForImportAndCompile;
    }
}
`;
}

/*
    Scaffolds the throwaway project: version file (probed from the same
    editor binary), a manifest carrying the artifact's UPM dependencies (the
    import itself brings the package in as embedded content under
    Packages/<name>), and the import driver.
*/
export function scaffoldProject(projectDir, editorVersion, dependencies) {
  if (!/^[0-9a-zA-Z][0-9a-zA-Z.\-_]*$/u.test(editorVersion)) {
    throw new Error(`implausible editor version: ${editorVersion}`);
  }
  fs.mkdirSync(path.join(projectDir, "ProjectSettings"), { recursive: true });
  fs.mkdirSync(path.join(projectDir, "Packages"), { recursive: true });
  fs.mkdirSync(path.join(projectDir, "Assets"), { recursive: true });
  fs.writeFileSync(
    path.join(projectDir, "ProjectSettings", "ProjectVersion.txt"),
    `m_EditorVersion: ${editorVersion}\n`
  );
  fs.writeFileSync(
    path.join(projectDir, "Packages", "manifest.json"),
    `${JSON.stringify({ dependencies }, null, 2)}\n`
  );
  fs.writeFileSync(path.join(projectDir, "Assets", "DxTerminalImportDrill.cs"), buildImportDriver());
}

const ASSEMBLY_NAME_PATTERN = /^[A-Za-z0-9._-]+$/u;
// Unity predefines these; an artifact asmdef reusing one would make the
// compiled-DLL check pass off the scratch project's own driver assembly.
const RESERVED_ASSEMBLY_NAMES = new Set(["Assembly-CSharp", "Assembly-CSharp-Editor", "Assembly-CSharp-firstpass"]);

/*
    Post-import validation. Purely file-system based against the artifact
    model; returns { failures, checks } and never throws for a failed check
    (failures carry the messages).
*/
export function validateImportedProject(projectDir, artifact) {
  const failures = [];
  const checks = [];
  const check = (name, ok, detail = "") => {
    checks.push({ name, ok, detail });
    if (!ok) {
      failures.push(`${name}${detail === "" ? "" : `: ${detail}`}`);
    }
  };

  const asmdefPaths = [];
  const analyzerPaths = [];
  for (const asset of artifact.assets) {
    const importedPath = path.join(projectDir, asset.pathname);
    const stat = fs.statSync(importedPath, { throwIfNoEntry: false });
    if (asset.isFolder) {
      check(`folder exists ${asset.pathname}`, stat?.isDirectory() === true);
    } else {
      check(`file exists ${asset.pathname}`, stat?.isFile() === true);
    }
    const importedMetaPath = `${importedPath}.meta`;
    const importedMeta = fs.existsSync(importedMetaPath)
      ? fs.readFileSync(importedMetaPath, "utf8")
      : null;
    check(`meta exists ${asset.pathname}.meta`, importedMeta !== null);
    if (importedMeta !== null) {
      const importedGuid = GUID_LINE_PATTERN.exec(importedMeta)?.[1]?.toLowerCase();
      check(
        `meta guid matches ${asset.pathname}`,
        importedGuid === asset.guid,
        `${importedGuid ?? "(none)"} != ${asset.guid}`
      );
    }
    if (asset.pathname.endsWith(".asmdef")) {
      asmdefPaths.push(asset.pathname);
    }
    if (asset.pathname.endsWith(".dll") && asset.pathname.includes("/Analyzers/")) {
      analyzerPaths.push({ assetPath: asset.pathname, importedMeta });
    }
  }

  for (const { assetPath, importedMeta } of analyzerPaths) {
    check(
      `analyzer label survives ${assetPath}`,
      importedMeta !== null && metaLabels(importedMeta).has("RoslynAnalyzer")
    );
  }

  check("artifact carries at least one asmdef", asmdefPaths.length > 0);
  for (const asmdefPath of asmdefPaths) {
    let assemblyName = null;
    try {
      const source = fs.readFileSync(path.join(projectDir, asmdefPath), "utf8");
      assemblyName = JSON.parse(source).name;
    } catch {
      assemblyName = null;
    }
    check(
      `asmdef parses ${asmdefPath}`,
      typeof assemblyName === "string" && assemblyName.length > 0
    );
    const safeName =
      typeof assemblyName === "string" &&
      ASSEMBLY_NAME_PATTERN.test(assemblyName) &&
      !RESERVED_ASSEMBLY_NAMES.has(assemblyName);
    check(
      `asmdef name is a safe assembly name ${asmdefPath}`,
      safeName,
      typeof assemblyName === "string" ? assemblyName : "(unreadable)"
    );
    if (safeName) {
      const compiled = path.join(projectDir, "Library", "ScriptAssemblies", `${assemblyName}.dll`);
      check(
        `compiled assembly exists ${assemblyName}.dll`,
        fs.statSync(compiled, { throwIfNoEntry: false })?.isFile() === true
      );
    }
  }

  return { failures, checks };
}

function sha256(buffer) {
  return crypto.createHash("sha256").update(buffer).digest("hex");
}

function readArtifactFile(artifactPath) {
  const stat = fs.statSync(artifactPath, { throwIfNoEntry: false });
  if (stat?.isFile() !== true) {
    throw new Error(`artifact is not a file: ${artifactPath}`);
  }
  return fs.readFileSync(artifactPath);
}

/*
    Probes the editor version with `<unity> -version` (no project, no license
    surface). The first stdout line shaped like a Unity version wins, so
    banner noise does not fail the probe. Injectable; tests stub this.
*/
export function probeEditorVersion(unityPath, versionArgs = ["-version"]) {
  const stdout = execFileSync(unityPath, versionArgs, { encoding: "utf8", timeout: 60_000 });
  for (const line of stdout.split(/\r?\n/)) {
    if (/^\d{4,}\.\d[\w.\-]*$/u.test(line.trim())) {
      return line.trim();
    }
  }
  throw new Error(`<unity> -version printed no plausible version for ${unityPath}: ${stdout.trim().slice(0, 200)}`);
}

function runUnityBatch(unityPath, projectDir, logPath, artifactPath, timeoutMs) {
  return new Promise((resolve) => {
    const child = spawn(
      unityPath,
      [
        "-batchmode",
        "-nographics",
        "-executeMethod",
        "DxTerminalImportDrill.Run",
        "-logFile",
        logPath,
        "-projectPath",
        projectDir
      ],
      {
        env: { ...process.env, [ARTIFACT_ENV]: artifactPath },
        stdio: ["ignore", "ignore", "ignore"]
      }
    );
    let settled = false;
    const timers = new Set();
    const clearTimers = () => {
      for (const timer of timers) {
        clearTimeout(timer);
      }
    };
    const finish = (result) => {
      if (settled) {
        return;
      }
      settled = true;
      resolve(result);
    };
    const timer = setTimeout(() => {
      child.kill();
      // SIGTERM can be ignored; make sure the editor dies even if it hangs.
      timers.add(
        setTimeout(() => {
          if (child.exitCode === null && child.signalCode === null) {
            child.kill("SIGKILL");
          }
        }, 10_000)
      );
      finish({ timedOut: true, code: null, signal: null });
    }, timeoutMs);
    timers.add(timer);
    child.on("close", (code, signal) => {
      clearTimers();
      finish({ timedOut: false, code, signal });
    });
    child.on("error", (error) => {
      clearTimers();
      finish({ timedOut: false, code: null, signal: null, spawnError: error.message });
    });
  });
}

function scanLogForErrors(logPath) {
  if (!fs.existsSync(logPath)) {
    return ["(unity produced no log file)"];
  }
  const found = [];
  for (const line of fs.readFileSync(logPath, "utf8").split(/\r?\n/)) {
    if (LOG_ERROR_PATTERNS.some((pattern) => pattern.test(line))) {
      found.push(line);
      if (found.length >= 10) {
        break;
      }
    }
  }
  return found;
}

export function parseArgs(argv) {
  const options = {
    artifact: "",
    unity: "",
    project: "",
    keep: false,
    timeoutMinutes: 20
  };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const next = () => {
      index += 1;
      if (index >= argv.length) {
        throw new Error(`missing value for ${arg}`);
      }
      return argv[index];
    };
    if (arg === "--artifact") {
      options.artifact = path.resolve(next());
    } else if (arg === "--unity") {
      options.unity = next();
    } else if (arg === "--project") {
      options.project = path.resolve(next());
    } else if (arg === "--keep") {
      options.keep = true;
    } else if (arg === "--timeout-minutes") {
      const value = Number(next());
      if (!Number.isFinite(value) || value <= 0) {
        throw new Error("--timeout-minutes must be a positive number");
      }
      options.timeoutMinutes = value;
    } else {
      throw new Error(`unknown argument: ${arg}`);
    }
  }
  if (options.artifact === "") {
    throw new Error("missing --artifact <path-to-.unitypackage>");
  }
  if (options.unity === "") {
    throw new Error("missing --unity <path-to-editor-binary>");
  }
  return options;
}

const USAGE =
  "usage: node import-drill.mjs --artifact <path-to-.unitypackage> --unity <editor-binary> " +
  "[--project <dir>] [--keep] [--timeout-minutes 20]";

export async function runImportDrill(options, probes = {}) {
  const runUnity = probes.runUnity ?? runUnityBatch;
  const probeVersion = probes.probeEditorVersion ?? probeEditorVersion;
  const reportDir = options.reportDir ?? path.join(REPO_ROOT, ".artifacts", "import-drill", reportStamp());
  fs.mkdirSync(reportDir, { recursive: true });
  const logPath = path.join(reportDir, "unity.log");
  const manifestPath = path.join(reportDir, "manifest.json");

  const artifactBuffer = readArtifactFile(options.artifact);
  const artifact = listArtifact(artifactBuffer);
  console.log(
    `[import-drill] artifact sha256 ${sha256(artifactBuffer).slice(0, 16)}...; ` +
      `${artifact.assets.length} entries under ${artifact.root}`
  );

  const projectDir = options.project !== "" ? options.project : path.join(reportDir, "project");
  if (fs.existsSync(projectDir) && fs.readdirSync(projectDir).length > 0) {
    throw new Error(`--project must name an empty or missing directory: ${projectDir}`);
  }
  const editorVersion = probeVersion(options.unity);
  const dependencyText =
    Object.keys(artifact.scratchDependencies).length > 0
      ? `upm deps: ${Object.entries(artifact.scratchDependencies).map(([id, version]) => `${id}@${version}`).join(", ")}`
      : "no upm deps";
  console.log(`[import-drill] editor ${editorVersion}; ${dependencyText}; project ${projectDir}`);
  scaffoldProject(projectDir, editorVersion, artifact.scratchDependencies);

  const started = Date.now();
  const run = await runUnity(options.unity, projectDir, logPath, options.artifact, options.timeoutMinutes * 60_000);
  const elapsedSeconds = Math.round((Date.now() - started) / 1000);

  let outcome = null;
  if (run.timedOut === true) {
    outcome = `timed out after ${options.timeoutMinutes} minutes (process killed)`;
  } else if (run.spawnError !== undefined) {
    outcome = `could not launch unity: ${run.spawnError}`;
  } else if (run.signal !== null && run.signal !== undefined) {
    outcome = `unity killed by signal ${run.signal}`;
  } else if (run.code !== 0) {
    outcome = `unity exited ${run.code}`;
  }
  const validation = outcome === null ? validateImportedProject(projectDir, artifact) : { failures: [], checks: [] };
  if (outcome === null && validation.failures.length > 0) {
    outcome = `validation failed:\n  - ${validation.failures.join("\n  - ")}`;
  }

  const manifest = {
    timestamp: new Date().toISOString(),
    failed: outcome !== null,
    artifact: options.artifact,
    artifactSha256: sha256(artifactBuffer),
    entries: artifact.assets.length,
    importRoot: artifact.root,
    upmDependencies: artifact.scratchDependencies,
    unity: options.unity,
    editorVersion,
    project: projectDir,
    unityExitCode: run.code ?? null,
    unitySignal: run.signal ?? null,
    timedOut: run.timedOut === true,
    elapsedSeconds,
    outcome,
    checks: validation.checks,
    logErrors: outcome === null ? [] : scanLogForErrors(logPath),
    revision: gitRevision()
  };
  fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);

  if (outcome !== null) {
    console.error(`[import-drill] FAILED: ${outcome}`);
    console.error(`[import-drill] kept for diagnosis: project ${projectDir}`);
    console.error(`[import-drill] log: ${logPath}`);
    console.error(`[import-drill] manifest: ${manifestPath}`);
    return manifest;
  }

  if (options.keep === true) {
    console.log(`[import-drill] kept project for inspection: ${projectDir}`);
  } else {
    fs.rmSync(projectDir, { recursive: true, force: true });
  }
  console.log(
    `[import-drill] ok: ${artifact.assets.length} entries imported under ${artifact.root}, ` +
      `package compiled, ${elapsedSeconds}s`
  );
  console.log(`[import-drill] manifest: ${manifestPath}`);
  return manifest;
}

function reportStamp() {
  return new Date().toISOString().replaceAll(/[:.\-]/gu, "").slice(0, 18);
}

function gitRevision() {
  try {
    return execFileSync("git", ["rev-parse", "HEAD"], { encoding: "utf8", cwd: REPO_ROOT }).trim();
  } catch {
    return "(unavailable)";
  }
}

async function main() {
  try {
    const options = parseArgs(process.argv.slice(2));
    const manifest = await runImportDrill(options);
    if (manifest.failed === true) {
      process.exitCode = 1;
    }
  } catch (error) {
    console.error(`[import-drill] ERROR: ${error.message}`);
    console.error(USAGE);
    process.exitCode = 1;
  }
}

const isMain =
  process.argv[1] !== undefined && fileURLToPath(import.meta.url) === path.resolve(process.argv[1]);

if (isMain) {
  await main();
}
