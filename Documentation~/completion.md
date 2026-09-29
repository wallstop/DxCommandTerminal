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

## A caret never lands inside a character

An emoji, an accented letter, a joined emoji sequence, and a flag are one
character but two or more UTF-16 code units, and a text field moves its
caret one code unit at a time. Every caret the console reads is snapped out
to the start of the character it is in, and every caret it computes is
snapped where it is computed, so:

- the token a provider receives is whole text, never half a surrogate;
- the replacement range covers the whole character, so accepting a
  candidate cannot leave half of one in the line;
- a paste lands beside the character instead of inside it.

The snap moves the caret to the front of the character, never past it. Two
limits are stated rather than hidden. The one sequence it does not join is
the Hangul one Unicode composes from separate jamo, so a caret can still
split a syllable that arrived decomposed. A paste leaves its caret at the
end of the pasted text: a clipboard cut mid-emoji would otherwise snap the
caret in front of the character just pasted, and every read of a caret is
snapped either way.

## Engine-backed values

Scene objects complete through the opt-in `SceneObjectArgumentAdapter<T>`,
which takes a `GameObject`, a `Component`, or a component subclass and
throws for anything else: `.RawParser(adapter.TryParse)
.Choices(adapter.GetChoices, adapter.FormatChoice)` - see
`SceneObjectCommands` in the imported samples. Layers and tags are not
object names, so the adapter does not cover them; a choice list built
from `LayerMask.LayerToName` or live `GameObject.tag` is a plain
dynamic choice - see `ObjectFilterCommands` in the samples.
Completions stay suggestions; execution validates again and reports a
shell error on a miss.

## Where next

- [Registering commands](commands.md) - where choices are declared.
- [Quick-launch bar](palette.md) - the same completion in the bar.
- [API Reference](xref:WallstopStudios.DxCommandTerminal.Backend.CommandCompletionContext) -
  `CommandCompletionContext`, `CommandAutoComplete`.
