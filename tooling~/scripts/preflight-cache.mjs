import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const CACHE_VERSION = 1;
const CACHE_DIRECTORY = path.join(path.dirname(fileURLToPath(import.meta.url)), ".preflight-cache");
const CACHE_DISABLED_CHECKS = new Set(["compat-check"]);

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

function repositorySnapshot() {
  const hash = crypto.createHash("sha256");
  for (const relativePath of repositoryFiles()) {
    const filePath = path.join(REPO_ROOT, relativePath);
    hash.update(relativePath);
    hash.update("\0");
    try {
      const stat = fs.lstatSync(filePath);
      hash.update(`${stat.mode}\0${stat.size}\0`);
      if (stat.isSymbolicLink()) {
        hash.update(fs.readlinkSync(filePath));
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

export function createCacheContext() {
  return {
    node: process.version,
    platform: process.platform,
    arch: process.arch,
    npm: commandVersion(process.platform === "win32" ? "npm.cmd" : "npm", ["--version"]),
    dotnet: commandVersion("dotnet", ["--version"]),
    dependencies: dependencyState(),
    docsApi: generatedState(path.join(REPO_ROOT, "tooling~", "docs", "obj", "api")),
    repository: repositorySnapshot()
  };
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
    `repo=${context.repository}`
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
