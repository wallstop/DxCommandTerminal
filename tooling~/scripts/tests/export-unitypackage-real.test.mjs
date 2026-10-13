/*
    Real-package export contract for tooling~/scripts/release/export-unitypackage.mjs
    (T14). Lives apart from the fixture tests so the parallel test runner overlaps
    it with every other file. Both exports ask for the cache, so a run pays one
    real build on a cold cache and two disk echoes on a warm one. What is pinned
    where now: THIS file pins the real tree (the real allowlist ships the full
    production tree, no Samples~, no Tests/); export-unitypackage.test.mjs's
    two-checkout test pins archive determinism with two independent fresh
    builds; export-unitypackage-cache.test.mjs pins the key's input classes
    (a packaged edit re-exports, a non-shipped edit does not) and the digest
    miss. No test compares two builds of the real tree anymore - the real
    tree's build is the expensive one, and determinism is tree-agnostic.
*/
import test from "node:test";
import assert from "node:assert";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { readArtifact } from "./support/unitypackage-artifact.mjs";

const toolingRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const packageRoot = path.resolve(toolingRoot, "..");
const exporterPath = path.join(toolingRoot, "scripts", "release", "export-unitypackage.mjs");
const { exportUnityPackage } = await import(pathToFileURL(exporterPath).href);

test("the real package exports and validates, and the cache echoes it byte-identically", () => {
  const artifact = exportUnityPackage({ packageRoot, out: "", cache: true });
  const repeated = exportUnityPackage({ packageRoot, out: "", cache: true });
  assert.strictEqual(artifact.buffer.equals(repeated.buffer), true);
  assert.strictEqual(artifact.name, "com.wallstop-studios.dxcommandterminal");
  const names = readArtifact(artifact.buffer).map((entry) => entry.name);
  assert.ok(names.every((name) => !name.includes("Samples~")));
  const pathnames = readArtifact(artifact.buffer)
    .filter((entry) => entry.name.endsWith("/pathname"))
    .map((entry) => entry.content.toString("utf8"));
  assert.ok(pathnames.every((pathname) => !pathname.includes("/Tests/") && !pathname.endsWith("/Tests")));
  assert.strictEqual(artifact.rootPrefix, "Packages/com.wallstop-studios.dxcommandterminal");
  assert.ok(artifact.fileCount > 350 && artifact.folderCount > 50,
    "the real allowlist must ship the full production tree");
});
