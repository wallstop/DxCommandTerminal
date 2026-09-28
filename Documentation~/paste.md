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

A quote still groups: `set name "two words"` pastes as one argument, not
two. What the paste changes is the text inside the quotes - a newline
inside a quoted argument becomes a space, because the paste normalizes
before the tokenizer ever sees it. Quoted whitespace is preserved when
you type, not when you paste. See
[Typed arguments](arguments.md#separators).

## Where the clipboard is unavailable

tvOS has no system clipboard, and a platform that will not answer without a
user gesture (a browser clipboard API) may read empty. Neither is an
error: the key is left alone, and whatever the platform does with it still
happens. Neither case is measured here; the degradation is the design, not
a fallback that failed.

## Limits

A control character in the copied text is left in the field. A single line
cannot show it, so it is invisible while you type, and it reaches the
argument as the exact character you copied. Dropping it would lose your
text; escaping it there would change the command. A command that prints it
gets the visible escape from the log.

## Where next

- [Typed arguments](arguments.md) - what a separator is.
- [Quick-launch bar](palette.md) - the same paste in the bar.
- [API Reference](xref:WallstopStudios.DxCommandTerminal.UI.CommandPaletteUI) -
  `CommandPaletteUI`.
