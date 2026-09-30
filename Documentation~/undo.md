# Undo

Ctrl+Z (Cmd+Z on macOS) takes the command line back one state at a time.
Ctrl+Shift+Z brings a state back. Both work in the terminal's command
line and in the quick-launch bar's search field.

A UI Toolkit text field has no history of its own, so the terminal
answers the key. There is nothing to configure.

## One state per change

Every change to the field is one state, and undo steps back one of them:

```text
giv        <- you type this
givx       <- and this
givxe      <- and this
givx       <- Ctrl+Z
giv        <- Ctrl+Z
```

Undo keeps going to the empty line and then stops. A key with nothing
left to undo does nothing at all; it does not clear the line.

Ctrl+Shift+Z walks the other way. It stops where you stopped, and a
keystroke after an undo ends the states above it, so a redo never brings
back a line you have moved on from.

## What is a state

Anything that changed the field's text, including the changes the
terminal made itself:

- A completion you applied with Tab.
- A line you recalled from history with the Up arrow.
- A paste.
- The clear a command run performs.

So a typo in a long command is one keystroke back rather than one
Backspace at a time, and a completion you did not want is one Ctrl+Z
away. The clear a run makes is a state like any other, so one more
Ctrl+Z brings the executed command back onto the line - it does not run
it again until you press Enter.

Undo is per surface. The command line and the search field have their
own histories, and neither can undo in the other. Closing the console
clears the command line's history, because the field it described is
gone.

The history holds 64 states. A session longer than that drops the
oldest, so undo reaches back through the last 64 changes and no
further.

## Limits

- One state per change, not one per run of typing. A word typed one
  letter at a time is that many Ctrl+Z.
- The caret is restored on Unity 2022.1 and newer. On 2021.3 the engine
  has no caret setter, so a restored line takes the caret at its end.
  The text is exact on every supported editor.
- A selection is not restored. The text and the caret are.

## Where next

- [Paste](paste.md) - the other key the terminal answers for the field.
- [Commands](commands.md) - what the line is run as.
- [Quick-launch bar](palette.md) - the same undo in the bar.
- [Typed arguments](arguments.md) - what the line is parsed into.
