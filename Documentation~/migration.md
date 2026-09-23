# Migrating from Command Terminal

DxCommandTerminal is a fork of
[Command Terminal](https://github.com/stillwwater/command_terminal).
The terminal workflow is the same: attach a UI component, toggle with a
hotkey, Tab completes, Up/Down walks history. Names and behaviors
changed where the fork fixed bugs or tightened guarantees.

## API mapping

| Command Terminal | DxCommandTerminal |
| --- | --- |
| `CommandArg.Int` / `.Float` / `.Bool` | `CommandArg.TryGet<T>(out T value)` - returns `false` on bad input instead of defaulting and logging |
| `Shell.RegisterCommands()` | Automatic: a source-generated catalog per assembly loads at shell startup (see [Source-generated registration](generator.md)) |
| `FRONTCOMMAND` methods | Removed. Use a builder command with subcommands, or one attributed command with typed arguments |
| `Shell.RunCommand(line)` (void) | `Terminal.Shell.RunCommand(line)` returns `bool`: `true` when a command ran |
| `Shell.IssuedErrorMessage` | `Shell.HasErrors` and `Shell.TryConsumeErrorMessage(out string)` |
| `Shell.Commands` (Dictionary) | `Terminal.Shell.Commands` (read-only view) |
| `Shell.AddCommand(...)` | Same name, validated inputs, returns `false` on a duplicate instead of corrupting the table |

Registration moves to the `Terminal` static facade
(`Terminal.Shell`, `Terminal.Log`, `Terminal.Buffer`); the shell itself
is `CommandShell`.

## Behavior changes to review

- Quoted input: `"give item 12"` reaches the command as one argument.
  Matched single or double quotes both work; a hanging quote takes the
  rest of the line.
- All name comparisons are `OrdinalIgnoreCase`; nothing depends on the
  player's culture.
- Command names, history filtering, error queueing, and buffer wrapping
  keep their names but validate inputs on every public method - calls
  that used to misbehave silently now reject with a log.
- Collections come back read-only. Code that cast them to mutable
  collections needs its own storage.

## Known limitations

- Completion is synchronous and local; async or network providers are
  out of scope by design.
- Variable watches, persistent command buttons, and a custom inspector
  redesign are not planned; measured performance defects in existing
  inspectors are in scope.
- Byte-level allocation counters report `Unavailable` on Unity's Mono -
  the allocation claims rest on a validated instrument, not raw bytes
  (see [Performance](performance.md)).

## Where next

- [Install](install.md) - package setup.
- [Registering commands](commands.md) - the three registration forms.
- [API Reference](xref:WallstopStudios.DxCommandTerminal.Backend.CommandShell) -
  `CommandShell` and the `Terminal` facade.
