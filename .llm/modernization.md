# Modernization contracts

Read this for modernization, compatibility, release acceptance, or performance work. Open tasks live in `PLAN.md`. Results and completed gates live in `progress/`. These contracts
remain binding after a task leaves the plan.

## Compatibility and scope

- Preserve public APIs, serialized assets, production assembly identities, and Unity 2021.3 minimum compatibility. New capabilities stay additive.
- Source-generated registration remains the primary path. Measure its overhead; do not replace it with broad reflection.
- CI never provisions Unity or consumes a license. Unity-dependent validation runs locally on pinned environments with retained evidence.
- Contextual completion stays synchronous and local. Network/async providers are out of scope.
- The palette remains an in-game runtime quick-launch surface. Edit Mode execution requires explicit opt-in and gameplay commands never become Edit Mode commands by default.
- Do not promise universal zero allocation. Claims cover only designated, measured operations with valid instruments.
- DxMessaging and unity-helpers are design references only: copy-adapt with attribution; no `uses:`, submodules, cross-repo actions, runtime references, or third-party Unity
  packaging actions. GitHub-owned actions may be pinned. Workflows remain Unity-free.
- MIT licensing, upstream attribution, and the no-paid-features policy remain binding.
- Keep tasks staged and reviewable. Partial evidence is not milestone completion.
- Do not silently weaken targets, compatibility promises, or visual checks.
- Preparing release artifacts is distinct from publishing. T13 and maintainer review must pass before modernization is complete; every external publish remains separately
  authorized.

## Measurement contract

Use at least 30 samples for startup/compile and 1,000 warmed samples for steady state; report n, median, p95, max, and raw timings; keep cold and warm workloads separate; pair
matched workloads; run allocation and wall-clock measurements separately; never substitute Editor numbers for player numbers. Keep artifacts in `.artifacts/` and interpretation in
`progress/`.

## Performance targets

These are acceptance targets, not current pass claims. See session evidence for results.

| Gate                                    | Target                                                  |
| --------------------------------------- | ------------------------------------------------------- |
| First palette interaction               | Visible and focused on the next rendered frame          |
| Capture suite                           | Every scenario green on a rendered host                 |
| Numeric allocation evidence             | Valid event/byte measurements for designated operations |
| First command readiness, 1,000 commands | p95 < 5 ms                                              |
| Completion provider, 1,000 candidates   | p95 < 1 ms                                              |
| Package compile overhead                | p95 <= one-file floor p95 + 100 ms (#124)               |
| Generator execution                     | CI median < 25 ms and p95 <= 50 ms; local p95 < 25 ms   |
| T13 release acceptance                  | Every matrix leg and compatibility fixture passes       |

## Execution references

- Release operations and current rehearsal behavior: [release runbook](../tooling~/docs/release-runbook.md).
- Consumer drill: `tooling~/scripts/t7/consumer/runner.mjs`.
- Import drill: `tooling~/scripts/t7/import-drill.mjs`.
- Host matrix: `npm --prefix tooling~ run t13:compat`.
- Windows player drill: `tooling~/scripts/t13/player_smoke.py`.
- Use short Windows editor/output roots, complete native support payloads, and a licensed regular editor build.
- Capture and baseline tooling: `tooling~/scripts/t11/` and `npm run t4:capture`.
- Measurement procedure: [hot-path-allocations](./skills/hot-path-allocations/SKILL.md).
- Unity measurement API: <https://docs.unity3d.com/Packages/com.unity.test-framework.performance@3.0/manual/measure-method.html>.
