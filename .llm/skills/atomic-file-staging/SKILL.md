---
name: atomic-file-staging
description: Place atomic-write staging files outside every tree another process enumerates by allowlist - a write-then-rename temp that is a sibling of its target inside an npm `files` walk, a fixture tree, or a Unity Assets folder gets packed or copied as a shipped file without a meta (live, mid-write, or as a crash leftover) and breaks the export or the build. Covers why the exporter stages under .artifacts/export-cache/, why fixture materialization stages under .artifacts/fixture-staging/, when leftovers need a prune and when they are inert, and the tree-equality test that pins the invariant. Use when adding or reviewing write-then-rename code, staging or temp files near a packaged or copied tree, fixing an npm-pack "shipped file without meta" violation, or writing a no-tmp-leak test.
metadata:
  category: Feature
---

# Atomic File Staging

## The one rule

A write-then-rename temp is a real file for the whole time it exists, so it must live where no walker can see it: outside every tree that anything enumerates by allowlist.
Placement is the contract; the rename only makes the final bytes atomic.

## Why sibling staging fails

`fs.writeFileSync(target + ".tmp", ...)` puts a walkable path inside the target's tree. Three ways it bites, all observed in #312:

- A crash or kill between write and rename leaves the temp behind. A later `npm pack --dry-run` walk ships it, and `collectAssets` throws `shipped file without meta` - the failure
  names the temp, not the cause.
- Even a live temp (milliseconds old) is visible to a concurrent walk. A second process exporting, packing, or stat-walking the same tree during materialization reads the temp as a
  packaged input.
- The input-signature keys (size, mtime, inode over the walked paths) move when the temp appears, so caches re-key on garbage.

Reproduce the failure mode by writing any file into the fixture's `Runtime/` and exporting: the export throws naming that file.

## The placement discipline

Stage inside an ignored, never-walked state directory of the same filesystem:

- The exporter's artifact cache stages under `.artifacts/export-cache/` (the `files` allowlist never names it) and prunes orphan `*.tmp` older than an hour, because every packaged
  change writes a ~7 MB pair.
- Fixture materialization stages under `.artifacts/fixture-staging/` with the target-relative name hashed in, and unlinks the staged file if the rename throws. No prune: leftovers
  only occur on a kill mid-write, are a few hundred bytes, and sit in an unwalked OS-temp directory - a prune would guard nothing the placement does not already guard.
- The preflight cache writer stages beside its destination inside the gitignored `.preflight-cache/` directory; the MCP config writer stages as a repo-root sibling, which is safe
  because no gate walks the root directory-wise, and its test pins the no-leak invariant explicitly.

The test for the class is tree equality: dirty one packaged file, materialize, then walk the tree minus the state directory and require the result to equal the canonical file set
exactly. An allowlist-aware comparison that only checks "no *.tmp" misses renamed or extra debris; equality catches everything.

## Edge cases

- Rename requires the same filesystem: stage under the same root, never the OS temp root.
- Failure cleanup: wrap write and rename, unlink the staged file in the failure path; a crash-leftover is handled by placement, not by finally.
- `renameSync` over an existing target is atomic on POSIX and Windows; two processes renaming identical bytes over each other is fine.
- A lone-surrogate body would round-trip as U+FFFD through a utf8 compare and rewrite every run - mtime churn, never corruption; keep fixture bodies valid UTF-8.
