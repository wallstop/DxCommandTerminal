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

/** Keeps the last `maxLines` lines of a check's output for failure reports. */
function tailLines(text, maxLines = OUTPUT_TAIL_LINES) {
  const lines = text.split(/\r?\n/).filter((line) => line.length > 0);
  const tail = lines.slice(-maxLines);
  const trimmed = tail.length < lines.length ? `... (${lines.length - tail.length} earlier lines trimmed)\n` : "";
  return trimmed + tail.join("\n");
}

/**
 * Runs every check concurrently. Resolves once all exits are collected; never
 * rejects for a check's non-zero exit (that is a result, not a crash).
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
          const capture = (chunk) => {
            output += chunk;
          };
          child.stdout.on("data", capture);
          child.stderr.on("data", capture);
          child.on("error", (error) => {
            output += `\n[preflight] failed to spawn: ${error.message}`;
          });
          child.on("close", (code) => {
            const result = {
              name: check.name,
              command: check.command,
              ok: code === 0,
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
      raw = argv[i + 1] ?? "";
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

export async function main(argv = process.argv.slice(2)) {
  const allChecks = buildChecks();
  const skip = parseSkip(argv, allChecks.map((check) => check.name));
  const checks = allChecks.filter((check) => !skip.includes(check.name));
  if (checks.length === 0) {
    console.error("[preflight] no checks to run; refusing to scan nothing");
    return 1;
  }

  console.log(`[preflight] running ${checks.length} checks in parallel...`);
  const results = await runChecks(checks, (result) => {
    const seconds = (result.durationMs / 1000).toFixed(1);
    console.log(result.ok ? `[preflight] ok   ${result.name} (${seconds}s)` : `[preflight] FAIL ${result.name} (${seconds}s)`);
  });

  const failures = results.filter((result) => !result.ok);
  for (const failure of failures) {
    console.error(`\n[preflight] --- ${failure.name} failed: ${failure.command} ---`);
    console.error(failure.output);
  }

  const totalSeconds = ((results.reduce((max, result) => Math.max(max, result.durationMs), 0)) / 1000).toFixed(1);
  const passed = results.length - failures.length;
  console.log(`[preflight] ${passed}/${results.length} passed, wall ${totalSeconds}s`);
  return failures.length > 0 ? 1 : 0;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  process.exitCode = await main();
}
