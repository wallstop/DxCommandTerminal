/*
    Unity-free .unitypackage exporter (T14).

    Walks the npm `files` allowlist exactly as consumers receive it (`npm pack
    --dry-run --json` is the single source of truth for shipped content), pairs
    every asset with its checked-in .meta (whose `guid:` becomes the artifact's
    GUID directory), and emits a deterministic ustar tarball normalized through
    gzip. Two checkouts of the same revision rebuild byte-identically: fixed
    tar header fields, fixed entry order, and a normalized gzip header (MTIME 0,
    OS 0xFF). Callers that only need the checkout's artifact may pass
    `cache: true` to answer from the content-keyed cache under
    `.artifacts/export-cache/`; the release flow always builds fresh.

    Samples~/ content is excluded: the tilde folder is invisible to Unity, its
    files ship without .meta files (Package Manager generates fresh metas when
    a consumer imports the sample), and there is no GUID identity to preserve.

    Format reference: each GUID directory contains `asset` (file content, files
    only), `asset.meta` (the checked-in meta verbatim), and `pathname` (the
    import destination, `--root-prefix` + asset path, no .meta suffix).
*/
import { execFileSync } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import zlib from "node:zlib";

const REPO_ROOT = path.resolve(fileURLToPath(new URL("../../..", import.meta.url)));

const DEFAULT_ROOT_PREFIX_TEMPLATE = "Packages/{name}";
const TAR_BLOCK = 512;
const TAR_RECORD = 20 * TAR_BLOCK;
const FOLDER_ASSET_PATTERN = /^folderAsset:\s*yes\b/m;
const GUID_LINE_PATTERN = /^guid:\s*([0-9a-fA-F]{32})\b/m;

// Node refuses to spawn .cmd/.bat without a shell on Windows (CVE-2024-27980
// hardening: execFileSync("npm.cmd") fails outright there). All arguments are
// literal npm flags with no spaces, so shell joining stays safe.
const NPM = process.platform === "win32" ? "npm.cmd" : "npm";
const NPM_SPAWN_OPTIONS = process.platform === "win32" ? { shell: true } : {};

function parseArgs(argv) {
  const options = {
    packageRoot: REPO_ROOT,
    out: "",
    rootPrefix: "",
    cache: false
  };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const next = () => {
      index += 1;
      if (index >= argv.length) {
        throw new Error(`missing value for ${arg}`);
      }
      return argv[index];
    };
    if (arg === "--package-root") {
      options.packageRoot = path.resolve(next());
    } else if (arg === "--out") {
      options.out = path.resolve(next());
    } else if (arg === "--root-prefix") {
      options.rootPrefix = next();
    } else {
      throw new Error(`unknown argument: ${arg}`);
    }
  }
  return options;
}

/*
    `npm pack --dry-run` costs ~1s of npm startup per spawn, and the allowlist
    is a pure function of package.json + the files field: stable within one
    process. Results memoize per resolved package root so repeated exports
    (byte-identical rebuild checks, release flows) spawn npm once; every fresh
    process - including the CI payload verification - still queries npm itself.
*/
const PACKAGED_LIST_CACHE = new Map();

function packagedList(packageRoot) {
  const cacheKey = path.resolve(packageRoot);
  const cached = PACKAGED_LIST_CACHE.get(cacheKey);
  if (cached !== undefined) {
    return cached;
  }
  const stdout = execFileSync(NPM, ["pack", "--dry-run", "--json"], {
    cwd: packageRoot,
    encoding: "utf8",
    maxBuffer: 64 * 1024 * 1024,
    ...NPM_SPAWN_OPTIONS
  });
  const reports = JSON.parse(stdout);
  if (!Array.isArray(reports) || reports.length !== 1) {
    throw new Error("npm pack --dry-run --json did not report exactly one package");
  }
  const report = reports[0];
  if (!Array.isArray(report.files)) {
    throw new Error("npm pack report carries no file list");
  }
  const result = {
    name: report.name,
    version: report.version,
    files: Object.freeze(report.files.map((file) => file.path).sort())
  };
  PACKAGED_LIST_CACHE.set(cacheKey, Object.freeze(result));
  return result;
}

/*
    The exported artifact is a pure function of the checkout's packaged
    content, this module's archive logic, and the requested import prefix.
    A test that needs the real artifact used to pay the whole build every
    time - one `npm pack` spawn plus a read of every packaged file - and
    one `npm test` run paid five of those builds across its parallel test
    processes. The disk cache answers from `.artifacts/export-cache/`
    instead.

    The key hashes the packaged INPUTS, not the tree's cleanliness: a stat
    signature (size, mtime, inode) over every file the `files` allowlist
    could ship - walked from the filesystem, so an untracked addition
    invalidates too - plus package.json itself, this module's source, the
    node version, and the requested root prefix. Editing a test, a doc, or
    tooling keeps the key and answers from the cache; editing anything
    packaged changes a stat and re-exports. Stated limits: a content change
    that preserves size, mtime, and inode of every packaged file defeats
    the key, and the npm version is not hashed - an npm upgrade under the
    same node is answered by clearing `.artifacts/export-cache` or by the
    next packaged edit. The release flow never passes `cache: true`;
    freshness there is the contract the cache borrows from.
 */
const EXPORTER_SOURCE_SHA256 = sha256Hex(fs.readFileSync(fileURLToPath(import.meta.url)));

function sha256Hex(buffer) {
  return crypto.createHash("sha256").update(buffer).digest("hex");
}

/*
    Every path that could change the shipped artifact: a walk of the
    package.json `files` entries (literal paths and directories), minus the
    negated entries, which cannot change what the exporter archives. The
    walk reads the filesystem, so an untracked new asset invalidates too.
    A glob metacharacter in a non-negated entry disables the cache: the
    walk cannot know which files a pattern matches, and guessing would
    answer stale bytes for the files it missed.
 */
const GLOB_CHARACTERS = /[*?[\]{}]/u;

function packagedInputPaths(packageRoot, files) {
  const negated = new Set();
  const negatedPrefixes = [];
  for (const entry of files) {
    if (!entry.startsWith("!")) {
      continue;
    }
    const path_ = entry.slice(1);
    if (path_.endsWith("/")) {
      negatedPrefixes.push(path_);
    } else {
      negated.add(path_);
    }
  }
  const negatedPath = (relative) => {
    if (negated.has(relative)) {
      return true;
    }
    for (const prefix of negatedPrefixes) {
      if (relative.startsWith(prefix)) {
        return true;
      }
    }
    return false;
  };
  const inputs = new Set();
  const visit = (relative, absolute) => {
    let entries;
    try {
      entries = fs.readdirSync(absolute, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      const childRelative = `${relative}/${entry.name}`;
      if (entry.isDirectory()) {
        visit(childRelative, path.join(absolute, entry.name));
      } else if (entry.isFile()) {
        inputs.add(childRelative);
      }
    }
  };
  for (const entry of files) {
    if (entry.startsWith("!")) {
      continue;
    }
    if (GLOB_CHARACTERS.test(entry)) {
      return null;
    }
    const absolute = path.join(packageRoot, entry);
    let stat;
    try {
      stat = fs.statSync(absolute);
    } catch {
      continue;
    }
    if (stat.isDirectory()) {
      visit(entry, absolute);
      continue;
    }
    if (!negatedPath(entry)) {
      inputs.add(entry);
    }
  }
  return [...inputs].filter((relative) => !negatedPath(relative)).sort();
}

/*
    Per-process constant, like the packaged list: the packaged inputs, this
    module's source, and the node version do not change under a running
    test, and each caller process would otherwise re-stat the walk. The
    requested root prefix is folded in per call, after the memo.
 */
const ARTIFACT_KEY_CACHE = new Map();

function artifactCacheKey(packageRoot, rootPrefix) {
  const resolved = path.resolve(packageRoot);
  const signature = (() => {
    const cached = ARTIFACT_KEY_CACHE.get(resolved);
    if (cached !== undefined) {
      return cached;
    }
    const computed = computePackagedInputSignature(resolved);
    ARTIFACT_KEY_CACHE.set(resolved, computed);
    return computed;
  })();
  if (signature === null) {
    return null;
  }
  return sha256Hex(
    `${signature}\n${EXPORTER_SOURCE_SHA256}\n${process.version}\n${rootPrefix ?? ""}\n`
  );
}

function computePackagedInputSignature(packageRoot) {
  let manifest;
  let paths;
  try {
    manifest = JSON.parse(fs.readFileSync(path.join(packageRoot, "package.json"), "utf8"));
  } catch {
    return null;
  }
  paths = packagedInputPaths(packageRoot, manifest.files ?? []);
  if (paths === null) {
    return null;
  }
  const signature = [];
  for (const relative of paths) {
    let stat;
    try {
      stat = fs.statSync(path.join(packageRoot, relative));
    } catch {
      /* Vanished between the walk and the stat: the next run sees it. */
      continue;
    }
    signature.push(`${relative}\u0000${stat.size}\u0000${stat.mtimeMs}\u0000${stat.ino}`);
  }
  return sha256Hex(
    `${sha256Hex(fs.readFileSync(path.join(packageRoot, "package.json")))}\n`
    + `${sha256Hex(signature.join("\n"))}`
  );
}

function cachedArtifactPaths(packageRoot, key) {
  const directory = path.join(packageRoot, ".artifacts", "export-cache");
  return {
    buffer: path.join(directory, `${key}.unitypackage`),
    meta: path.join(directory, `${key}.json`)
  };
}

function readCachedArtifact(packageRoot, key) {
  const paths = cachedArtifactPaths(packageRoot, key);
  try {
    const buffer = fs.readFileSync(paths.buffer);
    const meta = JSON.parse(fs.readFileSync(paths.meta, "utf8"));
    if (
      meta === null ||
      typeof meta !== "object" ||
      typeof meta.name !== "string" ||
      typeof meta.version !== "string" ||
      typeof meta.rootPrefix !== "string" ||
      typeof meta.sha256 !== "string" ||
      !Number.isInteger(meta.assetCount) ||
      !Number.isInteger(meta.folderCount) ||
      !Number.isInteger(meta.fileCount) ||
      !Number.isInteger(meta.excludedSampleCount)
    ) {
      return null;
    }
    /*
        The key proves what was exported; the digest proves the file on disk
        is what was exported. A truncated or half-renamed entry misses and
        the fresh export below repairs it instead of handing a test a broken
        artifact that fails three files away from the cause.
     */
    if (sha256Hex(buffer) !== meta.sha256) {
      return null;
    }
    return { buffer, meta };
  } catch {
    return null;
  }
}

function writeCachedArtifact(packageRoot, key, result) {
  const paths = cachedArtifactPaths(packageRoot, key);
  const meta = {
    name: result.name,
    version: result.version,
    rootPrefix: result.rootPrefix,
    sha256: result.sha256,
    assetCount: result.assetCount,
    folderCount: result.folderCount,
    fileCount: result.fileCount,
    excludedSampleCount: result.excludedSampleCount
  };
  try {
    fs.mkdirSync(path.dirname(paths.buffer), { recursive: true });
    /*
        Write-then-rename so a concurrent first export cannot be observed
        half-written; racing writers rename identical bytes over each other.
        Best-effort: a read-only or full disk still exports, it just never
        answers from the cache.
     */
    const temporaryBuffer = `${paths.buffer}.${process.pid}.tmp`;
    const temporaryMeta = `${paths.meta}.${process.pid}.tmp`;
    fs.writeFileSync(temporaryBuffer, result.buffer);
    fs.writeFileSync(temporaryMeta, `${JSON.stringify(meta)}\n`);
    fs.renameSync(temporaryBuffer, paths.buffer);
    fs.renameSync(temporaryMeta, paths.meta);
    pruneCachedArtifacts(packageRoot, key);
  } catch {
    /* The cache is an accelerator, never a dependency. */
  }
}

/*
    Keeps the newest few entries and best-effort removes the rest: every
    packaged change writes a ~7 MB pair, and nothing reads an old key
    again. Orphan `*.tmp` files from a process killed mid-write are swept
    once they are an hour old - a live writer's temporary file is seconds
    old, and deleting one mid-write only costs that process its cache
    write. Racing processes may delete an entry another is about to read;
    a miss re-exports, which is the behavior the race replaced.
 */
const TEMPORARY_FILE_AGE_MS = 60 * 60 * 1000;

function pruneCachedArtifacts(packageRoot, keepKey) {
  const directory = path.join(packageRoot, ".artifacts", "export-cache");
  let names;
  try {
    names = fs.readdirSync(directory);
  } catch {
    return;
  }
  const now = Date.now();
  const entries = [];
  for (const name of names) {
    if (name.endsWith(".tmp")) {
      try {
        const stat = fs.statSync(path.join(directory, name));
        if (now - stat.mtimeMs > TEMPORARY_FILE_AGE_MS) {
          fs.rmSync(path.join(directory, name), { force: true });
        }
      } catch {
        /* Vanished mid-prune: nothing to do. */
      }
      continue;
    }
    if (!name.endsWith(".unitypackage")) {
      continue;
    }
    const key = name.slice(0, -".unitypackage".length);
    if (key === keepKey) {
      continue;
    }
    try {
      entries.push({ key, mtimeMs: fs.statSync(path.join(directory, name)).mtimeMs });
    } catch {
      /* Vanished mid-prune: nothing to do. */
    }
  }
  entries.sort((left, right) => right.mtimeMs - left.mtimeMs);
  for (const entry of entries.slice(3)) {
    for (const suffix of [".unitypackage", ".json"]) {
      try {
        fs.rmSync(path.join(directory, `${entry.key}${suffix}`), { force: true });
      } catch {
        /* Best effort; a leftover entry only costs disk. */
      }
    }
  }
}

function readMetaText(packageRoot, metaPath) {
  return fs.readFileSync(path.join(packageRoot, metaPath), "utf8");
}

function extractGuid(metaText) {
  return GUID_LINE_PATTERN.exec(metaText)?.[1]?.toLowerCase() ?? null;
}

function isDirectory(packageRoot, relativePath) {
  try {
    return fs.statSync(path.join(packageRoot, relativePath)).isDirectory();
  } catch {
    return false;
  }
}

/*
    Pairs shipped files with their metas and validates GUID identity.
    Returns { assets, excludedSampleCount, violations } where each asset is
    { path, metaPath, isFolder, guid, metaText } sorted by path (ordinal).
*/
function collectAssets(packageRoot, shippedPaths) {
  const shippedSet = new Set(shippedPaths);
  const violations = [];
  const report = (message) => violations.push(message);

  const excludedSampleCount = shippedPaths.filter(
    (entry) => entry === "Samples~" || entry.startsWith("Samples~/")
  ).length;

  const assets = [];
  const guidOwners = new Map();

  for (const entry of shippedPaths) {
    if (entry === "Samples~" || entry.startsWith("Samples~/")) {
      continue;
    }
    if (entry.endsWith(".meta")) {
      continue;
    }
    const metaPath = `${entry}.meta`;
    if (!shippedSet.has(metaPath)) {
      report(`shipped file without meta: ${entry}`);
      continue;
    }
    assets.push({ path: entry, metaPath, isFolder: false });
  }

  for (const entry of shippedPaths) {
    if (!entry.endsWith(".meta")) {
      continue;
    }
    const target = entry.slice(0, -".meta".length);
    if (isDirectory(packageRoot, target)) {
      assets.push({ path: target, metaPath: entry, isFolder: true });
    } else if (!shippedSet.has(target)) {
      report(`orphan meta without its target file or folder: ${entry}`);
    }
  }

  assets.sort((left, right) => (left.path < right.path ? -1 : left.path > right.path ? 1 : 0));

  for (const asset of assets) {
    let metaText;
    try {
      metaText = readMetaText(packageRoot, asset.metaPath);
    } catch (error) {
      report(`unreadable meta for ${asset.path}: ${error.message}`);
      continue;
    }
    const guid = extractGuid(metaText);
    asset.metaText = metaText;
    if (guid === null) {
      report(`meta without a 32-hex guid: ${asset.metaPath}`);
      continue;
    }
    if (asset.isFolder && !FOLDER_ASSET_PATTERN.test(metaText)) {
      report(`folder meta without 'folderAsset: yes': ${asset.metaPath}`);
    }
    const owner = guidOwners.get(guid);
    if (owner !== undefined) {
      report(`duplicate guid ${guid} on both ${owner} and ${asset.path}`);
      continue;
    }
    guidOwners.set(guid, asset.path);
    asset.guid = guid;
  }

  return { assets, excludedSampleCount, violations };
}

function octal(value, length) {
  return value.toString(8).padStart(length - 1, "0") + "\0";
}

function tarHeader(name, contentLength, typeFlag) {
  if (Buffer.byteLength(name, "utf8") > 100) {
    throw new Error(`tar entry name exceeds 100 bytes: ${name}`);
  }
  const header = Buffer.alloc(TAR_BLOCK, 0);
  header.write(name, 0, 100, "utf8");
  header.write(octal(typeFlag === "5" ? 0o755 : 0o644, 8), 100, "utf8");
  header.write(octal(0, 8), 108, "utf8");
  header.write(octal(0, 8), 116, "utf8");
  header.write(octal(contentLength, 12), 124, "utf8");
  header.write(octal(0, 12), 136, "utf8");
  header.write("        ", 148, 8, "utf8");
  header.write(typeFlag, 156, "utf8");
  header.write("ustar\0", 257, "utf8");
  header.write("00", 263, "utf8");
  let checksum = 0;
  for (const byte of header) {
    checksum += byte;
  }
  header.write(octal(checksum, 7) + " ", 148, "utf8");
  return header;
}

function tarEntry(name, content, typeFlag) {
  const blocks = [];
  blocks.push(tarHeader(name, content.length, typeFlag));
  blocks.push(content);
  const padding = (TAR_BLOCK - (content.length % TAR_BLOCK)) % TAR_BLOCK;
  if (padding !== 0) {
    blocks.push(Buffer.alloc(padding, 0));
  }
  return Buffer.concat(blocks);
}

/*
    Deterministic ustar: entries sorted by GUID directory then entry name,
    fixed mode/uid/gid/mtime, and two zero blocks plus record padding at the
    end.
*/
function buildTar(assets, rootPrefix) {
  const content = Buffer.concat(
    assets.flatMap((asset) => {
      const guidDir = asset.guid;
      const pathname = `${rootPrefix}/${asset.path}`;
      const entries = [
        tarEntry(`${guidDir}/`, Buffer.alloc(0, 0), "5"),
        tarEntry(`${guidDir}/asset.meta`, Buffer.from(asset.metaText, "utf8"), "0")
      ];
      if (!asset.isFolder) {
        entries.push(tarEntry(`${guidDir}/asset`, fs.readFileSync(asset.absolutePath), "0"));
      }
      entries.push(tarEntry(`${guidDir}/pathname`, Buffer.from(pathname, "utf8"), "0"));
      return entries;
    })
  );
  let padding = TAR_RECORD - (content.length % TAR_RECORD);
  if (padding < 2 * TAR_BLOCK) {
    padding += TAR_RECORD;
  }
  return Buffer.concat([content, Buffer.alloc(padding, 0)]);
}

function gzipDeterministic(tar) {
  const gz = zlib.gzipSync(tar);
  gz[4] = 0;
  gz[5] = 0;
  gz[6] = 0;
  gz[7] = 0;
  gz[9] = 0xff;
  return gz;
}

function resolveRootPrefix(rootPrefix, packageName) {
  if (rootPrefix.length > 0) {
    return rootPrefix.replace(/\/+$/, "");
  }
  return DEFAULT_ROOT_PREFIX_TEMPLATE.replace("{name}", packageName);
}

/*
    Exports the .unitypackage for one package checkout. Throws on any
    collected violation; returns { buffer, name, version, rootPrefix,
    sha256, assetCount, folderCount, fileCount, excludedSampleCount } -
    the same shape a cache hit answers with.
*/
function exportUnityPackage(options) {
  const packageRoot = path.resolve(options.packageRoot);
  const requestedPrefix = options.rootPrefix ?? "";
  const cacheKey = options.cache === true ? artifactCacheKey(packageRoot, requestedPrefix) : null;
  if (cacheKey !== null) {
    const cached = readCachedArtifact(packageRoot, cacheKey);
    if (cached !== null) {
      const result = { ...cached.meta, buffer: cached.buffer };
      if (options.out && options.out.length > 0) {
        fs.mkdirSync(path.dirname(options.out), { recursive: true });
        fs.writeFileSync(options.out, result.buffer);
      }
      return result;
    }
  }
  const { name, version, files } = packagedList(packageRoot);
  const collected = collectAssets(packageRoot, files);
  if (0 < collected.violations.length) {
    throw new Error(
      `package content violates the export invariants:\n  - ${collected.violations.join("\n  - ")}`
    );
  }
  if (collected.assets.length === 0) {
    throw new Error("no assets discovered; refusing to emit an empty package");
  }
  for (const asset of collected.assets) {
    asset.absolutePath = path.join(packageRoot, asset.path);
  }
  const rootPrefix = resolveRootPrefix(requestedPrefix, name);
  const tar = buildTar(collected.assets, rootPrefix);
  const buffer = gzipDeterministic(tar);
  const result = {
    buffer,
    name,
    version,
    rootPrefix,
    sha256: sha256Hex(buffer),
    assetCount: collected.assets.length,
    folderCount: collected.assets.filter((asset) => asset.isFolder).length,
    fileCount: collected.assets.length - collected.assets.filter((asset) => asset.isFolder).length,
    excludedSampleCount: collected.excludedSampleCount
  };
  if (cacheKey !== null) {
    writeCachedArtifact(packageRoot, cacheKey, result);
  }
  if (options.out && options.out.length > 0) {
    fs.mkdirSync(path.dirname(options.out), { recursive: true });
    fs.writeFileSync(options.out, buffer);
  }
  return result;
}

function main() {
  let options;
  try {
    options = parseArgs(process.argv.slice(2));
  } catch (error) {
    console.error(`[unitypackage-export] ERROR: ${error.message}`);
    console.error(
      "usage: node export-unitypackage.mjs [--package-root <dir>] " +
        "[--out <path>] [--root-prefix <prefix>]"
    );
    process.exitCode = 1;
    return;
  }
  try {
    if (options.out.length === 0) {
      const manifest = JSON.parse(
        fs.readFileSync(path.join(options.packageRoot, "package.json"), "utf8")
      );
      options.out = `${manifest.name}-${manifest.version}.unitypackage`;
    }
    const result = exportUnityPackage(options);
    console.log(
      `[unitypackage-export] ${path.basename(options.out)}\n` +
        `[unitypackage-export] ${result.name}@${result.version}\n` +
        `[unitypackage-export] ${result.assetCount} assets ` +
        `(${result.fileCount} files, ${result.folderCount} folders); ` +
        `${result.excludedSampleCount} Samples~/ entries excluded\n` +
        `[unitypackage-export] import root: ${result.rootPrefix}\n` +
        `[unitypackage-export] ${(result.buffer.length / (1024 * 1024)).toFixed(2)} MB`
    );
  } catch (error) {
    console.error(`[unitypackage-export] ERROR: ${error.message}`);
    process.exitCode = 1;
  }
}

const isMain =
  process.argv[1] !== undefined && fileURLToPath(import.meta.url) === path.resolve(process.argv[1]);

if (isMain) {
  main();
}

export { collectAssets, exportUnityPackage, gzipDeterministic, packagedList, resolveRootPrefix, buildTar, artifactCacheKey, readCachedArtifact };
