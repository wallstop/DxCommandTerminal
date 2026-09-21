import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";
import {
  T4_DEFAULT_SCENARIOS,
  T4_EXPECTED_INCOMPLETE,
  parseT4Scenarios,
  validateT4Manifest,
  collectT4ManifestPaths
} from "../mcp/unity-mcp.mjs";

const validManifest = (overrides = {}) => ({
  scenario: "CapturesTerminalSmallSurface",
  capturedUtc: "2026-09-21T20:00:00.0000000Z",
  complete: true,
  resolution: { width: 397, height: 489 },
  logicalScale: 1,
  colorSpace: "Linear",
  unityVersion: "6000.4.6f1",
  graphicsApi: "Metal",
  theme: "dark-theme",
  font: "FiraMono-Regular",
  revision: "abc123",
  png: "CapturesTerminalSmallSurface.png",
  metrics: {
    width: 397,
    height: 489,
    distinctColors: 42,
    backgroundFraction: 0.62,
    backgroundRgb: "1B1B1E",
    pngBytes: 4_096
  },
  bounds: { minDistinctColors: 8, minBackgroundFraction: 0.3, maxBackgroundFraction: 0.995 },
  violations: [],
  ...overrides
});

describe("parseT4Scenarios", () => {
  it("defaults to the four terminal surface scenarios", () => {
    assert.deepEqual(parseT4Scenarios(undefined), [...T4_DEFAULT_SCENARIOS]);
  });

  it("splits and trims a comma-separated list", () => {
    assert.deepEqual(parseT4Scenarios(" A , b ,C"), ["A", "b", "C"]);
  });

  it("is idempotent: accepts an already-parsed array", () => {
    const once = parseT4Scenarios("A,B");
    assert.deepEqual(parseT4Scenarios(once), ["A", "B"]);
    assert.deepEqual(parseT4Scenarios([" A ", "b"]), ["A", "b"]);
  });

  it("treats null as the default and rejects non-string scalars cleanly", () => {
    assert.deepEqual(parseT4Scenarios(null), [...T4_DEFAULT_SCENARIOS]);
    assert.throws(() => parseT4Scenarios(42), /Invalid scenario name/);
  });

  it("rejects empty lists and malformed names", () => {
    assert.throws(() => parseT4Scenarios("  "), /at least one scenario/);
    assert.throws(() => parseT4Scenarios("ok,9bad"), /Invalid scenario name/);
    assert.throws(() => parseT4Scenarios("has space"), /Invalid scenario name/);
    assert.throws(() => parseT4Scenarios(["ok", "9bad"]), /Invalid scenario name/);
  });
});

describe("validateT4Manifest", () => {
  it("accepts a complete manifest with no violations", () => {
    assert.deepEqual(validateT4Manifest(validManifest(), true), []);
  });

  it("rejects schema violations", () => {
    const problems = validateT4Manifest({ scenario: "x" }, true);
    for (const expected of [
      "missing capturedUtc",
      "resolution must record",
      "metrics must record",
      "violations must be"
    ]) {
      assert.ok(
        problems.some((problem) => problem.startsWith(expected)),
        `expected a problem starting with '${expected}', got ${JSON.stringify(problems)}`
      );
    }
  });

  it("rejects a complete manifest that still lists violations", () => {
    const manifest = validManifest({ violations: ["distinctColors 1 < min 8"] });
    assert.deepEqual(
      validateT4Manifest(manifest, true),
      ["complete manifest must have no violations"]
    );
  });

  it("rejects an incomplete capture when completeness is expected", () => {
    const manifest = validManifest({ complete: false, violations: ["blank"] });
    assert.deepEqual(
      validateT4Manifest(manifest, true),
      ["capture incomplete: blank"]
    );
  });

  it("accepts the blank negative control only when it fails its bounds", () => {
    const scenario = T4_EXPECTED_INCOMPLETE[0];
    const passing = validManifest({
      scenario,
      complete: true,
      png: `${scenario}.png`
    });
    assert.deepEqual(
      validateT4Manifest(passing, false),
      ["expected an incomplete manifest (negative control must fail bounds)"]
    );

    const failing = validManifest({
      scenario,
      complete: false,
      png: `${scenario}.png`,
      violations: ["distinctColors 1 < min 8 (blank or nearly blank render)"]
    });
    assert.deepEqual(validateT4Manifest(failing, false), []);
  });
});

describe("collectT4ManifestPaths", () => {
  it("collects manifests newer than the cutoff, oldest first", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t4-manifests-"));
    try {
      const write = (stamp, name, mtimeMs) => {
        const directory = path.join(root, stamp);
        fs.mkdirSync(directory, { recursive: true });
        const filePath = path.join(directory, name);
        fs.writeFileSync(filePath, "{}");
        fs.utimesSync(filePath, new Date(mtimeMs), new Date(mtimeMs));
      };
      write("run-1", "a.manifest.json", 1_000);
      write("run-2", "b.manifest.json", 3_000);
      write("run-3", "not-a-manifest.txt", 5_000);

      assert.deepEqual(collectT4ManifestPaths(root, 500), [
        path.join(root, "run-1", "a.manifest.json"),
        path.join(root, "run-2", "b.manifest.json")
      ]);
      assert.deepEqual(collectT4ManifestPaths(root, 2_000), [
        path.join(root, "run-2", "b.manifest.json")
      ]);
      assert.deepEqual(collectT4ManifestPaths(root, 10_000), []);
      assert.deepEqual(collectT4ManifestPaths(path.join(root, "missing"), 0), []);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});
