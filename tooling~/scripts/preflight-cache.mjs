import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const CACHE_VERSION = 5;
const CACHE_DIRECTORY = path.join(path.dirname(fileURLToPath(import.meta.url)), ".preflight-cache");
const CHECKOUT_ID = (() => {
  try {
    return fs.realpathSync(REPO_ROOT);
  } catch {
    return REPO_ROOT;
  }
})();
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

/*
    Untracked files at or below this many bytes are hashed by content in
    the repo snapshot; larger ones hash their stat signature instead (see
    repositorySnapshot).
 */
const UNTRACKED_CONTENT_HASH_LIMIT = 1024 * 1024;

/*
    Content identity for tracked files comes from git's index, not from
    re-reading bytes: git status answers "worktree matches index" with
    the same stat machinery every caller would otherwise duplicate, so a
    clean tracked file keys at its index blob (one `ls-files -s`
    listing, ~25 ms for ~1,200 files) and only status-flagged files fall
    back to byte reads. The status call uses
    `--untracked-files=no`, which skips the untracked walk entirely.
    The state is computed lazily once per process (CI and --no-cache
    runs never key anything and never pay the spawns).

    The index-eol configuration (core.autocrlf, core.eol) is folded into
    the blob marker: with a clean/smudge filter the worktree bytes and
    the index blob genuinely differ while status says clean, so flipping
    the configuration must move keys, not age a cached pass.

    Stated limits, all inherited from the snapshot's existing contract:
    a file flagged assume-unchanged or skip-worktree hides its edits from
    status and is keyed at its index blob; the state is computed once per
    process, so an edit landing after the first keyed check in the same
    run is seen by the next run (the per-run memo contract); the folded
    configuration covers core.autocrlf and core.eol, while `text`/`eol`/
    `ident`/`working-tree-encoding` ATTRIBUTES and any LFS or custom
    clean/smudge filter drive worktree-vs-index divergence the marker
    does not see - this repository carries only `text eol=lf` over its
    own tooling paths and no filter over keyed roots today, and either
    landing over keyed roots needs to join this key before it can be
    trusted; and the dotnet lane keys pinned package versions, not the
    global NuGet cache contents, the same accepted limit a lockfile-keyed
    CI cache takes. When git is unavailable the whole identity falls
    back to byte reads.
 */
let gitFileIdentityState = null;

function gitFileIdentity() {
  if (gitFileIdentityState !== null) {
    return gitFileIdentityState;
  }
  try {
    const tracked = new Map();
    const listing = execFileSync("git", ["ls-files", "-s", "-z"], {
      cwd: REPO_ROOT,
      maxBuffer: 256 * 1024 * 1024,
      encoding: "utf8"
    });
    for (const record of listing.split("\0")) {
      const tab = record.indexOf("\t");
      if (tab < 0) {
        continue;
      }
      const meta = record.slice(0, tab).split(" ");
      if (meta.length < 3) {
        continue;
      }
      tracked.set(record.slice(tab + 1), meta[1]);
    }

    const status = execFileSync("git", ["status", "--porcelain=v1", "-z", "--untracked-files=no"], {
      cwd: REPO_ROOT,
      maxBuffer: 256 * 1024 * 1024,
      encoding: "utf8"
    });
    const dirty = parseStatusRecords(status).worktreeDirty;
    /* One spawn per key, each caught alone: `git config --get` exits 1
       for an UNSET key, so a shared try/catch would let a set autocrlf
       next to an unset eol read as both-unset and hide a real flip. */
    const configGet = (key) => {
      try {
        return execFileSync("git", ["config", "--get", key], { cwd: REPO_ROOT, encoding: "utf8" }).trim();
      } catch {
        return "<unset>";
      }
    };
    const eol = [configGet("core.autocrlf"), configGet("core.eol")].join("\0");
    gitFileIdentityState = { tracked, dirty, eol };
  } catch {
    gitFileIdentityState = { tracked: null, dirty: null, eol: "" };
  }
  return gitFileIdentityState;
}

/** Maps an absolute path inside the repo to its slash-separated repo-relative path; null outside. */
function repoRelativePath(target) {
  const relative = path.relative(REPO_ROOT, target);
  if (relative === "" || relative === ".." || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
    return null;
  }
  return relative.split(path.sep).join("/");
}

function statSignature(stat) {
  return `${stat.mode}\0${stat.size}\0${stat.mtimeMs}\0${stat.ino}\0`;
}

/*
    File identities are content hashes behind a cheap identity walk: the
    per-entry cost that made the old keying take 112 s serial was
    realpathSync plus readlinkSync on every entry - three metadata syscalls
    on a filesystem where one costs milliseconds - not the content reads.
    The walk keys visited directories by dev+ino instead of a resolved real
    path, reads directory entries with their types in one call, and hashes
    file bytes, so a same-size edit moves the key exactly like any other.
    Symlinks hash their link text and are followed, and the followed
    identity joins the visited set so a symlinked directory cycles out.
 */

function updatePathHash(hash, target, label, visited, excludes) {
  hash.update(`${label}\0`);
  let stat;
  try {
    stat = fs.lstatSync(target);
  } catch {
    hash.update("missing\0");
    return;
  }

  /*
      The walk's identity key is dev+ino, not a resolved real path:
      realpathSync cost three extra metadata syscalls per entry, and on a
      container where one stat costs milliseconds that tripled the keying
      wall. dev+ino answers the same question - have I walked this exact
      directory already - for the symlink-cycle guard, for a fraction of
      the cost. A symlink hashes its link text and is then followed, so a
      retargeted link still moves the key through the target's own walk.
   */
  const identity = `${stat.dev}:${stat.ino}`;
  if (visited.has(identity)) {
    hash.update("cycle\0");
    return;
  }
  visited.add(identity);

  if (stat.isSymbolicLink()) {
    let link;
    try {
      link = fs.readlinkSync(target);
    } catch {
      link = null;
    }
    if (link !== null) {
      hash.update(`${label}\0symlink\0${link}\0`);
    }

    /*
        The link is followed, like the realpath walk it replaces: the
        hashed state is the target's, and the followed identity joins the
        visited set so a symlinked directory cycles out through the same
        guard.
     */
    try {
      stat = fs.statSync(target);
    } catch {
      hash.update("broken\0");
      return;
    }
    const followed = `${stat.dev}:${stat.ino}`;
    if (visited.has(followed)) {
      hash.update("cycle\0");
      return;
    }
    visited.add(followed);
  }

  if (stat.isFile()) {
    const identity = gitFileIdentity();
    const relative = repoRelativePath(target);
    if (relative !== null && identity.tracked !== null) {
      const blob = identity.tracked.get(relative);
      if (blob !== undefined && !identity.dirty.has(relative)) {
        hash.update(`git-blob\0${blob}\0${identity.eol}\0`);
        return;
      }
    }
    /*
        Above the size cap the key hashes the stat signature instead of
        the bytes - the same documented coin the snapshot already flips
        for huge untracked files. The walk's wall was file reads, and the
        only regular victims here are build outputs and tool binaries
        (Generator~ obj/bin, /usr/bin/node, the npm install), which are
        immutable between real changes; a same-size, same-mtime edit to
        one huge file defeats its slice of the key.
     */
    if (stat.size > UNTRACKED_CONTENT_HASH_LIMIT) {
      hash.update(statSignature(stat));
      return;
    }
    hash.update(fs.readFileSync(target));
    return;
  }
  if (!stat.isDirectory()) {
    hash.update(`other\0${stat.mode}\0`);
    return;
  }

  let entries;
  try {
    entries = fs.readdirSync(target, { withFileTypes: true });
  } catch {
    hash.update("unreadable\0");
    return;
  }
  entries.sort((left, right) => (left.name < right.name ? -1 : left.name > right.name ? 1 : 0));
  for (const entry of entries) {
    const entryLabel = path.posix.join(label, entry.name);
    const relative = path
      .relative(REPO_ROOT, path.join(target, entry.name))
      .split(path.sep)
      .join("/");
    if (excludes.some((root) => relative === root || relative.startsWith(`${root}/`))) {
      hash.update(`${entryLabel}\0excluded\0`);
      continue;
    }
    updatePathHash(hash, path.join(target, entry.name), entryLabel, visited, excludes);
  }
}

export function pathState(value, excludes = []) {
  return combinedPathState(value, excludes, hashPathEntry);
}

function combinedPathState(value, excludes, resolveEntryState) {
  if (value === undefined) {
    return "unset";
  }
  const entries = Array.isArray(value) ? value : [value];
  if (entries.length === 1 && entries[0] === "") {
    return "empty";
  }
  const hash = crypto.createHash("sha256");
  for (const entry of entries) {
    hash.update(`${entry}\0`);
    hash.update(resolveEntryState(entry, excludes));
  }
  return hash.digest("hex");
}

function hashPathEntry(entry, excludes) {
  const hash = crypto.createHash("sha256");
  const resolved = path.resolve(REPO_ROOT, entry);
  hash.update(`${entry}\0`);
  updatePathHash(hash, resolved, entry, new Set(), excludes);
  return hash.digest("hex");
}

/*
    Parses one `git status --porcelain=v1 -z --untracked-files=...` output
    into the two views the cache needs.

    Record shapes (empirically pinned in this repo's contract tests):
    `XY path` for ordinary entries, `?? path` for untracked, and staged
    renames/copies as TWO records: `XY NEW\0OLD\0` - the flagged path
    (the destination, what exists on disk) comes first and carries the
    Y code; the ORIGINAL path follows as a bare record that is not a
    status line of its own.

    `changed` lists every path whose state differs from HEAD - either
    half dirty, or untracked. The snapshot folds those files' contents
    because a later rewrite of the same file keeps the status output
    byte-identical exactly as a worktree rewrite does.

    `worktreeDirty` holds only paths whose WORKTREE half (Y) is not
    space. The per-file identity uses this view on purpose: it answers
    "what are this file's bytes", and a clean worktree half means those
    bytes are the index blob, whatever the index-vs-HEAD half says.
 */
export function parseStatusRecords(output) {
  const changed = [];
  const worktreeDirty = new Set();
  const records = output.split("\0");
  for (let index = 0; index < records.length; index++) {
    const record = records[index];
    if (record === "") {
      continue;
    }
    if (record.startsWith("?? ")) {
      /* -z paths are literal (never quoted), so the slice is the path. */
      changed.push(record.slice(3));
      continue;
    }
    if (record.length < 4 || record[2] !== " ") {
      continue;
    }
    if (record[0] !== " " || record[1] !== " ") {
      changed.push(record.slice(3));
    }
    if (record[1] !== " ") {
      worktreeDirty.add(record.slice(3));
    }
    if (record[0] === "R" || record[0] === "C") {
      index++;
    }
  }
  return { changed, worktreeDirty };
}

function repositorySnapshot() {
  /*
      One git call instead of a stat per tracked file: `git status
      --porcelain` is index-backed and answers which tracked files changed
      and which untracked files exist, in one process on every filesystem,
      where the per-file walk cost tens of seconds of metadata syscalls on
      the dev container before a single check spawned.

      Status encodes an untracked file as a bare `?? <path>` line, so an
      edit to an untracked file's CONTENT leaves the output byte-identical
      while the check inputs move - and node-tests reads files outside its
      declared roots (.github/, .devcontainer/, .llm/), where a draft file
      would age a cached pass. The same holds for a DIRTY tracked file
      outside those roots: a second rewrite keeps the ` M <path>` line
      byte-identical while the content moves again. So every file status
      flags as changed - untracked or tracked - is hashed by content: the
      hash is incremental over raw bytes (a UTF-8 string join would decode
      two different binary files to the same replacement-character
      string), and a file above the size cap hashes its stat signature
      instead of its bytes, so an un-ignored dump in the worktree cannot
      reintroduce the keying wall. The cap trades the same documented coin
      as every stat key here: a same-size, same-mtime content edit to one
      huge file defeats its part of the key.

      Stated limit: a tracked file flagged assume-unchanged or
      skip-worktree hides its edits from status, and this snapshot does not
      re-hash tracked contents to catch it - that flag is a deliberate,
      per-developer opt-in, and re-walking every tracked file is the cost
      this snapshot exists to remove.
   */
  const output = execFileSync(
    "git",
    ["status", "--porcelain=v1", "-z", "--untracked-files=all"],
    {
      cwd: REPO_ROOT,
      maxBuffer: 256 * 1024 * 1024,
      encoding: "utf8"
    }
  );
  const hash = crypto.createHash("sha256");
  hash.update(output);
  const { changed } = parseStatusRecords(output);
  for (const relative of changed) {
    hash.update(relative);
    hash.update("\0");
    const absolute = path.join(REPO_ROOT, relative);
    try {
      const stat = fs.statSync(absolute);
      if (stat.isFile() && stat.size <= UNTRACKED_CONTENT_HASH_LIMIT) {
        hash.update(fs.readFileSync(absolute));
        continue;
      }
      hash.update(statSignature(stat));
    } catch {
      hash.update("unreadable");
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

function checkPathState(check, sources) {
  if (check.cachePaths === undefined) {
    return "unset";
  }
  return sources.pathState(check.cachePaths, check.cacheExcludes ?? []);
}

export function cacheKeyForCheck(check, context = createCacheContext()) {
  return buildCacheKey(check, context, {
    pathState: combinedPathStateDefault,
    repoSnapshot: repositorySnapshot
  });
}

function combinedPathStateDefault(value, excludes) {
  return combinedPathState(value, excludes, hashPathEntry);
}

/*
    One key computer per preflight run: it memoizes per-root states and the
    repo snapshot for the keying phase only, where the nine linters declare
    overlapping roots and every check would otherwise re-walk the same
    trees serially. The memo lives in the computer, never in the exported
    functions, so direct callers (the contract tests) keep observing the
    live filesystem.
 */
export function createCacheKeyComputer() {
  const entryCache = new Map();
  let repoState = null;
  return {
    keyFor(check, context = createCacheContext()) {
      return buildCacheKey(check, context, {
        pathState: (value, excludes) =>
          combinedPathState(value, excludes, (entry, excludes_) => {
            const memoKey = `${entry}\0${excludes_.join("\0")}`;
            let state = entryCache.get(memoKey);
            if (state === undefined) {
              state = hashPathEntry(entry, excludes_);
              entryCache.set(memoKey, state);
            }
            return state;
          }),
        repoSnapshot: () => (repoState ??= repositorySnapshot())
      });
    }
  };
}

function buildCacheKey(check, context, sources) {
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
    `repo-paths=${checkPathState(check, sources)}`,
    `repo-files=${check.cacheTrackedFiles === true ? sources.repoSnapshot() : ""}`
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
