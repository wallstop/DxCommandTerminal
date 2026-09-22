#!/usr/bin/env node
/*
    T11 baseline-update command (PLAN.md T11: regeneration behind an
    explicit command + human review). Promotes one T04 capture run into the
    committed baseline store after the captures passed their manifests:

        npm run t11:update -- --run .artifacts/t4

    - Scans --run recursively for <scenario>.manifest.json (a t4:capture
      writes one directory per test; the newest copy of each scenario wins,
      ties broken by the lexicographically latest path).
    - Refuses incomplete or schema-invalid captures, undecodable PNGs,
      scenario/manifest identity mismatches, and cross-provenance
      promotions; nothing is written unless the whole run promotes cleanly.
    - When a baseline already exists, compares first and reports the verdict
      (identical / changed with a gate PASS|FAIL), writing diff and overlay
      artifacts under .artifacts/t11/ whenever pixels differ at all. Nothing
      is auto-approved: the operator reviews the printed verdicts and the
      committed PNGs.

    Pure logic lives in promoteRun; this CLI owns I/O and exit codes.
*/
import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import {
  T4_DEFAULT_SCENARIOS,
  parseT4Scenarios,
  validateT4Manifest
} from "../mcp/unity-mcp.mjs";
import {
  BASELINE_STORE_VERSION,
  compareScenario,
  decodePng,
  emitComparisonArtifacts,
  environmentKey,
  environmentProvenance,
  provenanceOf,
  readBaselineIndex,
  writeBaselineIndex
} from "./comparator.mjs";

const DEFAULT_STORE = path.resolve(
  fileURLToPath(new URL("../../../Tests/Runtime/Capture/Baselines~", import.meta.url))
);
const DEFAULT_ARTIFACTS = path.resolve(
  fileURLToPath(new URL("../../../.artifacts/t11", import.meta.url))
);

/**
 * Newest manifest for each scenario under runDir (recursive, regular files
 * only). Ties on mtime resolve to the lexicographically latest path.
 */
export function collectRunManifests(runDir, scenarioNames) {
  if (!fs.existsSync(runDir)) throw new Error(`run directory does not exist: ${runDir}`);
  const found = new Map();
  const stack = [runDir];
  while (stack.length > 0) {
    const directory = stack.pop();
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const entryPath = path.join(directory, entry.name);
      if (entry.isDirectory()) {
        stack.push(entryPath);
        continue;
      }
      if (!entry.isFile()) continue;
      if (!entry.name.endsWith(".manifest.json")) continue;
      const scenario = entry.name.slice(0, -".manifest.json".length);
      if (!scenarioNames.includes(scenario)) continue;
      const candidate = { scenario, entryPath, mtimeMs: fs.statSync(entryPath).mtimeMs };
      const previous = found.get(scenario);
      if (
        previous === undefined ||
        previous.mtimeMs < candidate.mtimeMs ||
        (previous.mtimeMs === candidate.mtimeMs && previous.entryPath < candidate.entryPath)
      ) {
        found.set(scenario, candidate);
      }
    }
  }
  return scenarioNames.map((name) => found.get(name) ?? null);
}

function baselineEntryFrom(manifest, pngBytes) {
  return {
    png: `${manifest.scenario}.png`,
    theme: manifest.theme,
    font: manifest.font,
    capturedUtc: manifest.capturedUtc,
    metrics: {
      distinctColors: manifest.metrics.distinctColors,
      backgroundFraction: manifest.metrics.backgroundFraction,
      backgroundRgb: manifest.metrics.backgroundRgb,
      pngBytes
    }
  };
}

function indexEnvironmentFrom(provenance) {
  return {
    unityVersion: provenance.unityVersion,
    graphicsApi: provenance.graphicsApi,
    colorSpace: provenance.colorSpace,
    resolution: { width: provenance.width, height: provenance.height },
    logicalScale: provenance.logicalScale
  };
}

/**
 * Promotes a capture run into the baseline store.
 * Returns { written: [{scenario, envKey, verdict, gate, artifactsDir}], problems }.
 * `verdict` is "new", "identical", or "changed"; `gate` records whether the
 * replacement still satisfies the pixel gate against the previous baseline
 * (undefined for new). Changed pixels write diff artifacts for review -
 * replacing a baseline is a deliberate, reviewed act, so pixel drift is
 * reported loudly but never fails the promotion; invalid captures and
 * provenance mismatches do, because they would corrupt the store.
 * Writes are buffered and applied only when the whole run promotes cleanly.
 */
export function promoteRun({ runDir, storeDir, artifactsRoot, scenarioNames }) {
  const problems = [];
  const written = [];
  const pending = [];
  const indexes = new Map();
  let manifests;
  try {
    manifests = collectRunManifests(runDir, scenarioNames);
  } catch (error) {
    return { written, problems: [error.message] };
  }

  for (let position = 0; position < manifests.length; ++position) {
    const found = manifests[position];
    if (found === null) {
      problems.push(`no capture found for scenario ${scenarioNames[position]}`);
      continue;
    }

    let manifest;
    try {
      manifest = JSON.parse(fs.readFileSync(found.entryPath, "utf8"));
    } catch (error) {
      problems.push(`${found.entryPath}: unreadable (${error.message})`);
      continue;
    }
    // The filename decides what gets promoted; the JSON body must agree, so a
    // tampered body can never rename a write outside the store.
    if (manifest.scenario !== found.scenario) {
      problems.push(
        `${found.entryPath}: manifest scenario ${JSON.stringify(manifest.scenario)} `
          + `does not match its filename (${found.scenario})`
      );
      continue;
    }
    const schemaProblems = validateT4Manifest(manifest, true);
    if (schemaProblems.length > 0) {
      problems.push(`${found.entryPath}: ${schemaProblems.join("; ")}`);
      continue;
    }
    if (typeof manifest.theme !== "string" || manifest.theme.length === 0) {
      problems.push(`${found.entryPath}: theme must be a non-empty string`);
      continue;
    }
    if (manifest.font !== null && (typeof manifest.font !== "string" || manifest.font.length === 0)) {
      problems.push(`${found.entryPath}: font must be a non-empty string or null`);
      continue;
    }
    if (typeof manifest.colorSpace !== "string" || manifest.colorSpace.length === 0) {
      problems.push(`${found.entryPath}: colorSpace must be a non-empty string`);
      continue;
    }

    const actualPngPath = path.join(path.dirname(found.entryPath), manifest.png);
    let actualPng;
    try {
      actualPng = fs.readFileSync(actualPngPath);
      decodePng(actualPng);
    } catch (error) {
      problems.push(`${found.scenario}: ${error.message}`);
      continue;
    }

    let envKey;
    try {
      envKey = environmentKey(provenanceOf(manifest));
    } catch (error) {
      problems.push(`${found.scenario}: ${error.message}`);
      continue;
    }
    let index = indexes.get(envKey);
    if (index === undefined) {
      try {
        index = readBaselineIndex(storeDir, envKey);
      } catch (error) {
        problems.push(`${found.scenario}: ${error.message}`);
        continue;
      }
      index ??= {
        version: BASELINE_STORE_VERSION,
        environment: indexEnvironmentFrom(provenanceOf(manifest)),
        scenarios: {}
      };
      indexes.set(envKey, index);
    }

    const verdict = { scenario: found.scenario, envKey, actualPng, index, manifest };
    const baselinePngPath = path.join(storeDir, envKey, `${found.scenario}.png`);
    const entry = index.scenarios[found.scenario];
    if (fs.existsSync(baselinePngPath) && entry !== undefined) {
      if (entry === null || typeof entry !== "object") {
        problems.push(
          `${found.scenario}: baseline index entry is corrupt (${JSON.stringify(entry)})`
        );
        continue;
      }
      const outcome = compareScenario(
        fs.readFileSync(baselinePngPath),
        actualPng,
        environmentProvenance(index.environment, entry.theme, entry.font),
        provenanceOf(manifest)
      );
      if (outcome.result === null) {
        // Cross-environment promotion would silently corrupt the store.
        problems.push(
          `${found.scenario}: refusing to promote across provenance: `
            + `${outcome.problems.join("; ")}`
        );
        continue;
      }
      verdict.verdict = outcome.result.identical ? "identical" : "changed";
      verdict.gate = outcome.pass;
      if (!outcome.result.identical) {
        verdict.artifactsInputs = {
          baselinePng: fs.readFileSync(baselinePngPath),
          outcome
        };
      }
    } else {
      verdict.verdict = "new";
    }
    pending.push(verdict);
  }

  if (problems.length === 0) {
    const stamp = new Date().toISOString().replace(/[:.]/g, "-");
    for (const verdict of pending) {
      const environmentDir = path.join(storeDir, verdict.envKey);
      fs.mkdirSync(environmentDir, { recursive: true });
      const baselinePngPath = path.join(environmentDir, `${verdict.scenario}.png`);
      fs.writeFileSync(baselinePngPath, verdict.actualPng);
      verdict.index.scenarios[verdict.scenario] = baselineEntryFrom(
        verdict.manifest,
        fs.statSync(baselinePngPath).size
      );
      if (verdict.artifactsInputs !== undefined) {
        verdict.artifactsDir = emitComparisonArtifacts(
          path.join(artifactsRoot, `update-${stamp}`, verdict.scenario),
          verdict.artifactsInputs.baselinePng,
          verdict.actualPng,
          verdict.artifactsInputs.outcome
        );
      }
      writeBaselineIndex(storeDir, verdict.envKey, verdict.index);
      written.push({
        scenario: verdict.scenario,
        envKey: verdict.envKey,
        verdict: verdict.verdict,
        gate: verdict.gate,
        artifactsDir: verdict.artifactsDir
      });
    }
  }

  return { written, problems };
}

function main(argv) {
  let runDir;
  let storeDir = DEFAULT_STORE;
  let artifactsRoot = DEFAULT_ARTIFACTS;
  let scenarios = T4_DEFAULT_SCENARIOS;
  const readValue = (token) => {
    const value = argv[++index];
    if (value === undefined) {
      console.error(`Missing value for ${token}`);
      process.exit(2);
    }
    return value;
  };
  let index = 0;
  while (index < argv.length) {
    const token = argv[index];
    if (token === "--run") {
      runDir = path.resolve(readValue(token));
    } else if (token === "--store") {
      storeDir = path.resolve(readValue(token));
    } else if (token === "--artifacts") {
      artifactsRoot = path.resolve(readValue(token));
    } else if (token === "--scenarios") {
      // Only the pinned scenarios may be baselined: anything else would be
      // promoted cleanly and then fail the store coverage gate in CI.
      const requested = parseT4Scenarios(readValue(token));
      const unknown = requested.filter((name) => !T4_DEFAULT_SCENARIOS.includes(name));
      if (unknown.length > 0) {
        console.error(
          `Unknown pinned scenario(s): ${unknown.join(", ")}. `
            + `Pinned list: ${T4_DEFAULT_SCENARIOS.join(", ")}`
        );
        return 2;
      }
      scenarios = requested;
    } else if (token === "--help" || token === "-h") {
      console.log(
        "Usage: node tooling~/scripts/t11/update.mjs --run DIR [--store DIR] "
          + "[--artifacts DIR] [--scenarios LIST]  (LIST must be a subset of the "
          + "pinned T04 scenarios)"
      );
      return 0;
    } else {
      console.error(`Unknown argument: ${token}`);
      return 2;
    }
    ++index;
  }
  if (runDir === undefined) {
    console.error("--run is required (the T04 capture directory, e.g. .artifacts/t4)");
    return 2;
  }

  let promotion;
  try {
    promotion = promoteRun({ runDir, storeDir, artifactsRoot, scenarioNames: [...scenarios] });
  } catch (error) {
    console.error(`t11:update failed: ${error.message}`);
    return 1;
  }

  for (const entry of promotion.written) {
    const gateText =
      entry.gate === undefined ? "" : entry.gate ? " [gate: pass]" : " [gate: FAIL]";
    const suffix = entry.artifactsDir !== undefined ? ` (artifacts: ${entry.artifactsDir})` : "";
    console.log(
      `  ${entry.verdict.padEnd(9)} ${entry.scenario} -> ${entry.envKey}${gateText}${suffix}`
    );
  }
  if (promotion.problems.length > 0) {
    console.error(
      `T11 update rejected (nothing written):\n  - ${promotion.problems.join("\n  - ")}`
    );
    return 1;
  }
  console.log(
    `T11 update wrote ${promotion.written.length} baseline(s). Review the verdicts and the `
      + "committed PNGs before landing."
  );
  return 0;
}

// Direct-run guard: compare filesystem paths, because import.meta.url
// percent-encodes the tilde in tooling~/ (fileURLToPath decodes it back).
if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  process.exitCode = main(process.argv.slice(2));
}
