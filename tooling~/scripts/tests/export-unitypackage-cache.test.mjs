/*
    Cache contract for the exporter's `cache: true` path, on a fixture
    package. This file owns the cache mechanics, which are tree-agnostic:
    the cached bytes parse as the artifact, the entry sits under a key any
    process recomputes from the packaged inputs, a packaged edit re-exports
    while a non-shipped edit does not, and an entry whose bytes no longer
    match their digest never answers. The real tree's fullness and the
    archive's determinism are owned by export-unitypackage-real and the
    two-checkout fixture test. Fixtures keep the real checkout's
    .artifacts out of the test suite entirely.
 */
import test from "node:test";
import assert from "node:assert";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { artifactCacheKey, exportUnityPackage, readCachedArtifact } from "../release/export-unitypackage.mjs";
import { persistentFixturePackage } from "./support/fixture-package.mjs";
import { readArtifact } from "./support/unitypackage-artifact.mjs";

const exporterHref = pathToFileURL(
  path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "release", "export-unitypackage.mjs")
).href;

/*
    Roots persist under the OS temp directory (see persistentFixturePackage)
    so warm runs answer from the exporter's disk caches instead of spawning
    `npm pack` and rebuilding the archive. The read-only tests share one
    canonical root; the edit test mutates a variant root so the canonical
    bytes and their cache entries stay untouched.
 */
const canonicalPackage = () => persistentFixturePackage({ name: "cache.pkg", version: "0.2.0" });
const mutablePackage = () => persistentFixturePackage({ name: "cache.pkg", version: "0.2.0", variant: "edits" });

test("the cached export carries the packaged tree", () => {
  const packageRoot = canonicalPackage();
  const cached = exportUnityPackage({ packageRoot, out: "", cache: true });
  assert.strictEqual(cached.name, "cache.pkg");
  const names = readArtifact(cached.buffer).map((entry) => entry.name);
  assert.ok(names.length > 10, "the cached artifact must carry the packaged tree");
  assert.ok(names.every((name) => !name.includes("Samples~")));
});

test("the entry sits under the recomputable key and answers byte-identically", () => {
  const packageRoot = canonicalPackage();
  const key = artifactCacheKey(packageRoot);
  const entry = path.join(packageRoot, ".artifacts", "export-cache", `${key}.unitypackage`);
  const guarded = exportUnityPackage({ packageRoot, out: "", cache: true });
  assert.strictEqual(fs.existsSync(entry), true, "the cached export must record its entry");
  assert.strictEqual(fs.readFileSync(entry).equals(guarded.buffer), true);
});

test("a packaged edit re-exports; an excluded edit does not", async () => {
  /*
      The key memoizes per process, so the invalidation classes are tested
      across fresh module instances - a URL fragment makes a distinct module
      URL, so each import re-keys. That is the shape a real iteration has:
      one process exports, a later one (after the edit) must not answer the
      old bytes. Content edits change file sizes, so the stat signature
      moves regardless of mtime granularity; if Node ever dropped fragment
      identity the second assert fails loudly instead of passing vacuously.
   */
  const first = await import(exporterHref);
  const packageRoot = mutablePackage();
  const beforeKey = first.artifactCacheKey(packageRoot);
  const before = first.exportUnityPackage({ packageRoot, out: "", cache: true });

  // A shipped file changed: a fresh process must key the new inputs and
  // answer the fresh content, not the cached bytes.
  fs.writeFileSync(path.join(packageRoot, "Runtime", "Foo.cs"), "// foo, edited\n");
  const second = await import(`${exporterHref}#fresh-shipped-edit`);
  assert.notStrictEqual(second.artifactCacheKey(packageRoot), beforeKey, "a shipped edit must move the key");
  const afterEdit = second.exportUnityPackage({ packageRoot, out: "", cache: true });
  assert.strictEqual(afterEdit.buffer.equals(before.buffer), false, "the artifact must carry the edit");
  assert.ok(
    readArtifact(afterEdit.buffer).some((entry) => entry.name.endsWith("/asset") && entry.content.toString("utf8").includes("edited")),
    "the fresh build must be the answer"
  );

  // A file the allowlist excludes changed: the key and the answer stay.
  const stableKey = second.artifactCacheKey(packageRoot);
  fs.writeFileSync(path.join(packageRoot, "Runtime", "Analyzers", "Payload.dll"), "binary edit\n");
  const third = await import(`${exporterHref}#fresh-excluded-edit`);
  assert.strictEqual(third.artifactCacheKey(packageRoot), stableKey, "an excluded edit must not move the key");
  const stable = third.exportUnityPackage({ packageRoot, out: "", cache: true });
  assert.strictEqual(stable.buffer.equals(afterEdit.buffer), true, "the answer is unchanged");

  /*
      Leave the variant root's packaged bytes canonical so a later run's
      materialization is a no-op. The restore still bumps the restored
      files' mtimes, so that run's before-edit export re-keys and pays one
      `npm pack` - the win over master is that the edit-adjacent excluded
      edit and every canonical-root export stay cache hits.
   */
  fs.writeFileSync(path.join(packageRoot, "Runtime", "Foo.cs"), "// foo\n");
  fs.writeFileSync(path.join(packageRoot, "Runtime", "Analyzers", "Payload.dll"), "binary payload\n");
});

test("an entry whose bytes betray their digest is a miss, never an answer", () => {
  const packageRoot = canonicalPackage();
  /*
      A private probe key under the fixture's own cache directory: no
      parallel process ever reads it, so the entry the previous tests
      guard is never corrupted out from under a concurrent reader.
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
