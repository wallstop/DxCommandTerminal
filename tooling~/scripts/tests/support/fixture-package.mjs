/*
    A structurally complete stand-in package for the exporter-contract tests
    that do not need the real production tree: every file the `files`
    allowlist ships carries a checked-in meta, the runtime assembly references
    Unity.InputSystem (so the import drill derives the same scratch
    dependency the real tree produces), an Analyzers/ entry mirrors the
    analyzer payload, and Samples~ content ships no metas - the shape the
    exporter excludes by design. Fixtures build in milliseconds on ext4, so
    no test here pays the real tree's whole-package export or its 9p walk.
 */
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const FIXTURE_GUIDS = {
  manifest: "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1",
  readme: "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2",
  runtimeFolder: "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3",
  runtimeSource: "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4",
  runtimeAsmdef: "e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5",
  analyzersFolder: "f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6",
  analyzerPayload: "a7a7a7a7a7a7a7a7a7a7a7a7a7a7a7a7"
};

const FIXTURE_ASMDEF = {
  name: "FixtureAssembly",
  references: ["Unity.InputSystem"],
  includePlatforms: [],
  excludePlatforms: [],
  allowUnsafeCode: false,
  overrideReferences: false,
  precompiledReferences: [],
  autoReferenced: true,
  defineConstraints: [],
  versionDefines: [],
  noEngineReferences: false
};

export function makeFixturePackage(root, { name = "test.pkg", version = "0.1.0" } = {}) {
  fs.mkdirSync(root, { recursive: true });
  for (const [relative, content] of fixtureFileBodies({ name, version })) {
    const target = path.join(root, relative);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, content);
  }
  return root;
}

/*
    A stable home for one fixture shape, for the tests that export with
    `cache: true`: the exporter's pack query (`npm pack --dry-run`, a few
    hundred milliseconds of npm startup) and archive build are keyed on the
    packaged inputs under the fixture's own `.artifacts/export-cache/`, so a
    root that survives between runs turns every warm export into two cache
    reads. The directory name hashes the exact bytes the builder writes
    (name, version, and every file body), so a fixture change addresses a
    fresh root by construction and a stale one is unreachable, and files are
    rewritten only when their bytes changed so the input signature - size,
    mtime, inode - stays stable across runs. Tests that mutate a fixture
    pass a distinct `variant` and never share a root with canonical readers;
    the tree lives under the OS temp directory, so it costs nothing in the
    repository and resets with the machine.
 */
export function persistentFixturePackage({ name = "test.pkg", version = "0.1.0", variant = "" } = {}) {
  const files = fixtureFileBodies({ name, version });
  const identity = crypto.createHash("sha256");
  identity.update(`${variant}\0`);
  for (const [relative, content] of files) {
    identity.update(relative);
    identity.update("\0");
    identity.update(content);
    identity.update("\0");
  }
  const root = path.join(os.tmpdir(), "dxct-fixture-packages", identity.digest("hex").slice(0, 16));
  fs.mkdirSync(root, { recursive: true });
  for (const [relative, content] of files) {
    const target = path.join(root, relative);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    try {
      if (fs.readFileSync(target, "utf8") === content) continue;
    } catch {
      /* Absent: write it. */
    }
    /*
        Rename-into-place so a concurrent first materialization from another
        suite process (a second worktree on this machine) can never hand a
        reader a half-written fixture file.
     */
    const staged = `${target}.${process.pid}.tmp`;
    fs.writeFileSync(staged, content);
    fs.renameSync(staged, target);
  }
  return root;
}

function fixtureFileBodies({ name, version }) {
  const manifest = {
    name,
    version,
    files: [
      "Runtime",
      "Runtime.meta",
      "Runtime/Foo.cs",
      "Runtime/Foo.cs.meta",
      "Runtime/Fixture.asmdef",
      "Runtime/Fixture.asmdef.meta",
      "Runtime/Analyzers",
      "Runtime/Analyzers.meta",
      "Runtime/Analyzers/Payload.cs",
      "Runtime/Analyzers/Payload.cs.meta",
      "!Runtime/Analyzers/Payload.dll",
      "!Runtime/Analyzers/Payload.dll.meta",
      "README.md",
      "README.md.meta",
      "package.json",
      "package.json.meta",
      "Samples~"
    ]
  };
  const lines = [];
  const file = (relative, content) => lines.push([relative, content]);
  const meta = (assetPath, guid, isFolder) => {
    const entries = ["fileFormatVersion: 2", `guid: ${guid}`];
    if (isFolder) {
      entries.push("folderAsset: yes", "DefaultImporter:");
    }
    file(`${assetPath}.meta`, `${entries.join("\n")}\n`);
  };
  file("package.json", `${JSON.stringify(manifest, null, 2)}\n`);
  meta("package.json", FIXTURE_GUIDS.manifest, false);
  file("README.md", "# test pkg\n");
  meta("README.md", FIXTURE_GUIDS.readme, false);
  meta("Runtime", FIXTURE_GUIDS.runtimeFolder, true);
  file("Runtime/Foo.cs", "// foo\n");
  meta("Runtime/Foo.cs", FIXTURE_GUIDS.runtimeSource, false);
  file("Runtime/Fixture.asmdef", `${JSON.stringify(FIXTURE_ASMDEF, null, 2)}\n`);
  meta("Runtime/Fixture.asmdef", FIXTURE_GUIDS.runtimeAsmdef, false);
  meta("Runtime/Analyzers", FIXTURE_GUIDS.analyzersFolder, true);
  file("Runtime/Analyzers/Payload.cs", "// analyzer payload stand-in\n");
  meta("Runtime/Analyzers/Payload.cs", FIXTURE_GUIDS.analyzerPayload, false);
  file("Runtime/Analyzers/Payload.dll", "binary payload\n");
  file("Runtime/Analyzers/Payload.dll.meta", "fileFormatVersion: 2\n");
  file("Samples~/Widget.cs", "// sample\n");
  return lines;
}
