---
name: internal-only-analyzers
description: Author and maintain repo-internal Roslyn analyzers shipped under Runtime/Analyzers (DxCmd Unity fake-null rules) - internal-only distribution, Roslyn 3.8/4.x portability, payload rebuild, and positive-control drills. Use when changing analyzers, payloads, or Unity-null enforcement.
metadata:
  category: Core
---

# Internal-Only Analyzers

## Internal-only contract (owner decision, PR #103)

Repo analyzers must never reach client packages. Two layers; keep both:

1. Distribution: the analyzer builds as its own assembly
   (`Generator~/WallstopStudios.DxCommandTerminal.Analyzers/`) copied to
   `Runtime/Analyzers/`, but excluded from the npm `files` allowlist in
   `package.json`, so the UPM tarball and the release `.unitypackage` never
   carry it. Unity still loads it inside this repository (git checkout).
2. Code: `UnityObjectNullPatternAnalyzer.IsRepoCompilation` no-ops every
   diagnostic unless the compiled assembly's name starts with
   `WallstopStudios.DxCommandTerminal`, so a leaked binary stays inert in
   client code. Fixture compilations in tests must therefore use a repo
   assembly name (see `AnalyzeAs` in the analyzer tests).

Audit rule: any new analyzer, csc.rsp, or payload under `Runtime/` is a
potential enforcement leak; if it targets repo code only, it needs both
layers. Shipped tools (the source generator) are intentional and stay.

## Roslyn portability (build on 3.8 for Unity 2021.3, run on 4.x hosts)

- Compile against Microsoft.CodeAnalysis.CSharp 3.8.0, netstandard2.0,
  LangVersion 9. Prefer syntax + semantic model; avoid operation interfaces
  whose members were renamed across versions (3.8 lacks 4.x members like
  `IConditionalAccessOperation.Value`).
- Roslyn splits case labels: `CaseSwitchLabelSyntax` (constant, payload is
  the constant expression itself) vs `CasePatternSwitchLabelSyntax`
  (`case not null`, `case { }`). Register BOTH syntax kinds.
- Pattern payload shapes differ per host (bare null literal vs
  `ConstantPatternSyntax` wrapper): match shapes, not named members.
- Bypass idioms to pin with fixtures: `case not null`, `case { }`,
  `case not { }`, switch-expression null arms, `is { }` / `is not { }`,
  type parameters constrained to UnityEngine.Object, `??` inside lambdas.
  Type/property patterns are allowed (type/member tests, not null checks).

## Workflow

- Red first: add fixtures, run against a no-op analyzer, then implement.
- Rebuild payloads: `dotnet build` both csprojs under Generator~ (-c Release);
  verify with `verify-analyzer-payload.ps1` and `-TwoCheckout` (commit first;
  it compares against the committed tree).
- Positive-control drill on the live editor: add a temp file with a banned
  pattern, recompile via MCP, expect the DxCmd error, remove, recompile clean,
  then run the full PlayMode suite.
