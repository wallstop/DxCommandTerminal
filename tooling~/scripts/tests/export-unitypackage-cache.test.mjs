/*
    Cache contract for the exporter's `cache: true` path, against the real
    repository. The fresh build is covered by export-unitypackage-real; this
    file covers the accelerator: a cached answer is the fresh answer, a
    clean tree records the entry under a key any process can recompute, and
    an entry whose bytes no longer match their digest is a miss that
    repairs itself instead of handing a test a broken artifact.
 */
import test from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { artifactCacheKey, exportUnityPackage } from "../release/export-unitypackage.mjs";

const toolingRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const packageRoot = path.resolve(toolingRoot, "..");

test("a cached export answers the same bytes as a fresh one", () => {
  const fresh = exportUnityPackage({ packageRoot, out: "" });
  const cached = exportUnityPackage({ packageRoot, out: "", cache: true });
  assert.strictEqual(cached.buffer.equals(fresh.buffer), true);
  assert.strictEqual(cached.name, fresh.name);
  assert.strictEqual(cached.assetCount, fresh.assetCount);
  assert.strictEqual(cached.rootPrefix, fresh.rootPrefix);
});

test("a clean tree records the entry under the recomputable key; a corrupt entry misses", () => {
  const key = artifactCacheKey(packageRoot);
  if (key === null) {
    /*
        A dirty checkout keys to nothing, so there is no entry to inspect:
        the byte-equality test above is the whole contract there, and the
        dirty-tree re-export is the release flow's own behavior.
     */
    return;
  }
  const entry = path.join(packageRoot, ".artifacts", "export-cache", `${key}.unitypackage`);
  const guarded = exportUnityPackage({ packageRoot, out: "", cache: true });
  assert.strictEqual(fs.existsSync(entry), true, "the cached export must record its entry");
  const intact = Buffer.from(fs.readFileSync(entry));
  assert.strictEqual(intact.equals(guarded.buffer), true);

  fs.writeFileSync(entry, "corrupted beyond the digest");
  const repaired = exportUnityPackage({ packageRoot, out: "", cache: true });
  assert.strictEqual(repaired.buffer.equals(guarded.buffer), true, "a corrupt entry misses, never answers");
  assert.strictEqual(fs.readFileSync(entry).equals(guarded.buffer), true, "the miss repairs the entry");
});
