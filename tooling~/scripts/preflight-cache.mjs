import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const CACHE_VERSION = 1;
const CACHE_DIRECTORY = path.join(path.dirname(fileURLToPath(import.meta.url)), ".preflight-cache");
const CACHE_DISABLED_CHECKS = new Set(["compat-check"]);
const CACHE_SINGLE_PATH_KEYS = new Set([
  "DOCS_CATALOG",
  "DOCS_GUIDES_DIR",
  "DOCS_SAMPLES_DIR",
  "DOCS_BASELINES_DIR",
  "DOCS_API_DIR"
]);
const CACHE_ENVIRONMENT_KEYS = [
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
  "DOCS_API_DIR"
];

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
        updatePathHash(hash, filePath);
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

function updatePathHash(hash, target, visited = new Set()) {
  let realPath;
  let stat;
  try {
    realPath = fs.realpathSync(target);
    stat = fs.statSync(target);
  } catch {
    hash.update("missing");
    return;
  }
  if (visited.has(realPath)) {
    hash.update("cycle");
    return;
  }
  visited.add(realPath);
  if (stat.isFile()) {
    hash.update(fs.readFileSync(target));
    return;
  }
  if (!stat.isDirectory()) {
    hash.update("other");
    return;
  }
  const entries = fs.readdirSync(target).sort((left, right) => (left < right ? -1 : left > right ? 1 : 0));
  for (const entry of entries) {
    hash.update(entry);
    hash.update("\0");
    updatePathHash(hash, path.join(target, entry), visited);
  }
}

function dependencyState() {
  const nodeModules = path.join(REPO_ROOT, "tooling~", "node_modules");
  const lockfile = path.join(nodeModules, ".package-lock.json");
  if (fs.existsSync(lockfile)) {
    return hashText(fs.readFileSync(lockfile, "utf8"));
  }
  if (!fs.existsSync(nodeModules)) {
    return "missing";
  }
  const manifests = [];
  const walk = (directory) => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const entryPath = path.join(directory, entry.name);
      if (entry.isDirectory()) {
        walk(entryPath);
      } else if (entry.isFile() && entry.name === "package.json") {
        manifests.push(`${path.relative(nodeModules, entryPath)}\0${fs.readFileSync(entryPath, "utf8")}`);
      }
    }
  };
  walk(nodeModules);
  return hashText(manifests.sort().join("\n"));
}

function generatedState(directory) {
  if (!fs.existsSync(directory)) {
    return "missing";
  }
  const files = [];
  const walk = (current) => {
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const entryPath = path.join(current, entry.name);
      if (entry.isDirectory()) {
        walk(entryPath);
      } else if (entry.isFile()) {
        files.push(`${path.relative(directory, entryPath)}\0${fs.readFileSync(entryPath)}`);
      }
    }
  };
  walk(directory);
  return hashText(files.sort().join("\n"));
}

function commandVersion(command, args) {
  try {
    const options = { encoding: "utf8" };
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

export function createCacheContext() {
  return {
    node: process.version,
    platform: process.platform,
    arch: process.arch,
    npm: commandVersion(process.platform === "win32" ? "npm.cmd" : "npm", ["--version"]),
    dotnet: commandVersion("dotnet", ["--version"]),
    dependencies: dependencyState(),
    docsApi: generatedState(path.join(REPO_ROOT, "tooling~", "docs", "obj", "api")),
    files: repositoryFiles(),
    gitTracked: gitTrackedState()
  };
}

function pathState(value) {
  if (value === undefined) {
    return "unset";
  }
  if (value === "" || (Array.isArray(value) && value.length === 1 && value[0] === "")) {
    return "empty";
  }
  const entries = Array.isArray(value) ? value : value.split(path.delimiter);
  const hash = crypto.createHash("sha256");
  for (const entry of entries) {
    const resolved = path.resolve(REPO_ROOT, entry);
    hash.update(entry);
    hash.update("\0");
    const files = [];
    const visited = new Set();
    const walk = (target, prefix) => {
      let realPath;
      let stat;
      try {
        realPath = fs.realpathSync(target);
        stat = fs.statSync(target);
      } catch {
        files.push([prefix, "missing"]);
        return;
      }
      if (visited.has(realPath)) {
        files.push([prefix, "cycle"]);
        return;
      }
      visited.add(realPath);
      if (stat.isFile()) {
        files.push([prefix, fs.readFileSync(target)]);
        return;
      }
      if (!stat.isDirectory()) {
        files.push([prefix, "other"]);
        return;
      }
      for (const child of fs.readdirSync(target, { withFileTypes: true })) {
        walk(path.join(target, child.name), path.posix.join(prefix, child.name));
      }
    };
    walk(resolved, entry);
    files.sort(([left], [right]) => (left < right ? -1 : left > right ? 1 : 0));
    for (const [relativePath, contents] of files) {
      hash.update(relativePath);
      hash.update("\0");
      hash.update(contents);
    }
  }
  return hash.digest("hex");
}

function environmentState() {
  return CACHE_ENVIRONMENT_KEYS.map((key) => {
    const value = process.env[key];
    if (value === undefined) {
      return [key, "unset"];
    }
    const paths = CACHE_SINGLE_PATH_KEYS.has(key) ? [value] : value;
    return [key, pathState(paths)];
  });
}

function selectedFiles(check, context) {
  if (check.cachePaths === undefined) {
    return context.files ?? repositoryFiles();
  }
  const files = context.files ?? repositoryFiles();
  return files.filter((relativePath) =>
    check.cachePaths.some(
      (root) => relativePath === root || relativePath.startsWith(`${root}/`)
    )
  );
}

export function cacheKeyForCheck(check, context = createCacheContext()) {
  const identity = [
    `version=${CACHE_VERSION}`,
    `name=${check.name}`,
    `command=${check.command}`,
    `node=${context.node}`,
    `platform=${context.platform}`,
    `arch=${context.arch}`,
    `npm=${context.npm}`,
    `dotnet=${context.dotnet}`,
    `dependencies=${context.dependencies}`,
    `docs-api=${context.docsApi}`,
    `environment=${JSON.stringify(environmentState())}`,
    `git-tracked=${check.cacheGitTracked === true ? context.gitTracked : ""}`,
    `cache-paths=${JSON.stringify(check.cachePaths ?? null)}`,
    `repo=${repositorySnapshot(selectedFiles(check, context))}`
  ].join("\n");
  return hashText(identity);
}

function cachePath(check, key) {
  const name = hashText(check.name).slice(0, 16);
  return path.join(CACHE_DIRECTORY, `${name}-${key}.json`);
}

export function isCacheableCheck(check) {
  return !CACHE_DISABLED_CHECKS.has(check.name);
}

export function readCacheEntry(check, key) {
  if (process.env.CI || !isCacheableCheck(check)) {
    return false;
  }
  try {
    const entry = JSON.parse(fs.readFileSync(cachePath(check, key), "utf8"));
    return entry.version === CACHE_VERSION && entry.key === key && entry.ok === true;
  } catch {
    return false;
  }
}

export function writeCacheEntry(check, key) {
  if (process.env.CI || !isCacheableCheck(check)) {
    return;
  }
  const destination = cachePath(check, key);
  const temporary = `${destination}.${process.pid}.${crypto.randomBytes(6).toString("hex")}.tmp`;
  try {
    fs.mkdirSync(CACHE_DIRECTORY, { recursive: true });
    fs.writeFileSync(
      temporary,
      `${JSON.stringify({ version: CACHE_VERSION, key, ok: true, completedAt: new Date().toISOString() })}\n`,
      { encoding: "utf8", flag: "wx" }
    );
    fs.renameSync(temporary, destination);
  } catch {
    fs.rmSync(temporary, { force: true });
  }
}

export function clearCacheForCheck(check, key) {
  fs.rmSync(cachePath(check, key), { force: true });
}
