/*
    Cache contract for the exporter's `cache: true` path, against the real
    repository. The fresh build is covered by export-unitypackage-real; this
    file covers the accelerator: the cached bytes parse as the real artifact,
    a clean tree records the entry under a key any process can recompute,
    and an entry whose bytes no longer match their digest is a miss whose
    re-export rebuilds the same bytes - which is also how freshness is
    pinned here, without this file paying a dedicated fresh build.
 */
import test from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { artifactCacheKey, exportUnityPackage } from "../release/export-unitypackage.mjs";
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

test("a clean tree records the entry under the recomputable key; a corrupt entry misses", () => {
  const key = artifactCacheKey(packageRoot);
  if (key === null) {
    /*
        A dirty checkout keys to nothing, so there is no entry to inspect:
        the export above is a fresh build and the exporter's own digest
        checks are the contract there.
     */
    return;
  }
  const entry = path.join(packageRoot, ".artifacts", "export-cache", `${key}.unitypackage`);
  const guarded = exportUnityPackage({ packageRoot, out: "", cache: true });
  assert.strictEqual(fs.existsSync(entry), true, "the cached export must record its entry");
  assert.strictEqual(fs.readFileSync(entry).equals(guarded.buffer), true);

  fs.writeFileSync(entry, "corrupted beyond the digest");
  const repaired = exportUnityPackage({ packageRoot, out: "", cache: true });
  assert.strictEqual(repaired.buffer.equals(guarded.buffer), true, "a corrupt entry misses, never answers");
  assert.strictEqual(fs.readFileSync(entry).equals(guarded.buffer), true, "the miss repairs the entry");
});
