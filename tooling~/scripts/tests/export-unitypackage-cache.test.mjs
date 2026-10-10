/*
    Cache contract for the exporter's `cache: true` path, against the real
    repository. The fresh build is covered by export-unitypackage-real; this
    file covers the accelerator: the cached bytes parse as the real artifact,
    the entry sits under a key any process recomputes from the packaged
    inputs, and an entry whose bytes no longer match their digest never
    answers. The key hashes packaged inputs, so the entry survives edits
    outside the package - the shape of a normal local iteration.
 */
import test from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { artifactCacheKey, exportUnityPackage, readCachedArtifact } from "../release/export-unitypackage.mjs";
import { readArtifact } from "./support/unitypackage-artifact.mjs";

const toolingRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const packageRoot = path.resolve(toolingRoot, "..");

test("the cached export carries the production artifact", () => {
  const cached = exportUnityPackage({ packageRoot, out: "", cache: true });
  assert.strictEqual(cached.name, "com.wallstop-studios.dxcommandterminal");
  const names = readArtifact(cached.buffer).map((entry) => entry.name);
  assert.ok(names.length > 400, "the cached artifact must carry the production tree");
  assert.ok(names.every((name) => !name.includes("Samples~")));
});

test("the entry sits under the recomputable key and answers byte-identically", () => {
  const key = artifactCacheKey(packageRoot);
  if (key === null) {
    /* No package.json, no key: the export above was a fresh build. */
    return;
  }
  const entry = path.join(packageRoot, ".artifacts", "export-cache", `${key}.unitypackage`);
  const guarded = exportUnityPackage({ packageRoot, out: "", cache: true });
  assert.strictEqual(fs.existsSync(entry), true, "the cached export must record its entry");
  assert.strictEqual(fs.readFileSync(entry).equals(guarded.buffer), true);
});

test("an entry whose bytes betray their digest is a miss, never an answer", () => {
  const key = artifactCacheKey(packageRoot);
  if (key === null) {
    return;
  }
  const paths = {
    buffer: path.join(packageRoot, ".artifacts", "export-cache", `${key}.unitypackage`),
    meta: path.join(packageRoot, ".artifacts", "export-cache", `${key}.json`)
  };
  const intact = fs.readFileSync(paths.buffer);
  const intactMeta = fs.readFileSync(paths.meta);
  try {
    fs.writeFileSync(paths.buffer, "corrupted beyond the digest");
    assert.strictEqual(readCachedArtifact(packageRoot, key), null);
    fs.writeFileSync(paths.buffer, intact);
    fs.unlinkSync(paths.meta);
    assert.strictEqual(readCachedArtifact(packageRoot, key), null);
  } finally {
    /* Restore both halves; a test must not leave the entry missing. */
    fs.writeFileSync(paths.buffer, intact);
    fs.writeFileSync(paths.meta, intactMeta);
  }
});
