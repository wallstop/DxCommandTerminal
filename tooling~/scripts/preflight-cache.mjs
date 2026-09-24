import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const CACHE_VERSION = 3;
const CACHE_DIRECTORY = path.join(path.dirname(fileURLToPath(import.meta.url)), ".preflight-cache");
const CHECKOUT_ID = (() => {
  try {
    return fs.realpathSync(REPO_ROOT);
  } catch {
    return REPO_ROOT;
  }
})();
const CACHE_DISABLED_CHECKS = new Set(["compat-check"]);
const CACHE_PATH_KEYS = new Set([
  "COMPARISON_DIRECTION_ROOTS",
  "NESTED_TYPE_PLACEMENT_ROOTS",
  "MULTILINE_COMMENT_ROOTS",
  "LINQ_PRODUCTION_ROOTS",
  "STRING_EQUALITY_ROOTS",
  "OUT_PARAM_DISCIPLINE_ROOTS",
  "UNITY_NULL_ROOTS",
  "THEME_TOKEN_ROOTS",
  "DOCS_CATALOG",
  "DOCS_GUIDES_DIR",
  "DOCS_SAMPLES_DIR",
  "DOCS_BASELINES_DIR",
  "DOCS_API_DIR",
  "NPM_CONFIG_USERCONFIG",
  "NPM_CONFIG_GLOBALCONFIG",
  "BASH_ENV"
]);
const CACHE_SINGLE_PATH_KEYS = new Set(["DOCS_CATALOG", "DOCS_GUIDES_DIR", "DOCS_SAMPLES_DIR", "DOCS_BASELINES_DIR", "DOCS_API_DIR", "NPM_CONFIG_USERCONFIG", "NPM_CONFIG_GLOBALCONFIG", "BASH_ENV"]);
const CACHE_RUNTIME_KEYS = new Set([
  "PATH",
  "PATHEXT",
  "SHELL",
  "COMSPEC",
  "NODE_OPTIONS",
  "NODE_PATH",
  "TAR_OPTIONS",
  "GZIP",
  "TMPDIR",
  "TEMP",
  "TMP",
  "HOME",
  "USERPROFILE",
  "DOTNET_ROOT",
  "DOTNET_ROOT_X64",
  "DOTNET_CLI_HOME",
  "DOTNET_MULTILEVEL_LOOKUP",
  "NUGET_PACKAGES",
  "NPM_CONFIG_USERCONFIG",
  "NPM_CONFIG_GLOBALCONFIG",
  "GIT_CONFIG_GLOBAL",
  "GIT_CONFIG_SYSTEM",
  "GIT_CONFIG_NOSYSTEM",
  "GIT_DIR",
  "GIT_WORK_TREE",
  "GIT_INDEX_FILE",
  "GIT_OBJECT_DIRECTORY",
  "GIT_COMMON_DIR",
  "GIT_CEILING_DIRECTORIES"
]);

function hashText(value) {
  return crypto.createHash("sha256").update(value).digest("hex");
}

function repositoryFiles() {
  const output = execFileSync("git", ["ls-files", "-co", "--exclude-standard", "-z"], {
    cwd: REPO_ROOT,
    encoding: "utf8"
  });
  return output.split("\0").filter(Boolean).sort();
}

function updatePathHash(hash, target, label, visited, excludes) {
  hash.update(`${label}\0`);
  let link;
  try {
    link = fs.readlinkSync(target);
  } catch {
    link = null;
  }
  if (link !== null) {
    hash.update(`${label}\0symlink\0${link}\0`);
  }

  let realPath;
  let stat;
  try {
    realPath = fs.realpathSync(target);
    stat = fs.statSync(target);
  } catch {
    hash.update("missing\0");
    return;
  }
  if (visited.has(realPath)) {
    hash.update("cycle\0");
    return;
  }
  visited.add(realPath);
  if (stat.isFile()) {
    hash.update(`${stat.mode}\0${stat.size}\0`);
    hash.update(fs.readFileSync(target));
    return;
  }
  if (!stat.isDirectory()) {
    hash.update(`other\0${stat.mode}\0`);
    return;
  }

  const entries = fs.readdirSync(target).sort((left, right) => (left < right ? -1 : left > right ? 1 : 0));
  for (const entry of entries) {
    const entryLabel = path.posix.join(label, entry);
    const relative = path.relative(REPO_ROOT, path.join(target, entry)).split(path.sep).join("/");
    if (excludes.some((root) => relative === root || relative.startsWith(`${root}/`))) {
      hash.update(`${entryLabel}\0excluded\0`);
      continue;
    }
    updatePathHash(hash, path.join(target, entry), entryLabel, visited, excludes);
  }
}

export function pathState(value, excludes = []) {
  if (value === undefined) {
    return "unset";
  }
  const entries = Array.isArray(value) ? value : [value];
  if (entries.length === 1 && entries[0] === "") {
    return "empty";
  }
  const hash = crypto.createHash("sha256");
  for (const entry of entries) {
    const resolved = path.resolve(REPO_ROOT, entry);
    hash.update(`${entry}\0`);
    updatePathHash(hash, resolved, entry, new Set(), excludes);
  }
  return hash.digest("hex");
}

function repositorySnapshot(files = repositoryFiles()) {
  const hash = crypto.createHash("sha256");
  for (const relativePath of files) {
    const filePath = path.join(REPO_ROOT, relativePath);
    hash.update(relativePath);
    hash.update("\0");
    try {
      const stat = fs.lstatSync(filePath);
      hash.update(`${stat.mode}\0${stat.size}\0`);
      if (stat.isSymbolicLink()) {
        hash.update(fs.readlinkSync(filePath));
        updatePathHash(hash, filePath, relativePath, new Set(), []);
      } else if (stat.isFile()) {
        hash.update(fs.readFileSync(filePath));
      } else {
        hash.update("directory");
      }
    } catch (error) {
      if (error.code !== "ENOENT") {
        throw error;
      }
      hash.update("missing");
    }
  }
  return hash.digest("hex");
}

export function dependencyState(
  nodeModules = path.join(REPO_ROOT, "tooling~", "node_modules"),
  bakedMcp = "/opt/dxt-mcp"
) {
  return pathState([nodeModules, bakedMcp]);
}

export function dotnetToolState(name) {
  const manifestPath = path.join(REPO_ROOT, ".config", "dotnet-tools.json");
  let version = "unknown";
  try {
    const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
    version = manifest.tools[name]?.version ?? "unknown";
  } catch {
    return "missing";
  }
  const home = os.homedir();
  const cliHome = process.env.DOTNET_CLI_HOME ?? home;
  const nugetRoot = process.env.NUGET_PACKAGES ?? path.join(cliHome, ".nuget", "packages");
  return pathState([
    path.join(cliHome, ".dotnet", "tools", ".store", name),
    path.join(nugetRoot, name, version)
  ]);
}

function isExecutableFile(candidate) {
  try {
    if (!fs.statSync(candidate).isFile()) {
      return false;
    }
    if (process.platform === "win32") {
      return true;
    }
    fs.accessSync(candidate, fs.constants.X_OK);
    return true;
  } catch {
    return false;
  }
}

function executableCandidates(command) {
  if (path.isAbsolute(command) || command.includes("/") || command.includes("\\")) {
    const candidate = path.isAbsolute(command) ? command : path.resolve(REPO_ROOT, command);
    return isExecutableFile(candidate) ? [candidate] : [];
  }
  const searchPath = process.env.PATH ?? "";
  const extensions =
    process.platform === "win32" && path.extname(command).length === 0
      ? (process.env.PATHEXT ?? ".COM;.EXE;.BAT;.CMD").split(path.delimiter)
      : [""];
  if (process.platform === "win32") {
    for (const extension of extensions) {
      const candidate = path.resolve(REPO_ROOT, `${command}${extension}`);
      if (isExecutableFile(candidate)) {
        return [candidate];
      }
    }
  }
  for (const directory of searchPath.split(path.delimiter)) {
    if (directory.length === 0) {
      continue;
    }
    const resolvedDirectory = path.isAbsolute(directory) ? directory : path.resolve(REPO_ROOT, directory);
    for (const extension of extensions) {
      const candidate = path.join(resolvedDirectory, `${command}${extension}`);
      if (isExecutableFile(candidate)) {
        return [candidate];
      }
    }
  }
  return [];
}

export function resolvedExecutableState(command) {
  const candidates = executableCandidates(command);
  return candidates.length === 0 ? "missing" : pathState(candidates);
}

function npmPackageRoot(executable) {
  const paths = [path.dirname(fs.realpathSync(executable))];
  while (paths.length < 6) {
    const current = paths.at(-1);
    const manifest = path.join(current, "package.json");
    try {
      if (JSON.parse(fs.readFileSync(manifest, "utf8")).name === "npm") {
        return current;
      }
    } catch {
      if (path.dirname(current) === current) {
        break;
      }
    }
    paths.push(path.dirname(current));
  }
  return null;
}

export function npmExecutableState(command) {
  const candidate = executableCandidates(command)[0];
  if (candidate === undefined) {
    return "missing";
  }
  const paths = [candidate];
  const packageRoot = npmPackageRoot(candidate);
  if (packageRoot !== null) {
    paths.push(packageRoot);
  } else {
    paths.push(path.join(path.dirname(candidate), "node_modules", "npm"));
  }
  return pathState(paths);
}

function commandVersion(command, args) {
  try {
    const options = { cwd: REPO_ROOT, encoding: "utf8" };
    if (process.platform === "win32" && command.endsWith(".cmd")) {
      options.shell = true;
    }
    return execFileSync(command, args, options).trim();
  } catch {
    return "unavailable";
  }
}

function gitTrackedState() {
  return hashText(
    execFileSync("git", ["ls-files", "-z"], { cwd: REPO_ROOT, encoding: "utf8" })
  );
}

export function npmConfigState() {
  const home = os.homedir();
  const npm = process.platform === "win32" ? "npm.cmd" : "npm";
  return {
    files: pathState([
      path.join(REPO_ROOT, ".npmrc"),
      path.join(REPO_ROOT, "tooling~", ".npmrc"),
      process.env.NPM_CONFIG_USERCONFIG ?? path.join(home, ".npmrc"),
      process.env.NPM_CONFIG_GLOBALCONFIG ?? path.join(home, ".config", "npm", "npmrc")
    ]),
    effective: hashText(commandVersion(npm, ["--prefix", "tooling~", "config", "list", "--json"]))
  };
}

function gitConfigState() {
  const home = os.homedir();
  return {
    files: pathState([
      process.env.GIT_CONFIG_GLOBAL ?? path.join(home, ".gitconfig"),
      process.env.GIT_CONFIG_SYSTEM ?? path.join(home, ".config", "git", "config"),
      path.join(home, ".config", "git", "ignore")
    ]),
    effective: hashText(commandVersion("git", ["config", "--includes", "--list", "--show-origin"]))
  };
}

function lazy(context, key, factory) {
  let initialized = false;
  let value;
  Object.defineProperty(context, key, {
    enumerable: true,
    get() {
      if (!initialized) {
        value = factory();
        initialized = true;
      }
      return value;
    }
  });
}

export function createCacheContext() {
  const context = {
    node: process.version,
    platform: process.platform,
    arch: process.arch
  };
  lazy(context, "checkout", () => CHECKOUT_ID);
  const npm = process.platform === "win32" ? "npm.cmd" : "npm";
  lazy(context, "nodeExecutable", () => pathState(process.execPath));
  lazy(context, "nodeCommand", () => resolvedExecutableState("node"));
  lazy(context, "shellExecutable", () =>
    resolvedExecutableState(process.platform === "win32" ? process.env.ComSpec ?? "cmd.exe" : "/bin/sh")
  );
  lazy(context, "bashExecutable", () => resolvedExecutableState("bash"));
  lazy(context, "npm", () => commandVersion(npm, ["--version"]));
  lazy(context, "npmExecutable", () => npmExecutableState(npm));
  lazy(context, "dotnet", () => commandVersion("dotnet", ["--version"]));
  lazy(context, "dotnetExecutable", () => resolvedExecutableState("dotnet"));
  lazy(context, "git", () => commandVersion("git", ["--version"]));
  lazy(context, "gitExecutable", () => resolvedExecutableState("git"));
  lazy(context, "tar", () => commandVersion("tar", ["--version"]));
  lazy(context, "tarExecutable", () => resolvedExecutableState("tar"));
  lazy(context, "gzip", () => commandVersion("gzip", ["--version"]));
  lazy(context, "gzipExecutable", () => resolvedExecutableState("gzip"));
  lazy(context, "dependencies", () => dependencyState());
  lazy(context, "docfxTool", () => dotnetToolState("docfx"));
  lazy(context, "docsApi", () => pathState(path.join(REPO_ROOT, "tooling~", "docs", "obj", "api")));
  lazy(context, "files", () => repositoryFiles());
  lazy(context, "gitTracked", () => gitTrackedState());
  lazy(context, "npmConfig", () => npmConfigState());
  lazy(context, "gitConfig", () => gitConfigState());
  lazy(context, "implementation", () =>
    pathState(["tooling~/scripts/preflight.mjs", "tooling~/scripts/preflight-cache.mjs"])
  );
  return context;
}

function environmentEntries(keys, includeRuntime) {
  const selected = new Set(keys);
  if (includeRuntime) {
    for (const key of CACHE_RUNTIME_KEYS) {
      selected.add(key);
    }
    for (const key of Object.keys(process.env)) {
      if (
        key.startsWith("NPM_CONFIG_") ||
        key.startsWith("DOTNET_") ||
        key.startsWith("MSBUILD") ||
        key.startsWith("NUGET_")
      ) {
        selected.add(key);
      }
    }
  }
  return [...selected].sort();
}

function environmentState(
  keys = [],
  includeRuntime = false,
  includeNpmConfig = false,
  includeGitConfig = false,
  context = createCacheContext()
) {
  const state = environmentEntries(keys, includeRuntime).map((key) => {
    const value = process.env[key];
    if (value === undefined) {
      return [key, "unset"];
    }
    if (CACHE_PATH_KEYS.has(key)) {
      if (value === "") {
        return [key, "empty"];
      }
      return [key, pathState(CACHE_SINGLE_PATH_KEYS.has(key) ? value : value.split(path.delimiter))];
    }
    return [key, hashText(value)];
  });
  if (includeNpmConfig) {
    state.push(["npm-config", context.npmConfig]);
  }
  if (includeGitConfig) {
    state.push(["git-config", context.gitConfig]);
  }
  return state;
}

function checkPathState(check) {
  if (check.cachePaths === undefined) {
    return "unset";
  }
  return pathState(check.cachePaths, check.cacheExcludes ?? []);
}

export function cacheKeyForCheck(check, context = createCacheContext()) {
  const identity = [
    `version=${CACHE_VERSION}`,
    `name=${check.name}`,
    `command=${check.command}`,
    `checkout=${context.checkout ?? CHECKOUT_ID}`,
    `node=${context.node}`,
    `node-executable=${context.nodeExecutable ?? ""}`,
    `node-command=${context.nodeCommand ?? ""}`,
    `shell-executable=${context.shellExecutable ?? ""}`,
    `bash-executable=${check.cacheBash === true ? context.bashExecutable ?? "" : ""}`,
    `platform=${context.platform}`,
    `arch=${context.arch}`,
    `npm=${check.cacheNpm === true ? context.npm : ""}`,
    `npm-executable=${check.cacheNpm === true ? context.npmExecutable ?? "" : ""}`,
    `dotnet=${check.cacheDotnet === true ? context.dotnet : ""}`,
    `dotnet-executable=${check.cacheDotnet === true ? context.dotnetExecutable ?? "" : ""}`,
    `git=${check.cacheGit === true ? context.git : ""}`,
    `git-executable=${check.cacheGit === true ? context.gitExecutable ?? "" : ""}`,
    `tar=${check.cacheTar === true ? context.tar : ""}`,
    `tar-executable=${check.cacheTar === true ? context.tarExecutable ?? "" : ""}`,
    `gzip=${check.cacheGzip === true ? context.gzip : ""}`,
    `gzip-executable=${check.cacheGzip === true ? context.gzipExecutable ?? "" : ""}`,
    `dependencies=${check.cacheDependencies === true ? context.dependencies : ""}`,
    `docfx-tool=${check.cacheDotnetTool === true ? context.docfxTool : ""}`,
    `docs-api=${check.cacheDocsApi === true ? context.docsApi : ""}`,
    `implementation=${context.implementation}`,
    `environment=${JSON.stringify(environmentState(
      check.cacheEnvironment ?? [],
      check.cacheRuntimeEnvironment === true,
      check.cacheNpmConfig === true,
      check.cacheGitConfig === true,
      context
    ))}`,
    `git-tracked=${check.cacheGitTracked === true ? context.gitTracked : ""}`,
    `cache-paths=${JSON.stringify(check.cachePaths ?? null)}`,
    `cache-excludes=${JSON.stringify(check.cacheExcludes ?? null)}`,
    `repo-paths=${checkPathState(check)}`,
    `repo-files=${check.cacheTrackedFiles === true ? repositorySnapshot(context.files ?? repositoryFiles()) : ""}`
  ].join("\n");
  return hashText(identity);
}

function cachePath(check, key) {
  const name = hashText(check.name).slice(0, 16);
  return path.join(CACHE_DIRECTORY, `${name}-${key}.json`);
}

export function cachePathForCheck(check, key) {
  return cachePath(check, key);
}

function isCacheDirectory() {
  try {
    const stat = fs.lstatSync(CACHE_DIRECTORY);
    return stat.isDirectory() && !stat.isSymbolicLink();
  } catch {
    return false;
  }
}

function sameFile(left, right) {
  return left.dev === right.dev && left.ino === right.ino;
}

function closeDescriptor(descriptor) {
  if (descriptor === null) {
    return;
  }
  try {
    fs.closeSync(descriptor);
  } catch {
    return;
  }
}

function readRegularFile(filePath) {
  let descriptor = null;
  try {
    const before = fs.lstatSync(filePath);
    if (!before.isFile()) {
      return null;
    }
    const noFollow = fs.constants.O_NOFOLLOW ?? 0;
    descriptor = fs.openSync(filePath, fs.constants.O_RDONLY | noFollow);
    const opened = fs.fstatSync(descriptor);
    const after = fs.lstatSync(filePath);
    if (!sameFile(before, opened) || !sameFile(opened, after) || !after.isFile()) {
      return null;
    }
    return JSON.parse(fs.readFileSync(descriptor, "utf8"));
  } catch {
    return null;
  } finally {
    closeDescriptor(descriptor);
  }
}

export function isCacheableCheck(check) {
  if (CACHE_DISABLED_CHECKS.has(check.name)) {
    return false;
  }
  if (check.cacheRuntimeEnvironment === true && (process.env.NODE_OPTIONS || process.env.NODE_PATH)) {
    return false;
  }
  return true;
}

export function readCacheEntry(check, key) {
  if (process.env.CI || !isCacheableCheck(check) || !isCacheDirectory()) {
    return false;
  }
  const entry = readRegularFile(cachePath(check, key));
  return entry !== null && entry.version === CACHE_VERSION && entry.key === key && entry.ok === true;
}

export function writeCacheEntry(check, key) {
  if (process.env.CI || !isCacheableCheck(check)) {
    return false;
  }
  let temporary = null;
  let descriptor = null;
  try {
    fs.mkdirSync(CACHE_DIRECTORY, { recursive: true });
    if (!isCacheDirectory()) {
      return false;
    }
    const destination = cachePath(check, key);
    temporary = `${destination}.${process.pid}.${crypto.randomBytes(6).toString("hex")}.tmp`;
    const noFollow = fs.constants.O_NOFOLLOW ?? 0;
    descriptor = fs.openSync(
      temporary,
      fs.constants.O_WRONLY | fs.constants.O_CREAT | fs.constants.O_EXCL | noFollow,
      0o600
    );
    fs.writeFileSync(
      descriptor,
      `${JSON.stringify({ version: CACHE_VERSION, key, ok: true, completedAt: new Date().toISOString() })}\n`,
      { encoding: "utf8" }
    );
    fs.closeSync(descriptor);
    descriptor = null;
    fs.renameSync(temporary, destination);
    temporary = null;
    return true;
  } catch {
    return false;
  } finally {
    closeDescriptor(descriptor);
    if (temporary !== null) {
      try {
        fs.rmSync(temporary, { force: true });
      } catch {
        return false;
      }
    }
  }
}

export function clearCacheForCheck(check, key) {
  if (!isCacheDirectory()) {
    return;
  }
  try {
    fs.rmSync(cachePath(check, key), { force: true });
  } catch {
    return;
  }
}
