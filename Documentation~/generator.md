# Source-generated registration

`[RegisterCommand]` methods bind without a runtime reflection sweep. A
source generator runs at compile time, finds the attributed methods in
the same assembly, and emits a static catalog. At startup the shell
loads that catalog: direct delegates for bindable shapes, cached
by-name binders for the rest.

## What the generator needs

- The handler is `static` and the attribute sits on the method.
- The command name comes from the method name (`COMMAND` infix, suffix,
  or prefix is stripped); `Name = "..."` overrides it.
- Declare the containing class `partial`: the generator then injects a
  binder companion into it, so private handlers bind through generated
  code instead of reflection. A non-`partial` class still works - the
  catalog falls back to a cached by-name binder - but generated code
  roots the handler only in the `partial` case.

Every assembly that compiles with the package gets its own catalog. A
precompiled DLL without one still works - the shell falls back to a
reflection walk for that assembly, and player builds preserve those
handlers (see below).

## Deferral and timing

Catalogs load when the shell applies registration: at terminal startup
by default, or on first command request under deferred registration.
`CommandShell.AutoCommandsRegistered` reports the state;
`EnsureAutoCommandsRegistered` forces it early. See
[Lifetime](lifecycle.md).

## IL2CPP and WebGL stripping

Players strip managed code. The generated catalogs root attributed
commands with generated code, so they survive any stripping level.
Handlers that can only bind by name - private handlers in
non-`partial` classes, shapes the catalog cannot call directly, and
attributed methods in catalog-less DLLs - are preserved by a
player-build step (`CommandCompatibilityBake`) that feeds the Unity
linker a surgical `link.xml` manifest: it preserves exactly those
handlers, nothing else. The manifest lives under `Temp` for the linker
run only; your `Assets` folder is never touched.

Standalone IL2CPP Medium and WebGL Medium/High drills validate this on
real builds; evidence is summarized on
[issue #38](https://github.com/wallstop/DxCommandTerminal/issues/38).

## Rules of thumb

- Keep handlers in normal runtime assemblies; do not move commands into
  editor-only code you need at runtime (`EditorOnly = true` handles
  editor-only commands).
- New `CommandDefinition` metadata defaults to gameplay contexts; Edit
  Mode execution stays opt-in.
- No runtime code may depend on `UnityEditor`.

## Where next

- [Registering commands](commands.md) - attribute, manual, and builder forms.
- [API Reference](xref:WallstopStudios.DxCommandTerminal.Attributes.RegisterCommandAttribute) -
  the attribute and its options.
