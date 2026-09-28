# Paste

Ctrl+V (Cmd+V on macOS) pastes the system clipboard into whichever
console field has focus: the terminal's command line, or the
quick-launch bar's search input. A selection is replaced; a collapsed
caret inserts at its position.

A UI Toolkit text field has no clipboard of its own, so the terminal
answers the key. There is nothing to configure and no setting to turn
on.

## A pasted block is not typed input

Every run of whitespace collapses to the single space that separates
arguments. A copied stack trace, a copied log line, and a command
copied with its trailing newline all paste as one line of arguments:

```text
give item 42      <- a three-line clipboard pastes as this
```

A run at the start of the field separates nothing, so it is dropped.
Whitespace inside quotes is preserved, so `set name "two
words"` stays one argument - see
[Typed arguments](arguments.md#separators).

## Where the clipboard is unavailable

tvOS has no system clipboard, and a platform whose clipboard is
asynchronous (WebGL) reads empty. Neither is an error: the key is left
alone, and whatever the platform does with it still happens.

## Where next

- [Typed arguments](arguments.md) - what a separator is.
- [Quick-launch bar](palette.md) - the same paste in the bar.
- [API Reference](xref:WallstopStudios.DxCommandTerminal.UI.CommandPaletteUI) -
  `CommandPaletteUI`.
