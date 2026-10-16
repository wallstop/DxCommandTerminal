/*
    Runs all Unity-free local gates concurrently. Commands match CI without
    npm wrapper startup. Successful checks cache their declared inputs;
    external-runtime integration tests always run. --only=name1,name2 runs
    just those gates; --skip=name1,name2 runs everything but them.

    Usage: node tooling~/scripts/preflight.mjs [--only=a,b] [--skip=a,b] [--no-cache]
    Exit 1 on a failed check, an empty selection, or an unknown name.
    Shell commands resolve Windows shims the same way CI does.
*/
import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createCacheContext, createCacheKeyComputer, isCacheableCheck, readCacheEntry, writeCacheEntry } from "./preflight-cache.mjs";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");

/** The default gate set. Names are stable identifiers used by --skip. */
export function buildChecks() {
  const csharpCheck = (name, command, script, roots, cacheEnvironment) => ({
    name,
    command,
    cacheEnvironment,
    cacheRuntimeEnvironment: true,
    /*
        Generator~'s obj/bin are build outputs the linters never read as
        inputs; keying them made every first key pay a 3 s content walk
        (now stat signatures) and invalidated linter keys on every local
        generator build.
     */
    cacheExcludes: [
      "Generator~/WallstopStudios.DxCommandTerminal.Analyzers/obj",
      "Generator~/WallstopStudios.DxCommandTerminal.Analyzers/bin",
      "Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators/obj",
      "Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators/bin",
      "Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators.Tests/obj",
      "Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators.Tests/bin"
    ],
    cachePaths: [
      ...roots,
      `tooling~/scripts/${script}`,
      "tooling~/scripts/lint-comparison-direction.mjs"
    ]
  });
  return [
    {
      name: "node-tests",
      command:
        'node --test "tooling~/scripts/mcp/__tests__/*.test.mjs" "tooling~/scripts/tests/*.test.mjs"',
      /*
          External PowerShell/Python configuration prevents safe cache reuse.
          Fixtures stay outside other gates' inputs, so keying can overlap.
       */
      cache: false,
      startBeforeCache: true
    },
    {
      /*
          The player-tooling python suites CI runs in tooling-tests.yml,
          in CI's order. Not cached: the suites build their own fixture
          trees and finish in seconds, so a key saves nothing worth its
          staleness. Local selection mirrors the CI steps so a tooling
          change fails here first, not in a pushed branch's CI run.
       */
      name: "python-tests",
      cache: false,
      startBeforeCache: true,
      command: [
        "test_verify_opencode.py",
        "test_player_smoke.py",
        "test_player_readiness.py",
        "test_webgl_player.py"
      ]
        .map((pattern) => `${process.platform === "win32" ? "python" : "python3"} -m unittest discover -s tooling~/scripts/tests -p ${pattern}`)
        .join(" && ")
    },
    {
      name: "t11-check",
      command: "node tooling~/scripts/t11/check.mjs",
      cacheNpm: true,
      cacheRuntimeEnvironment: true,
      cacheNpmConfig: true,
      cachePaths: [
        "Tests/Runtime/Capture/Baselines~",
        "tooling~/scripts/t11",
        "tooling~/package.json"
      ]
    },
    {
      name: "package-validate",
      command: "node tooling~/scripts/release/validate-package-contents.mjs",
      cacheGitTracked: true,
      cacheGit: true,
      cacheTar: true,
      cacheGzip: true,
      cacheNpm: true,
      cacheRuntimeEnvironment: true,
      cacheNpmConfig: true,
      cacheGitConfig: true,
      cachePaths: [
        "Runtime",
        "Editor",
        "Packs",
        "Styles",
        "Fonts",
        "Samples~",
        "package.json",
        "README.md",
        "LICENSE",
        "CHANGELOG.md",
        "package.json.meta",
        "Runtime.meta",
        "Editor.meta",
        "Packs.meta",
        "Styles.meta",
        "Fonts.meta",
        "README.md.meta",
        "LICENSE.meta",
        "CHANGELOG.md.meta",
        ".gitignore",
        ".npmignore",
        "tooling~/scripts/release/validate-package-contents.mjs",
        "tooling~/package.json"
      ]
    },
    {
      name: "docs-guides",
      command: "node tooling~/docs/build.mjs --guides-only",
      cacheNpm: true,
      cacheRuntimeEnvironment: true,
      cacheNpmConfig: true,
      cacheDotnet: true,
      cacheDotnetTool: true,
      cacheDocsApi: true,
      cachePaths: [
        "global.json",
        "Documentation~",
        "Samples~/TerminalCommands",
        "Tests/Runtime/Capture/Baselines~",
        "tooling~/docs/build.mjs",
        "tooling~/docs/docfx.json",
        "tooling~/docs/toc.yml",
        "tooling~/scripts/t11",
        ".config/dotnet-tools.json",
        "tooling~/package.json"
      ]
    },
    /*
        Two Unity-free dotnet builds (runtime-2021.3.csproj and the
        tests-2021.3 compile gate below), each in its own directory since
        the compile gate moved to tooling~/compat/tests/ - concurrent
        restores in one obj used to scramble each other's references into
        a CS0246 storm (measured: a 1,294-error build that passes either
        build alone), and the old shared-obj layout is what the retired
        dotnet lane serialized. testhost, whose restore lands in
        tooling~/compat/testhost/obj, was already isolated.

        Both are cached (keyed on the sources each project compiles plus
        the dotnet environment). The old reason not to - "the input
        surface is the whole tree, so a key either hashes everything or
        goes stale" - was the keying wall that git-index identity removed:
        the key now costs one index listing and reads only dirty bytes.
        A warm no-op build still pays ~10 s of MSBuild evaluation on this
        host; a cache hit pays none.

        Stated limit: the cache keys the check's inputs, not MSBuild's
        obj state. A build killed mid-write (preflight's timeout) leaves
        obj in a state the next build repairs by timestamp, and a cache
        entry written before an external obj corruption would age a pass
        until the inputs move; --no-cache bypasses the cache.
     */
    {
      name: "compat-check",
      command: "dotnet build tooling~/compat/runtime-2021.3.csproj -c Release --nologo",
      cacheNpm: true,
      cacheDotnet: true,
      cacheRuntimeEnvironment: true,
      cachePaths: [
        "global.json",
        "Runtime",
        "tooling~/package.json",
        "tooling~/compat/runtime-2021.3.csproj"
      ],
      // The analyzer payload copied into Runtime/Analyzers is the
      // generator build's own output, excluded from both projects'
      // compilation - keying it would let the lane invalidate itself.
      cacheExcludes: ["Runtime/Analyzers"]
    },
    /*
        test:editor builds tests-2021.3 (the compile gate) and testhost
        (the execution host), and the testhost build applies the source
        generator. Preflight splits the command into its two halves so
        they run concurrently: each project restores into its own
        directory's obj (the compile gate moved to tooling~/compat/tests/
        for exactly that - the shared tooling~/compat/obj was the CS0246
        storm the retired dotnet lane existed for). compat-2021.yml keeps
        the chained npm script; the halves here are its steps in CI's
        order, called directly because the npm wrapper would chain them
        again.

        Keys: editor-compile spans what tests-2021.3 compiles (Runtime,
        all of Tests/Editor, the LogAssert stub, its csproj); editor-run
        spans what testhost compiles (selected Runtime and Tests/Editor
        sources, the stub surface in its own directory, and the generator
        its analyzer reference applies). The generator's test project,
        compat's T13 fixture project, and matrix.json compile nowhere
        here and are excluded or simply not rooted; obj/bin are excluded
        - they are the builds' own outputs, and keying them would
        invalidate the cache on every run the builds themselves run - and
        so is the NUnitLite result XML a direct testhost run drops into
        the keyed tree.
     */
    {
      name: "editor-compile",
      command: "dotnet build tooling~/compat/tests/tests-2021.3.csproj -c Release --nologo",
      cacheDotnet: true,
      cacheRuntimeEnvironment: true,
      cachePaths: [
        "global.json",
        "Runtime",
        "Tests/Editor",
        "tooling~/compat/tests"
      ],
      cacheExcludes: [
        "Runtime/Analyzers",
        "tooling~/compat/tests/obj",
        "tooling~/compat/tests/bin"
      ]
    },
    {
      name: "editor-run",
      command:
        "dotnet build tooling~/compat/testhost/testhost.csproj -c Release --nologo "
          + "&& dotnet run --project tooling~/compat/testhost -c Release --no-build --",
      cacheDotnet: true,
      cacheRuntimeEnvironment: true,
      cachePaths: [
        "global.json",
        "Runtime",
        "Tests/Editor",
        "Generator~",
        "tooling~/compat/testhost"
      ],
      cacheExcludes: [
        "Runtime/Analyzers",
        "tooling~/compat/testhost/obj",
        "tooling~/compat/testhost/bin",
        "tooling~/compat/testhost/TestResult.xml",
        "Generator~/WallstopStudios.DxCommandTerminal.Analyzers/obj",
        "Generator~/WallstopStudios.DxCommandTerminal.Analyzers/bin",
        "Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators/obj",
        "Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators/bin",
        "Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators.Tests"
      ]
    },
    csharpCheck(
      "lint-comparison-direction",
      "node tooling~/scripts/lint-comparison-direction.mjs",
      "lint-comparison-direction.mjs",
      ["Runtime", "Editor", "Tests", "Generator~"],
      ["COMPARISON_DIRECTION_ROOTS"]
    ),
    csharpCheck(
      "lint-member-ordering",
      "node tooling~/scripts/lint-member-ordering.mjs",
      "lint-member-ordering.mjs",
      ["Runtime", "Editor", "Tests", "Samples~", "Generator~"],
      ["NESTED_TYPE_PLACEMENT_ROOTS"]
    ),
    csharpCheck(
      "lint-multiline-comments",
      "node tooling~/scripts/lint-multiline-comments.mjs",
      "lint-multiline-comments.mjs",
      ["Runtime", "Editor", "Tests", "Generator~"],
      ["MULTILINE_COMMENT_ROOTS"]
    ),
    csharpCheck(
      "lint-minimal-comments",
      "node tooling~/scripts/lint-minimal-comments.mjs",
      "lint-minimal-comments.mjs",
      ["Runtime", "Editor", "Tests", "Generator~"],
      ["MINIMAL_COMMENT_ROOTS"]
    ),
    csharpCheck(
      "lint-linq-production",
      "node tooling~/scripts/lint-linq-production.mjs",
      "lint-linq-production.mjs",
      ["Runtime", "Editor"],
      ["LINQ_PRODUCTION_ROOTS"]
    ),
    csharpCheck(
      "lint-blocking-waits",
      "node tooling~/scripts/lint-blocking-waits.mjs",
      "lint-blocking-waits.mjs",
      ["Runtime", "Editor"],
      ["BLOCKING_WAIT_ROOTS"]
    ),
    csharpCheck(
      "lint-string-equality",
      "node tooling~/scripts/lint-string-equality.mjs",
      "lint-string-equality.mjs",
      ["Runtime", "Editor"],
      ["STRING_EQUALITY_ROOTS"]
    ),
    csharpCheck(
      "lint-out-param-discipline",
      "node tooling~/scripts/lint-out-param-discipline.mjs",
      "lint-out-param-discipline.mjs",
      ["Runtime", "Editor", "Tests", "Samples~", "Generator~"],
      ["OUT_PARAM_DISCIPLINE_ROOTS"]
    ),
    csharpCheck(
      "lint-unity-null-patterns",
      "node tooling~/scripts/lint-unity-null-patterns.mjs",
      "lint-unity-null-patterns.mjs",
      ["Runtime", "Editor", "Tests", "Generator~"],
      ["UNITY_NULL_ROOTS"]
    ),
    {
      name: "lint-theme-palette-tokens",
      command: "node tooling~/scripts/lint-theme-palette-tokens.mjs",
      cacheRuntimeEnvironment: true,
      cacheEnvironment: ["THEME_TOKEN_ROOTS"],
      cachePaths: ["Styles/Themes", "Styles/BaseStyles.uss", "tooling~/scripts/lint-theme-palette-tokens.mjs"]
    },
    {
      /*
          Keys the tracked markdown files it formats (index listing, not the
          whole snapshot: a C# edit must not re-run the markdown gate - the
          local mirror of CI's trigger diet) plus the formatter's own inputs:
          both config files prettier resolves (.prettierrc.json and
          .editorconfig) and the node_modules tree prettier runs from.
       */
      name: "lint-md-format",
      command: "node tooling~/scripts/lint-md-format.mjs",
      cacheGit: true,
      cacheGitConfig: true,
      cacheTrackedPaths: ["*.md", ":!.llm/skills/index.md", ".prettierrc.json", ".editorconfig"],
      cacheDependencies: true,
      cacheRuntimeEnvironment: true,
      cachePaths: ["tooling~/scripts/lint-md-format.mjs", ".prettierrc.json", ".editorconfig"]
    },
    {
      name: "lint-docs-catalog",
      command: "node tooling~/scripts/lint-docs-catalog.mjs",
      cacheRuntimeEnvironment: true,
      cacheDocsApi: true,
      cacheEnvironment: ["DOCS_CATALOG", "DOCS_GUIDES_DIR", "DOCS_SAMPLES_DIR", "DOCS_BASELINES_DIR", "DOCS_API_DIR"],
      cachePaths: [
        "Documentation~",
        "Samples~",
        "Tests/Runtime/Capture/Baselines~",
        "tooling~/scripts/t11",
        "tooling~/scripts/lint-docs-catalog.mjs"
      ]
    }
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
 * Runs every check concurrently and resolves once all exits are collected;
 * never rejects for a check's non-zero exit (that is a result, not a crash).
 * A check exceeding CHECK_TIMEOUT_MS is killed and reported as failed so a
 * hung gate cannot hang the run.
 */
export function runChecks(checks, { onSettled = () => {} } = {}) {
  return Promise.all(
    checks.map((check) => runOne(check).then((result) => {
      onSettled(result);
      return result;
    }))
  );
}

function runOne(check) {
  return new Promise((resolve) => {
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
      resolve({
        name: check.name,
        command: check.command,
        ok: code === 0 && !timedOut,
        durationMs: Date.now() - startedAt,
        output: tailLines(output)
      });
    });
  });
}

/** Extracts a comma-separated name list for `--skip` / `--only`. */
function parseNameList(argv, flag, knownNames) {
  let raw = "";
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === flag) {
      const value = argv[i + 1];
      if (value === undefined || value.startsWith("--")) {
        throw new Error(`${flag} requires a comma-separated name list`);
      }
      raw = value;
      i++;
    } else if (argv[i].startsWith(`${flag}=`)) {
      raw = argv[i].slice(flag.length + 1);
    }
  }
  const names = raw
    .split(",")
    .map((name) => name.trim())
    .filter((name) => name.length > 0);
  for (const name of names) {
    if (!knownNames.includes(name)) {
      throw new Error(`unknown ${flag} name '${name}' (known: ${knownNames.join(", ")})`);
    }
  }
  return names;
}

/** Extracts the --skip value (accepts `--skip=a,b` and `--skip a,b`). */
export function parseSkip(argv, knownNames) {
  return parseNameList(argv, "--skip", knownNames);
}

/** Extracts the --only value; the selection complement of --skip. */
export function parseOnly(argv, knownNames) {
  return parseNameList(argv, "--only", knownNames);
}

export async function main(argv = process.argv.slice(2), checks = buildChecks()) {
  const knownNames = checks.map((check) => check.name);
  const skip = parseSkip(argv, knownNames);
  const only = parseOnly(argv, knownNames);
  if (skip.length > 0 && only.length > 0) {
    throw new Error("--skip and --only are mutually exclusive");
  }
  const selected = checks.filter((check) => (only.length > 0 ? only.includes(check.name) : !skip.includes(check.name)));
  if (selected.length === 0) {
    console.error("[preflight] no checks to run; refusing to scan nothing");
    return 1;
  }

  const useCache = !process.env.CI && !argv.includes("--no-cache");
  const startedAt = Date.now();
  const cachedResults = [];
  const pending = [];
  const cacheKeys = new Map();
  const earlyChecks = useCache
    ? selected.filter((check) => check.startBeforeCache === true && !isCacheableCheck(check))
    : [];
  const onSettled = (result) => {
    const seconds = (result.durationMs / 1000).toFixed(1);
    if (result.ok) {
      const key = cacheKeys.get(result.name);
      if (key !== undefined && useCache) {
        writeCacheEntry(selected.find((check) => check.name === result.name), key);
      }
      console.log(`[preflight] ok   ${result.name} (${seconds}s)`);
    } else {
      console.log(`[preflight] FAIL ${result.name} (${seconds}s)`);
    }
  };
  if (earlyChecks.length > 0) {
    console.log(`[preflight] running ${earlyChecks.length} checks while checking caches...`);
  }
  const earlyResults = runChecks(earlyChecks, { onSettled });
  let cacheContext = null;
  let keyComputer = null;
  if (useCache && earlyChecks.length < selected.length) {
    try {
      cacheContext = createCacheContext();
      keyComputer = createCacheKeyComputer();
    } catch {
      console.warn("[preflight] cache setup failed; running every selected check");
    }
  }
  for (const check of selected) {
    if (earlyChecks.includes(check)) {
      continue;
    }
    let key = null;
    if (useCache && isCacheableCheck(check)) {
      try {
        key = keyComputer.keyFor(check, cacheContext);
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

  console.log(`[preflight] running ${pending.length} ${earlyChecks.length > 0 ? "additional " : ""}checks in parallel...`);
  const groups = await Promise.all([earlyResults, runChecks(pending, { onSettled })]);
  const results = [...cachedResults, ...groups.flat()];

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
