/*
    Real-package export contract for tooling~/scripts/release/export-unitypackage.mjs
    (T14). Lives apart from the fixture tests so the parallel test runner overlaps
    it with every other file. Both exports ask for the cache: on a cold cache
    the first call is the full build and the second is its digest-checked disk
    echo; on a warm one both are echoes, so the equality below is nearly free
    and no run here pays a second real build. The fresh-build side of the
    contract is CI's cold backstop (a fresh runner always misses) plus the
    fixture tests, which build identical trees twice and compare.
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

test("the real package exports, validates, and rebuilds byte-identically", () => {
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
