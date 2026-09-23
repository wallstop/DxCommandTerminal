# Your first command

Commands are plain C#. The typed builder below registers a `heal` command
with a validated argument, a default value, and Tab-completable choices.

## The sample

The typed-builder sample ships with the package (`Package Manager >
DxCommandTerminal > Samples > Import`). Its simplest component (staged at
build time from `Samples~/TerminalCommands/SimpleCommands.cs`, so this
excerpt is always the shipped code):

[!code-csharp[SimpleCommands](samples/SimpleCommands.cs)]

What this registers:

- `heal <amount> [target]` - `amount` is a required `int` validated to 1-100
  (out-of-range input is rejected, not clamped), `target` defaults to `self`
  and completes to `self`, `ally`, `enemy`.
- Successful registration produces a `CommandRegistrationHandle` via
  `Terminal.Shell.AddCommand`'s `out` parameter. Disposing it removes exactly
  that command, which is why the sample base class (`TerminalCommandSample`)
  disposes every handle on disable.
- Registration needs a live shell. If the component enables before any
  `TerminalUI` built its shell, it logs an error instead of throwing - keep
  sample components in a scene with a terminal, or register from `Start`.

## Run it

1. Enter Play Mode and open the terminal (`` ` ``).
2. Type `heal 50 ally` and press Enter:

    ```text
    Healed ally for 50 HP.
    ```

3. Type `heal ` and press Tab - the target choices complete. Type `heal x`
   and the parser rejects the input instead of defaulting silently.

## Other registration forms

- `[RegisterCommand]` on a static method - discovered by the source
  generator, no runtime code needed.
- `Terminal.Shell.AddCommand(name, handler, min, max, help)` - manual,
  imperative registration.

Both are documented in [Registering commands](commands.md).

## Where next

- [Registering commands](commands.md) - every registration form, side by side.
- [Typed arguments](arguments.md) - parsing, validation, custom parsers.
- [Quick-launch bar](palette.md) - run commands without the terminal.
- [API Reference](xref:WallstopStudios.DxCommandTerminal.Backend.CommandBuilder) -
  `CommandBuilder`, `CommandDefinition`, `CommandShell`, and the rest of the
  Runtime API.
