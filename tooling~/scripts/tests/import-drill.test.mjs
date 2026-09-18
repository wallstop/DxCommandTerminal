/*
    Contract tests for the import drill (T13/T14 gate, issues #84/#85). The
    Unity side effects are injected; no test launches Unity, spawns an editor,
    or touches a license. Real subprocesses are limited to a node `-e` probe
    standing in for `Unity -version` and the exporter's npm pack for the
    real-artifact smoke.
*/
import test from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import zlib from "node:zlib";
import { fileURLToPath, pathToFileURL } from "node:url";

const drillScript = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../release/import-drill.mjs"
);
const toolingRoot = path.resolve(path.dirname(drillScript), "../..");
const {
  buildImportDriver,
  importUnityArgs,
  listArtifact,
  metaLabels,
  parseArgs,
  probeEditorVersion,
  readTar,
  runImportDrill,
  SETTLE_PHASE_ENV,
  scaffoldProject,
  settleUnityArgs,
  validateImportedProject
} = await import(pathToFileURL(drillScript).href);

const tempDirs = [];
test.after(() => {
  for (const dir of tempDirs) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

function tempRoot(label) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), `dxt-drill-${label}-`));
  tempDirs.push(dir);
  return dir;
}

const RUNTIME_GUID = "0123456789abcdef0123456789abcdef";
const EDITOR_GUID = "ffffffffffffffffffffffffffffffff";
const ANALYZER_GUID = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
const FOLDER_GUID = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
const ROOT = "Packages/com.wallstop-studios.dxcommandterminal";
const RUNTIME_ASMDEF = JSON.stringify({ name: "WallstopStudios.DxCommandTerminal", references: ["Unity.InputSystem"] });

function tarEntry(name, contents, typeflag = "0") {
  const header = Buffer.alloc(512);
  header.write(name, 0, "utf8");
  header.write(`${contents.length.toString(8).padStart(11, "0")} `, 124, "utf8");
  header.write(typeflag, 156, "utf8");
  header.write("ustar", 257, "utf8");
  let checksum = 0;
  for (const byte of header) {
    checksum += byte;
  }
  header.write(`${checksum.toString(8).padStart(6, "0")} `, 148, "utf8");
  const dataBlocks = Math.ceil(contents.length / 512) * 512;
  const padded = Buffer.concat([Buffer.from(contents, "utf8"), Buffer.alloc(dataBlocks - contents.length)]);
  return Buffer.concat([header, padded]);
}

function metaFor(guid) {
  return `fileFormatVersion: 2\nguid: ${guid}\n`;
}

/*
    Builds an in-memory artifact with the shape the exporter emits: one GUID
    directory entry (typeflag 5) per asset, then `asset` (files only),
    `asset.meta`, and `pathname`, plus the two terminating zero blocks.
*/
function buildArtifact(entries) {
  const blocks = [];
  for (const entry of entries) {
    blocks.push(tarEntry(`${entry.guid}/`, "", "5"));
    if (entry.asset !== undefined) {
      blocks.push(tarEntry(`${entry.guid}/asset`, entry.asset));
    }
    blocks.push(tarEntry(`${entry.guid}/asset.meta`, entry.metaText ?? metaFor(entry.guid)));
    blocks.push(tarEntry(`${entry.guid}/pathname`, entry.pathname));
  }
  blocks.push(Buffer.alloc(1024));
  return zlib.gzipSync(Buffer.concat(blocks));
}

function standardArtifact(overrides = {}) {
  const entries = [
    { guid: FOLDER_GUID, pathname: `${ROOT}/Runtime/Helper`, isFolder: true },
    {
      guid: RUNTIME_GUID,
      pathname: `${ROOT}/Runtime/CommandTerminal/Backend/Terminal.cs`,
      asset: "// runtime"
    },
    {
      guid: ANALYZER_GUID,
      pathname: `${ROOT}/Runtime/Analyzers/DxGenerators.dll`,
      asset: "MZ",
      metaText: `fileFormatVersion: 2\nguid: ${ANALYZER_GUID}\nlabels:\n- RoslynAnalyzer\nPluginImporter:\n`
    },
    {
      guid: EDITOR_GUID,
      pathname: `${ROOT}/Runtime/WallstopStudios.DxCommandTerminal.asmdef`,
      asset: RUNTIME_ASMDEF
    },
    ...((overrides.extraEntries ?? []) ?? [])
  ];
  if (overrides.withoutAnalyzer === true) {
    return buildArtifact(entries.filter((entry) => entry.guid !== ANALYZER_GUID));
  }
  if (overrides.withoutAsmdef === true) {
    return buildArtifact(entries.filter((entry) => entry.guid !== EDITOR_GUID));
  }
  return buildArtifact(entries);
}

test("readTar reads plain ustar entries and stops at the zero blocks", () => {
  const tar = readTar(buildArtifact([{ guid: RUNTIME_GUID, pathname: `${ROOT}/a.txt`, asset: "x" }]));
  assert.deepStrictEqual([...tar.keys()], [`${RUNTIME_GUID}/asset`, `${RUNTIME_GUID}/asset.meta`, `${RUNTIME_GUID}/pathname`]);
});

test("readTar skips the exporter's bare GUID directory entries but keeps other directory entries", () => {
  const buffer = zlib.gzipSync(
    Buffer.concat([
      tarEntry(`${RUNTIME_GUID}/`, "", "5"),
      tarEntry(`${RUNTIME_GUID}/asset.meta`, metaFor(RUNTIME_GUID)),
      tarEntry(`${RUNTIME_GUID}/pathname`, `${ROOT}/Folder`),
      tarEntry("Packages/", "", "5"),
      Buffer.alloc(1024)
    ])
  );
  const tar = readTar(buffer);
  assert.deepStrictEqual([...tar.keys()], [`${RUNTIME_GUID}/asset.meta`, `${RUNTIME_GUID}/pathname`, "Packages"]);
});

const readTarRejections = [
  { name: "pax extension header", typeflag: "x", matches: /unsupported tar entry type/ },
  { name: "gnu long-name header", typeflag: "L", matches: /unsupported tar entry type/ },
  { name: "symlink entry", typeflag: "2", matches: /unsupported tar entry type/ },
  { name: "corrupted size field", typeflag: "0", corruptSize: "9999999999X", matches: /malformed tar size field/ }
];

for (const rejection of readTarRejections) {
  test(`readTar fails closed: ${rejection.name}`, () => {
    const entry = tarEntry(`${RUNTIME_GUID}/asset`, "x", rejection.typeflag);
    if (rejection.corruptSize !== undefined) {
      entry.write(rejection.corruptSize, 124, "utf8");
    }
    const buffer = zlib.gzipSync(Buffer.concat([entry, Buffer.alloc(1024)]));
    assert.throws(() => readTar(buffer), rejection.matches);
  });
}

test("readTar fails closed on duplicate entry names", () => {
  const buffer = zlib.gzipSync(
    Buffer.concat([
      tarEntry(`${RUNTIME_GUID}/asset`, "first"),
      tarEntry(`${RUNTIME_GUID}/asset`, "second"),
      Buffer.alloc(1024)
    ])
  );
  assert.throws(() => readTar(buffer), /duplicate tar entry/);
});

test("readTar fails closed on trailing garbage", () => {
  const buffer = zlib.gzipSync(
    Buffer.concat([tarEntry(`${RUNTIME_GUID}/asset`, "x"), Buffer.alloc(1024), Buffer.from("junk")])
  );
  assert.throws(() => readTar(buffer), /trailing garbage/);
});

test("listArtifact fails closed: two GUIDs claim one pathname", () => {
  const buffer = buildArtifact([
    { guid: RUNTIME_GUID, pathname: `${ROOT}/a.txt`, asset: "x" },
    { guid: EDITOR_GUID, pathname: `${ROOT}/a.txt`, asset: "y" }
  ]);
  assert.throws(() => listArtifact(buffer), /claimed by both/);
});

test("scratch dependency derivation fails closed on malformed references", () => {
  assert.throws(
    () =>
      listArtifact(
        standardArtifact({
          extraEntries: [
            { guid: "cccccccccccccccccccccccccccccccc", pathname: `${ROOT}/Extra.asmdef`, asset: JSON.stringify({ name: "X", references: "Automatic" }) }
          ]
        })
      ),
    /malformed references/
  );
});

test("listArtifact models folders, files, order, and the shared import root", () => {
  const artifact = listArtifact(standardArtifact());
  assert.strictEqual(artifact.root, ROOT);
  assert.deepStrictEqual(
    artifact.assets.map((asset) => [asset.pathname, asset.isFolder]),
    [
      [`${ROOT}/Runtime/Analyzers/DxGenerators.dll`, false],
      [`${ROOT}/Runtime/CommandTerminal/Backend/Terminal.cs`, false],
      [`${ROOT}/Runtime/Helper`, true],
      [`${ROOT}/Runtime/WallstopStudios.DxCommandTerminal.asmdef`, false]
    ]
  );
});

test("listArtifact derives scratch UPM dependencies from the artifact's asmdefs", () => {
  const artifact = listArtifact(standardArtifact());
  assert.deepStrictEqual(artifact.scratchDependencies, { "com.unity.inputsystem": "1.7.0" });
  const withTests = listArtifact(
    standardArtifact({
      extraEntries: [
        {
          guid: "cccccccccccccccccccccccccccccccc",
          pathname: `${ROOT}/Tests/Runtime/WallstopStudios.DxCommandTerminal.Tests.Runtime.asmdef`,
          asset: JSON.stringify({
            name: "WallstopStudios.DxCommandTerminal.Tests.Runtime",
            references: ["WallstopStudios.DxCommandTerminal", "UnityEngine.TestRunner"],
            defineConstraints: ["UNITY_INCLUDE_TESTS"]
          })
        }
      ]
    })
  );
  assert.deepStrictEqual(withTests.scratchDependencies, {
    "com.unity.inputsystem": "1.7.0",
    "com.unity.test-framework": "1.1.33"
  });
});

const dependencyViolations = [
  {
    name: "reference unknown assembly",
    asset: JSON.stringify({ name: "X", references: ["Some.Mystery.Assembly"] }),
    matches: /references unknown assembly Some\.Mystery\.Assembly/
  },
  {
    name: "unreadable asmdef",
    asset: "not json",
    matches: /unreadable asmdef/
  }
];

for (const violation of dependencyViolations) {
  test(`scratch dependency derivation fails closed: ${violation.name}`, () => {
    assert.throws(
      () =>
        listArtifact(
          standardArtifact({
            extraEntries: [
              {
                guid: "cccccccccccccccccccccccccccccccc",
                pathname: `${ROOT}/Extra.asmdef`,
                asset: violation.asset
              }
            ]
          })
        ),
      violation.matches
    );
  });
}

test("listArtifact fails closed: empty archive", () => {
  assert.throws(() => listArtifact(buildArtifact([])), /no GUID directories/);
});

const artifactViolations = [
  {
    name: "missing pathname",
    entries: [{ guid: RUNTIME_GUID, asset: "x", noPathname: true }],
    matches: /no pathname/
  },
  {
    name: "missing meta",
    entries: [{ guid: RUNTIME_GUID, pathname: `${ROOT}/a.txt`, asset: "x", noMeta: true }],
    matches: /no asset\.meta/
  },
  {
    name: "guid disagreement",
    entries: [{ guid: RUNTIME_GUID, pathname: `${ROOT}/a.txt`, asset: "x", metaText: metaFor(EDITOR_GUID) }],
    matches: /disagrees with directory/
  },
  {
    name: "unsafe pathname",
    entries: [{ guid: RUNTIME_GUID, pathname: `${ROOT}/../../etc/passwd`, asset: "x" }],
    matches: /unsafe pathname/
  },
  {
    name: "mixed roots",
    entries: [
      { guid: RUNTIME_GUID, pathname: `${ROOT}/a.txt`, asset: "x" },
      { guid: EDITOR_GUID, pathname: "Assets/other.txt", asset: "x" }
    ],
    matches: /do not share one import root/
  },
  {
    name: "unexpected member",
    entries: [{ guid: RUNTIME_GUID, pathname: `${ROOT}/a.txt`, asset: "x", extra: "junk" }],
    matches: /unexpected member junk/
  }
];

for (const violation of artifactViolations) {
  test(`listArtifact fails closed: ${violation.name}`, () => {
    const blocks = [];
    for (const entry of violation.entries ?? []) {
      blocks.push(tarEntry(`${entry.guid}/`, "", "5"));
      if (entry.noMeta !== true) {
        blocks.push(tarEntry(`${entry.guid}/asset.meta`, entry.metaText ?? metaFor(entry.guid)));
      }
      if (entry.asset !== undefined) {
        blocks.push(tarEntry(`${entry.guid}/pathname`, entry.pathname ?? ""));
        blocks.push(tarEntry(`${entry.guid}/asset`, entry.asset));
      }
      if (entry.extra !== undefined) {
        blocks.push(tarEntry(`${entry.guid}/${entry.extra}`, ""));
      }
    }
    const buffer = zlib.gzipSync(Buffer.concat([...blocks, Buffer.alloc(1024)]));
    assert.throws(() => listArtifact(buffer), violation.matches);
  });
}

test("metaLabels reads inline and list label blocks in any order", () => {
  assert.deepStrictEqual(metaLabels("labels: RoslynAnalyzer\n"), new Set(["RoslynAnalyzer"]));
  assert.deepStrictEqual(metaLabels("labels:\n- RoslynAnalyzer\n"), new Set(["RoslynAnalyzer"]));
  assert.deepStrictEqual(
    metaLabels("labels:\n  - SomethingElse\n  - RoslynAnalyzer\n"),
    new Set(["SomethingElse", "RoslynAnalyzer"])
  );
  assert.deepStrictEqual(metaLabels("guid: aabb\n"), new Set());
});

test("buildImportDriver is deterministic, phase-guarded, and waits out compilation", () => {
  const first = buildImportDriver();
  assert.strictEqual(first, buildImportDriver());
  assert.match(first, /InitializeOnLoad/);
  assert.match(first, /DX_IMPORT_DRILL_SETTLE/);
  assert.match(first, /isCompiling \|\| EditorApplication\.isUpdating/);
  assert.match(first, /EditorApplication\.Exit\(0\)/);
  assert.match(first, /idle past the settle deadline/);
  assert.ok(
    first.indexOf("isCompiling") < first.indexOf("idle past the settle deadline"),
    "the settle deadline is only enforced while the pipeline is idle"
  );
  assert.doesNotMatch(first, /ImportPackage/);
  assert.doesNotMatch(first, /SessionState/);
  assert.doesNotMatch(first, /"[A-Za-z]:[\\/]|\/workspaces\//u);
});

test("the settle phase environment arms the generated driver", () => {
  assert.deepStrictEqual(SETTLE_PHASE_ENV, { DX_IMPORT_DRILL_SETTLE: "1" });
  const driver = buildImportDriver();
  assert.match(driver, /GetEnvironmentVariable\("DX_IMPORT_DRILL_SETTLE"\) == "1"/);
});

test("importUnityArgs and settleUnityArgs shape the two Unity invocations", () => {
  assert.deepStrictEqual(importUnityArgs("/proj", "/a.unitypackage", "/log1"), [
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath",
    "/proj",
    "-importPackage",
    "/a.unitypackage",
    "-logFile",
    "/log1"
  ]);
  assert.deepStrictEqual(settleUnityArgs("/proj", "/log2"), [
    "-batchmode",
    "-nographics",
    "-executeMethod",
    "DxTerminalImportDrill.WaitForImportAndCompile",
    "-projectPath",
    "/proj",
    "-logFile",
    "/log2"
  ]);
});

test("scaffoldProject writes the version file, dependency manifest, and driver", () => {
  const project = tempRoot("scaffold");
  scaffoldProject(project, "6000.4.6f1", { "com.unity.inputsystem": "1.7.0" });
  assert.strictEqual(
    fs.readFileSync(path.join(project, "ProjectSettings", "ProjectVersion.txt"), "utf8"),
    "m_EditorVersion: 6000.4.6f1\n"
  );
  assert.deepStrictEqual(JSON.parse(fs.readFileSync(path.join(project, "Packages", "manifest.json"), "utf8")), {
    dependencies: { "com.unity.inputsystem": "1.7.0" }
  });
  assert.strictEqual(
    fs.readFileSync(path.join(project, "Assets", "DxTerminalImportDrill.cs"), "utf8"),
    buildImportDriver()
  );
});

const scaffoldRejections = ["not a version\n", "../evil", "6000.4.6f1 (hash)", ""];

for (const version of scaffoldRejections) {
  test(`scaffoldProject rejects implausible editor version: ${JSON.stringify(version)}`, () => {
    assert.throws(() => scaffoldProject(tempRoot("bad"), version, {}), /implausible editor version/);
  });
}

/*
    Materializes a fake post-import project tree on disk matching the artifact
    exactly the way Unity would (imported files + metas + compiled assemblies).
*/
function materializeImportedProject(project, artifact, overrides = {}) {
  const write = (relative, contents) => {
    const target = path.join(project, relative);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, contents);
  };
  for (const asset of artifact.assets) {
    if (asset.isFolder) {
      fs.mkdirSync(path.join(project, asset.pathname), { recursive: true });
    } else {
      write(asset.pathname, asset.pathname.endsWith(".asmdef") ? asset.asmdefSource : `content of ${asset.guid}`);
    }
    if (overrides.mutateMeta?.(asset.pathname) !== true) {
      write(`${asset.pathname}.meta`, asset.metaText);
    }
    const asmdef = asset.pathname.endsWith(".asmdef")
      ? JSON.parse(fs.readFileSync(path.join(project, asset.pathname), "utf8"))
      : null;
    if (asmdef !== null && overrides.dropCompiled !== true) {
      write(path.join("Library", "ScriptAssemblies", `${asmdef.name}.dll`), "MZ");
    }
  }
}

function writeFile(target, contents) {
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, contents);
}

test("validateImportedProject accepts a complete import", () => {
  const project = tempRoot("valid");
  const artifact = listArtifact(standardArtifact());
  materializeImportedProject(project, artifact);
  const result = validateImportedProject(project, artifact);
  assert.deepStrictEqual(result.failures, []);
  const names = result.checks.map((check) => check.name);
  assert.ok(names.includes(`analyzer label survives ${ROOT}/Runtime/Analyzers/DxGenerators.dll`));
  assert.ok(names.includes("compiled assembly exists WallstopStudios.DxCommandTerminal.dll"));
});

const importFailures = [
  {
    name: "missing imported file",
    mutate: (project, artifact) => {
      fs.rmSync(path.join(project, `${ROOT}/Runtime/CommandTerminal/Backend/Terminal.cs`));
    },
    matches: /file exists .*Terminal\.cs/
  },
  {
    name: "meta guid rewritten by Unity",
    mutate: (project, artifact) => {
      writeFile(path.join(project, `${ROOT}/Runtime/Analyzers/DxGenerators.dll.meta`), metaFor("cccccccccccccccccccccccccccccccc"));
    },
    matches: /meta guid matches .*DxGenerators\.dll/
  },
  {
    name: "analyzer label stripped",
    mutate: (project, artifact) => {
      writeFile(path.join(project, `${ROOT}/Runtime/Analyzers/DxGenerators.dll.meta`), metaFor(ANALYZER_GUID));
    },
    matches: /analyzer label survives/
  },
  {
    name: "compiled assembly missing",
    mutate: (project, artifact) => {
      fs.rmSync(path.join(project, "Library/ScriptAssemblies/WallstopStudios.DxCommandTerminal.dll"));
    },
    matches: /compiled assembly exists/
  },
  {
    name: "asmdef is not JSON",
    mutate: (project, artifact) => {
      writeFile(path.join(project, `${ROOT}/Runtime/WallstopStudios.DxCommandTerminal.asmdef`), "not json");
    },
    matches: /asmdef parses/
  },
  {
    name: "asmdef name escapes ScriptAssemblies",
    mutate: (project, artifact) => {
      writeFile(
        path.join(project, `${ROOT}/Runtime/WallstopStudios.DxCommandTerminal.asmdef`),
        JSON.stringify({ name: "../Evil/Name" })
      );
    },
    matches: /safe assembly name/
  },
  {
    name: "asmdef name is a Unity-reserved assembly",
    mutate: (project, artifact) => {
      writeFile(
        path.join(project, `${ROOT}/Runtime/WallstopStudios.DxCommandTerminal.asmdef`),
        JSON.stringify({ name: "Assembly-CSharp" })
      );
    },
    matches: /safe assembly name/
  }
];

for (const failure of importFailures) {
  test(`validateImportedProject fails closed: ${failure.name}`, () => {
    const project = tempRoot("invalid");
    const artifact = listArtifact(standardArtifact());
    materializeImportedProject(project, artifact);
    failure.mutate(project, artifact);
    const result = validateImportedProject(project, artifact);
    assert.match(result.failures.join("\n"), failure.matches);
  });
}

test("validateImportedProject fails closed: artifact without any asmdef", () => {
  const project = tempRoot("no-asmdef");
  const artifact = listArtifact(standardArtifact({ withoutAsmdef: true }));
  materializeImportedProject(project, artifact);
  const result = validateImportedProject(project, artifact);
  assert.match(result.failures.join("\n"), /at least one asmdef/);
});

test("probeEditorVersion reads a version from a real spawned probe (node stands in for Unity)", () => {
  const version = probeEditorVersion(process.execPath, ["-e", "console.log('6000.4.6f1')"]);
  assert.strictEqual(version, "6000.4.6f1");
});

test("probeEditorVersion tolerates banner noise and fails closed without a version line", () => {
  const noisy = probeEditorVersion(process.execPath, [
    "-e",
    "console.log('some banner'); console.log('2022.3.51f1')"
  ]);
  assert.strictEqual(noisy, "2022.3.51f1");
  assert.throws(() => probeEditorVersion(process.execPath, ["-e", "console.log('')"]), /no plausible version/);
});

test("parseArgs applies defaults and validates input", () => {
  const parsed = parseArgs(["--artifact", "a.unitypackage", "--unity", "/unity"]);
  assert.strictEqual(parsed.artifact, path.resolve("a.unitypackage"));
  assert.strictEqual(parsed.unity, "/unity");
  assert.strictEqual(parsed.project, "");
  assert.strictEqual(parsed.keep, false);
  assert.strictEqual(parsed.timeoutMinutes, 20);
  const timed = parseArgs(["--artifact", "a", "--unity", "u", "--project", "p", "--keep", "--timeout-minutes", "5"]);
  assert.strictEqual(timed.keep, true);
  assert.strictEqual(timed.timeoutMinutes, 5);
  assert.throws(() => parseArgs(["--artifact", "a"]), /missing --unity/);
  assert.throws(() => parseArgs(["--unity", "u"]), /missing --artifact/);
  assert.throws(() => parseArgs(["--artifact", "a", "--unity", "u", "--unknown"]), /unknown argument/);
  assert.throws(() => parseArgs(["--artifact"]), /missing value/);
  assert.throws(() => parseArgs(["--artifact", "a", "--unity", "u", "--timeout-minutes", "0"]), /positive number/);
});

/*
    Stubs the generic Unity process runner. Each phase is `{ imports, logLines,
    result }`; the stub dispatches on the invocation shape (phase 1 carries
    -importPackage, phase 2 -executeMethod). A phase left undefined settles
    successfully without writing a log.
*/
function stubRunUnity({ importPhase = {}, settlePhase = {}, calls = [] } = {}) {
  return async (unityPath, args, logPath, timeoutMs) => {
    const phase = args.includes("-importPackage") ? importPhase : settlePhase;
    calls.push({ args, logPath, timeoutMs });
    if (phase.imports === true) {
      const artifactPath = args[args.indexOf("-importPackage") + 1];
      const projectDir = args[args.indexOf("-projectPath") + 1];
      const artifact = listArtifact(fs.readFileSync(artifactPath));
      materializeImportedProject(projectDir, artifact);
    }
    if (phase.logLines !== undefined) {
      writeFile(logPath, phase.logLines.join("\n"));
    }
    return (
      phase.result ?? { timedOut: false, code: 0, signal: null }
    );
  };
}

test("runImportDrill completes end to end and cleans up the scratch project", async () => {
  const reportDir = tempRoot("run-ok");
  const artifactPath = path.join(reportDir, "drill.unitypackage");
  fs.writeFileSync(artifactPath, standardArtifact());
  const calls = [];
  const manifest = await runImportDrill(
    { ...parseArgs(["--artifact", artifactPath, "--unity", "unity"]), reportDir },
    {
      probeEditorVersion: () => "6000.4.6f1",
      runUnity: stubRunUnity({
        importPhase: { imports: true, logLines: ["Import package from :drill.unitypackage !"] },
        settlePhase: { logLines: ["[import-drill] import and compile settled"] },
        calls
      })
    }
  );
  assert.strictEqual(manifest.outcome, null);
  assert.strictEqual(manifest.failed, false);
  assert.deepStrictEqual(manifest.logErrors, []);
  assert.strictEqual(manifest.editorVersion, "6000.4.6f1");
  assert.strictEqual(manifest.timedOut, false);
  assert.strictEqual(manifest.entries, 4);
  assert.strictEqual(manifest.importRoot, ROOT);
  assert.deepStrictEqual(manifest.upmDependencies, { "com.unity.inputsystem": "1.7.0" });
  assert.ok(manifest.checks.length > 0, "manifest records validation checks");
  assert.ok(manifest.artifactSha256.length === 64);
  assert.strictEqual(manifest.unityImport.exitCode, 0);
  assert.strictEqual(manifest.unityImport.timedOut, false);
  assert.strictEqual(manifest.unitySettle.exitCode, 0);
  assert.strictEqual(calls.length, 2, "import phase runs before the settle phase");
  assert.ok(calls[0].args.includes("-importPackage"));
  assert.ok(calls[1].args.includes("-executeMethod"));
  assert.ok(calls[1].args.includes("DxTerminalImportDrill.WaitForImportAndCompile"));
  assert.ok(!fs.existsSync(manifest.project), "scratch project should be removed on success");
});

test("runImportDrill keeps the project with --keep and ignores unrelated warnings", async () => {
  const reportDir = tempRoot("run-keep");
  const artifactPath = path.join(reportDir, "drill.unitypackage");
  fs.writeFileSync(artifactPath, standardArtifact());
  const manifest = await runImportDrill(
    { ...parseArgs(["--artifact", artifactPath, "--unity", "unity", "--keep"]), reportDir },
    {
      probeEditorVersion: () => "6000.4.6f1",
      runUnity: stubRunUnity({
        importPhase: {
          imports: true,
          logLines: [
            "warning CS0168: The variable 'unused' is declared but never used",
            "Import package from :drill.unitypackage !"
          ]
        },
        settlePhase: { logLines: ["[import-drill] import and compile settled"] }
      })
    }
  );
  assert.strictEqual(manifest.outcome, null);
  assert.deepStrictEqual(manifest.logErrors, []);
  assert.ok(fs.existsSync(path.join(manifest.project, "Assets", "DxTerminalImportDrill.cs")));
  assert.ok(fs.existsSync(manifest.unityImport.log));
  assert.ok(fs.existsSync(manifest.unitySettle.log));
});

const compilationFailures = [
  "Assets/Terminal.cs(12,3): error CS1002: ; expected",
  "Aborting batchmode due to failure",
  "Scripts have compiler errors",
  "warning CS8032: An instance of analyzer DxGenerators cannot be created",
  "warning CS8784: Generator 'DxGenerators' failed to initialize.",
  "warning CS8785: Generator 'DxGenerators' failed to generate source.",
  "warning CS9057: The analyzer assembly 'DxGenerators' references version '4.8.0.0' of the compiler, which is newer than the currently running version '4.3.0.0'.",
  "warning AD0001: Analyzer 'DxGenerators' threw an exception."
];

const runFailures = [
  ...compilationFailures.map((diagnostic) => ({
    name: `exit zero with compiled DLLs and ${diagnostic}`,
    behavior: {
      importPhase: {
        imports: true,
        result: { timedOut: false, code: 0, signal: null },
        logLines: ["Import package from :drill.unitypackage !", diagnostic]
      },
      settlePhase: { logLines: ["[import-drill] import and compile settled"] }
    },
    matches: /unity log validation failed/,
    expectedLogErrors: [diagnostic]
  })),
  {
    name: "exit zero with compiled DLLs but no import log",
    behavior: {
      importPhase: { imports: true, result: { timedOut: false, code: 0, signal: null } },
      settlePhase: { logLines: ["[import-drill] import and compile settled"] }
    },
    matches: /unity log validation failed/,
    expectedLogErrors: ["(unity produced no log file)"]
  },
  {
    name: "import phase exits nonzero",
    behavior: {
      importPhase: {
        imports: false,
        result: { timedOut: false, code: 3, signal: null },
        logLines: ["Aborting batchmode due to failure"]
      }
    },
    matches: /unity import phase exited 3/,
    logMatches: /Aborting batchmode/,
    assertSettleNeverRan: true
  },
  {
    name: "settle phase exits nonzero",
    behavior: {
      importPhase: { imports: true, result: { timedOut: false, code: 0, signal: null } },
      settlePhase: { result: { timedOut: false, code: 3, signal: null } }
    },
    matches: /unity settle phase exited 3/
  },
  {
    name: "unity killed by signal",
    behavior: {
      importPhase: { imports: false, result: { timedOut: false, code: null, signal: "SIGKILL" } }
    },
    matches: /unity import phase killed by signal SIGKILL/
  },
  {
    name: "unity times out",
    behavior: {
      importPhase: { imports: false, result: { timedOut: true, code: null, signal: null } }
    },
    matches: /unity import phase timed out/,
    assertTimedOut: true
  },
  {
    name: "settle phase times out",
    behavior: {
      importPhase: { imports: true, result: { timedOut: false, code: 0, signal: null } },
      settlePhase: { result: { timedOut: true, code: null, signal: null } }
    },
    matches: /unity settle phase timed out/,
    assertTimedOut: true
  },
  {
    name: "unity cannot launch",
    behavior: {
      importPhase: { imports: false, result: { timedOut: false, code: null, signal: null, spawnError: "ENOENT" } }
    },
    matches: /could not launch unity/
  },
  {
    name: "import is incomplete",
    behavior: {
      importPhase: {
        imports: false,
        result: { timedOut: false, code: 0, signal: null },
        logLines: ["Assets/Terminal.cs(12,3): error CS1002: ; expected"]
      },
      settlePhase: { result: { timedOut: false, code: 0, signal: null } }
    },
    matches: /validation failed/,
    logMatches: /error CS1002/
  }
];

for (const failure of runFailures) {
  test(`runImportDrill fails closed: ${failure.name}`, async () => {
    const reportDir = tempRoot("run-fail");
    const artifactPath = path.join(reportDir, "drill.unitypackage");
    fs.writeFileSync(artifactPath, standardArtifact());
    const manifest = await runImportDrill(
      { ...parseArgs(["--artifact", artifactPath, "--unity", "unity"]), reportDir },
      {
        probeEditorVersion: () => "6000.4.6f1",
        runUnity: stubRunUnity(failure.behavior)
      }
    );
    assert.strictEqual(manifest.failed, true);
    assert.match(manifest.outcome, failure.matches);
    if (failure.name === "import is incomplete") {
      assert.ok(manifest.checks.length > 0, "validation failures record their checks");
    }
    if (failure.assertSettleNeverRan === true) {
      assert.strictEqual(manifest.unitySettle, null);
    }
    if (failure.assertTimedOut === true) {
      assert.strictEqual(manifest.timedOut, true);
    }
    if (failure.logMatches !== undefined) {
      assert.match(manifest.logErrors.join("\n"), failure.logMatches);
    }
    if (failure.expectedLogErrors !== undefined) {
      assert.deepStrictEqual(manifest.logErrors, failure.expectedLogErrors);
      assert.ok(manifest.checks.length > 0);
      assert.ok(manifest.checks.every((check) => check.ok));
      assert.deepStrictEqual(
        JSON.parse(fs.readFileSync(path.join(reportDir, "manifest.json"), "utf8")),
        manifest
      );
    }
    assert.ok(fs.existsSync(manifest.project), "failed runs keep the project for diagnosis");
    assert.ok(fs.existsSync(path.join(reportDir, "manifest.json")));
    if (failure.behavior.importPhase?.logLines !== undefined) {
      assert.ok(fs.existsSync(path.join(reportDir, "unity-import.log")));
    }
    if (failure.behavior.settlePhase?.logLines !== undefined) {
      assert.ok(fs.existsSync(path.join(reportDir, "unity-settle.log")));
    }
  });
}

test("runImportDrill refuses a non-empty --project directory", async () => {
  const reportDir = tempRoot("run-busy");
  const artifactPath = path.join(reportDir, "drill.unitypackage");
  fs.writeFileSync(artifactPath, standardArtifact());
  const busy = path.join(reportDir, "project");
  fs.mkdirSync(busy);
  writeFile(path.join(busy, "stale.txt"), "left over");
  await assert.rejects(
    () =>
      runImportDrill(
        { ...parseArgs(["--artifact", artifactPath, "--unity", "unity", "--project", busy]), reportDir },
        {
          probeEditorVersion: () => "6000.4.6f1",
          runUnity: stubRunUnity({ importPhase: { imports: true } })
        }
      ),
    /empty or missing directory/
  );
});

test("runImportDrill fails closed on a corrupt artifact before touching Unity", async () => {
  const reportDir = tempRoot("run-corrupt");
  const artifactPath = path.join(reportDir, "drill.unitypackage");
  fs.writeFileSync(artifactPath, Buffer.from("not a tarball"));
  let unityTouched = false;
  await assert.rejects(
    () =>
      runImportDrill(
        { ...parseArgs(["--artifact", artifactPath, "--unity", "unity"]), reportDir },
        {
          probeEditorVersion: () => {
            unityTouched = true;
            return "6000.4.6f1";
          },
          runUnity: async () => {
            unityTouched = true;
            return { timedOut: false, code: 0, signal: null };
          }
        }
      ),
    /incorrect header|invariant|unexpected|truncated/u
  );
  assert.strictEqual(unityTouched, false);
});

test("the reader parses the real exporter artifact", async () => {
  const exporter = await import(
    pathToFileURL(path.join(path.dirname(drillScript), "export-unitypackage.mjs")).href
  );
  const { buffer } = exporter.exportUnityPackage({ packageRoot: path.resolve(toolingRoot, ".."), out: "" });
  const artifact = listArtifact(buffer);
  assert.strictEqual(artifact.root, "Packages/com.wallstop-studios.dxcommandterminal");
  assert.ok(artifact.assets.length > 300, "the real artifact must carry the full tree");
  assert.deepStrictEqual(artifact.scratchDependencies, {
    "com.unity.inputsystem": "1.7.0",
    "com.unity.test-framework": "1.1.33"
  });
  const analyzers = artifact.assets.filter((asset) => asset.pathname.includes("/Analyzers/"));
  assert.ok(analyzers.length > 0, "the analyzer payload must ride the artifact");
  assert.ok(artifact.assets.some((asset) => asset.pathname.endsWith(".asmdef")));
  assert.strictEqual(artifact.assets.filter((asset) => asset.pathname.includes("..")).length, 0);
});
