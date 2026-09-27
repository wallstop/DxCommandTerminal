import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";
import {
  T4_ALL_SCENARIOS,
  T4_DEFAULT_SCENARIOS,
  T4_EXPECTED_INCOMPLETE,
  T4_VARIANT_SCENARIOS,
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

describe("T4 scenario registries", () => {
  it("keeps the env-variant registry disjoint from the pinned canon", () => {
    assert.ok(T4_VARIANT_SCENARIOS.length > 0);
    for (const name of T4_VARIANT_SCENARIOS) {
      assert.ok(!T4_DEFAULT_SCENARIOS.includes(name), `${name} must stay out of the canon`);
    }
    assert.deepEqual(T4_ALL_SCENARIOS, [...T4_DEFAULT_SCENARIOS, ...T4_VARIANT_SCENARIOS]);
  });

  it("never baselines an expected-incomplete scenario", () => {
    for (const name of T4_EXPECTED_INCOMPLETE) {
      assert.ok(
        !T4_ALL_SCENARIOS.includes(name),
        `${name} fails its bounds by design and must stay out of the baseline registries`
      );
    }
  });
});

describe("parseT4Scenarios", () => {
  it("defaults to every capture scenario (canon plus env variants)", () => {
    assert.deepEqual(parseT4Scenarios(undefined), [...T4_ALL_SCENARIOS]);
  });

  it("treats null as the default too", () => {
    assert.deepEqual(parseT4Scenarios(null), [...T4_ALL_SCENARIOS]);
  });

  it("splits and trims a comma-separated list", () => {
    assert.deepEqual(parseT4Scenarios(" A , b ,C"), ["A", "b", "C"]);
  });

  it("is idempotent: accepts an already-parsed array", () => {
    const once = parseT4Scenarios("A,B");
    assert.deepEqual(parseT4Scenarios(once), ["A", "B"]);
    assert.deepEqual(parseT4Scenarios([" A ", "b"]), ["A", "b"]);
  });

  it("rejects non-string scalars cleanly", () => {
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
  // A capture run stamps its directory `yyyy-MM-ddTHH-mm-ss-fffZ` in UTC, and
  // that stamp is what scopes collection to the run at hand.
  const stamp = (iso) => iso.replace(/[-:]/gu, "-").replace(".", "-");

  it("collects the runs at or after the cutoff, one newest copy per scenario", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t4-manifests-"));
    try {
      const write = (directory, name, mtimeMs) => {
        const full = path.join(root, directory);
        fs.mkdirSync(full, { recursive: true });
        const filePath = path.join(full, name);
        fs.writeFileSync(filePath, "{}");
        fs.utimesSync(filePath, new Date(mtimeMs), new Date(mtimeMs));
      };
      const older = stamp("2026-09-27T01:00:00.000Z");
      const previous = stamp("2026-09-27T01:14:40.000Z");
      const current = stamp("2026-09-27T01:14:45.854Z");
      const cutoff = Date.parse("2026-09-27T01:14:45.000Z");

      write(older, "CapturesTerminalSmallSurface.manifest.json", 1_000);
      write(previous, "CapturesTerminalSmallSurface.manifest.json", 2_000);
      write(previous, "CapturesCommandPaletteSurface.manifest.json", 3_000);
      write(current, "CapturesTerminalSmallSurface.manifest.json", 9_000);
      write(current, "CapturesCommandPaletteSurface.manifest.json", 8_000);
      write(current, "not-a-manifest.txt", 9_500);
      fs.mkdirSync(path.join(root, "run-1"), { recursive: true });
      fs.writeFileSync(path.join(root, "run-1", "a.manifest.json"), "{}");

      // The previous run is excluded even though its file mtimes are inside a
      // naive window, and the current run's copy of each scenario is kept.
      const collected = collectT4ManifestPaths(root, cutoff);
      assert.deepEqual(
        collected.map((entry) => path.basename(entry.filePath)),
        [
          "CapturesCommandPaletteSurface.manifest.json",
          "CapturesTerminalSmallSurface.manifest.json"
        ]
      );
      assert.deepEqual(
        collected.map((entry) => entry.mtimeMs),
        [8_000, 9_000]
      );

      // No cutoff still yields one entry per scenario, and never a directory
      // that is not a run directory.
      assert.deepEqual(collectT4ManifestPaths(root, 0).length, 2);
      // The cutoff is an epoch instant, so a time after the last run collects
      // nothing.
      assert.deepEqual(collectT4ManifestPaths(root, Date.parse("2026-09-27T02:00:00.000Z")), []);
      assert.deepEqual(collectT4ManifestPaths(path.join(root, "missing"), 0), []);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});
