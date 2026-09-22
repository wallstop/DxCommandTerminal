#!/usr/bin/env node
/*
    T11 baseline store integrity gate - Unity-free, CI-safe (PLAN.md T11:
    "CI gates only Unity-free integrity checks"). Validates every
    environment under Tests/Runtime/Capture/Baselines~/:
    - index schema and provenance fields,
    - scenario coverage (exactly the pinned T04 scenarios, no gaps, no strays),
    - PNG decodability and dimensions,
    - index-to-file agreement (recorded pngBytes equals the file on disk).

    Pure checks live in checkBaselineStore; this CLI owns I/O and exit codes.
*/
import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { T4_DEFAULT_SCENARIOS } from "../mcp/unity-mcp.mjs";
import {
  BASELINE_STORE_VERSION,
  decodePng,
  environmentKey,
  environmentProvenance,
  readBaselineIndex
} from "./comparator.mjs";

const DEFAULT_STORE = path.resolve(
  fileURLToPath(new URL("../../../Tests/Runtime/Capture/Baselines~", import.meta.url))
);

/**
 * Validates one environment directory. Returns { scenarios, problems }.
 * `expectedScenarios` defaults to the pinned T04 capture scenarios.
 */
export function checkBaselineEnvironment(storeDir, envKey, expectedScenarios) {
  const problems = [];
  const expected = expectedScenarios ?? T4_DEFAULT_SCENARIOS;
  let index;
  try {
    index = readBaselineIndex(storeDir, envKey);
  } catch (error) {
    return { scenarios: [], problems: [`index.json is unreadable (${error.message})`] };
  }
  if (index === null) return { scenarios: [], problems: ["missing index.json"] };
  if (index.version !== BASELINE_STORE_VERSION) {
    problems.push(`unsupported index version ${JSON.stringify(index.version)}`);
  }

  const environment = index.environment;
  if (environment === null || typeof environment !== "object") {
    problems.push("missing environment block");
  } else {
    for (const field of ["unityVersion", "graphicsApi", "colorSpace"]) {
      if (typeof environment[field] !== "string" || environment[field].length === 0) {
        problems.push(`environment.${field} must be a non-empty string`);
      }
    }
    const resolution = environment.resolution;
    if (
      resolution === null ||
      typeof resolution !== "object" ||
      !Number.isInteger(resolution.width) ||
      !Number.isInteger(resolution.height) ||
      resolution.width < 1 ||
      resolution.height < 1
    ) {
      problems.push("environment.resolution must record positive integer width/height");
    }
    if (typeof environment.logicalScale !== "number" || !(environment.logicalScale > 0)) {
      problems.push("environment.logicalScale must be a positive number");
    }
    // The directory name is the lookup key at capture time: a renamed or
    // hand-moved environment would silently disable the pixel gate.
    try {
      const expectedKey = environmentKey(
        environmentProvenance(environment, "theme-key-check", null)
      );
      if (expectedKey !== envKey) {
        problems.push(`directory name ${envKey} does not match its environment (${expectedKey})`);
      }
    } catch (error) {
      problems.push(`environment key derivation failed: ${error.message}`);
    }
  }

  const scenarios = index.scenarios;
  if (scenarios === null || typeof scenarios !== "object" || Array.isArray(scenarios)) {
    problems.push("missing scenarios block");
    return { scenarios: [], problems };
  }

  for (const name of expected) {
    if (!Object.hasOwn(scenarios, name)) problems.push(`missing baseline scenario ${name}`);
  }
  for (const name of Object.keys(scenarios)) {
    if (!expected.includes(name)) problems.push(`unexpected baseline scenario ${name}`);
  }

  const checked = [];
  for (const name of Object.keys(scenarios).sort()) {
    if (!expected.includes(name)) continue;
    const entry = scenarios[name];
    if (entry === null || typeof entry !== "object") {
      problems.push(`${name}: entry is not an object`);
      continue;
    }
    for (const field of ["png", "capturedUtc"]) {
      if (typeof entry[field] !== "string" || entry[field].length === 0) {
        problems.push(`${name}: missing ${field}`);
      }
    }
    // The PNG reference must be a bare filename inside the environment
    // directory; anything else is a broken or hostile index.
    if (entry.png !== `${name}.png`) {
      problems.push(`${name}: png must be the bare filename ${name}.png`);
    }
    // Theme and font are provenance, not content: they must be comparable
    // (non-empty string), but a closed terminal legitimately captures with
    // a null font, so null is a legal, recorded value.
    for (const field of ["theme", "font"]) {
      const value = entry[field];
      if (value !== null && (typeof value !== "string" || value.length === 0)) {
        problems.push(`${name}: ${field} must be a non-empty string or null`);
      }
    }
    if (
      entry.metrics === null ||
      typeof entry.metrics !== "object" ||
      !Number.isInteger(entry.metrics.pngBytes) ||
      entry.metrics.pngBytes < 1
    ) {
      problems.push(`${name}: metrics.pngBytes must be a positive integer`);
      continue;
    }
    const pngPath = path.join(storeDir, envKey, entry.png ?? "");
    if (!fs.existsSync(pngPath)) {
      problems.push(`${name}: missing PNG ${entry.png ?? "<none>"}`);
      continue;
    }

    let pngBytes = 0;
    let decoded;
    try {
      pngBytes = fs.statSync(pngPath).size;
      decoded = decodePng(fs.readFileSync(pngPath));
    } catch (error) {
      problems.push(`${name}: ${error.message}`);
      continue;
    }

    if (
      environment !== null &&
      typeof environment === "object" &&
      environment.resolution !== undefined &&
      (decoded.width !== environment.resolution.width || decoded.height !== environment.resolution.height)
    ) {
      problems.push(
        `${name}: PNG is ${decoded.width}x${decoded.height} but the environment records `
          + `${environment.resolution.width}x${environment.resolution.height}`
      );
    }
    if (entry.metrics.pngBytes !== pngBytes) {
      problems.push(
        `${name}: index records ${entry.metrics.pngBytes} png bytes but the file holds ${pngBytes}`
      );
    }
    checked.push(name);
  }

  return { scenarios: checked, problems };
}

/** Validates the whole store; returns the list of problems (empty = green). */
export function checkBaselineStore(storeDir, expectedScenarios) {
  const problems = [];
  if (!fs.existsSync(storeDir)) return [`baseline store is missing at ${storeDir}`];

  const environments = fs
    .readdirSync(storeDir, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort();
  // Dirent.isDirectory() is lstat-based, so symlinked environment directories
  // are skipped here but would still be followed by capture-time reads; that
  // asymmetry is accepted because the store is reviewed content.
  if (environments.length === 0) return ["baseline store holds no environment directories"];

  let checkedScenarios = 0;
  for (const envKey of environments) {
    const result = checkBaselineEnvironment(storeDir, envKey, expectedScenarios);
    for (const problem of result.problems) problems.push(`${envKey}: ${problem}`);
    checkedScenarios += result.scenarios.length;
  }
  console.log(
    `T11 store: ${environments.length} environment(s), ${checkedScenarios} baseline PNG(s) checked.`
  );
  return problems;
}

function main(argv) {
  let storeDir = DEFAULT_STORE;
  for (let index = 0; index < argv.length; ++index) {
    const token = argv[index];
    if (token === "--store") {
      const value = argv[++index];
      if (value === undefined) {
        console.error("Missing value for --store");
        return 2;
      }
      storeDir = path.resolve(value);
    } else if (token === "--help" || token === "-h") {
      console.log("Usage: node tooling~/scripts/t11/check.mjs [--store DIR]");
      return 0;
    } else {
      console.error(`Unknown argument: ${token}`);
      return 2;
    }
  }

  const problems = checkBaselineStore(storeDir);
  if (problems.length > 0) {
    console.error(`T11 store rejected:\n  - ${problems.join("\n  - ")}`);
    return 1;
  }
  console.log("T11 store approved.");
  return 0;
}

// Direct-run guard: compare filesystem paths, because import.meta.url
// percent-encodes the tilde in tooling~/ (fileURLToPath decodes it back).
if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  process.exitCode = main(process.argv.slice(2));
}
