import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(fileURLToPath(new URL("../../..", import.meta.url)));

const REQUIRED_FILES = [
  "README.md",
  "README.md.meta",
  "LICENSE",
  "LICENSE.meta",
  "CHANGELOG.md",
  "CHANGELOG.md.meta",
  "package.json.meta",
  "Runtime/WallstopStudios.DxCommandTerminal.asmdef",
  "Runtime/WallstopStudios.DxCommandTerminal.asmdef.meta",
  "Editor/WallstopStudios.DxCommandTerminal.Editor.asmdef",
  "Editor/WallstopStudios.DxCommandTerminal.Editor.asmdef.meta",
  "Tests/Runtime/WallstopStudios.DxCommandTerminal.Tests.Runtime.asmdef",
  "Tests/Runtime/WallstopStudios.DxCommandTerminal.Tests.Runtime.asmdef.meta"
];

const FORBIDDEN_EXACT = new Set([
  "AGENTS.md",
  "CLAUDE.md",
  "doc.md",
  ".cursorrules",
  ".editorconfig",
  ".gitattributes",
  ".gitignore",
  ".dockerignore",
  ".pre-commit-config.yaml",
  ".env.example",
  "package-lock.json",
  ".DS_Store"
]);

const FORBIDDEN_PREFIXES = [
  "tooling~",
  "node_modules",
  "scripts",
  ".git",
  ".github",
  ".llm",
  ".devcontainer",
  ".artifacts",
  "progress",
  ".env",
  ".vscode",
  ".codex",
  ".cursor",
  ".copilot",
  ".nanocoder",
  // Documentation media (README screenshots and the demo GIF): consumers do
  // not import it, and the README references it through absolute repository
  // URLs so npm rendering keeps working without shipping it (#47).
  "Media"
];

// Node refuses to spawn .cmd/.bat without a shell on Windows (CVE-2024-27980
// hardening: execFileSync("npm.cmd") fails outright there). Flag arguments are
// literal; the one path argument (--pack-destination) is quoted so shell
// joining cannot split a temp dir containing spaces.
const NPM = process.platform === "win32" ? "npm.cmd" : "npm";
const NPM_SPAWN_OPTIONS = process.platform === "win32" ? { shell: true } : {};

const tempDirs = [];
process.on("exit", () => {
  for (const dir of tempDirs) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

function fail(message) {
  console.error(`[package-validate] ERROR: ${message}`);
  process.exitCode = 1;
}

function readText(filePath) {
  try {
    return fs.readFileSync(filePath, "utf8");
  } catch {
    return null;
  }
}

function pack() {
  const destination = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-pack-"));
  tempDirs.push(destination);
  const destinationArgument =
    process.platform === "win32" ? `"${destination}"` : destination;
  const stdout = execFileSync(NPM, ["pack", "--pack-destination", destinationArgument], {
    cwd: REPO_ROOT,
    encoding: "utf8",
    ...NPM_SPAWN_OPTIONS
  });
  return path.join(destination, stdout.trim().split(/\r?\n/).at(-1));
}

function listTarball(tarball) {
  const stdout = execFileSync("tar", ["-tzf", tarball], { encoding: "utf8" });
  return stdout
    .split(/\r?\n/)
    .filter(Boolean)
    .map((entry) => entry.replace(/^package\//, ""))
    .filter((entry) => entry.length > 0 && !entry.endsWith("/"));
}

function extractFile(tarball, entry) {
  return execFileSync("tar", ["-xzf", tarball, "-O", `package/${entry}`], {
    encoding: "utf8",
    maxBuffer: 16 * 1024 * 1024
  });
}

function extractTarball(tarball) {
  const destination = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-extract-"));
  tempDirs.push(destination);
  execFileSync("tar", ["-xzf", tarball, "-C", destination]);
  return path.join(destination, "package");
}

function extractGuids(text) {
  return [...text.matchAll(/guid: ([0-9a-f]{32})/g)].map((match) => match[1]);
}

function trackedFiles() {
  const stdout = execFileSync("git", ["ls-files"], { cwd: REPO_ROOT, encoding: "utf8" });
  return new Set(stdout.split(/\r?\n/).filter(Boolean));
}

function main() {
  const rootManifest = JSON.parse(
    fs.readFileSync(path.join(REPO_ROOT, "package.json"), "utf8")
  );
  const tarball = pack();
  const entries = listTarball(tarball);
  const entrySet = new Set(entries);
  let problems = 0;

  const check = (ok, message) => {
    if (!ok) {
      fail(message);
      problems += 1;
    }
  };

  const shippedManifest = JSON.parse(extractFile(tarball, "package.json"));
  check(
    shippedManifest.name === rootManifest.name && shippedManifest.version === rootManifest.version,
    `shipped manifest identity mismatch: expected ${rootManifest.name}@${rootManifest.version}, ` +
      `got ${shippedManifest.name}@${shippedManifest.version}`
  );

  const allowlist = rootManifest.files;
  check(
    Array.isArray(allowlist) && 0 < allowlist.length,
    "package.json must declare an npm files allowlist"
  );
  if (Array.isArray(allowlist)) {
    const tracked = trackedFiles();
    /*
       Interpret the allowlist the way npm does: entries starting with "!"
       exclude (repo-internal payloads that Unity loads from the checkout but
       no distributed artifact may carry, e.g. the internal analyzers DLL),
       the rest include. The expected tarball is tracked files that are
       included and not excluded.
    */
    const includes = allowlist.filter((root) => !root.startsWith("!"));
    const excludes = allowlist
      .filter((root) => root.startsWith("!"))
      .map((root) => root.slice(1));
    check(0 < includes.length, "npm files allowlist has no include patterns");
    const matched = (entry, roots) =>
      roots.some((root) => entry === root || entry.startsWith(`${root}/`));
    const expected = new Set(["package.json"]);
    for (const entry of tracked) {
      if (matched(entry, includes) && !matched(entry, excludes)) {
        expected.add(entry);
      }
    }
    const notShipped = [...tracked].filter(
      (entry) => matched(entry, includes) && matched(entry, excludes)
    );
    for (const entry of notShipped) {
      if (!entry.endsWith(".dll") && !entry.endsWith(".dll.meta")) {
        fail(`internal-only exclusion covers a non-payload file: ${entry}`);
        problems += 1;
      }
    }
    for (const missing of expected) {
      if (!entrySet.has(missing)) {
        fail(`tracked file missing from tarball: ${missing}`);
        problems += 1;
      }
    }
    for (const extra of entrySet) {
      if (!expected.has(extra)) {
        fail(`untracked or unallowed file shipped in tarball: ${extra}`);
        problems += 1;
      }
    }
  }

  for (const required of REQUIRED_FILES) {
    check(entrySet.has(required), `required file missing from tarball: ${required}`);
  }

  for (const entry of entries) {
    if (FORBIDDEN_EXACT.has(entry)) {
      fail(`non-shippable file in tarball: ${entry}`);
      problems += 1;
      continue;
    }
    if (FORBIDDEN_PREFIXES.some((prefix) => entry.startsWith(`${prefix}/`))) {
      fail(`non-shippable path in tarball: ${entry}`);
      problems += 1;
    }
  }

  // Precompiled payload invariant (#22): the only shipped DLL is the source
  // generator analyzer payload. A vendored precompiled dependency
  // (System.Collections.Immutable et al) once broke consumer assembly
  // loading; any new DLL outside the analyzer payload fails the package.
  for (const entry of entries) {
    if (entry.endsWith(".dll") && !entry.startsWith("Runtime/Analyzers/")) {
      fail(`precompiled DLL outside the analyzer payload (#22): ${entry}`);
      problems += 1;
    }
  }

  const shippedFiles = entries.filter((entry) => !entry.endsWith("/"));
  const impliedDirectories = new Set();
  for (const file of shippedFiles) {
    let current = path.dirname(file);
    while (current && current !== "." && !impliedDirectories.has(current)) {
      impliedDirectories.add(current);
      current = path.dirname(current);
    }
  }

  // Samples~ content is exempt from the meta checks below: the tilde
  // folder is invisible to Unity inside the package cache, and Package
  // Manager copies samples into the consuming project on import, where
  // Unity generates fresh metas for the copied files.
  for (const directory of impliedDirectories) {
    if (directory === "Samples~" || directory.startsWith("Samples~/")) {
      continue;
    }

    check(
      entrySet.has(`${directory}.meta`),
      `shipped directory without its folder meta: ${directory}`
    );
  }

  for (const file of shippedFiles) {
    if (file.endsWith(".meta")) {
      const target = file.slice(0, -".meta".length);
      check(
        entrySet.has(target) || impliedDirectories.has(target),
        `orphan meta in tarball: ${file}`
      );
    } else if (file !== "package.json" && !file.startsWith("Samples~/")) {
      check(entrySet.has(`${file}.meta`), `shipped file without meta: ${file}`);
    }
  }

  for (const asmdef of REQUIRED_FILES.filter((candidate) => candidate.endsWith(".asmdef"))) {
    let parsed;
    try {
      parsed = JSON.parse(extractFile(tarball, asmdef));
    } catch (error) {
      check(false, `asmdef is not valid JSON: ${asmdef} (${error.message})`);
      continue;
    }
    check(
      typeof parsed.name === "string" && 0 < parsed.name.length,
      `asmdef missing a name: ${asmdef}`
    );
  }

  // Sample invariants: package.json declares every shipped Samples~/
  // directory (Package Manager lists and imports exactly the declared
  // samples), each declared sample ships an .asmdef so importing it
  // compiles, and no Samples~ content ships outside a declaration.
  const declaredSamples = Array.isArray(rootManifest.samples)
    ? rootManifest.samples
    : [];
  const shippedSampleEntries = entries.filter((entry) =>
    entry.startsWith("Samples~/")
  );
  if (0 < shippedSampleEntries.length) {
    check(
      0 < declaredSamples.length,
      "Samples~/ content ships without a package.json samples declaration"
    );
  }
  for (const sample of declaredSamples) {
    if (sample === null || typeof sample !== "object") {
      fail(`sample entry is not an object: ${JSON.stringify(sample) ?? String(sample)}`);
      problems += 1;
      continue;
    }
    check(
      typeof sample.path === "string" && sample.path.startsWith("Samples~/"),
      `sample path must live under Samples~/: ${sample.path}`
    );
    check(
      typeof sample.displayName === "string" && 0 < sample.displayName.length,
      `sample is missing a displayName: ${sample.path}`
    );
    const sampleFiles = shippedSampleEntries.filter((entry) =>
      entry.startsWith(`${sample.path}/`)
    );
    check(
      0 < sampleFiles.length,
      `declared sample missing from tarball: ${sample.path}`
    );
    check(
      sampleFiles.some((entry) => entry.endsWith(".asmdef")),
      `declared sample ships without an .asmdef: ${sample.path}`
    );
  }
  for (const entry of shippedSampleEntries) {
    check(
      declaredSamples.some(
        (sample) =>
          sample != null &&
          typeof sample === "object" &&
          typeof sample.path === "string" &&
          entry.startsWith(`${sample.path}/`)
      ),
      `shipped sample file outside every declared sample: ${entry}`
    );
  }

  // Font payload invariants (#52): every shipped font file must be referenced
  // by a shipped asset pack, and every pack reference must resolve to a
  // shipped asset. Together they keep the font payload curated - unreferenced
  // weights, italics, or variable-font duplicates fail the pack.
  const extracted = extractTarball(tarball);
  const guidToAssetPath = new Map();
  for (const entry of entries) {
    if (!entry.endsWith(".meta")) {
      continue;
    }
    const metaText = readText(path.join(extracted, entry));
    if (metaText === null) {
      continue;
    }
    const guid = extractGuids(metaText)[0];
    if (guid !== undefined) {
      guidToAssetPath.set(guid, entry.slice(0, -".meta".length));
    }
  }

  const packAssetPaths = entries.filter((entry) => /^Packs\/.*\.asset$/.test(entry));
  const referencedGuids = new Set();
  for (const packAsset of packAssetPaths) {
    const assetText = readText(path.join(extracted, packAsset));
    if (assetText === null) {
      continue;
    }
    for (const guid of extractGuids(assetText)) {
      referencedGuids.add(guid);
    }
  }

  for (const entry of entries) {
    if (!/^Fonts\/.*\.(ttf|otf)$/.test(entry)) {
      continue;
    }
    const metaText = readText(path.join(extracted, `${entry}.meta`));
    if (metaText === null) {
      // Reported by the shipped-file-without-meta check above.
      continue;
    }
    const guid = extractGuids(metaText)[0];
    check(
      guid !== undefined && referencedGuids.has(guid),
      `shipped font not referenced by any asset pack (payload bloat): ${entry}`
    );
  }

  for (const guid of referencedGuids) {
    check(
      guidToAssetPath.has(guid),
      `asset pack references an asset missing from the tarball (guid ${guid})`
    );
  }

  console.log(
    `[package-validate] ${path.basename(tarball)}\n` +
      `[package-validate] ${entries.length} entries checked; ` +
      `${problems === 0 ? "all content checks passed" : `${problems} problem(s) found`}`
  );
  if (0 < problems) {
    process.exitCode = 1;
  }
}

main();
