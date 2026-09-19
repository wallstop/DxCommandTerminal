---
name: hot-path-allocations
description: Keep DxCommandTerminal hot paths (per-keystroke sweeps, per-frame updates, per-log writes) allocation-free on Unity's Mono - measured allocation facts, version-gated collection snapshots, long version counters, and warmed AllocatingGCMemory probe methodology. Use when optimizing a hot path, adding a sweep over a live collection, adding a version/generation counter, or writing or debugging allocation tests.
metadata:
  category: Performance
---

# Hot-Path Allocations

Measured on Unity 6000.4.6f1 (Unity's Mono) with the repo's control-validated instrument
(`Tests/Runtime/Allocation/`). Facts below are runtime-specific; re-probe before relying on
them on a new runtime.

## What allocates on Unity's Mono

- Every pass over a `SortedDictionary` or `SortedSet` allocates: `Keys`/`Values` hand out a
  fresh collection, and the enumerator is a class. A per-keystroke or per-frame sweep of a
  live sorted collection allocates every pass, even when the collection is empty.
- `char.ToLowerInvariant`, `string` comparisons (`StartsWith`, `Equals`, ordinal and
  OrdinalIgnoreCase), and `HashSet`/`Dictionary` adds (default or `OrdinalIgnoreCase`)
  are allocation-free once warmed. Do not "optimize" them preemptively.
- `Terminal.Log` pays stack-trace extraction per call (~0.4 ms median on the pinned
  editor); that is deliberate caller attribution, not a defect.

## Version-gated snapshots

When a hot path must sweep a collection owned by another type:

1. Cache a caller-owned `List<T>` snapshot next to the sweeper (the auto-complete's
   `_commandNames`).
2. Key the cache on a mutation version exposed by the owner (`CommandShell.CommandsVersion`,
   a plain `long`). Rebuild only when the version changed; keep a `bool` dirty flag for the
   first build instead of a sentinel value.
3. The invariant lives at the owner: EVERY mutation of the collection bumps the version
   (`Add`, `TryAdd` success, `Remove` success, `Clear`, batch removals). Over-bumping is
   harmless (spurious rebuild); under-bumping serves stale data. Greppable contract comment
   sits on the version field; the accessor comment restates it.
4. Versions are `long`, matching `CommandLog.Version`. Do not introduce `uint`/`int`
   counters or sentinel-initialized stamps.

Constructor-time work is not a hot path: bulk add then sort once (`List.Sort`) instead of
insertion-time binary searches (quadratic). Keep a seen set only to preserve
first-inserted-wins for case-variant duplicates.

## Probe methodology (allocation tests)

- `Is.AllocatingGCMemory` probes MUST warm their subject and the probe path before the
  measured window (8+ iterations). Unwarmed probes false-positive on first-call JIT for
  string comparisons, dictionary adds, and other BCL paths - whole bisect rounds have been
  burned by this; only warmed verdicts count.
- Millisecond-scale timing windows (readiness sweeps and similar) collect before measuring:
  `GC.Collect(2, ...)` + `WaitForPendingFinalizers` + a second collect keeps cross-fixture
  heap pressure out of the p95 tail. In full-suite runs the 1,000-command readiness p95
  rode gen0 pauses past its 15 ms tripwire (isolated runs sat at ~9-11 ms) until the
  windows started collecting first (CommandDiscoveryScalingTests.MeasureReadiness).
- Zero-allocation claims go through `AllocationAssertions.AssertZeroAllocations` (it fails
  closed when the instrument cannot see its positive control). Never assert on raw
  `AllocatingGCMemory` yourself.
- Documented, deliberate allocations (stack-trace extraction, tokenize substrings) use
  `AssertDetectsAllocation` so a future optimization must consciously update them.

## Sweep checklist for a new hot path

- Enumerating a sorted collection per call? Snapshot + version (above).
- Building strings per call? `CachedStringBuilder.Rent` (context.md rule 23).
- New mutation site on a snapshotted collection? Bump the version.
- New allocation test? Warm first; pin through `AllocationAssertions`.
