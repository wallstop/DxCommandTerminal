---
name: webgl-command-registration
description: Handle WebGL and IL2CPP managed stripping constraints for RegisterCommandAttribute reflection-based command discovery in DxCommandTerminal (Managed Stripping Level settings, trims, link.xml). Use when commands vanish in WebGL/player builds, when configuring build settings, or when touching the reflection path in CommandShell/attribute scanning.
metadata:
  category: Performance
---

# WebGL Command Registration

## The constraint

Attribute-based command discovery is catalog-first: the bundled source generator emits an
internal `WallstopStudios.DxCommandTerminal.Generated.CommandCatalog` into every assembly that
declares `[RegisterCommand]` methods, and the shell binds those catalogs directly. Catalogs
still address handler methods by name (direct delegate creation for accessible methods, exact
reflection identity for private ones), so IL2CPP managed stripping can still remove attributed
methods the generated code cannot prove referenced - the failure mode is the same, just
narrower. Assemblies without a catalog (precompiled DLLs) fall back to the reflection scan,
which has the original full exposure.

**Required setting** (Player > WebGL > Other Settings > Optimizations > Managed Stripping Level):
`Low`, `Minimal`, or `None`. `Medium` or `High` breaks command registration.

The package ships `Runtime/link.xml` preserving its own runtime assembly, so built-in
commands and generated catalogs survive stripping at every level. That link.xml covers
only `WallstopStudios.DxCommandTerminal` - consumer assemblies declaring their own
`[RegisterCommand]` methods still need the settings above (or the mitigations below) at
`Medium`/`High`. Keep the link.xml entry in sync if the runtime assembly is ever renamed;
`tooling~/scripts/release/validate-package-contents.mjs` fails the package build if it is
missing or no longer preserves that assembly name.

## When the stripping level cannot be lowered

1. **Manual registration** avoids reflection entirely:
   `Terminal.Shell.AddCommand("name", handler, min, max, help)` - stripping-safe because the
   delegate is referenced directly.
2. **`[Preserve]`** (`UnityEngine.Scripting.Preserve`) on command methods/holders keeps them
   through stripping, but is per-site and easy to forget - prefer manual registration for
   stripping-restricted pipelines.
3. **link.xml** can preserve whole assemblies (`<assembly fullname="YourGame" preserve="all"/>`);
   use surgically, it defeats stripping benefits at that scope. The package's own
   `Runtime/link.xml` follows this pattern for its runtime assembly only.

## Rules for Runtime changes

- Keep reflection confined to the command-registration scan path; do not add new
  reflection-dependent public APIs to `Runtime/` (see `context.md` Unity Package Rules).
- `EditorOnly`/`DevelopmentOnly` commands are filtered by build target, not by stripping; they
  still require the stripping fix above on WebGL.
- After touching the registration path, validate on a WebGL build with an attribute-registered
  command before merging.

## Diagnosing "commands missing in build"

1. Check Managed Stripping Level first (screenshot in README).
2. Confirm the command is static and attributed - attribute discovery never sees instance
   methods (see [register-terminal-command](../register-terminal-command/SKILL.md)).
3. If the method survives but args fail, suspect stripping of parsers - see
   [custom-argument-parsing](../custom-argument-parsing/SKILL.md) (registered parsers are
   user-referenced, built-ins are intrinsic).
