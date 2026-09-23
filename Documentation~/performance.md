# Performance

Performance claims are measured, not asserted. Every claim is pinned by
a test in the package's automated suites, so a regression fails CI
instead of shipping.

## Measured claims

The repo README's "Measured Performance" section maps each claim to its
pinning test. Highlights:

- Designated hot paths allocate nothing when warmed: typing completion,
  history traversal + copy + wrap push, borrowed-view dispatch, log
  writes without stack traces, steady refresh passes, per-keystroke
  hint sweeps.
- Text command execution allocates by design (tokenizing). It is
  asserted as such, never claimed zero.
- First command readiness in a 1,000-command domain: p95 under 5 ms.
- Provider completion with 1,000 candidates: p95 around 0.1 ms.

## How measurement works

- Zero-allocation claims prove the instrument first: a positive control
  must detect a forced allocation before a zero claim can pass, and a
  window on an unvalidated instrument can never report zero.
- Byte-level allocation counters stay inert on Unity's Mono; they
  report `Unavailable` rather than a fake zero.
- Timing tripwires in the benchmark suites catch order-of-magnitude
  regressions. They are guardrails, not benchmarks: do not quote their
  numbers as performance results.
- Editor numbers never stand in for player numbers. IL2CPP and WebGL
  behavior is validated with local build drills.

## Writing allocation-free commands

Reuse buffers the shell hands you. `PrecedingArguments` on a completion
context and the borrowed argument views are views, not copies - do not
call `ToArray()` per keystroke. Build strings with interpolation, not
long `+` chains. Register once, not per frame.

## Where next

- [Source-generated registration](generator.md) - no reflection sweeps at startup.
- [API Reference](xref:WallstopStudios.DxCommandTerminal.Backend.CommandShell) -
  `TryComplete`, borrowed views, and history buffers.
