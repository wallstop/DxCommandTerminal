---
name: webgl-command-registration
description: Handle WebGL and IL2CPP managed stripping constraints for RegisterCommandAttribute reflection-based command discovery in DxCommandTerminal (Managed Stripping Level settings, trims, link.xml). Use when commands vanish in WebGL/player builds, when configuring build settings, or when touching the reflection path in CommandShell/attribute scanning.
metadata:
  category: Performance
---

# WebGL Command Registration

## The constraint

Attribute-based command discovery uses reflection over loaded assemblies. IL2CPP managed
stripping can remove the attributes/methods it cannot see referenced, so the terminal "forgets"
its commands in WebGL builds.

**Required setting** (Player > WebGL > Other Settings > Optimizations > Managed Stripping Level):
`Low`, `Minimal`, or `None`. `Medium` or `High` breaks command registration.

## When the stripping level cannot be lowered

1. **Manual registration** avoids reflection entirely:
   `Terminal.Shell.AddCommand("name", handler, min, max, help)` - stripping-safe because the
   delegate is referenced directly.
2. **`[Preserve]`** (`UnityEngine.Scripting.Preserve`) on command methods/holders keeps them
   through stripping, but is per-site and easy to forget - prefer manual registration for
   stripping-restricted pipelines.
3. **link.xml** can preserve whole assemblies (`<assembly fullname="YourGame" preserve="all"/>`);
   use surgically, it defeats stripping benefits at that scope.

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
