/*
    Parallel local preflight: one command that runs every Unity-free gate the
    repo enforces on a normal change, concurrently.

    Why: an agent (or human) validating an edit today serially runs the node
    suite, the C# linters, the baseline gate, the package gate, and the docs
    loop. Each pays its own startup, and the wall time is the SUM (~25-30s).
    Run concurrently, the wall time is the slowest single check (~5s) and one
    command replaces the list.

    Checks (all Unity-free, same commands CI runs):
      - node suite, t11 baseline gate, package content gate, docs guides build,
        compat compile tripwire
      - the nine C#/asset linters (mirrors the pre-commit fleet + CI)

    Usage:
      node tooling~/scripts/preflight.mjs [--skip=name1,name2]

    Exit codes: 0 = all checks passed, 1 = at least one failed (or the check
    set resolved empty, or --skip named an unknown check).

    Windows note: every command runs through the shell, so npm.cmd / dotnet
    shims resolve the same way they do in CI.
*/
import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { cacheKeyForCheck, createCacheContext, isCacheableCheck, readCacheEntry, writeCacheEntry } from "./preflight-cache.mjs";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");

/** The default gate set. Names are stable identifiers used by --skip. */
export function buildChecks() {
  return [
    { name: "node-tests", command: "npm --prefix tooling~ test" },
    { name: "t11-check", command: "npm --prefix tooling~ run t11:check" },
    { name: "package-validate", command: "npm --prefix tooling~ run package:validate" },
    { name: "docs-guides", command: "npm --prefix tooling~ run docs:guides" },
    { name: "compat-check", command: "npm --prefix tooling~ run compat:check" },
    { name: "lint-comparison-direction", command: "node tooling~/scripts/lint-comparison-direction.mjs" },
    { name: "lint-member-ordering", command: "node tooling~/scripts/lint-member-ordering.mjs" },
    { name: "lint-multiline-comments", command: "node tooling~/scripts/lint-multiline-comments.mjs" },
    { name: "lint-linq-production", command: "node tooling~/scripts/lint-linq-production.mjs" },
    { name: "lint-string-equality", command: "node tooling~/scripts/lint-string-equality.mjs" },
    { name: "lint-out-param-discipline", command: "node tooling~/scripts/lint-out-param-discipline.mjs" },
    { name: "lint-unity-null-patterns", command: "node tooling~/scripts/lint-unity-null-patterns.mjs" },
    { name: "lint-theme-palette-tokens", command: "node tooling~/scripts/lint-theme-palette-tokens.mjs" },
    { name: "lint-docs-catalog", command: "node tooling~/scripts/lint-docs-catalog.mjs" }
  ];
}

const OUTPUT_TAIL_LINES = 40;
const CHECK_TIMEOUT_MS = 5 * 60 * 1000;

/** Keeps the last `maxLines` lines of a check's output for failure reports. */
function tailLines(text, maxLines = OUTPUT_TAIL_LINES) {
  const lines = text.split(/\r?\n/).filter((line) => line.length > 0);
  const tail = lines.slice(-maxLines);
  const trimmed = tail.length < lines.length ? `... (${lines.length - tail.length} earlier lines trimmed)\n` : "";
  return trimmed + tail.join("\n");
}

/**
 * Runs every check concurrently. Resolves once all exits are collected; never
 * rejects for a check's non-zero exit (that is a result, not a crash). A check
 * exceeding CHECK_TIMEOUT_MS is killed and reported as failed so a hung gate
 * cannot hang the run.
 */
export function runChecks(checks, { onSettled = () => {} } = {}) {
  return Promise.all(
    checks.map(
      (check) =>
        new Promise((resolve) => {
          const startedAt = Date.now();
          const child = spawn(check.command, {
            shell: true,
            cwd: REPO_ROOT,
            stdio: ["ignore", "pipe", "pipe"]
          });
          let output = "";
          let timedOut = false;
          const capture = (chunk) => {
            output += chunk;
          };
          const timer = setTimeout(() => {
            timedOut = true;
            output += `\n[preflight] check exceeded ${CHECK_TIMEOUT_MS / 1000}s; killed`;
            child.kill("SIGKILL");
          }, CHECK_TIMEOUT_MS);
          child.stdout.on("data", capture);
          child.stderr.on("data", capture);
          child.on("error", (error) => {
            output += `\n[preflight] failed to spawn: ${error.message}`;
          });
          child.on("close", (code) => {
            clearTimeout(timer);
            const result = {
              name: check.name,
              command: check.command,
              ok: code === 0 && !timedOut,
              durationMs: Date.now() - startedAt,
              output: tailLines(output)
            };
            onSettled(result);
            resolve(result);
          });
        })
    )
  );
}

/** Extracts the --skip value (accepts `--skip=a,b` and `--skip a,b`). */
export function parseSkip(argv, knownNames) {
  let raw = "";
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--skip") {
      const value = argv[i + 1];
      if (value === undefined || value.startsWith("--")) {
        throw new Error("--skip requires a comma-separated name list");
      }
      raw = value;
      i++;
    } else if (argv[i].startsWith("--skip=")) {
      raw = argv[i].slice("--skip=".length);
    }
  }
  const names = raw
    .split(",")
    .map((name) => name.trim())
    .filter((name) => name.length > 0);
  for (const name of names) {
    if (!knownNames.includes(name)) {
      throw new Error(`unknown --skip name '${name}' (known: ${knownNames.join(", ")})`);
    }
  }
  return names;
}

export async function main(argv = process.argv.slice(2), checks = buildChecks()) {
  const skip = parseSkip(argv, checks.map((check) => check.name));
  const selected = checks.filter((check) => !skip.includes(check.name));
  if (selected.length === 0) {
    console.error("[preflight] no checks to run; refusing to scan nothing");
    return 1;
  }

  const useCache = !process.env.CI && !argv.includes("--no-cache");
  const startedAt = Date.now();
  const cachedResults = [];
  const pending = [];
  const cacheKeys = new Map();
  const cacheContext = useCache ? createCacheContext() : null;
  for (const check of selected) {
    let key = null;
    if (useCache && isCacheableCheck(check)) {
      try {
        key = cacheKeyForCheck(check, cacheContext);
        cacheKeys.set(check.name, key);
      } catch {
        key = null;
      }
    }
    if (key !== null && readCacheEntry(check, key)) {
      cachedResults.push({ name: check.name, command: check.command, ok: true, durationMs: 0, output: "", cached: true });
      console.log(`[preflight] cached ${check.name}`);
    } else {
      pending.push(check);
    }
  }

  console.log(`[preflight] running ${pending.length} checks in parallel...`);
  const settled = await runChecks(pending, {
    onSettled: (result) => {
      const seconds = (result.durationMs / 1000).toFixed(1);
      if (result.ok) {
        const key = cacheKeys.get(result.name);
        if (key !== undefined && useCache) {
          writeCacheEntry({ name: result.name, command: result.command }, key);
        }
        console.log(`[preflight] ok   ${result.name} (${seconds}s)`);
      } else {
        console.log(`[preflight] FAIL ${result.name} (${seconds}s)`);
      }
    }
  });
  const results = [...cachedResults, ...settled];

  const failures = results.filter((result) => !result.ok);
  for (const failure of failures) {
    console.error(`\n[preflight] --- ${failure.name} failed: ${failure.command} ---`);
    console.error(failure.output);
  }

  const totalSeconds = ((Date.now() - startedAt) / 1000).toFixed(1);
  const passed = results.length - failures.length;
  const cached = results.filter((result) => result.cached === true).length;
  console.log(`[preflight] ${passed}/${results.length} passed, ${cached} cached, wall ${totalSeconds}s`);
  return failures.length > 0 ? 1 : 0;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  process.exitCode = await main();
}
