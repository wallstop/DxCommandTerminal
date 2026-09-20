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
`[UnityEngine.Scripting.Preserve]` - on the catalog class and on its `Collect` entry method -
whenever the compilation resolves `UnityEngine.Scripting.PreserveAttribute` (every Unity
compilation does; a noEngineReferences compilation gets no attribute and keeps the historical
behavior). Unity linker semantics: a type-level preserve keeps only the type and its default
constructor, while a method-level preserve roots the method, its declaring type, and the
method's reachable dependency graph - so the attribute on `Collect` is what keeps `Build`,
the binder factories, and every directly created handler delegate alive at any Managed
Stripping Level, on IL2CPP and WebGL alike.

Two paths stay reflection-bound and are strippable at `Medium` or `High`:

1. Handlers in private, non-partial types: the catalog binds them by exact reflection
   identity (string-addressed, invisible to the linker).
2. Assemblies without a catalog (precompiled DLLs): discovered by the reflection scan.

**Player builds cover both automatically**: the player compatibility bake
(`Runtime/CommandTerminal/Backend/CommandCompatibilityBake.cs`, editor-only) implements
`IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile`, feeding the stripping stage an
additional link.xml (written under `Temp`) that preserves exactly those handlers. Assets
files are not used - Unity only auto-loads Assets files named exactly `link.xml`. Its
rooting rule mirrors the emitter per method: in a generated assembly, a handler is rooted
only when its shape is directly bindable (void return, one by-value `CommandArg[]`
parameter, no generic method, no open-generic declaring chain) AND it is accessible
(public / internal / protected internal) AND - when inaccessible - its declaring type
carries the partial companion; everything else static and attributed (minus `EditorOnly`)
is preserved, and whole catalog-less assemblies' handlers are preserved. Assembly entries
carry `ignoreIfMissing="1"`, so entries for assemblies a given build does not contain are
inert. Manual mitigation below remains for workflows that never run the editor build hook.
**Required setting**: any Managed Stripping Level works for generated catalogs of
accessible handlers; with the bake, the reflection-bound paths also survive `Medium`/`High`
in player builds.

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
- The bake's rooting rules (`IsDirectlyBindable`, the partial-companion probe,
  `CommandShell.CatalogTypeName`) mirror the emitter's binding forms. Change them together:
  a new binder form in `CatalogEmitter` must update the bake in the same change, and the
  bake tests pin the public / companion / inaccessible cases against the real generator.
- `EditorOnly`/`DevelopmentOnly` commands are filtered by build target, not by stripping;
  generated catalogs carry the same `[Preserve]` regardless (the bake preserves
  `DevelopmentOnly` handlers because dev builds register them, and skips `EditorOnly`).
- After touching the registration path, the generator, or the bake, validate on an IL2CPP
  player build with a private attributed command before merging (the IL2CPP/WebGL strip
  matrix on issue #38 tracks the drill evidence).

## Diagnosing "commands missing in build"

1. Check Managed Stripping Level first (screenshot in README).
2. Confirm the command is static and attributed - attribute discovery never sees instance
   methods (see [register-terminal-command](../register-terminal-command/SKILL.md)).
3. If the method survives but args fail, suspect stripping of parsers - see
   [custom-argument-parsing](../custom-argument-parsing/SKILL.md) (registered parsers are
   user-referenced, built-ins are intrinsic).
