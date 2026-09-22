import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";
import {
  checkBaselineEnvironment,
  checkBaselineStore
} from "../t11/check.mjs";
import { compareT4Baselines } from "../mcp/unity-mcp.mjs";
import {
  collectRunManifests,
  promoteRun
} from "../t11/update.mjs";
import {
  encodePng,
  readBaselineIndex,
  writeBaselineIndex
} from "../t11/comparator.mjs";

const SCENARIOS = ["CapturesSurfaceA", "CapturesSurfaceB"];
const WIDTH = 32;
const HEIGHT = 24;

/** Deterministic fixture image; `seed` shifts every channel within a byte. */
function fixtureImage(seed = 0) {
  const data = Buffer.alloc(WIDTH * HEIGHT * 4);
  for (let offset = 0; offset < data.length; offset += 4) {
    data[offset] = (offset / 4) % 256;
    data[offset + 1] = 80 + seed;
    data[offset + 2] = 160;
    data[offset + 3] = 255;
  }
  return data;
}

const manifest = (scenario, overrides = {}) => ({
  scenario,
  capturedUtc: "2026-09-22T04:00:00.0000000Z",
  complete: true,
  resolution: { width: WIDTH, height: HEIGHT },
  logicalScale: 1,
  colorSpace: "Linear",
  unityVersion: "6000.4.6f1",
  graphicsApi: "Metal",
  theme: "dark-theme",
  font: "FiraMono-Regular",
  png: `${scenario}.png`,
  metrics: {
    width: WIDTH,
    height: HEIGHT,
    distinctColors: 42,
    backgroundFraction: 0.62,
    backgroundRgb: "#1B1B1E",
    pngBytes: 0
  },
  violations: [],
  ...overrides
});

/** Writes a self-consistent capture run: <run>/<stamp>/<scenario>.{png,manifest.json}. */
function writeRun(root, entries) {
  const runDir = path.join(root, "run");
  const stampDir = path.join(runDir, "2026-09-22T04-00-00-000Z");
  fs.mkdirSync(stampDir, { recursive: true });
  for (const [scenario, image, overrides] of entries) {
    const m = manifest(scenario, overrides);
    const png = encodePng(WIDTH, HEIGHT, image);
    m.metrics.pngBytes = png.length;
    fs.writeFileSync(path.join(stampDir, `${scenario}.png`), png);
    fs.writeFileSync(
      path.join(stampDir, `${scenario}.manifest.json`),
      JSON.stringify(m, null, 2)
    );
  }
  return runDir;
}

function createStore(root) {
  const store = path.join(root, "Baselines~");
  fs.mkdirSync(store, { recursive: true });
  return store;
}

describe("collectRunManifests", () => {
  it("finds requested scenarios recursively and prefers the newest copy", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-run-"));
    try {
      const runDir = writeRun(root, [
        ["CapturesSurfaceA", fixtureImage(), {}],
        ["CapturesSurfaceB", fixtureImage(), {}]
      ]);
      // A stale duplicate of SurfaceA in a second directory must lose to the newer file.
      const older = path.join(runDir, "2026-09-22T03-00-00-000Z");
      fs.mkdirSync(older, { recursive: true });
      fs.writeFileSync(path.join(older, "CapturesSurfaceA.png"), encodePng(WIDTH, HEIGHT, fixtureImage()));
      fs.writeFileSync(path.join(older, "CapturesSurfaceA.manifest.json"), "{}");
      fs.utimesSync(path.join(older, "CapturesSurfaceA.manifest.json"), new Date(1_000), new Date(1_000));

      const found = collectRunManifests(runDir, [...SCENARIOS]);
      assert.deepEqual(
        found.map((entry) => entry.scenario),
        SCENARIOS
      );
      assert.ok(found[0].entryPath.includes("04-00-00"));
      assert.equal(collectRunManifests(runDir, ["Missing"])[0], null);    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it("ignores the palette repeat and negative-control manifests", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-run-"));
    try {
      const runDir = writeRun(root, [
        ["CapturesSurfaceA", fixtureImage(), {}],
        ["CapturesSurfaceA-repeat", fixtureImage(), {}],
        ["BlankRenderFailsBounds", Buffer.alloc(WIDTH * HEIGHT * 4), { complete: false, violations: ["blank"] }]
      ]);
      assert.deepEqual(
        collectRunManifests(runDir, [...SCENARIOS]).map((entry) =>
          entry === null ? null : entry.scenario
        ),
        ["CapturesSurfaceA", null]
      );
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});

describe("promoteRun", () => {
  it("seeds a new store with valid captures", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-promote-"));
    try {
      const runDir = writeRun(root, [
        ["CapturesSurfaceA", fixtureImage(), {}],
        ["CapturesSurfaceB", fixtureImage(1), {}]
      ]);
      const store = createStore(root);
      const { written, problems } = promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: [...SCENARIOS]
      });
      assert.deepEqual(problems, []);
      assert.deepEqual(
        written.map((entry) => entry.verdict),
        ["new", "new"]
      );
      const index = readBaselineIndex(store, "6000.4.6f1-metal-linear-32x24-scale1");
      assert.deepEqual(Object.keys(index.scenarios), SCENARIOS);
      assert.equal(index.scenarios.CapturesSurfaceA.theme, "dark-theme");
      assert.equal(
        fs.statSync(path.join(store, "6000.4.6f1-metal-linear-32x24-scale1", "CapturesSurfaceB.png")).size,
        index.scenarios.CapturesSurfaceB.metrics.pngBytes
      );
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it("replaces a baseline and reports identical or changed verdicts", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-promote-"));
    try {
      const runDir = writeRun(root, [
        ["CapturesSurfaceA", fixtureImage(), {}],
        ["CapturesSurfaceB", fixtureImage(), {}]
      ]);
      const store = createStore(root);
      const promote = () =>
        promoteRun({
          runDir,
          storeDir: store,
          artifactsRoot: path.join(root, "artifacts"),
          scenarioNames: [...SCENARIOS]
        });
      assert.deepEqual(promote().problems, []);

      // Same pixels: identical, no artifacts.
      const identical = promote();
      assert.deepEqual(
        identical.written.map((entry) => entry.verdict),
        ["identical", "identical"]
      );
      assert.ok(identical.written.every((entry) => entry.artifactsDir === undefined));

      // Different pixels: changed with a failing gate verdict, diff
      // artifacts written for review; the promotion itself still lands.
      const stampDir = path.join(runDir, "2026-09-22T04-00-00-000Z");
      const png = encodePng(WIDTH, HEIGHT, fixtureImage(3));
      fs.writeFileSync(path.join(stampDir, "CapturesSurfaceA.png"), png);
      const changedManifest = manifest("CapturesSurfaceA");
      changedManifest.metrics.pngBytes = png.length;
      fs.writeFileSync(
        path.join(stampDir, "CapturesSurfaceA.manifest.json"),
        JSON.stringify(changedManifest, null, 2)
      );

      const changed = promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: [...SCENARIOS]
      });
      assert.deepEqual(changed.problems, []);
      assert.equal(changed.written[0].verdict, "changed");
      assert.equal(changed.written[0].gate, false);
      assert.ok(fs.existsSync(changed.written[0].artifactsDir));
      assert.ok(fs.existsSync(path.join(changed.written[0].artifactsDir, "diff.png")));
      assert.ok(fs.existsSync(path.join(changed.written[0].artifactsDir, "report.json")));
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it("refuses incomplete captures, missing scenarios, and undecodable PNGs", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-promote-"));
    try {
      const store = createStore(root);
      const missing = promoteRun({
        runDir: path.join(root, "missing-run"),
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: [...SCENARIOS]
      });
      assert.equal(missing.written.length, 0);
      assert.equal(missing.problems.length, 1);
      assert.ok(missing.problems[0].includes("does not exist"));

      const incomplete = writeRun(root, [
        ["CapturesSurfaceA", fixtureImage(), { complete: false, violations: ["blank"] }]
      ]);
      const rejected = promoteRun({
        runDir: incomplete,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: [...SCENARIOS]
      });
      assert.equal(rejected.written.length, 0);
      assert.equal(rejected.problems.length, 2);
      assert.ok(rejected.problems.some((problem) => problem.includes("capture incomplete")));
      assert.ok(rejected.problems.some((problem) => problem.includes("no capture found")));

      const corrupt = writeRun(root, [["CapturesSurfaceA", fixtureImage(), {}]]);
      const stampDir = path.join(corrupt, "2026-09-22T04-00-00-000Z");
      fs.writeFileSync(path.join(stampDir, "CapturesSurfaceA.png"), Buffer.from("not a png"));
      const undecodable = promoteRun({
        runDir: corrupt,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: ["CapturesSurfaceA"]
      });
      assert.equal(undecodable.written.length, 0);
      assert.ok(undecodable.problems[0].includes("undecodable PNG"));
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it("flags a replacement whose pixels drift outside the gate", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-promote-"));
    try {
      const runDir = writeRun(root, [["CapturesSurfaceA", fixtureImage(), {}]]);
      const store = createStore(root);
      promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: ["CapturesSurfaceA"]
      });

      // Replace with content that violates the budget on most pixels.
      const drifted = fixtureImage();
      for (let offset = 0; offset < drifted.length; offset += 4) drifted[offset] = 255 - drifted[offset];
      const stampDir = path.join(root, "run", "drift");
      fs.mkdirSync(stampDir, { recursive: true });
      fs.writeFileSync(path.join(stampDir, "CapturesSurfaceA.png"), encodePng(WIDTH, HEIGHT, drifted));
      const m = manifest("CapturesSurfaceA");
      m.metrics.pngBytes = encodePng(WIDTH, HEIGHT, drifted).length;
      fs.writeFileSync(
        path.join(stampDir, "CapturesSurfaceA.manifest.json"),
        JSON.stringify(m)
      );
      // Pin mtimes so "newest wins" cannot flake on a coarse-mtime filesystem.
      fs.utimesSync(
        path.join(runDir, "2026-09-22T04-00-00-000Z", "CapturesSurfaceA.manifest.json"),
        new Date(1_000),
        new Date(1_000)
      );
      fs.utimesSync(path.join(stampDir, "CapturesSurfaceA.manifest.json"), new Date(2_000), new Date(2_000));

      const result = promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: ["CapturesSurfaceA"]
      });
      assert.deepEqual(result.problems, []);
      assert.equal(result.written[0].verdict, "changed");
      assert.equal(result.written[0].gate, false);
      assert.ok(fs.existsSync(result.written[0].artifactsDir));

      // Cross-environment promotion is refused outright: same environment
      // key (theme is not part of it), different theme provenance.
      const foreign = manifest("CapturesSurfaceA", { theme: "light-theme" });
      foreign.metrics.pngBytes = encodePng(WIDTH, HEIGHT, drifted).length;
      const foreignDir = path.join(root, "run", "foreign");
      fs.mkdirSync(foreignDir, { recursive: true });
      fs.writeFileSync(path.join(foreignDir, "CapturesSurfaceA.png"), encodePng(WIDTH, HEIGHT, drifted));
      fs.writeFileSync(path.join(foreignDir, "CapturesSurfaceA.manifest.json"), JSON.stringify(foreign));
      fs.utimesSync(path.join(foreignDir, "CapturesSurfaceA.manifest.json"), new Date(3_000), new Date(3_000));
      const refused = promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: ["CapturesSurfaceA"]
      });
      assert.equal(refused.written.length, 0);
      assert.ok(
        refused.problems.some((problem) => problem.includes("refusing to promote across provenance"))
      );

      // A tampered body (scenario disagreeing with its filename) is refused,
      // so a hostile manifest can never rename a write outside the store.
      const impostor = manifest("CapturesSurfaceB");
      impostor.metrics.pngBytes = encodePng(WIDTH, HEIGHT, drifted).length;
      fs.writeFileSync(
        path.join(foreignDir, "CapturesSurfaceA.manifest.json"),
        JSON.stringify(impostor)
      );
      fs.utimesSync(path.join(foreignDir, "CapturesSurfaceA.manifest.json"), new Date(4_000), new Date(4_000));
      const tampered = promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: ["CapturesSurfaceA"]
      });
      assert.equal(tampered.written.length, 0);
      assert.ok(tampered.problems.some((problem) => problem.includes("does not match its filename")));

      // A manifest without theme/font is refused instead of poisoning the store.
      const themeless = manifest("CapturesSurfaceA");
      delete themeless.theme;
      themeless.metrics.pngBytes = encodePng(WIDTH, HEIGHT, drifted).length;
      fs.writeFileSync(
        path.join(foreignDir, "CapturesSurfaceA.manifest.json"),
        JSON.stringify(themeless)
      );
      fs.utimesSync(path.join(foreignDir, "CapturesSurfaceA.manifest.json"), new Date(5_000), new Date(5_000));
      const poisoned = promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: ["CapturesSurfaceA"]
      });
      assert.equal(poisoned.written.length, 0);
      assert.ok(poisoned.problems.some((problem) => problem.includes("theme must be")));

      // A manifest without colorSpace would derive a garbage env key; refuse it.
      const colorless = manifest("CapturesSurfaceA");
      delete colorless.colorSpace;
      colorless.metrics.pngBytes = encodePng(WIDTH, HEIGHT, drifted).length;
      fs.writeFileSync(
        path.join(foreignDir, "CapturesSurfaceA.manifest.json"),
        JSON.stringify(colorless)
      );
      fs.utimesSync(path.join(foreignDir, "CapturesSurfaceA.manifest.json"), new Date(5_000), new Date(5_000));
      const colorlessResult = promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: ["CapturesSurfaceA"]
      });
      assert.equal(colorlessResult.written.length, 0);
      assert.ok(colorlessResult.problems.some((problem) => problem.includes("colorSpace must be")));
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it("refuses a corrupt baseline index entry instead of crashing", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-promote-"));
    try {
      const runDir = writeRun(root, [["CapturesSurfaceA", fixtureImage(), {}]]);
      const store = createStore(root);
      const envKey = "6000.4.6f1-metal-linear-32x24-scale1";
      const index = {
        version: 1,
        environment: {
          unityVersion: "6000.4.6f1",
          graphicsApi: "Metal",
          colorSpace: "Linear",
          resolution: { width: WIDTH, height: HEIGHT },
          logicalScale: 1
        },
        scenarios: { CapturesSurfaceA: null }
      };
      fs.mkdirSync(path.join(store, envKey), { recursive: true });
      writeBaselineIndex(store, envKey, index);
      fs.writeFileSync(
        path.join(store, envKey, "CapturesSurfaceA.png"),
        encodePng(WIDTH, HEIGHT, fixtureImage())
      );

      const result = promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: ["CapturesSurfaceA"]
      });
      assert.equal(result.written.length, 0);
      assert.ok(result.problems.some((problem) => problem.includes("entry is corrupt")));
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it("treats an orphaned baseline PNG without an index entry as new", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-promote-"));
    try {
      const runDir = writeRun(root, [["CapturesSurfaceA", fixtureImage(), {}]]);
      const store = createStore(root);
      const envKey = "6000.4.6f1-metal-linear-32x24-scale1";
      const index = {
        version: 1,
        environment: {
          unityVersion: "6000.4.6f1",
          graphicsApi: "Metal",
          colorSpace: "Linear",
          resolution: { width: WIDTH, height: HEIGHT },
          logicalScale: 1
        },
        scenarios: {}
      };
      fs.mkdirSync(path.join(store, envKey), { recursive: true });
      writeBaselineIndex(store, envKey, index);
      fs.writeFileSync(
        path.join(store, envKey, "CapturesSurfaceA.png"),
        encodePng(WIDTH, HEIGHT, fixtureImage())
      );

      const result = promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: ["CapturesSurfaceA"]
      });
      assert.deepEqual(result.problems, []);
      assert.equal(result.written[0].verdict, "new");
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});

describe("compareT4Baselines", () => {
  const ENV = "6000.4.6f1-metal-linear-32x24-scale1";

  /** Seeds <repoRoot>/Tests/Runtime/Capture/Baselines~ via a promote, then runs the gate. */
  function setup(root, baselineImage) {
    const store = path.join(root, "Tests", "Runtime", "Capture", "Baselines~");
    fs.mkdirSync(store, { recursive: true });
    const runDir = writeRun(root, [["CapturesSurfaceA", baselineImage, {}]]);
    const promote = promoteRun({
      runDir,
      storeDir: store,
      artifactsRoot: path.join(root, "artifacts"),
      scenarioNames: ["CapturesSurfaceA"]
    });
    assert.deepEqual(promote.problems, []);
    return { store, runDir };
  }

  function gate(root, store, actualImage, manifestOverrides = {}) {
    const png = encodePng(WIDTH, HEIGHT, actualImage);
    const base = manifest("CapturesSurfaceA");
    const captureManifest = {
      ...base,
      metrics: { ...base.metrics, pngBytes: png.length },
      ...manifestOverrides
    };
    const captureDir = path.join(root, "capture");
    fs.mkdirSync(captureDir, { recursive: true });
    fs.writeFileSync(path.join(captureDir, "CapturesSurfaceA.png"), png);
    const manifestPath = path.join(captureDir, "CapturesSurfaceA.manifest.json");
    fs.writeFileSync(manifestPath, JSON.stringify(captureManifest));
    const problems = [];
    compareT4Baselines(
      { repoRoot: root },
      [{ scenario: "CapturesSurfaceA", manifest: captureManifest, manifestPath }],
      problems
    );
    return problems;
  }

  it("approves a byte-identical capture and flags pixel drift with artifacts", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-gate-"));
    try {
      const { store } = setup(root, fixtureImage());
      assert.deepEqual(gate(root, store, fixtureImage()), []);

      const problems = gate(root, store, fixtureImage(9));
      assert.equal(problems.length, 1);
      assert.ok(problems[0].includes("pixels differ from the baseline"));
      assert.ok(problems[0].includes("review artifacts at"));
      assert.ok(fs.readdirSync(path.join(root, ".artifacts", "t11")).length > 0);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it("reports pending without a store or environment, and never fails there", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-gate-"));
    try {
      const png = encodePng(WIDTH, HEIGHT, fixtureImage());
      const base = manifest("CapturesSurfaceA");
      base.metrics.pngBytes = png.length;
      const problems = [];
      compareT4Baselines(
        { repoRoot: root },
        [{ scenario: "CapturesSurfaceA", manifest: base, manifestPath: path.join(root, "x.json") }],
        problems
      );
      assert.deepEqual(problems, []);

      const { store } = setup(root, fixtureImage());
      const foreign = { ...base, graphicsApi: "Vulkan" };
      const foreignProblems = [];
      compareT4Baselines(
        { repoRoot: root },
        [{ scenario: "CapturesSurfaceA", manifest: foreign, manifestPath: path.join(root, "x.json") }],
        foreignProblems
      );
      assert.deepEqual(foreignProblems, []);
      assert.ok(fs.existsSync(store));
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it("fails loudly on a corrupted index instead of reading it as pending", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-gate-"));
    try {
      const { store } = setup(root, fixtureImage());
      fs.writeFileSync(path.join(store, ENV, "index.json"), "null");
      assert.ok(gate(root, store, fixtureImage())[0].includes("baseline compare failed"));

      fs.writeFileSync(path.join(store, ENV, "index.json"), "{}");
      assert.ok(gate(root, store, fixtureImage())[0].includes("baseline compare failed"));

      // Entry present but the PNG gone must fail too, not pass as pending.
      fs.rmSync(path.join(store, ENV, "CapturesSurfaceA.png"));
      assert.ok(gate(root, store, fixtureImage())[0].includes("baseline compare failed"));
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it("labels a provenance mismatch as provenance, not pixels", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-gate-"));
    try {
      const { store } = setup(root, fixtureImage());
      const problems = gate(root, store, fixtureImage(), { theme: "light-theme" });
      assert.equal(problems.length, 1);
      assert.ok(problems[0].includes("baseline provenance mismatch"));
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});

describe("checkBaselineStore", () => {
  it("approves a store that covers exactly the pinned scenarios", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-check-"));
    try {
      const runDir = writeRun(root, [
        ["CapturesSurfaceA", fixtureImage(), {}],
        ["CapturesSurfaceB", fixtureImage(), {}]
      ]);
      const store = createStore(root);
      promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: [...SCENARIOS]
      });
      assert.deepEqual(checkBaselineStore(store, SCENARIOS), []);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it("rejects a missing or empty store", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-check-"));
    try {
      assert.equal(checkBaselineStore(path.join(root, "nope"), SCENARIOS).length, 1);
      const store = createStore(root);
      assert.equal(checkBaselineStore(store, SCENARIOS).length, 1);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it("catches a renamed environment directory (the lookup key must match)", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-check-"));
    try {
      const runDir = writeRun(root, [
        ["CapturesSurfaceA", fixtureImage(), {}],
        ["CapturesSurfaceB", fixtureImage(), {}]
      ]);
      const store = createStore(root);
      const envKey = "6000.4.6f1-metal-linear-32x24-scale1";
      promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: [...SCENARIOS]
      });
      fs.renameSync(path.join(store, envKey), path.join(store, "renamed-env"));
      const problems = checkBaselineStore(store, SCENARIOS);
      assert.ok(
        problems.some((problem) => problem.includes("does not match its environment")),
        JSON.stringify(problems)
      );
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it("enforces coverage: gaps, strays, corrupt PNGs, and index lies", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-check-"));
    try {
      const runDir = writeRun(root, [
        ["CapturesSurfaceA", fixtureImage(), {}],
        ["CapturesSurfaceB", fixtureImage(), {}]
      ]);
      const store = createStore(root);
      const envKey = "6000.4.6f1-metal-linear-32x24-scale1";
      promoteRun({
        runDir,
        storeDir: store,
        artifactsRoot: path.join(root, "artifacts"),
        scenarioNames: [...SCENARIOS]
      });

      // Gap: SurfaceB missing from coverage.
      const gap = checkBaselineEnvironment(store, envKey, ["CapturesSurfaceA"]);
      assert.ok(gap.problems.some((problem) => problem.includes("unexpected baseline scenario")));
      const gapStore = checkBaselineStore(store, ["CapturesSurfaceA", "CapturesSurfaceC"]);
      assert.ok(gapStore.some((problem) => problem.includes("missing baseline scenario CapturesSurfaceC")));

      // Stray PNG referenced by the index but not on disk.
      const index = readBaselineIndex(store, envKey);
      index.scenarios.CapturesSurfaceB.png = "ghost.png";
      writeBaselineIndex(store, envKey, index);
      const stray = checkBaselineEnvironment(store, envKey, SCENARIOS);
      assert.ok(stray.problems.some((problem) => problem.includes("missing PNG ghost.png")));

      // Corrupt baseline PNG (index restored first).
      const pngBytes = fs.readFileSync(path.join(store, envKey, "CapturesSurfaceB.png"));
      index.scenarios.CapturesSurfaceB.png = "CapturesSurfaceB.png";
      writeBaselineIndex(store, envKey, index);
      fs.writeFileSync(path.join(store, envKey, "CapturesSurfaceB.png"), Buffer.from("junk"));
      const corrupt = checkBaselineEnvironment(store, envKey, SCENARIOS);
      assert.ok(corrupt.problems.some((problem) => problem.includes("undecodable PNG")));

      // Index lying about the byte size.
      fs.writeFileSync(path.join(store, envKey, "CapturesSurfaceB.png"), pngBytes);
      const restored = readBaselineIndex(store, envKey);
      restored.scenarios.CapturesSurfaceB.metrics.pngBytes = 1;
      writeBaselineIndex(store, envKey, restored);
      const lie = checkBaselineEnvironment(store, envKey, SCENARIOS);
      assert.ok(lie.problems.some((problem) => problem.includes("png bytes")));
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});
