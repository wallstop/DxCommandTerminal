/*
    Cache contract for the exporter's `cache: true` path, against the real
    repository. Freshness is owned elsewhere - export-unitypackage-real
    builds fresh, and the t13 gate compares the cache's answer against a
    fresh build of the same checkout. This file covers the accelerator: the
    cached bytes parse as the real artifact, the entry sits under a key any
    process recomputes from the packaged inputs, and an entry whose bytes
    no longer match their digest never answers. The key hashes packaged
    inputs, so the entry survives edits outside the package - the shape of
    a normal local iteration.
 */
import test from "node:test";
import assert from "node:assert";
import crypto from "node:crypto";
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
  /*
      A private probe key under the real cache directory: no parallel
      process ever reads it, so the shared entry this file guards is never
      corrupted out from under a concurrent reader.
   */
  const key = crypto.createHash("sha256").update("dxct-cache-probe-key").digest("hex");
  const directory = path.join(packageRoot, ".artifacts", "export-cache");
  const paths = {
    buffer: path.join(directory, `${key}.unitypackage`),
    meta: path.join(directory, `${key}.json`)
  };
  const meta = {
    name: "probe",
    version: "0.0.0",
    rootPrefix: "probe",
    sha256: "0".repeat(64),
    assetCount: 0,
    folderCount: 0,
    fileCount: 0,
    excludedSampleCount: 0
  };
  fs.mkdirSync(directory, { recursive: true });
  try {
    fs.writeFileSync(paths.buffer, "bytes");
    fs.writeFileSync(paths.meta, `${JSON.stringify(meta)}\n`);
    assert.strictEqual(
      readCachedArtifact(packageRoot, key),
      null,
      "bytes that betray their recorded digest never answer"
    );
    meta.sha256 = crypto.createHash("sha256").update("bytes").digest("hex");
    fs.writeFileSync(paths.meta, `${JSON.stringify(meta)}\n`);
    assert.notStrictEqual(readCachedArtifact(packageRoot, key), null);
    fs.writeFileSync(paths.meta, "{not json");
    assert.strictEqual(readCachedArtifact(packageRoot, key), null, "an unreadable meta misses");
  } finally {
    fs.rmSync(paths.buffer, { force: true });
    fs.rmSync(paths.meta, { force: true });
  }
});
