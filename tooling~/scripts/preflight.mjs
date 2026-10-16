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

/*
    The node suite (CI's `npm --prefix tooling~ test` glob pair) split into
    two gates with honest per-file input enumeration (#307). CI keeps the
    full globs, so coverage does not move; locally a C# edit skips the
    tooling-keyed half and a tooling-only edit skips the tree-keyed half.
    Assignment rule: a file whose read surface reaches outside tooling~
    goes in the tree-keyed half - worst case it re-runs, and a stale pass
    is the hazard that matters.
 */
export const NODE_TEST_SPLITS = {
  /*
      Inputs ⊆ tooling~ ∪ .github/workflows ∪ .llm: the mcp tests drive
      unity-mcp.mjs against temp trees, the release/versioning/t11/lint
      tests import scripts under tooling~ and point linter roots at temp
      fixtures, publish-release/docs-pages/generator-workflow read workflow
      YAML, and python-entries runs the typed-python linters over the live
      .llm tree through uv. Keys: tooling~ (scripts, fixtures, manifest),
      the workflow files, .llm, the bash shim two members execute snippets
      with, the uv runtime python-entries resolves through, the installed
      dependency bytes the mcp tests load the SDK from, and the npm
      config the exporter members pack their fixtures under.
   */
  "node-tests-tooling": [
    "tooling~/scripts/mcp/__tests__/bridge-port.test.mjs",
    "tooling~/scripts/mcp/__tests__/client-configs.test.mjs",
    "tooling~/scripts/mcp/__tests__/dotenv.test.mjs",
    "tooling~/scripts/mcp/__tests__/env-and-capture.test.mjs",
    "tooling~/scripts/mcp/__tests__/ports.test.mjs",
    "tooling~/scripts/mcp/__tests__/tests-command.test.mjs",
    "tooling~/scripts/tests/docs-pages.test.mjs",
    "tooling~/scripts/tests/export-unitypackage.test.mjs",
    "tooling~/scripts/tests/export-unitypackage-cache.test.mjs",
    "tooling~/scripts/tests/generator-workflow.test.mjs",
    "tooling~/scripts/tests/import-drill.test.mjs",
    "tooling~/scripts/tests/lint-blocking-waits.test.mjs",
    "tooling~/scripts/tests/lint-comparison-direction.test.mjs",
    "tooling~/scripts/tests/lint-docs-catalog.test.mjs",
    "tooling~/scripts/tests/lint-linq-production.test.mjs",
    "tooling~/scripts/tests/lint-multiline-comments.test.mjs",
    "tooling~/scripts/tests/lint-out-param-discipline.test.mjs",
    "tooling~/scripts/tests/lint-pr-copy.test.mjs",
    "tooling~/scripts/tests/lint-string-equality.test.mjs",
    "tooling~/scripts/tests/lint-unity-null-patterns.test.mjs",
    "tooling~/scripts/tests/prepare-release.test.mjs",
    "tooling~/scripts/tests/publish-release.test.mjs",
    "tooling~/scripts/tests/python-entries.test.mjs",
    "tooling~/scripts/tests/t11-comparator.test.mjs",
    "tooling~/scripts/tests/t11-commands.test.mjs",
    "tooling~/scripts/tests/t4-capture.test.mjs",
    "tooling~/scripts/tests/t7-consumer-drill.test.mjs",
    "tooling~/scripts/tests/versioning.test.mjs"
  ],
  /*
      Inputs reach the live root trees: the lint sweeps and display-text
      walk Runtime/Editor/Tests/Generator~/Packs/Styles/Samples~, t13 and
      export-unitypackage-real read the packaged allowlist plus the mcp
      capture sources, analyzer-placement hunts stray payload copies from
      the repo root down (Media/ and progress/ included), and
      preflight.test reads .config and tooling~/compat. Keys: every walked
      root plus the tooling~ scripts those members import, and both suite
      directories - their bytes are the gate's own inputs, and the
      completeness pin that fails on an unassigned new suite file lives in
      preflight.test.mjs, so a file added to either half re-runs this gate
      and fails loudly. Stated coin: a NEW repo-root entry the stray hunt
      walks - a loose file named exactly like an analyzer payload, or a
      whole new root-level directory - defeats the key until the next
      keyed edit.
   */
  "node-tests-csharp": [
    "tooling~/scripts/tests/analyzer-placement.test.mjs",
    "tooling~/scripts/tests/display-text.test.mjs",
    "tooling~/scripts/tests/export-unitypackage-real.test.mjs",
    "tooling~/scripts/tests/lint-exception-tostring.test.mjs",
    "tooling~/scripts/tests/lint-member-ordering.test.mjs",
    "tooling~/scripts/tests/lint-minimal-comments.test.mjs",
    "tooling~/scripts/tests/lint-theme-palette-tokens.test.mjs",
    "tooling~/scripts/tests/preflight.test.mjs",
    "tooling~/scripts/tests/t13-compatibility-fixtures.test.mjs"
  ]
};

/** The default gate set. Names are stable identifiers used by --skip. */
export function buildChecks() {
  const pythonCommand = process.platform === "win32" ? "python" : "python3";
  const nodeTestsCheck = (name, cachePaths, extra = {}) => ({
    name,
    command: `node --test ${NODE_TEST_SPLITS[name].map((file) => `"${file}"`).join(" ")}`,
    cacheRuntimeEnvironment: true,
    cachePaths,
    ...extra
  });
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
    nodeTestsCheck(
      "node-tests-tooling",
      ["tooling~", ".github/workflows", ".llm"],
      {
        /*
            python-entries resolves the typed-python linters through uv,
            and publish-release/generator-workflow execute workflow
            snippets through bash - both runtimes join the key the same
            way npm and git do for the checks that spawn them. The mcp
            tests import unity-mcp.mjs, which loads the MCP SDK from
            tooling~/node_modules (or the baked /opt/dxt-mcp tree), so
            the installed dependency bytes join the key too, and the
            export-unitypackage members spawn npm pack against their
            fixture roots, so the npm config joins it the way
            package-validate keys its own npm pack.

            Stated coin: the uv venv (tooling~/.venv, uv.lock - both
            git-ignored) is keyed only through the uv release here; uv
            run re-syncs the venv from the tracked pyproject before
            executing, so a recreated or drifted venv heals at run time
            instead of moving this key. The npm that packs the exporter
            fixtures is keyed through its install bytes under
            tooling~/node_modules (cacheDependencies) and its config, not
            through a resolved-executable walk - a global npm outside the
            workspace is the same documented coin the exporter's own
            artifact cache states, and a node upgrade carries its npm
            behind the always-keyed node version.
         */
        cacheUv: true,
        cacheBash: true,
        cacheDependencies: true,
        cacheNpmConfig: true
      }
    ),
    nodeTestsCheck(
      "node-tests-csharp",
      [
        "Runtime",
        "Editor",
        "Tests",
        "Generator~",
        "Packs",
        "Styles",
        "Fonts",
        "Samples~",
        "Media",
        "progress",
        "package.json",
        "package.json.meta",
        "CHANGELOG.md",
        "CHANGELOG.md.meta",
        "LICENSE",
        "LICENSE.meta",
        "README.md",
        "README.md.meta",
        ".config",
        ".npmignore",
        ".gitignore",
        "tooling~/compat",
        "tooling~/scripts/release",
        "tooling~/scripts/t13",
        "tooling~/scripts/tests",
        "tooling~/scripts/mcp/__tests__",
        "tooling~/scripts/preflight.mjs",
        "tooling~/scripts/preflight-cache.mjs",
        "tooling~/scripts/lint-comparison-direction.mjs",
        "tooling~/scripts/lint-exception-tostring.mjs",
        "tooling~/scripts/lint-member-ordering.mjs",
        "tooling~/scripts/lint-minimal-comments.mjs",
        "tooling~/scripts/lint-theme-palette-tokens.mjs",
        "tooling~/scripts/lib",
        "tooling~/scripts/mcp/DxTerminalStateCapture.cs.txt",
        "tooling~/scripts/mcp/DxTerminalTestRunReporter.cs.txt"
      ],
      {
        // export-unitypackage-real spawns npm pack over the packaged
        // allowlist; key the npm bytes and config through the workspace
        // install (the cheap dependencies fold), not an executable walk.
        cacheDependencies: true,
        cacheNpmConfig: true
      }
    ),
    {
      /*
          The player-tooling python suites CI runs in tooling-tests.yml,
          in CI's order. One interpreter for all four suites (CI keeps one
          discover step each): measured 4.1 s -> 1.0 s standalone here, more
          saved under preflight's parallel load. unittest aggregates the
          module list without failing between suites, and any suite failure
          still fails the gate's exit code. The cwd option hosts the suites'
          own directory; a shell `cd` would make this gate the only one
          parsing a tilde path through cmd.exe on Windows.

          Cached: the suite files (the whole tests directory - worst case a
          new suite file re-runs this gate, a stale pass is the hazard that
          matters), the modules they exercise (.devcontainer for the OpenCode
          verifier, tooling~/scripts/t13 for the player tooling), and the
          python runtime join the key. Everything else the suites touch is a
          temp fixture they build themselves, and the ambient PATH folds in
          through the runtime environment the way it does for every cached
          gate (the delegator-style resolutions happen inside the suite).
       */
      name: "python-tests",
      cwd: "tooling~/scripts/tests",
      command: `${pythonCommand} -m unittest test_verify_opencode test_player_smoke test_player_readiness test_webgl_player`,
      cachePython: true,
      cacheRuntimeEnvironment: true,
      cachePaths: [".devcontainer", "tooling~/scripts/t13", "tooling~/scripts/tests"]
    },
    {
      /*
          CI's ubuntu-only devcontainer contract step: the typed port of the
          retired test-devcontainer.sh (issue #220). The suite drives the
          real .devcontainer lifecycle scripts through /bin/bash with
          stubbed CLIs, so it skips itself on Windows exactly where CI
          gates the step to one leg. Keys: the whole .devcontainer
          directory (every script and baked config the suite reads or
          copies), the suite file, bash, and the python runtime.
       */
      name: "devcontainer-contract",
      cwd: "tooling~/scripts/tests",
      command: `${pythonCommand} -m unittest test_devcontainer`,
      cachePython: true,
      cacheBash: true,
      cacheRuntimeEnvironment: true,
      cachePaths: [".devcontainer", "tooling~/scripts/tests/test_devcontainer.py"]
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
      cwd: check.cwd ? path.resolve(REPO_ROOT, check.cwd) : REPO_ROOT,
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
