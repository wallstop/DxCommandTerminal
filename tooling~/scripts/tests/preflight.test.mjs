/*
    Contract tests for tooling~/scripts/preflight.mjs.

    Data-driven over the runner contract: the default check set is non-empty
    with unique names, --skip filters by exact name and refuses unknown names,
    runCollect reports per-check pass/fail with captured output, and main
    refuses to scan nothing.
*/
import { test, after } from "node:test";
import assert from "node:assert";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const toolingRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const preflightPath = path.join(toolingRoot, "scripts", "preflight.mjs");
const { buildChecks, parseSkip, parseOnly, runChecks, main, NODE_TEST_SPLITS } = await import(
  pathToFileURL(preflightPath).href
);
const {
  cacheKeyForCheck,
  cachePathForCheck,
  clearCacheForCheck,
  createCacheContext,
  createCacheKeyComputer,
  dependencyState,
  dotnetToolState,
  isCacheableCheck,
  npmConfigState,
  npmExecutableState,
  parseStatusRecords,
  pathState,
  readCacheEntry,
  resolvedExecutableState,
  writeCacheEntry
} = await import(
  pathToFileURL(path.join(toolingRoot, "scripts", "preflight-cache.mjs")).href
);

function writeTempScript(body) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-test-"));
  const file = path.join(dir, "subject.mjs");
  fs.writeFileSync(file, body);
  tempDirs.push(dir);
  return file;
}

const tempDirs = [];
after(() => {
  for (const dir of tempDirs) fs.rmSync(dir, { recursive: true, force: true });
});

/*
    A throwaway repository for the listing-identity contract tests. The
    module derives REPO_ROOT from its own location, so a COPY of
    preflight-cache.mjs inside the scratch tree makes the scratch tree the
    keyed repository: every git spawn lands on a three-file repo on the
    local scratch filesystem (milliseconds) instead of the real checkout
    over a 9p mount (seconds each), and no probe ever touches real tracked
    state. The per-process listing memo is why each observation still runs
    in its own child importing that copy.
 */
function scratchRepo() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-repo-"));
  tempDirs.push(root);
  const scripts = path.join(root, "tooling~", "scripts");
  fs.mkdirSync(scripts, { recursive: true });
  fs.copyFileSync(
    path.join(toolingRoot, "scripts", "preflight-cache.mjs"),
    path.join(scripts, "preflight-cache.mjs")
  );
  fs.writeFileSync(path.join(root, ".gitignore"), ".preflight-cache/\nignored-scratch/\n");
  fs.writeFileSync(path.join(root, ".gitattributes"), "*.txt text eol=lf\n");
  fs.writeFileSync(path.join(scripts, "tracked-fixture.txt"), "tracked bytes\n");
  /* Command-local settings avoid global config and separate setup processes. */
  const git = (...args) => execFileSync("git", [
    "-c", "user.email=probe@example.invalid",
    "-c", "user.name=preflight probe",
    "-c", "commit.gpgsign=false",
    ...args
  ], { cwd: root, encoding: "utf8" });
  git("init", "-q", "-b", "main");
  git("add", "-A");
  git("commit", "-q", "--no-verify", "-m", "scratch fixture");
  const scratchScripts = "tooling~/scripts";
  const run = async (body) => {
    const { execFile } = await import("node:child_process");
    const { promisify } = await import("node:util");
    const moduleHref = pathToFileURL(path.join(scripts, "preflight-cache.mjs")).href;
    const subject = writeTempScript(
      `const module = await import(${JSON.stringify(moduleHref)});\n${body}`
    );
    const { stdout } = await promisify(execFile)(process.execPath, [subject]);
    return stdout.trim();
  };
  return { root, git, scripts, scratchScripts, run };
}

function case_(name, overrides) {
  return { name, ...overrides };
}

test("buildChecks: unique names, non-empty commands, expected canaries", () => {
  const checks = buildChecks();
  assert.ok(checks.length > 0, "check set must not be empty");
  const names = checks.map((check) => check.name);
  assert.deepEqual(new Set(names).size, names.length, "check names must be unique");
  for (const check of checks) {
    assert.equal(typeof check.command, "string");
    assert.ok(check.command.trim().length > 0, `${check.name} needs a command`);
  }
  const expected = [
    "node-tests-tooling",
    "node-tests-csharp",
    "t11-check",
    "package-validate",
    "docs-guides",
    "compat-check",
    "editor-compile",
    "editor-run",
    "lint-linq-production",
    "lint-docs-catalog",
    "lint-md-format"
  ];
  for (const name of expected) {
    assert.ok(names.includes(name), `expected check '${name}' in the default set`);
  }
  for (const check of checks) {
    if (check.cache !== false) {
      assert.equal(check.cacheRuntimeEnvironment, true, `${check.name} must key inherited runtime state`);
    }
  }
  const compatCheck = checks.find((check) => check.name === "compat-check");
  assert.equal(compatCheck.lane, undefined, "no check shares build state anymore; every check owns a lane");
  assert.equal(compatCheck.cacheDotnet, true);
  assert.deepEqual(compatCheck.cachePaths, [
    "global.json",
    "Runtime",
    "tooling~/package.json",
    "tooling~/compat/runtime-2021.3.csproj"
  ]);
  assert.deepEqual(compatCheck.cacheExcludes, ["Runtime/Analyzers"]);
  const editorCompileCheck = checks.find((check) => check.name === "editor-compile");
  assert.equal(editorCompileCheck.lane, undefined);
  assert.equal(editorCompileCheck.cacheDotnet, true);
  assert.ok(editorCompileCheck.cachePaths.includes("Tests/Editor"));
  assert.ok(
    editorCompileCheck.cachePaths.includes("tooling~/compat/tests"),
    "the compile gate must key the directory its project and stub live in"
  );
  for (const buildOutput of ["tooling~/compat/tests/obj", "tooling~/compat/tests/bin"]) {
    assert.ok(
      editorCompileCheck.cacheExcludes.includes(buildOutput),
      `editor-compile must not key its own build output ${buildOutput}`
    );
  }
  assert.ok(
    !editorCompileCheck.cacheExcludes.includes("tooling~/compat/obj"),
    "the compile gate no longer writes the shared compat obj"
  );
  /*
      Directory isolation is the retired dotnet lane's replacement: two
      projects restoring into one obj scramble each other's assets into a
      CS0246 storm, so the compile gate's project must not sit beside
      runtime-2021.3.csproj - and no obj-path override may try to
      reintroduce isolation from inside the project body (that fires
      after Microsoft.Common.props resolved the extensions path, and the
      restore still collides).
   */
  const testsDirectory = fs
    .readdirSync(path.join(toolingRoot, "compat", "tests"))
    .filter((entry) => entry.endsWith(".csproj"));
  assert.deepEqual(
    testsDirectory,
    ["tests-2021.3.csproj"],
    "tests-2021.3.csproj must be the only project in its directory"
  );
  const csproj = fs.readFileSync(
    path.join(toolingRoot, "compat", "tests", "tests-2021.3.csproj"),
    "utf8"
  );
  assert.ok(!csproj.includes("<BaseIntermediateOutputPath"), "directory isolation replaces the obj-path override");
  const editorRunCheck = checks.find((check) => check.name === "editor-run");
  assert.equal(editorRunCheck.lane, undefined, "testhost's restore owns its obj state, so it runs outside the dotnet lane");
  assert.equal(editorRunCheck.cacheDotnet, true);
  assert.ok(editorRunCheck.cachePaths.includes("Tests/Editor"));
  assert.ok(editorRunCheck.cachePaths.includes("Generator~"));
  for (const buildOutput of ["Runtime/Analyzers", "tooling~/compat/testhost/obj", "tooling~/compat/testhost/bin", "tooling~/compat/testhost/TestResult.xml"]) {
    assert.ok(
      editorRunCheck.cacheExcludes.includes(buildOutput),
      `editor-run must not key its own build output ${buildOutput}`
    );
  }
  for (const check of checks) {
    assert.equal(
      check.command.includes("npm --prefix"),
      false,
      `${check.name} must run its script directly; the npm wrapper only adds a startup per check`
    );
  }
  const linterCheck = checks.find((check) => check.name === "lint-linq-production");
  assert.ok(
    linterCheck.cacheExcludes.includes("Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators/obj"),
    "the C# linters must not key generator build outputs"
  );
  const packageCheck = checks.find((check) => check.name === "package-validate");
  assert.equal(packageCheck.cacheGitTracked, true);
  assert.equal(packageCheck.cacheNpm, true);
  assert.equal(packageCheck.cacheGzip, true);
  assert.equal(packageCheck.cacheNpmConfig, true);
  assert.equal(packageCheck.cacheGitConfig, true);
  assert.equal(packageCheck.cacheRuntimeEnvironment, true);
  assert.ok(packageCheck.cachePaths.includes(".npmignore"));
  const toolingCheck = checks.find((check) => check.name === "node-tests-tooling");
  const csharpCheck = checks.find((check) => check.name === "node-tests-csharp");
  for (const gate of [toolingCheck, csharpCheck]) {
    assert.notEqual(gate.cache, false, `${gate.name} must be cacheable`);
    assert.equal(gate.startBeforeCache, undefined, `${gate.name} keys before running`);
    assert.equal(gate.cacheRuntimeEnvironment, true);
  }
  assert.deepEqual(toolingCheck.cachePaths, ["tooling~", ".github/workflows", ".llm"]);
  assert.equal(toolingCheck.cacheUv, true, "python-entries resolves the linters through uv");
  assert.equal(toolingCheck.cacheBash, true, "workflow snippets execute through bash");
  assert.equal(csharpCheck.cacheUv, undefined, "no tree-keyed member spawns uv");
  assert.deepEqual(csharpCheck.cachePaths, [
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
    "tooling~/scripts/tests/support",
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
  ]);
  const docsCheck = checks.find((check) => check.name === "docs-guides");
  assert.equal(docsCheck.cacheDotnetTool, true);
  assert.equal(docsCheck.cacheDocsApi, true);
  assert.ok(docsCheck.cachePaths.includes("tooling~/docs/toc.yml"));
  assert.equal(docsCheck.cacheEnvironment, undefined);
});

test("node suite split: every suite file is assigned to exactly one gate", () => {
  const suiteDirectories = ["scripts/mcp/__tests__", "scripts/tests"];
  const onDisk = suiteDirectories.flatMap((directory) =>
    fs
      .readdirSync(path.join(toolingRoot, directory))
      .filter((name) => name.endsWith(".test.mjs"))
      .map((name) => `tooling~/${directory}/${name}`)
  );
  const assigned = Object.values(NODE_TEST_SPLITS).flat();
  assert.deepEqual(
    [...assigned].sort(),
    [...new Set(onDisk)].sort(),
    "the splits must enumerate exactly the files CI's globs run - a new suite file failing here must be assigned to a gate"
  );
  const seen = new Set();
  for (const [name, files] of Object.entries(NODE_TEST_SPLITS)) {
    for (const file of files) {
      assert.ok(!seen.has(file), `${file} is assigned to more than one gate`);
      seen.add(file);
    }
    const check = buildChecks().find((candidate) => candidate.name === name);
    const commandFiles = [...check.command.matchAll(/"([^"]+\.test\.mjs)"/gu)].map((match) => match[1]);
    assert.deepEqual(commandFiles, files, `${name}'s command must run exactly its split`);
  }
});

test("cacheUv folds the uv runtime into the key only for checks that declare it", () => {
  const base = { name: "uv-gate", command: "node probe.mjs" };
  const withUv = { ...base, cacheUv: true };
  const context = createCacheContext();
  const keyOf = (check, uv, uvExecutable) =>
    cacheKeyForCheck(check, { ...context, uv, uvExecutable });
  assert.notEqual(
    keyOf(withUv, "uv 1.0", "state-a"),
    keyOf(withUv, "uv 2.0", "state-a"),
    "a uv upgrade must move the key"
  );
  assert.notEqual(
    keyOf(withUv, "uv 1.0", "state-a"),
    keyOf(withUv, "uv 1.0", "state-b"),
    "a uv executable move must move the key"
  );
  assert.equal(
    keyOf(base, "uv 1.0", "state-a"),
    keyOf(base, "uv 2.0", "state-b"),
    "a check that never spawns uv must not key it"
  );
});

const skipCases = [
  case_("inline form filters by exact name", {
    argv: ["--skip=node-tests,docs-guides"],
    skipped: ["node-tests", "docs-guides"]
  }),
  case_("space form filters identically", {
    argv: ["--skip", "node-tests"],
    skipped: ["node-tests"]
  }),
  case_("whitespace and empty segments are ignored", {
    argv: ["--skip=node-tests, , docs-guides ,"],
    skipped: ["node-tests", "docs-guides"]
  }),
  case_("no flag skips nothing", { argv: [], skipped: [] })
];

for (const { name, argv, skipped } of skipCases) {
  test(`parseSkip: ${name}`, () => {
    const known = ["node-tests", "docs-guides", "t11-check"];
    const remaining = parseSkip(argv, known);
    assert.deepEqual(
      remaining.sort(),
      [...skipped].sort(),
      "skip must list exactly the skipped names"
    );
  });
}

test("parseSkip: unknown name fails loudly with the known set", () => {
  assert.throws(() => parseSkip(["--skip=nope"], ["node-tests"]), /unknown --skip name 'nope'/);
});

const onlyCases = [
  case_("inline form selects by exact name", {
    argv: ["--only=node-tests,docs-guides"],
    selected: ["node-tests", "docs-guides"]
  }),
  case_("space form selects identically", {
    argv: ["--only", "t11-check"],
    selected: ["t11-check"]
  }),
  case_("whitespace and empty segments are ignored", {
    argv: ["--only=node-tests, , docs-guides ,"],
    selected: ["node-tests", "docs-guides"]
  }),
  case_("no flag selects everything", { argv: [], selected: [] })
];

for (const { name, argv, selected } of onlyCases) {
  test(`parseOnly: ${name}`, () => {
    const known = ["node-tests", "docs-guides", "t11-check"];
    const got = parseOnly(argv, known);
    assert.deepEqual(
      got.sort(),
      [...selected].sort(),
      "only must list exactly the selected names"
    );
  });
}

test("parseOnly: unknown name fails loudly with the known set", () => {
  assert.throws(() => parseOnly(["--only=nope"], ["node-tests"]), /unknown --only name 'nope'/);
});

const strictOnlyCases = [
  case_("missing value", { argv: ["--only"] }),
  case_("flag-shaped value", { argv: ["--only", "--help"] })
];

for (const { name, argv } of strictOnlyCases) {
  test(`parseOnly: ${name} fails instead of selecting nothing`, () => {
    assert.throws(() => parseOnly(argv, ["node-tests"]), /--only requires a comma-separated name list/);
  });
}

const strictSkipCases = [
  case_("missing value", { argv: ["--skip"] }),
  case_("flag-shaped value", { argv: ["--skip", "--help"] })
];

for (const { name, argv } of strictSkipCases) {
  test(`parseSkip: ${name} fails instead of skipping silently`, () => {
    assert.throws(() => parseSkip(argv, ["node-tests"]), /--skip requires a comma-separated name list/);
  });
}

const runCases = [
  case_("passing command", {
    body: "process.exit(0);",
    expectOk: true,
    outputFragment: null
  }),
  case_("failing command exits non-zero", {
    body: "console.error('boom-marker'); process.exit(3);",
    expectOk: false,
    outputFragment: "boom-marker"
  })
];

for (const { name, body, expectOk, outputFragment } of runCases) {
  test(`runChecks: ${name}`, async () => {
    const subject = writeTempScript(body);
    const results = await runChecks([{ name: "subject", command: `node "${subject}"` }]);
    assert.equal(results.length, 1);
    assert.equal(results[0].ok, expectOk, `subject should ${expectOk ? "pass" : "fail"}`);
    assert.ok(results[0].durationMs >= 0, "duration must be recorded");
    if (outputFragment !== null) {
      assert.ok(results[0].output.includes(outputFragment), "failing output must be captured");
    }
  });
}

test("runChecks: settles once per check and preserves results", async () => {
  const subjects = ["a", "b"].map((label) =>
    writeTempScript(`console.log('${label}'); process.exit(0);`)
  );
  const settled = [];
  const results = await runChecks(
    subjects.map((subject, index) => ({
      name: `check-${index}`,
      command: `node "${subject}"`
    })),
    { onSettled: (result) => settled.push(result.name) }
  );
  assert.equal(results.length, subjects.length);
  assert.deepEqual(settled.sort(), ["check-0", "check-1"], "one onSettled per check");
  assert.ok(results.every((result) => result.ok), "all subjects pass");
});

test("runChecks: checks run concurrently with no shared-lane serialization", async () => {
  /*
      The dotnet lane is retired: every compat project restores into its
      own directory's obj, so no two checks share build state and none
      may serialize behind another. The pin: a still-running check
      OBSERVES the second one starting.

      The window is a poll deadline, not a fixed hold: the first check
      waits up to 5 s for the second's start marker, so pass/fail does
      not depend on the second child's spawn landing inside a 300 ms
      hold - a runner under load used to fail the fixed window (spawn
      lag past it) while the checks really did overlap. Each child owns
      its own marker file, so no read-modify-write can lose a sibling's
      update.
   */
  const markerDir = path.join(os.tmpdir(), `dxct-preflight-lane-${process.pid}-${Date.now()}`);
  const head = (ownFile, siblingFile) => `
    const fs = await import("node:fs");
    const own = ${JSON.stringify(ownFile)};
    const sibling = ${JSON.stringify(siblingFile)};
    const write = (fields) => fs.writeFileSync(own, JSON.stringify(fields));
    const siblingStarted = () => {
      try {
        return fs.existsSync(sibling) && JSON.parse(fs.readFileSync(sibling, "utf8")).start > 0;
      } catch {
        return false;
      }
    };
  `;
  const lead = `
    ${head(path.join(markerDir, "A.marker.json"), path.join(markerDir, "B.marker.json"))}
    write({ start: Date.now(), end: 0, sawSibling: false });
    const deadline = Date.now() + 5000;
    while (Date.now() < deadline) {
      if (siblingStarted()) break;
      await new Promise((resolve) => setTimeout(resolve, 10));
    }
    write({ ...JSON.parse(fs.readFileSync(own, "utf8")), end: Date.now(), sawSibling: siblingStarted() });
    process.exit(0);
  `;
  const follower = `
    ${head(path.join(markerDir, "B.marker.json"), path.join(markerDir, "A.marker.json"))}
    write({ start: Date.now(), end: Date.now() });
    process.exit(0);
  `;
  try {
    fs.mkdirSync(markerDir, { recursive: true });
    const results = await runChecks([
      { name: "slow-a", command: `node "${writeTempScript(lead)}"` },
      { name: "slow-b", command: `node "${writeTempScript(follower)}"` }
    ]);
    assert.ok(
      results.every((result) => result.ok),
      `both lane children must run cleanly: ${JSON.stringify(results.map((result) => ({ name: result.name, ok: result.ok, output: result.output })))}`
    );
    const state = JSON.parse(fs.readFileSync(path.join(markerDir, "A.marker.json"), "utf8"));
    assert.ok(
      state.sawSibling,
      "a second check must overlap a still-running one, not queue behind it"
    );
  } finally {
    fs.rmSync(markerDir, { recursive: true, force: true });
  }
});

test("main: skipping every check refuses to scan nothing", async () => {
  const all = buildChecks().map((check) => check.name);
  const exitCode = await main([`--skip=${all.join(",")}`]);
  assert.equal(exitCode, 1, "empty check set must exit 1");
});

test("main: unknown --skip name throws before spawning", async () => {
  await assert.rejects(() => main(["--skip=definitely-not-a-check"]), /unknown --skip name/);
});

test("main: unknown --only name throws before spawning", async () => {
  await assert.rejects(() => main(["--only=definitely-not-a-check"]), /unknown --only name/);
});

test("main: --skip and --only are mutually exclusive", async () => {
  await assert.rejects(() => main(["--skip=t11-check", "--only=t11-check"]), /mutually exclusive/);
});

test("main: --only runs just the selected checks", async () => {
  const pass = writeTempScript("console.log('selected'); process.exit(0);");
  const fail = writeTempScript("console.error('unselected-ran-marker'); process.exit(1);");
  const logged = [];
  const originalLog = console.log;
  console.log = (line) => logged.push(line);
  try {
    const exitCode = await main(["--only=selected-a,selected-b"], [
      { name: "selected-a", command: `node "${pass}"` },
      { name: "selected-b", command: `node "${pass}"` },
      { name: "unselected", command: `node "${fail}"` }
    ]);
    assert.equal(exitCode, 0, "selected checks must pass");
    const output = logged.join("\n");
    assert.ok(output.includes("ok   selected-a"), `selected check must run: ${output}`);
    assert.ok(output.includes("ok   selected-b"), `selected check must run: ${output}`);
    assert.ok(output.includes("2/2 passed"), `summary must count only the selection: ${output}`);
    assert.ok(!output.includes("unselected"), `unselected check must not run: ${output}`);
  } finally {
    console.log = originalLog;
  }
});

test("main: injected checks drive the happy path and exit 0", async () => {
  const pass = writeTempScript("console.log('fine'); process.exit(0);");
  const logged = [];
  const originalLog = console.log;
  console.log = (line) => logged.push(line);
  try {
    const exitCode = await main([], [
      { name: "stub-pass", command: `node "${pass}"` }
    ]);
    assert.equal(exitCode, 0, "all-pass set must exit 0");
    const output = logged.join("\n");
    assert.ok(output.includes("ok   stub-pass"), `success line must report the check: ${output}`);
    assert.ok(output.includes("1/1 passed"), `summary must count the checks: ${output}`);
  } finally {
    console.log = originalLog;
  }
});

test("cache keys ignore files outside a check's declared inputs", () => {
  const baseContext = {
    node: "node",
    platform: "linux",
    arch: "x64",
    npm: "npm",
    dotnet: "dotnet",
    dependencies: "deps",
    docsApi: "api",
    files: ["package.json"]
  };
  const check = { name: "scoped", command: "node scoped.mjs", cachePaths: ["package.json"] };
  const unrelatedContext = { ...baseContext, files: ["package.json", "README.md"] };
  assert.equal(cacheKeyForCheck(check, baseContext), cacheKeyForCheck(check, unrelatedContext));
});

test("cache keys include the cache and check implementation", () => {
  const context = {
    node: "node",
    platform: "linux",
    arch: "x64",
    files: [],
    implementation: "cache-v1"
  };
  const check = { name: "self-invalidating", command: "node check.mjs" };
  assert.notEqual(
    cacheKeyForCheck(check, context),
    cacheKeyForCheck(check, { ...context, implementation: "cache-v2" })
  );
});

test("cache keys are isolated by checkout", () => {
  const context = {
    node: "node",
    platform: "linux",
    arch: "x64",
    files: [],
    checkout: "checkout-a"
  };
  const check = { name: "checkout-isolated", command: "node check.mjs" };
  assert.notEqual(
    cacheKeyForCheck(check, context),
    cacheKeyForCheck(check, { ...context, checkout: "checkout-b" })
  );
});

test("package cache keys include git index state", () => {
  const context = {
    node: "node",
    platform: "linux",
    arch: "x64",
    npm: "npm",
    dotnet: "dotnet",
    dependencies: "deps",
    docsApi: "api",
    files: ["package.json"],
    gitTracked: "index-a"
  };
  const check = { name: "package", command: "npm pack", cacheGitTracked: true };
  assert.notEqual(
    cacheKeyForCheck(check, context),
    cacheKeyForCheck(check, { ...context, gitTracked: "index-b" })
  );
});

test("cache keys include supported environment overrides", () => {
  const context = {
    node: "node",
    platform: "linux",
    arch: "x64",
    npm: "npm",
    dotnet: "dotnet",
    dependencies: "deps",
    docsApi: "api",
    files: ["package.json"]
  };
  const check = {
    name: "scoped",
    command: "node scoped.mjs",
    cacheEnvironment: ["THEME_TOKEN_ROOTS", "DOCS_API_DIR"],
    cachePaths: ["package.json"]
  };
  const original = process.env.THEME_TOKEN_ROOTS;
  const originalDocsApi = process.env.DOCS_API_DIR;
  try {
    delete process.env.THEME_TOKEN_ROOTS;
    const unset = cacheKeyForCheck(check, context);
    process.env.THEME_TOKEN_ROOTS = "fixture";
    assert.notEqual(unset, cacheKeyForCheck(check, context));
    delete process.env.THEME_TOKEN_ROOTS;
    delete process.env.DOCS_API_DIR;
    const docsUnset = cacheKeyForCheck(check, context);
    process.env.DOCS_API_DIR = "";
    assert.notEqual(docsUnset, cacheKeyForCheck(check, context));
  } finally {
    if (original === undefined) delete process.env.THEME_TOKEN_ROOTS;
    else process.env.THEME_TOKEN_ROOTS = original;
    if (originalDocsApi === undefined) delete process.env.DOCS_API_DIR;
    else process.env.DOCS_API_DIR = originalDocsApi;
  }
});

test("cache keys include contents at an overridden path", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-override-"));
  const overrideDirectory = path.join(directory, path.delimiter === ":" ? "api:model" : "api-model");
  fs.mkdirSync(overrideDirectory);
  const file = path.join(overrideDirectory, "fixture.txt");
  const context = {
    node: "node",
    platform: "linux",
    arch: "x64",
    npm: "npm",
    dotnet: "dotnet",
    dependencies: "deps",
    docsApi: "api",
    files: []
  };
  const check = {
    name: "override",
    command: "node override.mjs",
    cacheEnvironment: ["DOCS_API_DIR"]
  };
  const original = process.env.DOCS_API_DIR;
  try {
    fs.writeFileSync(file, "first");
    process.env.DOCS_API_DIR = overrideDirectory;
    const first = cacheKeyForCheck(check, context);
    fs.writeFileSync(file, "second");
    assert.notEqual(first, cacheKeyForCheck(check, context));
  } finally {
    if (original === undefined) delete process.env.DOCS_API_DIR;
    else process.env.DOCS_API_DIR = original;
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

test("cache keys hash declared files and ignore unrelated filesystem state", () => {
  const cacheDirectory = path.join(toolingRoot, "scripts", ".preflight-cache");
  fs.mkdirSync(cacheDirectory, { recursive: true });
  const root = fs.mkdtempSync(path.join(cacheDirectory, "contract-"));
  const input = path.join(root, "input");
  const unrelated = path.join(root, "unrelated");
  fs.mkdirSync(input);
  fs.mkdirSync(unrelated);
  const file = path.join(input, "ignored.cs");
  const context = {
    node: "node",
    platform: "linux",
    arch: "x64",
    files: []
  };
  const check = { name: "filesystem", command: "node filesystem.mjs", cachePaths: [input] };
  try {
    fs.writeFileSync(file, "first");
    const first = cacheKeyForCheck(check, context);
    fs.writeFileSync(path.join(unrelated, "ignored.cs"), "unrelated");
    assert.equal(first, cacheKeyForCheck(check, context));
    fs.writeFileSync(file, "second");
    const changed = cacheKeyForCheck(check, context);
    assert.notEqual(first, changed);
    fs.rmSync(input, { recursive: true, force: true });
    const missing = cacheKeyForCheck(check, context);
    assert.notEqual(changed, missing);
    fs.mkdirSync(input);
    assert.notEqual(missing, cacheKeyForCheck(check, context));

    /* Freeze tool observations: a policy edit must invalidate SDK gates
       even when the currently observed SDK version has not changed. */
    const sdkPin = path.join(root, "global.json");
    const checks = buildChecks();
    const sdkChecks = checks.filter((candidate) => candidate.cacheDotnet === true || candidate.cacheDotnetTool === true);
    assert.ok(sdkChecks.length > 0);
    const scoped = (candidate) => ({
      ...candidate,
      cacheRuntimeEnvironment: false,
      cachePaths: candidate.cachePaths.map((entry) => path.join(root, entry)),
      cacheExcludes: (candidate.cacheExcludes ?? []).map((entry) => path.join(root, entry))
    });
    const frozenTools = { ...context, dotnet: "9.0.306", docfxTool: "unchanged-docfx", docsApi: "unchanged-api", npm: "unchanged-npm" };
    const observe = () => new Map(sdkChecks.map((candidate) => [candidate.name, cacheKeyForCheck(scoped(candidate), frozenTools)]));
    const unrelatedGate = scoped(checks.find((candidate) => candidate.name === "t11-check"));
    fs.writeFileSync(sdkPin, '{"sdk":{"version":"9.0.306","rollForward":"disable"}}\n');
    const pinned = observe();
    const unrelatedKey = cacheKeyForCheck(unrelatedGate, frozenTools);
    fs.writeFileSync(sdkPin, '{"sdk":{"version":"9.0.306","rollForward":"latestPatch"}}\n');
    const policyChanged = observe();
    for (const candidate of sdkChecks) {
      assert.notEqual(pinned.get(candidate.name), policyChanged.get(candidate.name), `${candidate.name} must invalidate when SDK selection policy changes`);
    }
    assert.equal(unrelatedKey, cacheKeyForCheck(unrelatedGate, frozenTools), "an SDK-only edit must not invalidate a non-SDK gate");
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

/*
    One record shape per case, from `git status --porcelain=v1 -z`. The
    rename order is the trap: v1 -z emits `XY NEW\0OLD\0` (destination
    first, original second), so a parser that stores ORIG and skips the
    destination keys the renamed file at its index blob and ages a
    cached pass across worktree edits.
 */
const statusRecordCases = [
  case_("untracked is changed only", {
    output: "?? new.rs",
    changed: ["new.rs"],
    worktreeDirty: []
  }),
  case_("worktree-dirty tracked is both", {
    output: " M src.cs",
    changed: ["src.cs"],
    worktreeDirty: ["src.cs"]
  }),
  case_("index-only change is changed but not worktree-dirty", {
    output: "M  src.cs",
    changed: ["src.cs"],
    worktreeDirty: []
  }),
  case_("staged add is changed but not worktree-dirty", {
    output: "A  src.cs",
    changed: ["src.cs"],
    worktreeDirty: []
  }),
  case_("staged rename flags the destination, not the original", {
    output: "R  moved.cs\0original.cs",
    changed: ["moved.cs"],
    worktreeDirty: []
  }),
  case_("staged rename plus worktree edit flags the destination twice over", {
    output: "RM moved.cs\0original.cs",
    changed: ["moved.cs"],
    worktreeDirty: ["moved.cs"]
  }),
  case_("staged copy behaves like a rename", {
    output: "C  copy.cs\0source.cs",
    changed: ["copy.cs"],
    worktreeDirty: []
  }),
  case_("staged delete is changed without a worktree half", {
    output: "D  gone.cs",
    changed: ["gone.cs"],
    worktreeDirty: []
  }),
  case_("unmerged is both", {
    output: "UU both.cs",
    changed: ["both.cs"],
    worktreeDirty: ["both.cs"]
  })
];

for (const { name, output, changed, worktreeDirty } of statusRecordCases) {
  test(`parseStatusRecords: ${name}`, () => {
    const parsed = parseStatusRecords(output);
    assert.deepEqual([...parsed.changed].sort(), [...changed].sort());
    assert.deepEqual([...parsed.worktreeDirty].sort(), [...worktreeDirty].sort());
  });
}

test("parseStatusRecords: the rename original never leaks in as its own entry", () => {
  const parsed = parseStatusRecords("R  moved.cs\0original.cs\0?? tail.rs\0");
  assert.deepEqual(parsed.changed.sort(), ["moved.cs", "tail.rs"]);
});

test("a tracked root keys from the listings: an ignored file under it does not move the key", async () => {
  /*
      Listing-based identity keys what git sees. This pins the coin the
      module documents on a path the module does NOT auto-exclude (the
      scratch .gitignore ignores `ignored-scratch/` anywhere): an ignored
      file under a git-visible root is invisible to its key, so scratch
      and cache writes near keyed roots never invalidate them. Two
      children: the per-process listing memo must not hide the recompute.
   */
  const repo = scratchRepo();
  const probe = path.join(repo.scripts, "ignored-scratch", "listing-coin-probe.tmp");
  fs.mkdirSync(path.dirname(probe), { recursive: true });
  const observe = () => repo.run(`console.log(module.pathState(${JSON.stringify(repo.scratchScripts)}));`);
  try {
    const before = await observe();
    fs.writeFileSync(probe, "ignored bytes one\n");
    assert.equal(await observe(), before, "an ignored file under a tracked root must not move the key");
  } finally {
    fs.rmSync(probe, { force: true });
  }
});

test("an untracked file under a tracked root moves the key", async () => {
  /*
      The listings are computed once per process, so each key
      observation runs in a child: the probe file must exist before the
      child's identity forms for the child to see it.
   */
  const repo = scratchRepo();
  const probe = path.join(repo.scripts, `listing-untracked-probe-${process.pid}.tmp`);
  const observe = () => repo.run(`console.log(module.pathState(${JSON.stringify(repo.scratchScripts)}));`);
  try {
    const before = await observe();
    fs.writeFileSync(probe, "untracked bytes\n");
    assert.notEqual(await observe(), before, "an untracked file must move the root's key");
  } finally {
    fs.rmSync(probe, { force: true });
  }
});

test("a dirty tracked file inside a tracked root moves the key by content", async () => {
  /*
      Status names the dirty file but two rewrites produce the
      byte-identical status line, so the fold must hash the file's
      bytes: the second rewrite must move the key again. Each
      observation runs in a child so the per-process identity observes
      the edit.
   */
  const repo = scratchRepo();
  const tracked = path.join(repo.scripts, "tracked-fixture.txt");
  const original = fs.readFileSync(tracked, "utf8");
  const observe = () => repo.run(`console.log(module.pathState(${JSON.stringify(repo.scratchScripts)}));`);
  try {
    const before = await observe();
    fs.writeFileSync(tracked, `${original}identity probe one\n`);
    const dirtyOnce = await observe();
    assert.notEqual(dirtyOnce, before, "a dirty tracked file must move a root containing it");
    fs.writeFileSync(tracked, `${original}identity probe two\n`);
    assert.notEqual(await observe(), dirtyOnce, "a second rewrite with an identical status line must move the key again");
  } finally {
    fs.writeFileSync(tracked, original);
  }
});

test("an ignored root still walks the filesystem: its content edits move the key", () => {
  const cacheDirectory = path.join(toolingRoot, "scripts", ".preflight-cache");
  fs.mkdirSync(cacheDirectory, { recursive: true });
  const root = fs.mkdtempSync(path.join(cacheDirectory, "ignored-root-"));
  const file = path.join(root, "scratch.txt");
  try {
    const before = pathState(root);
    fs.writeFileSync(file, "one");
    const afterOne = pathState(root);
    assert.notEqual(afterOne, before, "an ignored root must key its contents");
    fs.writeFileSync(file, "two");
    assert.notEqual(pathState(root), afterOne, "an ignored root must keep moving with its contents");
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

for (const collision of ["directory-file", "sibling-directories"]) {
  test(`filesystem walk preserves content with rounded inode collisions: ${collision}`, (context) => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-inodes-"));
    const first = collision === "directory-file" ? root : path.join(root, "first");
    const second = path.join(root, "second");
    fs.mkdirSync(first, { recursive: true });
    if (collision === "sibling-directories") fs.mkdirSync(second);
    const file = collision === "directory-file" ? second : path.join(second, "scratch.txt");
    fs.writeFileSync(file, "one");
    const originalStat = fs.lstatSync;
    const identities = new Map([[first, 9007199254740992n], [second, 9007199254740993n]]);
    assert.equal(Number(identities.get(first)), Number(identities.get(second)));
    context.mock.method(fs, "lstatSync", (target, options) => {
      const stat = originalStat(target, options);
      const identity = identities.get(target);
      if (identity === undefined) return stat;
      return new Proxy(stat, {
        get(object, property) {
          if (property === "ino") return options?.bigint ? identity : Number(identity);
          const value = Reflect.get(object, property);
          return typeof value === "function" ? value.bind(object) : value;
        }
      });
    });
    try {
      const before = pathState(root);
      fs.writeFileSync(file, "two");
      assert.notEqual(pathState(root), before, "distinct filesystem entries must retain their content in the key");
    } finally {
      context.mock.restoreAll();
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
}

test("filesystem walk stops an ancestor directory symlink cycle", { skip: process.platform === "win32" }, (context) => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-cycle-"));
  const originalRead = fs.readdirSync;
  let directoryReads = 0;
  context.mock.method(fs, "readdirSync", (...args) => {
    directoryReads++;
    return originalRead(...args);
  });
  try {
    fs.mkdirSync(path.join(root, "nested"));
    fs.symlinkSync(root, path.join(root, "nested", "ancestor"));
    const file = path.join(root, "scratch.txt");
    fs.writeFileSync(file, "one");
    const before = pathState(root);
    assert.equal(directoryReads, 2, "each directory must be walked once");
    directoryReads = 0;
    fs.writeFileSync(file, "two");
    assert.notEqual(pathState(root), before);
    assert.equal(directoryReads, 2, "ancestor links must stop before another directory read");
  } finally {
    context.mock.restoreAll();
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test("a committed edit moves the snapshot key where status goes quiet", async () => {
  /*
      The committed half of the snapshot class: status names only
      uncommitted state, so a commit is exactly the change the status
      output stays silent about. The fold must carry the index blobs,
      or a committed edit to a tracked file outside a check's declared
      roots ages the cached pass.

      The probe commits in the scratch repository - the snapshot module
      copy makes it the keyed tree - so the test is hermetic: nothing
      touches the real checkout, and the skip the real-branch probe
      needed (a reset-based cleanup would destroy uncommitted work) no
      longer exists. A mid-test kill leaves the temp directory for the
      suite's after() to remove.
   */
  const repo = scratchRepo();
  const probeRelative = ".dxct-commit-probe.tmp";
  const probe = path.join(repo.root, probeRelative);
  const observe = () =>
    repo.run(`
        const check = { name: "snapshot", command: "node snapshot.mjs", cacheTrackedFiles: true };
        const key = () => module.cacheKeyForCheck(check, { node: "node", platform: "linux", arch: "x64", files: [] });
        console.log(key());
      `);
  try {
    const before = await observe();
    fs.writeFileSync(probe, "committed bytes\n");
    repo.git("add", probeRelative);
    repo.git("commit", "-q", "--no-verify", "-m", "snapshot commit probe");
    const statusAfter = repo.git("status", "--porcelain=v1", "--untracked-files=all", "--", probeRelative).trim();
    assert.equal(statusAfter, "", "the probe must be committed-clean: status names nothing");
    assert.notEqual(await observe(), before, "a committed edit must move the snapshot key");
  } finally {
    fs.rmSync(probe, { force: true });
  }
});

test("a staged rewrite moves the snapshot key where the status line stays identical", async () => {
  /*
      Thread-two class: an index-only change (`A `) contributes its
      status line and nothing else, and a later staged rewrite of the
      same path produces the byte-identical line again - so the snapshot
      must fold the staged content, or node-tests reads a file outside
      its declared roots through a cached pass from before the rewrite.
      The probe is a dot-file at the scratch root; the whole repository
      is the suite's temp directory, so a crash leaves nothing behind.
   */
  const repo = scratchRepo();
  const probeRelative = ".dxct-snapshot-probe.tmp";
  const probe = path.join(repo.root, probeRelative);
  const observe = () =>
    repo.run(`
        const check = { name: "snapshot", command: "node snapshot.mjs", cacheTrackedFiles: true };
        const key = () => module.cacheKeyForCheck(check, { node: "node", platform: "linux", arch: "x64", files: [] });
        console.log(key());
      `);
  try {
    fs.writeFileSync(probe, "staged content one\n");
    repo.git("add", probeRelative);
    const stagedOnce = await observe();
    fs.writeFileSync(probe, "staged content two\n");
    repo.git("add", probeRelative);
    const statusAfter = repo.git("status", "--porcelain=v1", "--", probeRelative).trim();
    assert.equal(statusAfter, `A  ${probeRelative}`, "the probe must be index-only dirty");
    assert.notEqual(
      await observe(),
      stagedOnce,
      "a second staged rewrite with an identical status line must move the snapshot key"
    );
  } finally {
    try {
      repo.git("rm", "-q", "-f", "--cached", probeRelative);
    } catch {
      /* The probe never reached the index; removal below is enough. */
    }
    fs.rmSync(probe, { force: true });
  }
});

test("a dirty tracked file outside declared roots moves keys by content", async () => {
  /*
      The repo snapshot backs checks that read files outside their declared
      roots. Status encodes a dirty tracked file as ` M <path>`, which is
      byte-identical across two rewrites of the same file - so keying the
      snapshot on the status output alone ages a cached pass when the
      second rewrite lands after the first one was cached. The snapshot
      must fold each flagged file's content (under the size cap) into the
      hash; each observation runs in a child so the per-process git
      identity observes the edit from a clean index. The probe dirties the
      scratch repository's own tracked .gitattributes - the keyed tree is
      the scratch tree, so no real tracked state is ever touched.
   */
  const repo = scratchRepo();
  const tracked = path.join(repo.root, ".gitattributes");
  const original = fs.readFileSync(tracked, "utf8");
  const observe = () =>
    repo.run(`
        const check = { name: "snapshot", command: "node snapshot.mjs", cacheTrackedFiles: true };
        const key = () => module.cacheKeyForCheck(check, { node: "node", platform: "linux", arch: "x64", files: [] });
        console.log(key());
      `);
  let clean = "";
  try {
    clean = await observe();
    fs.writeFileSync(tracked, `${original}identity probe one\n`);
    const dirtyOnce = await observe();
    assert.notEqual(dirtyOnce, clean, "a dirty tracked file must move the snapshot key");
    fs.writeFileSync(tracked, `${original}identity probe two\n`);
    const dirtyAgain = await observe();
    assert.notEqual(
      dirtyAgain,
      dirtyOnce,
      "a second rewrite with identical status output must still move the snapshot key"
    );
  } finally {
    fs.writeFileSync(tracked, original);
  }
  assert.equal(await observe(), clean, "restoring the bytes must restore the key");
});

test("a pathspec-scoped listing keys only the files under the pathspec", async () => {
  /*
      The tracked-listing keying is how a whole-tree gate stops paying for
      edits it never reads: the listing is `ls-files -s` restricted to the
      check's pathspecs (here, the markdown gate's own set), so an edit
      under them moves the key, an edit anywhere else does not, and an
      untracked draft under them does not either - the gates keyed this
      way read tracked files only. The probe stages two markdown files in
      the scratch repository; the dirty-worktree byte fold is what makes
      mdA's rewrite re-key.
   */
  const repo = scratchRepo();
  const mdA = path.join(repo.root, "notes-a.md");
  const mdB = path.join(repo.root, "nested", "notes-b.md");
  fs.mkdirSync(path.join(repo.root, "nested"), { recursive: true });
  fs.writeFileSync(mdA, "one\n");
  fs.writeFileSync(mdB, "one\n");
  repo.git("add", "-A");
  const txt = path.join(repo.root, "tooling~", "scripts", "tracked-fixture.txt");
  const txtOriginal = fs.readFileSync(txt, "utf8");
  const observe = (pathspecs) =>
    repo.run(`
        const check = { name: "listing", command: "node listing.mjs", cacheTrackedPaths: ${JSON.stringify(pathspecs)} };
        const key = () => module.cacheKeyForCheck(check, { node: "node", platform: "linux", arch: "x64", files: [] });
        console.log(key());
      `);
  const gateSpecs = ["*.md", ":!.llm/skills/index.md", ":!nested/notes-b.md", ".prettierrc.json", ".editorconfig"];
  const untrackedDraft = path.join(repo.root, "untracked-draft.md");
  try {
    const clean = await observe(gateSpecs);
    fs.writeFileSync(mdA, "two\n");
    const mdDirty = await observe(gateSpecs);
    assert.notEqual(mdDirty, clean, "an edit under the pathspec must move the key");
    fs.writeFileSync(txt, `${txtOriginal}unrelated\n`);
    assert.equal(
      await observe(gateSpecs),
      mdDirty,
      "an edit outside the pathspec must not move the key"
    );
    fs.writeFileSync(untrackedDraft, "untracked scratch\n");
    assert.equal(
      await observe(gateSpecs),
      mdDirty,
      "an untracked file under the pathspec must not move the key: the gates keyed here read tracked files only"
    );

    const withExclusion = await observe(gateSpecs);
    fs.writeFileSync(mdB, "excluded\n");
    assert.equal(
      await observe(gateSpecs),
      withExclusion,
      "an edit under an exclude pathspec must not move the key"
    );
    fs.writeFileSync(mdA, "three\n");
    assert.notEqual(
      await observe(gateSpecs),
      withExclusion,
      "an edit under a non-excluded pathspec must move the key"
    );
  } finally {
    fs.rmSync(untrackedDraft, { force: true });
    fs.rmSync(mdA, { force: true });
    fs.rmSync(mdB, { force: true });
    fs.writeFileSync(txt, txtOriginal);
  }
});

test("the markdown gate keys its own files, not the whole snapshot", () => {
  const mdCheck = buildChecks().find((check) => check.name === "lint-md-format");
  assert.deepEqual(
    mdCheck.cacheTrackedPaths,
    ["*.md", ":!.llm/skills/index.md", ".prettierrc.json", ".editorconfig"],
    "the gate reads tracked markdown (index exempt) and both config files prettier resolves"
  );
  assert.deepEqual(
    mdCheck.cachePaths,
    ["tooling~/scripts/lint-md-format.mjs", ".prettierrc.json", ".editorconfig"],
    "the configs key the gate directly, not only through the tracked listing"
  );
  assert.equal(mdCheck.cacheTrackedFiles, undefined, "the whole-snapshot key would re-run the gate on any edit");
  assert.equal(mdCheck.cacheDependencies, true, "prettier runs from node_modules; its tree must key the gate");
  const context = {
    node: "node", platform: process.platform, arch: process.arch, files: [],
    dependencies: "dependencies", git: "git-version", gitExecutable: "git-bytes", gitConfig: "git-config"
  };
  const original = cacheKeyForCheck(mdCheck, context);
  for (const input of ["git", "gitExecutable", "gitConfig"]) {
    assert.notEqual(original, cacheKeyForCheck(mdCheck, { ...context, [input]: "changed" }), `markdown must key ${input}`);
  }
});

test("the run's key computer answers the live key and moves with its inputs", () => {
  const cacheDirectory = path.join(toolingRoot, "scripts", ".preflight-cache");
  fs.mkdirSync(cacheDirectory, { recursive: true });
  const root = fs.mkdtempSync(path.join(cacheDirectory, "computer-"));
  const shared = path.join(root, "shared");
  const own = path.join(root, "own");
  fs.mkdirSync(shared);
  fs.mkdirSync(own);
  const sharedFile = path.join(shared, "common.cs");
  const ownFile = path.join(own, "own.cs");
  const context = {
    node: "node",
    platform: "linux",
    arch: "x64",
    files: []
  };
  const sharedCheck = { name: "shared", command: "node shared.mjs", cachePaths: [shared] };
  const ownCheck = { name: "own", command: "node own.mjs", cachePaths: [own, shared] };
  try {
    fs.writeFileSync(sharedFile, "one");
    fs.writeFileSync(ownFile, "one");
    const computer = createCacheKeyComputer();

    /*
        The computer's answer must be the live path's answer - a drift
        between them would let main() key the cache on something no
        contract test ever pinned.
     */
    assert.equal(computer.keyFor(sharedCheck, context), cacheKeyForCheck(sharedCheck, context));

    /*
        The memo is per fresh computer: a file edit after one key must move
        a NEW computer's key (each preflight run builds a new one), while
        the same run's repeat reads stay served from the memo.
     */
    const before = computer.keyFor(sharedCheck, context);
    assert.equal(computer.keyFor(sharedCheck, context), before);
    fs.writeFileSync(sharedFile, "two");
    assert.notEqual(
      createCacheKeyComputer().keyFor(sharedCheck, context),
      before,
      "a fresh computer must observe the edited input"
    );

    /*
        The memo is keyed by root, so a check declaring an extra root pays
        for that root's own state, not for whichever check walked first.
     */
    fs.writeFileSync(ownFile, "three");
    const ownKey = computer.keyFor(ownCheck, context);
    fs.writeFileSync(ownFile, "four");
    assert.notEqual(
      createCacheKeyComputer().keyFor(ownCheck, context),
      ownKey,
      "the second declared root's content must move the key"
    );

    /*
        Two checks on one root with different excludes key differently:
        the memo key is root AND excludes, so a future simplification to
        keying by root alone cannot serve one check the other's state.
     */
    const excludedCheck = {
      name: "excluded",
      command: "node excluded.mjs",
      cachePaths: [root],
      cacheExcludes: [own]
    };
    assert.notEqual(computer.keyFor(excludedCheck, context), ownKey);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test("cache keys include nested names and empty directories", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-names-"));
  const context = {
    node: "node",
    platform: "linux",
    arch: "x64",
    files: []
  };
  const check = { name: "names", command: "node names.mjs", cachePaths: [directory] };
  try {
    const original = path.join(directory, "original.cs");
    const renamed = path.join(directory, "renamed.cs");
    fs.writeFileSync(original, "same contents");
    const initial = cacheKeyForCheck(check, context);
    fs.renameSync(original, renamed);
    const afterRename = cacheKeyForCheck(check, context);
    assert.notEqual(initial, afterRename);
    fs.mkdirSync(path.join(directory, "empty"));
    assert.notEqual(afterRename, cacheKeyForCheck(check, context));
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

test("dependency state includes installed dependency and baked fallback contents", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-dependencies-"));
  const nodeModules = path.join(directory, "node_modules");
  const bakedMcp = path.join(directory, "baked-mcp");
  const file = path.join(bakedMcp, "dependency.js");
  try {
    fs.mkdirSync(bakedMcp, { recursive: true });
    fs.writeFileSync(file, "first");
    const first = dependencyState(nodeModules, bakedMcp);
    fs.writeFileSync(file, "second");
    assert.notEqual(first, dependencyState(nodeModules, bakedMcp));
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

test("resolved executable state includes file contents", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-executable-"));
  const executable = path.join(directory, "tool");
  try {
    fs.writeFileSync(executable, "first");
    fs.chmodSync(executable, 0o755);
    const first = resolvedExecutableState(executable);
    fs.writeFileSync(executable, "second");
    assert.notEqual(first, resolvedExecutableState(executable));
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

test("resolved executable state fingerprints only the first relative PATH match", () => {
  const cacheDirectory = path.join(toolingRoot, "scripts", ".preflight-cache");
  fs.mkdirSync(cacheDirectory, { recursive: true });
  const directory = fs.mkdtempSync(path.join(cacheDirectory, "executable-path-"));
  const firstDirectory = path.join(directory, "first");
  const secondDirectory = path.join(directory, "second");
  fs.mkdirSync(firstDirectory);
  fs.mkdirSync(secondDirectory);
  const command = process.platform === "win32" ? "preflight-tool.cmd" : "preflight-tool";
  const first = path.join(firstDirectory, command);
  const second = path.join(secondDirectory, command);
  const originalPath = process.env.PATH;
  try {
    fs.writeFileSync(first, "first");
    fs.writeFileSync(second, "shadowed");
    fs.chmodSync(first, 0o755);
    fs.chmodSync(second, 0o755);
    const repoRoot = path.resolve(toolingRoot, "..");
    process.env.PATH = [firstDirectory, secondDirectory]
      .map((entry) => path.relative(repoRoot, entry))
      .join(path.delimiter);
    const initial = resolvedExecutableState(command);
    fs.writeFileSync(second, "changed");
    assert.equal(initial, resolvedExecutableState(command));
    fs.writeFileSync(first, "changed");
    assert.notEqual(initial, resolvedExecutableState(command));
  } finally {
    if (originalPath === undefined) delete process.env.PATH;
    else process.env.PATH = originalPath;
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

test("Windows executable lookup includes the child working directory", { skip: process.platform !== "win32" }, () => {
  const repoRoot = path.resolve(toolingRoot, "..");
  const name = `preflight-tool-${process.pid}.cmd`;
  const executable = path.join(repoRoot, name);
  const originalPath = process.env.PATH;
  try {
    fs.writeFileSync(executable, "first");
    process.env.PATH = "";
    assert.notEqual(resolvedExecutableState(name), "missing");
  } finally {
    if (originalPath === undefined) delete process.env.PATH;
    else process.env.PATH = originalPath;
    fs.rmSync(executable, { force: true });
  }
});

test("npm executable state includes the resolved package contents", { skip: process.platform === "win32" }, () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-npm-package-"));
  const packageRoot = path.join(directory, "node_modules", "npm");
  const bin = path.join(packageRoot, "bin");
  const cli = path.join(bin, "npm-cli.js");
  const executable = path.join(directory, "npm");
  const moduleFile = path.join(packageRoot, "lib", "module.js");
  try {
    fs.mkdirSync(bin, { recursive: true });
    fs.mkdirSync(path.dirname(moduleFile), { recursive: true });
    fs.writeFileSync(path.join(packageRoot, "package.json"), '{"name":"npm"}\n');
    fs.writeFileSync(cli, "first");
    fs.chmodSync(cli, 0o755);
    fs.writeFileSync(moduleFile, "first");
    fs.symlinkSync(cli, executable);
    const first = npmExecutableState(executable);
    fs.writeFileSync(moduleFile, "second");
    assert.notEqual(first, npmExecutableState(executable));
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

test("dotnet tool state follows a custom CLI home", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-dotnet-home-"));
  const originalCliHome = process.env.DOTNET_CLI_HOME;
  const originalNugetPackages = process.env.NUGET_PACKAGES;
  try {
    process.env.DOTNET_CLI_HOME = directory;
    delete process.env.NUGET_PACKAGES;
    const manifest = JSON.parse(
      fs.readFileSync(path.resolve(toolingRoot, "..", ".config", "dotnet-tools.json"), "utf8")
    );
    const version = manifest.tools.docfx.version;
    const storeFile = path.join(directory, ".dotnet", "tools", ".store", "docfx", "tool.dll");
    const packageFile = path.join(directory, ".nuget", "packages", "docfx", version, "tool.dll");
    fs.mkdirSync(path.dirname(storeFile), { recursive: true });
    fs.mkdirSync(path.dirname(packageFile), { recursive: true });
    fs.writeFileSync(storeFile, "first");
    fs.writeFileSync(packageFile, "first");
    const first = dotnetToolState("docfx");
    fs.writeFileSync(packageFile, "second");
    assert.notEqual(first, dotnetToolState("docfx"));
  } finally {
    if (originalCliHome === undefined) delete process.env.DOTNET_CLI_HOME;
    else process.env.DOTNET_CLI_HOME = originalCliHome;
    if (originalNugetPackages === undefined) delete process.env.NUGET_PACKAGES;
    else process.env.NUGET_PACKAGES = originalNugetPackages;
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

test("npm config state includes user config contents", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-npm-config-"));
  const config = path.join(directory, "npmrc");
  const original = process.env.NPM_CONFIG_USERCONFIG;
  try {
    process.env.NPM_CONFIG_USERCONFIG = config;
    fs.writeFileSync(config, "user-agent=first\n");
    const first = npmConfigState();
    fs.writeFileSync(config, "user-agent=second\n");
    assert.notEqual(first.files, npmConfigState().files);
  } finally {
    if (original === undefined) delete process.env.NPM_CONFIG_USERCONFIG;
    else process.env.NPM_CONFIG_USERCONFIG = original;
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

test("path state follows symlink target changes", { skip: process.platform === "win32" }, () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-symlink-"));
  const target = path.join(directory, "target.yml");
  const link = path.join(directory, "linked.yml");
  try {
    fs.writeFileSync(target, "first");
    fs.symlinkSync(target, link);
    const first = pathState(link);
    fs.writeFileSync(target, "second");
    assert.notEqual(first, pathState(link));
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

test("environment inputs are scoped to checks that read them", () => {
  const context = {
    node: "node",
    platform: "linux",
    arch: "x64",
    files: []
  };
  const docs = { name: "docs", command: "node docs.mjs", cacheEnvironment: ["DOCS_API_DIR"] };
  const theme = { name: "theme", command: "node theme.mjs", cacheEnvironment: ["THEME_TOKEN_ROOTS"] };
  const original = process.env.THEME_TOKEN_ROOTS;
  try {
    delete process.env.THEME_TOKEN_ROOTS;
    const docsBefore = cacheKeyForCheck(docs, context);
    const themeBefore = cacheKeyForCheck(theme, context);
    process.env.THEME_TOKEN_ROOTS = "fixture";
    assert.equal(docsBefore, cacheKeyForCheck(docs, context));
    assert.notEqual(themeBefore, cacheKeyForCheck(theme, context));
  } finally {
    if (original === undefined) delete process.env.THEME_TOKEN_ROOTS;
    else process.env.THEME_TOKEN_ROOTS = original;
  }
});

test("runtime environment invalidates checks that inherit it", () => {
  const context = {
    node: "node",
    platform: "linux",
    arch: "x64",
    files: [],
    config: { npm: "npm", git: "git" }
  };
  const runtime = {
    name: "runtime",
    command: "npm test",
    cacheRuntimeEnvironment: true
  };
  const scoped = { name: "scoped", command: "node scoped.mjs" };
  const original = process.env.NODE_OPTIONS;
  try {
    delete process.env.NODE_OPTIONS;
    const runtimeBefore = cacheKeyForCheck(runtime, context);
    const scopedBefore = cacheKeyForCheck(scoped, context);
    process.env.NODE_OPTIONS = "--no-warnings";
    assert.notEqual(runtimeBefore, cacheKeyForCheck(runtime, context));
    assert.equal(scopedBefore, cacheKeyForCheck(scoped, context));
  } finally {
    if (original === undefined) delete process.env.NODE_OPTIONS;
    else process.env.NODE_OPTIONS = original;
  }
});

test("opaque Node preload state disables caching", () => {
  const check = { name: "runtime", command: "node runtime.mjs", cacheRuntimeEnvironment: true };
  const originalOptions = process.env.NODE_OPTIONS;
  const originalPath = process.env.NODE_PATH;
  try {
    delete process.env.NODE_OPTIONS;
    delete process.env.NODE_PATH;
    assert.equal(isCacheableCheck(check), true);
    process.env.NODE_OPTIONS = "--require ./setup.mjs";
    assert.equal(isCacheableCheck(check), false);
    delete process.env.NODE_OPTIONS;
    process.env.NODE_PATH = "./modules";
    assert.equal(isCacheableCheck(check), false);
  } finally {
    if (originalOptions === undefined) delete process.env.NODE_OPTIONS;
    else process.env.NODE_OPTIONS = originalOptions;
    if (originalPath === undefined) delete process.env.NODE_PATH;
    else process.env.NODE_PATH = originalPath;
  }
});

test("main: caches successful checks and --no-cache bypasses the cache", async () => {
  const marker = path.join(fs.mkdtempSync(path.join(os.tmpdir(), "preflight-marker-")), "count");
  const pass = writeTempScript(`import fs from "node:fs"; fs.appendFileSync(${JSON.stringify(marker)}, "x"); process.exit(0);`);
  const check = { name: "cached-check", command: `node "${pass}"` };
  const lines = [];
  const originalLog = console.log;
  const originalCi = process.env.CI;
  delete process.env.CI;
  console.log = (line) => lines.push(String(line));
  try {
    assert.equal(await main([], [check]), 0);
    assert.equal(await main([], [check]), 0);
    assert.ok(lines.some((line) => line.includes("cached cached-check")));
    const countAfterCachedRun = fs.readFileSync(marker, "utf8").length;
    assert.equal(await main(["--no-cache"], [check]), 0);
    assert.equal(fs.readFileSync(marker, "utf8").length, countAfterCachedRun + 1);
  } finally {
    console.log = originalLog;
    if (originalCi === undefined) {
      delete process.env.CI;
    } else {
      process.env.CI = originalCi;
    }
    fs.rmSync(path.dirname(marker), { recursive: true, force: true });
  }
});

test("main: uncached integration checks reject stale passes after environment changes", async () => {
  const marker = path.join(fs.mkdtempSync(path.join(os.tmpdir(), "preflight-uncached-")), "count");
  tempDirs.push(path.dirname(marker));
  const subject = writeTempScript(`
    import fs from "node:fs";
    fs.appendFileSync(${JSON.stringify(marker)}, "x");
    process.exit(process.env.UV_PROJECT_ENVIRONMENT === "broken-environment" ? 1 : 0);
  `);
  const check = { ...buildChecks().find((candidate) => candidate.name === "python-tests"), command: `node "${subject}"` };
  const originalCi = process.env.CI;
  const originalEnvironment = process.env.UV_PROJECT_ENVIRONMENT;
  const originalLog = console.log;
  const originalError = console.error;
  const lines = [];
  delete process.env.CI;
  delete process.env.UV_PROJECT_ENVIRONMENT;
  console.log = console.error = (line) => lines.push(String(line));
  let key;
  try {
    key = cacheKeyForCheck(check);
    assert.equal(writeCacheEntry({ ...check, cache: true }, key), true, "seed a legacy pass");
    assert.equal(readCacheEntry({ ...check, cache: true }, key), true);
    assert.equal(readCacheEntry(check, key), false, "ignore the legacy pass");
    assert.equal(writeCacheEntry(check, key), false, "never save an integration pass");
    assert.equal(await main([], [check]), 0);
    assert.equal(await main([], [check]), 0);
    assert.equal(fs.readFileSync(marker, "utf8"), "xx", "both calls execute the child");
    process.env.UV_PROJECT_ENVIRONMENT = "broken-environment";
    assert.equal(await main([], [check]), 1, "surface the changed child outcome");
    assert.equal(fs.readFileSync(marker, "utf8"), "xxx");
    assert.ok(!lines.some((line) => line.includes("cached python-tests")));
  } finally {
    if (key !== undefined) clearCacheForCheck(check, key);
    console.log = originalLog;
    console.error = originalError;
    if (originalCi === undefined) delete process.env.CI;
    else process.env.CI = originalCi;
    if (originalEnvironment === undefined) delete process.env.UV_PROJECT_ENVIRONMENT;
    else process.env.UV_PROJECT_ENVIRONMENT = originalEnvironment;
  }
});

test("main: early checks overlap cache inspection and both groups report failures", async () => {
  for (const failingGroup of ["early", "keyed"]) {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-early-"));
    tempDirs.push(directory);
    const started = path.join(directory, "started");
    const finished = path.join(directory, "finished");
    const early = writeTempScript(`
      import fs from "node:fs";
      fs.writeFileSync(${JSON.stringify(started)}, "started");
      process.exit(${failingGroup === "early" ? 1 : 0});
    `);
    const keyed = writeTempScript(`
      import fs from "node:fs";
      fs.writeFileSync(${JSON.stringify(finished)}, "finished");
      process.exit(${failingGroup === "keyed" ? 1 : 0});
    `);
    const probe = writeTempScript(`
      import fs from "node:fs";
      const deadline = Date.now() + 5000;
      const signal = new Int32Array(new SharedArrayBuffer(4));
      while (!fs.existsSync(${JSON.stringify(started)}) && Date.now() < deadline) Atomics.wait(signal, 0, 0, 10);
      process.exit(fs.existsSync(${JSON.stringify(started)}) ? 0 : 1);
    `);
    let observedStart = false;
    let inspected = false;
    const check = {
      name: "key-inspection",
      command: `node "${keyed}"`,
      get cachePaths() {
        if (!inspected) {
          inspected = true;
          try {
            execFileSync(process.execPath, [probe]);
            observedStart = true;
          } catch { /* Report ordering through the assertion after main settles. */ }
        }
        return [];
      }
    };
    const originalCi = process.env.CI;
    delete process.env.CI;
    try {
      assert.equal(await main([], [
        { name: "early-start", command: `node "${early}"`, cache: false, startBeforeCache: true },
        check
      ]), 1, `report the ${failingGroup} failure`);
      assert.ok(observedStart, "the early child must start before cached inputs are inspected");
      assert.ok(fs.existsSync(finished), "await the keyed child even when the early child fails");
    } finally {
      clearCacheForCheck(check, cacheKeyForCheck(check));
      if (originalCi === undefined) delete process.env.CI;
      else process.env.CI = originalCi;
    }
  }
});

test("cache storage rejects malformed entries without failing writes", () => {
  const check = {
    name: `malformed-cache-storage-${process.pid}-${Date.now()}`,
    command: "node malformed-cache.mjs"
  };
  const key = "malformed-key";
  const entryPath = cachePathForCheck(check, key);
  const cacheDirectory = path.dirname(entryPath);
  const targetPath = `${entryPath}.target`;
  const originalCi = process.env.CI;
  delete process.env.CI;
  try {
    fs.mkdirSync(cacheDirectory, { recursive: true });

    fs.writeFileSync(entryPath, "not json");
    assert.equal(readCacheEntry(check, key), false);
    assert.equal(writeCacheEntry(check, key), true);
    assert.equal(readCacheEntry(check, key), true);
    fs.rmSync(entryPath, { force: true });

    fs.mkdirSync(entryPath);
    assert.equal(readCacheEntry(check, key), false);
    assert.equal(writeCacheEntry(check, key), false);
    fs.rmSync(entryPath, { recursive: true, force: true });

    if (process.platform !== "win32") {
      fs.writeFileSync(targetPath, "not a cache entry");
      fs.symlinkSync(targetPath, entryPath);
      assert.equal(readCacheEntry(check, key), false);
      assert.equal(writeCacheEntry(check, key), true);
      assert.equal(fs.lstatSync(entryPath).isFile(), true);
      assert.equal(JSON.parse(fs.readFileSync(entryPath, "utf8")).key, key);
    }
  } finally {
    if (originalCi === undefined) delete process.env.CI;
    else process.env.CI = originalCi;
    fs.rmSync(entryPath, { force: true });
    fs.rmSync(targetPath, { force: true });
  }
});

test("cache cleanup removes an entry link without following its target", { skip: process.platform === "win32" }, () => {
  const check = {
    name: `cache-cleanup-${process.pid}-${Date.now()}`,
    command: "node cleanup.mjs"
  };
  const key = "cleanup-key";
  const entryPath = cachePathForCheck(check, key);
  const targetPath = `${entryPath}.target`;
  try {
    fs.mkdirSync(path.dirname(entryPath), { recursive: true });
    fs.writeFileSync(targetPath, "keep");
    fs.symlinkSync(targetPath, entryPath);
    clearCacheForCheck(check, key);
    assert.equal(fs.existsSync(entryPath), false);
    assert.equal(fs.existsSync(targetPath), true);
  } finally {
    fs.rmSync(entryPath, { force: true });
    fs.rmSync(targetPath, { force: true });
  }
});

test("main: keeps cache when selected checks are skipped", async () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "preflight-marker-"));
  const selectedMarker = path.join(directory, "selected");
  const skippedMarker = path.join(directory, "skipped");
  const first = writeTempScript(`import fs from "node:fs"; fs.appendFileSync(${JSON.stringify(selectedMarker)}, "x"); process.exit(0);`);
  const second = writeTempScript(`import fs from "node:fs"; fs.appendFileSync(${JSON.stringify(skippedMarker)}, "y"); process.exit(0);`);
  const checks = [
    { name: "cached-selected", command: `node "${first}"` },
    { name: "skipped", command: `node "${second}"` }
  ];
  const originalLog = console.log;
  const originalCi = process.env.CI;
  delete process.env.CI;
  console.log = () => {};
  try {
    assert.equal(await main(["--skip=skipped"], checks), 0);
    assert.equal(fs.readFileSync(selectedMarker, "utf8"), "x");
    assert.equal(fs.existsSync(skippedMarker), false);
    assert.equal(await main(["--skip=skipped"], checks), 0);
    assert.equal(fs.readFileSync(selectedMarker, "utf8"), "x");
    assert.equal(fs.existsSync(skippedMarker), false);
  } finally {
    console.log = originalLog;
    if (originalCi === undefined) delete process.env.CI;
    else process.env.CI = originalCi;
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

test("main: failing check prints its output and exits 1", async () => {
  const pass = writeTempScript("process.exit(0);");
  const fail = writeTempScript("console.error('burst-marker'); process.exit(9);");
  const lines = [];
  const originalError = console.error;
  const originalLog = console.log;
  console.error = (line) => lines.push(String(line));
  console.log = (line) => lines.push(String(line));
  try {
    const exitCode = await main([], [
      { name: "stub-pass", command: `node "${pass}"` },
      { name: "stub-fail", command: `node "${fail}"` }
    ]);
    assert.equal(exitCode, 1, "any failure must exit 1");
    const report = lines.join("\n");
    assert.ok(report.includes("FAIL stub-fail"), "status line must flag the failing check");
    assert.ok(report.includes("stub-fail failed"), "failure header must name the check");
    assert.ok(report.includes("burst-marker"), "failure output must be printed");
    assert.ok(report.includes("1/2 passed"), "summary must count both checks");
  } finally {
    console.error = originalError;
    console.log = originalLog;
  }
});
