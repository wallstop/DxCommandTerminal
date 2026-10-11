/*
    Real-package export contract for tooling~/scripts/release/export-unitypackage.mjs
    (T14). Lives apart from the fixture tests so the parallel test runner overlaps
    its `npm pack` spawn + full archive builds (the suite's wall-time pole) with
    every other file. The rebuild answers through the exporter's cache: on a
    cold cache that is a second full build (CI's every run), on a warm one it
    is the digest-checked bytes a previous process recorded - so the equality
    below always spans two builds, and warm runs do not pay the second one.
    Coverage is unchanged: same assertions as before the split, just no longer
    gating the suite's critical path.
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
  const artifact = exportUnityPackage({ packageRoot, out: "" });
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
