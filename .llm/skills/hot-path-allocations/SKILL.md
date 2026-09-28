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

- **`string` is not a value-typed enumerable, so `foreach` over one always allocates.**
  `string.GetEnumerator()` returns `System.CharEnumerator`, a class - verified by reflection
  (`typeof(string).GetMethod("GetEnumerator").ReturnType.IsValueType == false`, alongside
  `List<char>`, which is `true`). Rule 11's "value-based enumerables" therefore does not
  cover strings, and there is no "off a hot path is fine" exemption: the enumerator is
  allocated per walk whether or not the path is measured, and a `foreach` over a string is
  the only way to add an allocation to a loop that had none. Walk a string with a counting
  loop in production code; `foreach` is fine in tests, where allocation does not matter and
  readability wins. The two standing cases, both fixed in PR #180 with the reason in a
  comment beside each loop: `CommandPaletteSearch.IsSubsequence` (per command name per rank
  tier per keystroke) and `StringExtensions.NeedsLowerInvariantConversion` (per command name
  when the completion list rebuilds, and a 1,000-command rebuild is a measured gate). Sweep
  for `for (int i = 0; i < <string>.Length; ++i)` where `i` is used for nothing but
  `text[i]` - those are the loops that should be `foreach` - and the reverse,
  `foreach` over a string, which should be a counting loop.
- Every pass over a `SortedDictionary` or `SortedSet` allocates: `Keys`/`Values` hand out a
  fresh collection, and the enumerator is a class. A per-keystroke or per-frame sweep of a
  live sorted collection allocates every pass, even when the collection is empty.
- `char.ToLowerInvariant`, `string` comparisons (`StartsWith`, `Equals`, ordinal and
  OrdinalIgnoreCase), and `HashSet`/`Dictionary` adds (default or `OrdinalIgnoreCase`)
  are allocation-free once warmed. Do not "optimize" them preemptively.
- Splitting + joining strings allocates an array plus one substring per line; when the
  output is derivable in one index walk, build it into a rented `CachedStringBuilder`
  instead (see `CommandLog.ReduceStackTrace` - cut log-write median 0.40 -> 0.25 ms, and
  pin the rewrite against the old algorithm kept as a test-side reference).
- Prefer a **member collection** over any shared pool: a buffer owned by
  the instance that does the work needs no lease, no copy-safety proof,
  no re-entrancy analysis, and is reclaimed with its owner. Examples:
  `CommandAutoComplete._knownWords` (construction dedupe = bulk add, one
  sort, collapse adjacent case-insensitive duplicates in place - keep the
  ordinal-smallest casing of each run so the result stays deterministic
  despite unstable sorts) and `CommandLog._traceBuilder` (one builder per
  log instance, reused per write). Reach for a shared pool only when no
  single instance owns the work (heterogeneous static/instance call
  sites), as with `CachedStringBuilder`.
- Shared pools MUST reclaim - growth-only retention is a leak
  (issue #108 review). Evict on return: `CachedStringBuilder` drops a
  builder whose capacity exceeds `MaxRetainedBuilderCapacity` (8192), so
  one-off spikes reclaim instead of pinning; retained memory is bounded
  by slots x bound. Recheck eviction in tests by renting oversized,
  returning, and re-renting (the replacement must be small).
- For shared rented buffers, the scope must be copy-safe and the pool
  re-entrant (review round 4): a per-copy `_returned` flag is not
  copy-safe (two copies each run Dispose), and a single
  `[ThreadStatic]` slot is not re-entrant. Use the lease-guarded slot
  pool: `CachedLease`/`CachedLeases` (adapted from unity-helpers'
  `DisposalLease`, MIT) - the generation lives outside the struct, so
  exactly one copy of a scope wins the claim even if a stale copy is
  disposed after the slot was re-rented - plus `CachedSlotStorage<T>`
  (slot-indexed buffer storage) and a per-thread free list. Every rent
  leases a distinct slot, so nested rents never share a buffer.
- On Unity's Mono, `ConcurrentStack.Push` allocates a node on every call,
  so `ConcurrentStack`-backed pools allocate on every buffer RETURN. The
  lease free list is plain int fields - the whole rent-use-return cycle
  allocates nothing. Verify with a pin over the whole cycle, not just the
  rent.
- Scope structs are values: use one only as the direct subject of a
  `using` - a copy shares the same lease slot (that is safe), but a copy
  disposed after the buffer was re-rented must lose its claim, which the
  generation check guarantees. Sweep with
  `rg "= new (HashSet|List|Dictionary)" Runtime/` and classify each hit
  cold vs per-call.
- `Terminal.Log` pays stack-trace extraction per call (~0.25 ms median on the pinned
  editor after the reduction pass); that is deliberate caller attribution, not a defect.
- A captureless lambda is cached by the compiler as a singleton delegate (zero per-call
  allocation), but adding ONE capture silently converts it to a per-call closure plus
  a retained object. Declare captureless lambda arguments `static` (C# 9; PR #112
  review): `ConditionalWeakTable.GetValue` factories, `Lazy<T>` factories, and
  long-lived `RegisterCallback` registrations - the compiler then turns an accidental
  capture into a compile error. Long-lived UI callbacks also root whatever they capture
  through the element's callback registry, so `static` + explicit user-args state (the
  `TerminalUI` input callback pattern) is the default there. Verify with a warmed
  `AssertZeroAllocations` pin over the memo/registration read path.
- `ConditionalWeakTable<TKey, TValue>` only accepts REFERENCE-TYPE values
  (`TValue : class`; it cannot hold a struct). The correct memo payload is one small
  immutable class instance per weak key (see `AssemblyClassification`); do not replace
  the table with a `Dictionary<Assembly, T>` to get struct values - a strong-keyed
  dictionary roots every key (assembly) for the domain's lifetime, a growth-only leak.

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

## Loop bounds: hoist a `Count`, never a mutated collection's length

- Hoist `List<T>.Count`, interface `Count`, and UIToolkit `childCount` out of a counting
  loop's condition: those are property or interface reads, re-dispatched per iteration.
- **Never hoist a `Length`/`Count` read when the loop body mutates that collection.** The
  inline re-read is what makes the loop terminate; a hoisted value pins a stale count. Three
  in-repo loops are traps that read like hoistable candidates, and all three are commented
  as such: `CommandShell`'s provider and scan-assembly removals (`RemoveAt` in the body),
  `CommandAutoComplete.CollapseCaseInsensitiveDuplicates`' outer rewrite loop
  (`words[writeIndex] = ...` in the body - its *inner* read loop is safe and is hoisted), and
  `TerminalUI`'s `while (content.childCount < logs.Count)`, which appends the labels it is
  counting. Hoisting any of them is a bug, not an optimization.
- Hoisting `string.Length`/`array.Length` is a **measured wash, not a win**: with tiered JIT
  disabled the inline and hoisted forms were identical at the median (0.00%, n=2000 x 7
  interleaved reps, 13- and 200-char workloads). The JIT already hoists the load; the
  "bounds-check elision" rationale that used to sit in context.md rule 18 was never real.
  Write whichever form reads clearly and keep one form per type.

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
- When a tripwire asserts p95 over a window where p95 is effectively a maximum
  (roughly n < 100), it must assert a stable statistic instead. At n samples, p95
  is the `ceil(0.95 * n) - 1` index: at n=30 that is the second-worst sample, so
  two stalled samples cross it, and the 30th sample is never read at all; at n=300
  it is the 285th, which one stall cannot move, so a p95 bound there is already
  sound. Under that threshold, assert the median at the budget and p95 at a loose
  multiple of it, and name the statistic, sample count, margin, and the
  median/p95/max series in the failure. State the cost where the budget is set: at
  n=30 a split tolerates 15 of 30 samples over budget where a p95 bound tolerates
  1, and a fixed multiplier leaves the least room over what a tier actually
  measures where that tier's headroom was tightest. Never relax a budget to make
  a run pass.
- Zero-allocation claims go through `AllocationAssertions.AssertZeroAllocations` (it fails
  closed when the instrument cannot see its positive control). Never assert on raw
  `AllocatingGCMemory` yourself.
- Micro-benchmarks must run with tiered compilation off (`DOTNET_TieredCompilation=0`) before
  a difference is real. Left on, the same comparison reported a 52% median win for hoisting a
  `string.Length` read on one run, 7% on the next, and 0% with tiering off - the 52% was a
  variant reaching full optimization earlier, and p95 showed no effect in any run. Interleave
  the variants and alternate which runs first, or the first variant absorbs warmup. A
  dramatic single-run delta with a stable p95 is a tiering artifact, not a finding.
- Documented, deliberate allocations (stack-trace extraction, tokenize substrings) use
  `AssertDetectsAllocation` so a future optimization must consciously update them.

## Sweep checklist for a new hot path

- A member collection can own it? Prefer that over any shared pool.
- Enumerating a sorted collection per call? Snapshot + version (above).
- Building strings per call? `CachedStringBuilder.Rent` (context.md rule 23).
- Shared rented buffer? Lease-guarded slots + evict oversized on return.
- New mutation site on a snapshotted collection? Bump the version.
- New allocation test? Warm first; pin through `AllocationAssertions`.
- New lambda argument on a memoization/registration path? Captureless means `static`.
