/*
    Contract tests for tooling~/scripts/release/export-unitypackage.mjs (T14).

    The exporter must pair every npm-shipped file with its checked-in meta,
    exclude Samples~ (no metas exist there by design), refuse broken GUID
    identity fail-closed, and rebuild byte-identically from two checkouts.
    A minimal tar reader (tests/support/unitypackage-artifact.mjs) pins the
    artifact structure itself: GUID directory names, asset/asset.meta/pathname
    contents, folder entries without an asset, and the deterministic header
    fields (mtime 0, uid/gid 0, fixed modes, ustar magic) plus the normalized
    gzip header (MTIME 0, OS 0xFF).

    The real-package export (the suite's wall-time pole: an `npm pack` spawn
    plus two full archive builds) lives in export-unitypackage-real.test.mjs so
    the parallel test runner overlaps it with everything else.
*/
import test from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { entryMap, readArtifact } from "./support/unitypackage-artifact.mjs";

// The other tooling tests call the tooling~ root "repoRoot"; the exported
// package root is one level above it.
const toolingRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const exporterPath = path.join(toolingRoot, "scripts", "release", "export-unitypackage.mjs");
const { collectAssets, exportUnityPackage } = await import(
  pathToFileURL(exporterPath).href
);

const tempDirs = [];
test.after(() => {
  for (const dir of tempDirs) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

function tempRoot(label) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), `dxt-export-${label}-`));
  tempDirs.push(dir);
  return dir;
}

function writeMeta(root, assetPath, guid, isFolder) {
  const metaPath = `${assetPath}.meta`;
  fs.mkdirSync(path.dirname(path.join(root, metaPath)), { recursive: true });
  const lines = ["fileFormatVersion: 2", `guid: ${guid}`];
  if (isFolder) {
    lines.push("folderAsset: yes", "DefaultImporter:");
  }
  fs.writeFileSync(path.join(root, metaPath), `${lines.join("\n")}\n`);
}

/*
    A minimal but structurally complete package: root manifest, a file at the
    root, one folder asset, a nested file, and Samples~ content without metas.
*/
const GUIDS = {
  manifest: "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1",
  readme: "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2",
  runtime: "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3",
  foo: "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4"
};

// Every exported asset path and the role naming its GUID.
const ASSETS = [
  ["package.json", "manifest"],
  ["README.md", "readme"],
  ["Runtime", "runtime"],
  ["Runtime/Foo.cs", "foo"]
];

function makeFixture(root) {
  fs.writeFileSync(path.join(root, "package.json"), JSON.stringify({ name: "test.pkg", version: "0.1.0" }));
  writeMeta(root, "package.json", GUIDS.manifest, false);
  fs.writeFileSync(path.join(root, "README.md"), "# test pkg\n");
  writeMeta(root, "README.md", GUIDS.readme, false);
  fs.mkdirSync(path.join(root, "Runtime"), { recursive: true });
  writeMeta(root, "Runtime", GUIDS.runtime, true);
  fs.writeFileSync(path.join(root, "Runtime", "Foo.cs"), "// foo\n");
  writeMeta(root, "Runtime/Foo.cs", GUIDS.foo, false);
  fs.mkdirSync(path.join(root, "Samples~"), { recursive: true });
  fs.writeFileSync(path.join(root, "Samples~", "Widget.cs"), "// sample\n");
}

test("exported artifact carries every fixture asset under the import root", () => {
  const root = tempRoot("structure");
  makeFixture(root);
  const result = exportUnityPackage({ packageRoot: root, out: "" });
  const entries = entryMap(readArtifact(result.buffer));

  for (const [assetPath, role] of ASSETS) {
    const guid = GUIDS[role];
    const prefix = `${guid}/`;
    assert.ok(entries.has(`${prefix}asset.meta`), `missing asset.meta for ${assetPath}`);
    assert.ok(entries.has(`${prefix}pathname`), `missing pathname for ${assetPath}`);
    assert.strictEqual(
      entries.get(`${prefix}asset.meta`).content.toString("utf8"),
      fs.readFileSync(path.join(root, `${assetPath}.meta`), "utf8"),
      `asset.meta must be the checked-in meta verbatim: ${assetPath}`
    );
    assert.strictEqual(
      entries.get(`${prefix}pathname`).content.toString("utf8"),
      `Packages/test.pkg/${assetPath}`,
      `pathname must name the import destination: ${assetPath}`
    );
  }
  for (const folderGuid of [GUIDS.runtime]) {
    assert.strictEqual(entries.get(`${GUIDS.runtime}/`).typeflag, "5", "folder asset must be a dir entry");
    assert.strictEqual(entries.get(`${GUIDS.runtime}/`).mode, "0000755");
    assert.strictEqual(entries.has(`${GUIDS.runtime}/asset`), false, "folder assets carry no asset file");
  }
  assert.strictEqual(entries.get(`${GUIDS.foo}/asset`).content.toString("utf8"), "// foo\n");
  assert.strictEqual(entries.get(`${GUIDS.foo}/asset`).mode, "0000644");
  assert.strictEqual(result.excludedSampleCount, 1);
});

test("Samples~ content is excluded from the artifact", () => {
  const root = tempRoot("samples");
  makeFixture(root);
  const result = exportUnityPackage({ packageRoot: root, out: "" });
  const entries = readArtifact(result.buffer).map((entry) => entry.name);
  assert.ok(entries.every((name) => !name.includes("Samples~")));
  assert.strictEqual(
    entries.filter((name) => name.endsWith("/pathname")).length,
    ASSETS.length,
    "only metas-carrying assets may export"
  );
});

test("root prefix override moves the import destination", () => {
  const root = tempRoot("prefix");
  makeFixture(root);
  const result = exportUnityPackage({ packageRoot: root, out: "", rootPrefix: "Assets/test.pkg/" });
  const entries = readArtifact(result.buffer);
  const pathname = entries.find((entry) => entry.name.endsWith("/pathname")).content.toString("utf8");
  assert.ok(pathname.startsWith("Assets/test.pkg/"), `unexpected pathname root: ${pathname}`);
});

test("two checkouts rebuild byte-identically", () => {
  const first = tempRoot("checkout-a");
  const second = tempRoot("checkout-b");
  makeFixture(first);
  makeFixture(second);
  const firstArtifact = exportUnityPackage({ packageRoot: first, out: "" }).buffer;
  const secondArtifact = exportUnityPackage({ packageRoot: second, out: "" }).buffer;
  assert.strictEqual(firstArtifact.equals(secondArtifact), true);
});

test("a file without a meta fails the export", () => {
  const root = tempRoot("no-meta");
  makeFixture(root);
  fs.rmSync(path.join(root, "README.md.meta"));
  const shipped = ["README.md", "package.json"];
  const collected = collectAssets(root, shipped);
  assert.ok(
    collected.violations.some((message) => message.includes("shipped file without meta: README.md")),
    `expected a missing-meta violation, got: ${collected.violations.join("; ")}`
  );
});

test("broken and duplicate GUIDs fail the export", () => {
  const root = tempRoot("guids");
  makeFixture(root);
  writeMeta(root, "Runtime/Bar.cs", "not-a-guid", false);
  fs.writeFileSync(path.join(root, "Runtime", "Bar.cs"), "// bar\n");
  const { violations } = collectAssets(root, [
    "package.json",
    "package.json.meta",
    "README.md",
    "README.md.meta",
    "Runtime",
    "Runtime.meta",
    "Runtime/Foo.cs",
    "Runtime/Foo.cs.meta",
    "Runtime/Bar.cs",
    "Runtime/Bar.cs.meta"
  ]);
  assert.ok(violations.some((message) => message.includes("meta without a 32-hex guid")));

  const duplicateRoot = tempRoot("guids-dup");
  makeFixture(duplicateRoot);
  writeMeta(duplicateRoot, "Runtime/Bar.cs", GUIDS.foo, false);
  fs.writeFileSync(path.join(duplicateRoot, "Runtime", "Bar.cs"), "// bar\n");
  const duplicate = collectAssets(duplicateRoot, [
    "package.json",
    "package.json.meta",
    "README.md",
    "README.md.meta",
    "Runtime",
    "Runtime.meta",
    "Runtime/Foo.cs",
    "Runtime/Foo.cs.meta",
    "Runtime/Bar.cs",
    "Runtime/Bar.cs.meta"
  ]);
  assert.ok(
    duplicate.violations.some((message) => message.includes("duplicate guid")),
    `expected a duplicate-guid violation, got: ${duplicate.violations.join("; ")}`
  );
});

test("a folder meta without folderAsset: yes fails the export", () => {
  const root = tempRoot("folder-flag");
  makeFixture(root);
  fs.writeFileSync(
    path.join(root, "Runtime.meta"),
    "fileFormatVersion: 2\nguid: c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3\n"
  );
  const { violations } = collectAssets(root, [
    "package.json",
    "package.json.meta",
    "README.md",
    "README.md.meta",
    "Runtime",
    "Runtime.meta",
    "Runtime/Foo.cs",
    "Runtime/Foo.cs.meta"
  ]);
  assert.ok(
    violations.some((message) => message.includes("folderAsset: yes")),
    `expected a folderAsset violation, got: ${violations.join("; ")}`
  );
});

test("an orphan meta without its target fails the export", () => {
  const root = tempRoot("orphan");
  makeFixture(root);
  writeMeta(root, "Gone.cs", "e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", false);
  const { violations } = collectAssets(root, [
    "package.json",
    "package.json.meta",
    "README.md",
    "README.md.meta",
    "Runtime",
    "Runtime.meta",
    "Runtime/Foo.cs",
    "Runtime/Foo.cs.meta",
    "Gone.cs.meta"
  ]);
  assert.ok(
    violations.some((message) => message.includes("orphan meta without its target")),
    `expected an orphan-meta violation, got: ${violations.join("; ")}`
  );
});
