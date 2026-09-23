# Completion

Tab completes command names and argument values - in the terminal and in
the quick-launch bar alike. Choices come from the command definition;
dynamic providers can read live game state.

## Static and dynamic choices

Static choices complete fixed values. Dynamic choices run a provider on
every completion request, so the list reflects the game right now:

[!code-csharp[InventoryCommands](samples/InventoryCommands.cs)]

`pickup`'s item argument and `inventory remove`'s item argument both
complete from the current inventory. Tab stages across arguments: with
`inventory remove` typed, the next Tab offers what the command's own
next argument can take.

## Provider context is scoped to the command

A provider always sees the command's own arguments:
`context.ActiveArgumentIndex` and `context.PrecedingArguments` are
relative to that command, and the shell shifts them per routing level
for subcommands. A provider never sees the parent command's tokens.

## Engine-backed values

Scene objects, layers, and tags complete through the opt-in
`SceneObjectArgumentAdapter<T>` (`GameObject`, `Component`, and
component subclasses): `.RawParser(adapter.TryParse)
.Choices(adapter.GetChoices, adapter.FormatChoice)` - see
`SceneObjectCommands` in the imported samples. Completions stay
suggestions; execution validates again and reports a shell error on a
miss.

## Where next

- [Registering commands](commands.md) - where choices are declared.
- [Quick-launch bar](palette.md) - the same completion in the bar.
- [API Reference](xref:WallstopStudios.DxCommandTerminal.Backend.CommandCompletionContext) -
  `CommandCompletionContext`, `CommandAutoComplete`.
