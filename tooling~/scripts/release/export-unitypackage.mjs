/*
    Unity-free .unitypackage exporter (T14).

    Walks the npm `files` allowlist exactly as consumers receive it (`npm pack
    --dry-run --json` is the single source of truth for shipped content), pairs
    every asset with its checked-in .meta (whose `guid:` becomes the artifact's
    GUID directory), and emits a deterministic ustar tarball normalized through
    gzip. Two checkouts of the same revision rebuild byte-identically: fixed
    tar header fields, fixed entry order, and a normalized gzip header (MTIME 0,
    OS 0xFF).

    Samples~/ content is excluded: the tilde folder is invisible to Unity, its
    files ship without .meta files (Package Manager generates fresh metas when
    a consumer imports the sample), and there is no GUID identity to preserve.

    Format reference: each GUID directory contains `asset` (file content, files
    only), `asset.meta` (the checked-in meta verbatim), and `pathname` (the
    import destination, `--root-prefix` + asset path, no .meta suffix).
*/
import { execFileSync } from "node:child_process";
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
    rootPrefix: ""
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

function packagedList(packageRoot) {
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
  return {
    name: report.name,
    version: report.version,
    files: report.files.map((file) => file.path).sort()
  };
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
    collected violation; returns { buffer, assetCount, folderCount,
    fileCount, excludedSampleCount, rootPrefix }.
*/
function exportUnityPackage(options) {
  const packageRoot = path.resolve(options.packageRoot);
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
  const rootPrefix = resolveRootPrefix(options.rootPrefix ?? "", name);
  const tar = buildTar(collected.assets, rootPrefix);
  const buffer = gzipDeterministic(tar);
  if (options.out && options.out.length > 0) {
    fs.mkdirSync(path.dirname(options.out), { recursive: true });
    fs.writeFileSync(options.out, buffer);
  }
  return {
    buffer,
    name,
    version,
    rootPrefix,
    assetCount: collected.assets.length,
    folderCount: collected.assets.filter((asset) => asset.isFolder).length,
    fileCount: collected.assets.length - collected.assets.filter((asset) => asset.isFolder).length,
    excludedSampleCount: collected.excludedSampleCount
  };
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

export { collectAssets, exportUnityPackage, gzipDeterministic, packagedList, resolveRootPrefix, buildTar };
