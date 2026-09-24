/*
    Contract tests for tooling~/scripts/preflight.mjs.

    Data-driven over the runner contract: the default check set is non-empty
    with unique names, --skip filters by exact name and refuses unknown names,
    runCollect reports per-check pass/fail with captured output, and main
    refuses to scan nothing.
*/
import { test, after } from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const toolingRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const preflightPath = path.join(toolingRoot, "scripts", "preflight.mjs");
const { buildChecks, parseSkip, runChecks, main } = await import(
  pathToFileURL(preflightPath).href
);
const {
  cacheKeyForCheck,
  cachePathForCheck,
  dependencyState,
  dotnetToolState,
  isCacheableCheck,
  npmConfigState,
  npmExecutableState,
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
    "node-tests",
    "t11-check",
    "package-validate",
    "docs-guides",
    "compat-check",
    "lint-linq-production",
    "lint-docs-catalog"
  ];
  for (const name of expected) {
    assert.ok(names.includes(name), `expected check '${name}' in the default set`);
  }
  for (const check of checks) {
    if (check.name !== "compat-check") {
      assert.equal(check.cacheRuntimeEnvironment, true, `${check.name} must key inherited runtime state`);
    }
  }
  const packageCheck = checks.find((check) => check.name === "package-validate");
  assert.equal(packageCheck.cacheGitTracked, true);
  assert.equal(packageCheck.cacheNpm, true);
  assert.equal(packageCheck.cacheGzip, true);
  assert.equal(packageCheck.cacheNpmConfig, true);
  assert.equal(packageCheck.cacheGitConfig, true);
  assert.equal(packageCheck.cacheRuntimeEnvironment, true);
  assert.ok(packageCheck.cachePaths.includes(".npmignore"));
  const nodeCheck = checks.find((check) => check.name === "node-tests");
  assert.equal(nodeCheck.cacheTrackedFiles, true);
  assert.equal(nodeCheck.cacheDependencies, true);
  assert.equal(nodeCheck.cacheGzip, true);
  assert.equal(nodeCheck.cacheBash, true);
  assert.deepEqual(nodeCheck.cacheEnvironment, ["BASH_ENV"]);
  assert.ok(nodeCheck.cacheExcludes.includes("tooling~/scripts/.preflight-cache"));
  const docsCheck = checks.find((check) => check.name === "docs-guides");
  assert.equal(docsCheck.cacheDotnetTool, true);
  assert.equal(docsCheck.cacheDocsApi, true);
  assert.ok(docsCheck.cachePaths.includes("tooling~/docs/toc.yml"));
  assert.equal(docsCheck.cacheEnvironment, undefined);
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

test("main: skipping every check refuses to scan nothing", async () => {
  const all = buildChecks().map((check) => check.name);
  const exitCode = await main([`--skip=${all.join(",")}`]);
  assert.equal(exitCode, 1, "empty check set must exit 1");
});

test("main: unknown --skip name throws before spawning", async () => {
  await assert.rejects(() => main(["--skip=definitely-not-a-check"]), /unknown --skip name/);
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
      assert.equal(readCacheEntry(check, key), true);
    }
  } finally {
    if (originalCi === undefined) delete process.env.CI;
    else process.env.CI = originalCi;
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
