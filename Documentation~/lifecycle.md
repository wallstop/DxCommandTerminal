# Lifetime

Commands live in a shell. Who owns the shell, when to register, and how
to unregister.

## The shell exists once

The first `TerminalUI` (or `CommandPaletteUI`) in the scene creates the
shared session; `Terminal.Shell` exposes it. Component enable order is
undefined: if your component enables before any terminal built its
shell, `Terminal.Shell` is null - keep the component in a scene with a
terminal, or register from `Start`.

If the terminal uses **Reset State On Init**, it rebuilds the shell
during its own startup. Register from `Start` or later, or the
registration is discarded with the old shell.

## Register on enable, dispose on disable

The shared sample base defers to `TerminalCommandSample`:

[!code-csharp[TerminalCommandSample](samples/TerminalCommandSample.cs)]

A duplicate name fails the add (`AddCommand` returns `false`) and
leaves the handle null. Disposing removes exactly that registration -
never a later replacement with the same name - so enable/disable cycles
stay safe. The raw pattern without the base:

[!code-csharp[LifecycleCommands](samples/LifecycleCommands.cs)]

## Discovery timing

`[RegisterCommand]` methods bind when the shell applies registration:
at terminal startup by default, or on the first command request when the
shell is initialized with deferred registration
(`CommandShell.EnsureAutoCommandsRegistered` forces it early;
`AutoCommandsRegistered` reports the state). Clearing auto commands
cancels a pending scan.

## Where next

- [Registering commands](commands.md) - registration forms.
- [API Reference](xref:WallstopStudios.DxCommandTerminal.Backend.CommandShell) -
  `CommandShell`, `CommandRegistrationHandle`.
