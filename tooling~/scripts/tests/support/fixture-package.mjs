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
import fs from "node:fs";
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

function writeMeta(root, assetPath, guid, isFolder) {
  const metaPath = `${assetPath}.meta`;
  fs.mkdirSync(path.dirname(path.join(root, metaPath)), { recursive: true });
  const lines = ["fileFormatVersion: 2", `guid: ${guid}`];
  if (isFolder) {
    lines.push("folderAsset: yes", "DefaultImporter:");
  }
  fs.writeFileSync(path.join(root, metaPath), `${lines.join("\n")}\n`);
}

export function makeFixturePackage(root, { name = "test.pkg", version = "0.1.0" } = {}) {
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
      "README.md",
      "README.md.meta",
      "package.json",
      "package.json.meta",
      "Samples~"
    ]
  };
  fs.mkdirSync(root, { recursive: true });
  fs.writeFileSync(path.join(root, "package.json"), `${JSON.stringify(manifest, null, 2)}\n`);
  writeMeta(root, "package.json", FIXTURE_GUIDS.manifest, false);
  fs.writeFileSync(path.join(root, "README.md"), "# test pkg\n");
  writeMeta(root, "README.md", FIXTURE_GUIDS.readme, false);
  fs.mkdirSync(path.join(root, "Runtime"), { recursive: true });
  writeMeta(root, "Runtime", FIXTURE_GUIDS.runtimeFolder, true);
  fs.writeFileSync(path.join(root, "Runtime", "Foo.cs"), "// foo\n");
  writeMeta(root, "Runtime/Foo.cs", FIXTURE_GUIDS.runtimeSource, false);
  fs.writeFileSync(path.join(root, "Runtime", "Fixture.asmdef"), `${JSON.stringify(FIXTURE_ASMDEF, null, 2)}\n`);
  writeMeta(root, "Runtime/Fixture.asmdef", FIXTURE_GUIDS.runtimeAsmdef, false);
  fs.mkdirSync(path.join(root, "Runtime", "Analyzers"), { recursive: true });
  writeMeta(root, "Runtime/Analyzers", FIXTURE_GUIDS.analyzersFolder, true);
  fs.writeFileSync(path.join(root, "Runtime", "Analyzers", "Payload.cs"), "// analyzer payload stand-in\n");
  writeMeta(root, "Runtime/Analyzers/Payload.cs", FIXTURE_GUIDS.analyzerPayload, false);
  fs.mkdirSync(path.join(root, "Samples~"), { recursive: true });
  fs.writeFileSync(path.join(root, "Samples~", "Widget.cs"), "// sample\n");
  return root;
}
