import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import {
  EDIT_TARGET,
  parseDrillArgs,
  planPhases,
  runDrill,
  SETTLE_DRIVER_SOURCE,
  scaffoldProject,
  summarizePairs
} from "../t7/consumer/runner.mjs";

function scratch() {
  return fs.mkdtempSync(path.join(os.tmpdir(), "dxct-t7-consumer-"));
}

function drillOptions(outDir) {
  const artifact = path.join(outDir, "artifact.unitypackage");
  fs.writeFileSync(artifact, "not-a-real-package");
  const unity = path.join(outDir, "unity");
  fs.writeFileSync(unity, "not-a-real-binary");
  return {
    unity,
    artifact,
    out: path.join(outDir, "artifacts"),
    pairs: 2,
    timeoutMinutes: 30
  };
}

test("drill args require the three host paths and validate counts", () => {
  assert.throws(() => parseDrillArgs([]), /--unity is required/);
  assert.throws(() => parseDrillArgs(["--unity", "u"]), /--artifact is required/);
  assert.throws(
    () => parseDrillArgs(["--unity", "u", "--artifact", "a"]),
    /--out is required/
  );
  assert.throws(
    () => parseDrillArgs(["--unity", "u", "--artifact", "a", "--out", "o", "--pairs", "0"]),
    /--pairs must be a positive integer/
  );
  assert.throws(
    () => parseDrillArgs(["--unity", "u", "--artifact", "a", "--out", "o", "--timeout-minutes", "x"]),
    /--timeout-minutes must be a positive integer/
  );
  assert.throws(
    () => parseDrillArgs(["--unity", "u", "--artifact", "a", "--out", "o", "--wat", "1"]),
    /unknown option: --wat/
  );
});

test("drill args accept equals form and defaults", () => {
  const options = parseDrillArgs(["--unity=u", "--artifact=a", "--out=o"]);
  assert.deepEqual(
    { unity: options.unity, artifact: options.artifact, out: options.out, pairs: options.pairs, timeoutMinutes: options.timeoutMinutes },
    { unity: "u", artifact: "a", out: "o", pairs: 4, timeoutMinutes: 30 }
  );
});

test("phase plan pairs every control with its edit and quits batch phases", () => {
  const options = { out: "/out", artifact: "/a.unitypackage", pairs: 2 };
  const phases = planPhases(options);
  assert.deepEqual(phases.map((phase) => phase.name), [
    "import",
    "settle",
    "control-1",
    "edit-1",
    "control-2",
    "edit-2"
  ]);

  const importPhase = phases[0];
  assert.ok(importPhase.args.includes("-importPackage"));
  assert.ok(importPhase.args.includes("/a.unitypackage"));
  assert.ok(importPhase.args.includes("-quit"));
  assert.ok(!importPhase.args.includes("-executeMethod"));
  const projectArg = importPhase.args[importPhase.args.indexOf("-projectPath") + 1];
  assert.equal(projectArg, path.join("/out", "project"), "the plan derives the scratch project from --out");

  const settlePhase = phases[1];
  assert.ok(settlePhase.args.includes("-executeMethod"));
  assert.equal(settlePhase.args[settlePhase.args.indexOf("-executeMethod") + 1], "DxT7ConsumerSettle.WaitForIdle");
  assert.ok(!settlePhase.args.includes("-quit"));
  assert.deepEqual(settlePhase.env, { DX_T7_CONSUMER_SETTLE: "1" }, "the settle env guard keeps the import phase inert");
  assert.equal(phases[0].env, undefined, "the import phase must not carry the settle env");

  for (const phase of phases) {
    assert.ok(phase.args.includes("-batchmode"), `${phase.name} runs batch`);
    assert.ok(phase.args.includes("-nographics"), `${phase.name} runs nographics`);
    assert.equal(phase.args[phase.args.indexOf("-projectPath") + 1], projectArg, `${phase.name} targets the same project`);
  }

  const controlLog = phases[2].args[phases[2].args.indexOf("-logFile") + 1];
  const editLog = phases[3].args[phases[3].args.indexOf("-logFile") + 1];
  assert.notEqual(controlLog, editLog, "paired runs must not share a log file");
});

test("scaffold pins the editor version, upm floors, and settle driver; refuses reuse", () => {
  const root = scratch();
  const project = path.join(root, "project");
  scaffoldProject(project, "6000.4.6f1", SETTLE_DRIVER_SOURCE);
  assert.equal(
    fs.readFileSync(path.join(project, "ProjectSettings", "ProjectVersion.txt"), "utf8"),
    "m_EditorVersion: 6000.4.6f1\n"
  );
  const manifest = JSON.parse(fs.readFileSync(path.join(project, "Packages", "manifest.json"), "utf8"));
  assert.equal(manifest.dependencies["com.unity.inputsystem"], "1.7.0");
  assert.equal(manifest.dependencies["com.unity.test-framework"], "1.1.33");
  const driver = fs.readFileSync(path.join(project, "Assets", "Editor", "DxT7ConsumerSettle.cs"), "utf8");
  assert.ok(driver.includes("EditorApplication.Exit(0)"));
  assert.ok(driver.includes("isCompiling || EditorApplication.isUpdating"));
  assert.throws(() => scaffoldProject(project, "6000.4.6f1", SETTLE_DRIVER_SOURCE), /empty or missing/);
});

test("scaffold rejects implausible editor versions", () => {
  const root = scratch();
  assert.throws(
    () => scaffoldProject(path.join(root, "p"), "6000.4.6f1\nLicensing client connected", SETTLE_DRIVER_SOURCE),
    /implausible editor version/
  );
  assert.throws(() => scaffoldProject(path.join(root, "q"), "", SETTLE_DRIVER_SOURCE), /implausible editor version/);
});

test("settle driver survives domain reloads and self-exits with codes", () => {
  assert.ok(SETTLE_DRIVER_SOURCE.includes("[InitializeOnLoad]"), "the subscription must re-arm after reloads");
  assert.ok(SETTLE_DRIVER_SOURCE.includes('GetEnvironmentVariable("DX_T7_CONSUMER_SETTLE")'));
  assert.ok(SETTLE_DRIVER_SOURCE.includes("EditorApplication.Exit(3)"));
  assert.ok(SETTLE_DRIVER_SOURCE.includes("[dx-t7-consumer] pipeline idle"));
});

test("pair summary reports medians, range, and recompile attribution", () => {
  assert.equal(summarizePairs([]).pairs, 0);
  const summary = summarizePairs([
    { controlMs: 15000, editMs: 15600, deltaMs: 600, editRequestedRecompile: true, compileTimeDeltaMs: 150 },
    { controlMs: 15200, editMs: 15300, deltaMs: 100, editRequestedRecompile: true, compileTimeDeltaMs: 120 },
    { controlMs: 14900, editMs: 15000, deltaMs: 100, editRequestedRecompile: false, compileTimeDeltaMs: 130 }
  ]);
  assert.equal(summary.pairs, 3);
  assert.equal(summary.controlMedianMs, 15000);
  assert.equal(summary.editMedianMs, 15300);
  assert.equal(summary.deltaMedianMs, 100);
  assert.equal(summary.deltaMinMs, 100);
  assert.equal(summary.deltaMaxMs, 600);
  assert.equal(summary.recompilePairs, 2);
  assert.equal(summary.compileTimeDeltaMedianMs, 130);
});

test("run drill attributes each paired run from its own unity log", async () => {
  const root = scratch();
  const options = drillOptions(root);
  const fakeProject = path.join(options.out, "project");
  const fakeLog = (phase, recompiled) => {
    const logPath = phase.args[phase.args.indexOf("-logFile") + 1];
    const compileTime = recompiled ? "647" : "500";
    const lines = [
      "\tScripting: domain reloads=1, domain reload time=400 ms, "
        + `compile time=${compileTime} ms, other=30 ms`
    ];
    if (recompiled) {
      lines.unshift(
        "[ScriptCompilation] Requested script compilation because: "
          + "AssetDatabase observed changes in script compilation related files"
      );
    }
    fs.writeFileSync(logPath, `${lines.join("\n")}\n`);
    return { exitCode: 0, timedOut: false };
  };

  const seenPhases = [];
  const runPhase = async (unity, phase) => {
    seenPhases.push(phase.name);
    if (phase.kind === "import") {
      fs.mkdirSync(path.join(fakeProject, EDIT_TARGET, ".."), { recursive: true });
      fs.writeFileSync(path.join(fakeProject, EDIT_TARGET), "// shipped terminal source\n");
    }
    if (phase.kind === "control" || phase.kind === "edit") {
      return fakeLog(phase, phase.kind === "edit");
    }
    return { exitCode: 0, timedOut: false };
  };

  const manifest = await runDrill(options, {
    runPhase,
    probeEditorVersion: () => "6000.4.6f1",
    notify: () => {}
  });

  assert.equal(manifest.failed, false, manifest.failure ?? "drill failed");
  assert.equal(manifest.editorVersion, "6000.4.6f1");
  assert.deepEqual(seenPhases, ["import", "settle", "control-1", "edit-1", "control-2", "edit-2"]);
  assert.equal(manifest.pairs.pairs, 2);
  assert.equal(manifest.pairs.recompilePairs, 2, "each edit run must show the recompile request line");
  assert.equal(manifest.pairs.compileTimeDeltaMedianMs, 147, "compile attribution is edit 647 minus control 500");
  for (const record of manifest.pairRecords) {
    assert.equal(record.editRequestedRecompile, true);
    assert.equal(record.editCompileTimeMs, 647);
    assert.equal(record.controlCompileTimeMs, 500);
    assert.equal(record.compileTimeDeltaMs, 147);
  }
  const persisted = JSON.parse(fs.readFileSync(path.join(options.out, "manifest.json"), "utf8"));
  assert.equal(persisted.failed, false);
  assert.equal(fs.existsSync(fakeProject), false, "success deletes the scratch project");
});

test("run drill retains the scratch project with --keep and on failure", async () => {
  const root = scratch();
  const options = drillOptions(root);
  options.keep = true;
  const fakeProject = path.join(options.out, "project");
  const manifest = await runDrill(options, {
    runPhase: async (unity, phase) => {
      if (phase.kind === "import") {
        fs.mkdirSync(path.join(fakeProject, EDIT_TARGET, ".."), { recursive: true });
        fs.writeFileSync(path.join(fakeProject, EDIT_TARGET), "// shipped terminal source\n");
      }
      if (phase.kind === "control" || phase.kind === "edit") {
        const logPath = phase.args[phase.args.indexOf("-logFile") + 1];
        fs.writeFileSync(logPath, "\tScripting: domain reloads=1, domain reload time=400 ms, compile time=500 ms, other=30 ms\n");
      }
      return { exitCode: 0, timedOut: false };
    },
    probeEditorVersion: () => "6000.4.6f1",
    notify: () => {}
  });
  assert.equal(manifest.failed, false, manifest.failure ?? "drill failed");
  assert.equal(fs.existsSync(fakeProject), true, "--keep retains the scratch project");
  const source = fs.readFileSync(path.join(fakeProject, EDIT_TARGET), "utf8");
  assert.ok(source.includes("t7 consumer drill sample 1"), "each pair appends its sample line");

  const failing = drillOptions(scratch());
  await runDrill(failing, {
    runPhase: async () => ({ exitCode: 1, timedOut: false }),
    probeEditorVersion: () => "6000.4.6f1",
    notify: () => {}
  });
  assert.equal(
    fs.existsSync(path.join(failing.out, "project")),
    true,
    "a failed run keeps the project for diagnosis"
  );
});

test("drill args accept the keep flag without a value", () => {
  const options = parseDrillArgs(["--unity", "u", "--artifact", "a", "--out", "o", "--keep"]);
  assert.equal(options.keep, true);
  assert.throws(() => parseDrillArgs(["--unity", "u", "--artifact", "a", "--out", "o", "--keep=1"]), /does not take a value/);
});

test("run drill fails closed when a phase exits non-zero", async () => {
  const root = scratch();
  const options = drillOptions(root);
  let calls = 0;
  const manifest = await runDrill(options, {
    runPhase: async () => ({ exitCode: calls++ === 0 ? 0 : 1, timedOut: false }),
    probeEditorVersion: () => "6000.4.6f1",
    notify: () => {}
  });
  assert.equal(manifest.failed, true);
  assert.match(manifest.failure, /settle exited 1/);
  assert.equal(manifest.phases.length, 2, "the run must stop at the failing phase");
});

test("run drill fails closed on a silent no-op import", async () => {
  const root = scratch();
  const options = drillOptions(root);
  const manifest = await runDrill(options, {
    // A no-op import: every phase "succeeds" but the package never lands.
    runPhase: async () => ({ exitCode: 0, timedOut: false }),
    probeEditorVersion: () => "6000.4.6f1",
    notify: () => {}
  });
  assert.equal(manifest.failed, true);
  assert.match(manifest.failure, /import did not land the package/);
  assert.equal(manifest.pairRecords.length, 0, "no pairs may be measured without the package");
  assert.equal(
    fs.existsSync(path.join(options.out, "project")),
    true,
    "a failed run keeps the project for diagnosis"
  );
});

test("run drill records a timed-out phase and keeps the project", async () => {
  const root = scratch();
  const options = drillOptions(root);
  const manifest = await runDrill(options, {
    runPhase: async () => ({ exitCode: null, timedOut: true }),
    probeEditorVersion: () => "6000.4.6f1",
    notify: () => {}
  });
  assert.equal(manifest.failed, true);
  assert.match(manifest.failure, /outlived the drill deadline/);
  assert.equal(manifest.phases[0].timedOut, true);
  assert.equal(fs.existsSync(path.join(options.out, "project")), true);
});

test("run drill reports null compile attribution when logs carry no summary", async () => {
  const root = scratch();
  const options = drillOptions(root);
  const fakeProject = path.join(options.out, "project");
  const manifest = await runDrill(options, {
    runPhase: async (unity, phase) => {
      if (phase.kind === "import") {
        fs.mkdirSync(path.join(fakeProject, EDIT_TARGET, ".."), { recursive: true });
        fs.writeFileSync(path.join(fakeProject, EDIT_TARGET), "// shipped terminal source\n");
      }
      if (phase.kind === "control" || phase.kind === "edit") {
        fs.writeFileSync(phase.args[phase.args.indexOf("-logFile") + 1], "no scripting summary here\n");
      }
      return { exitCode: 0, timedOut: false };
    },
    probeEditorVersion: () => "6000.4.6f1",
    notify: () => {}
  });
  assert.equal(manifest.failed, false, manifest.failure ?? "drill failed");
  assert.equal(manifest.pairs.recompilePairs, 0);
  assert.equal(manifest.pairs.compileTimeDeltaMedianMs, null, "no attribution must not fabricate a median");
  for (const record of manifest.pairRecords) {
    assert.equal(record.compileTimeDeltaMs, null);
  }
});
