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
declares `[RegisterCommand]` methods, and the shell binds those catalogs directly. The catalog
type is reached only through reflection (`assembly.GetType`/`GetMethod`), so managed stripping
could otherwise remove it and every command it carries. The generator therefore emits
`[UnityEngine.Scripting.Preserve]` onto the catalog class whenever the compilation resolves
`UnityEngine.Scripting.PreserveAttribute` (every Unity compilation does; a
noEngineReferences compilation gets no attribute and keeps the historical behavior). The
class-level preserve roots the catalog and, through the catalog members' direct delegate
creations, every accessible handler - including all built-in commands - at any Managed
Stripping Level, on IL2CPP and WebGL alike.

Two paths stay reflection-bound and can still be stripped at `Medium` or `High`:

1. Handlers in private, non-partial types: the catalog binds them by exact reflection
   identity (string-addressed, invisible to the linker).
2. Assemblies without a catalog (precompiled DLLs): discovered by the reflection scan.

**Required setting**: any Managed Stripping Level works for generated catalogs of accessible
handlers. For the two paths above, `Low`, `Minimal`, or `None` is still required unless the
mitigations below apply.

## When the stripping level cannot be lowered

1. **Manual registration** avoids reflection entirely:
   `Terminal.Shell.AddCommand("name", handler, min, max, help)` - stripping-safe because the
   delegate is referenced directly.
2. **`[Preserve]`** (`UnityEngine.Scripting.Preserve`) on command methods/holders keeps them
   through stripping, but is per-site and easy to forget - prefer manual registration for
   stripping-restricted pipelines.
3. **link.xml** can preserve whole assemblies (`<assembly fullname="YourGame" preserve="all"/>`);
   use surgically, it defeats stripping benefits at that scope. The package ships none: the
   generator-emitted `[Preserve]` covers the generated surface without forcing whole-assembly
   preservation on consumers.

## Rules for Runtime changes

- Keep reflection confined to the command-registration scan path; do not add new
  reflection-dependent public APIs to `Runtime/` (see `context.md` Unity Package Rules).
- If the emitter gains a new reflection-by-name binding, extend the generated code's
  stripping protection in the same change and pin it in the generator driver tests (see
  `GeneratedCatalogCarriesStrippingPreservation`); the payload byte-compare lane fails on
  a stale shipped analyzer DLL.
- `EditorOnly`/`DevelopmentOnly` commands are filtered by build target, not by stripping;
  generated catalogs carry the same `[Preserve]` regardless.
- After touching the registration path or the generator, validate on a WebGL build with an
  attribute-registered command before merging.

## Diagnosing "commands missing in build"

1. Check Managed Stripping Level first (screenshot in README).
2. Confirm the command is static and attributed - attribute discovery never sees instance
   methods (see [register-terminal-command](../register-terminal-command/SKILL.md)).
3. If the method survives but args fail, suspect stripping of parsers - see
   [custom-argument-parsing](../custom-argument-parsing/SKILL.md) (registered parsers are
   user-referenced, built-ins are intrinsic).
