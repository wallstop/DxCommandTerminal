---
name: log-echo-contract
description: Preserve the DxCommandTerminal log echo contract - both console surfaces write the typed line into Terminal.Buffer as TerminalLogType.Input before a command handler runs, so a handler that reads the log sees its own echo as the newest entry. Covers OutputLength, why a fixed -1/-2 offset is wrong, why the palette and the copy commands filter differently, why the log view must still SHOW echoes, and the test-helper trap that hides all of it. Use when writing or reviewing a command that reads the log, when a command returns its own name or a command line instead of a message, when touching trace/copy-last/copy-log/CommandPaletteUI.CollectOutput, or when a log-reading test passes against code the product never runs.
metadata:
  category: Feature
---

# The Log Echo Contract

## The rule

Both console surfaces echo the line a developer typed into the log BEFORE the
command handler is reached:

| Surface | Site |
| --- | --- |
| `TerminalUI.EnterCommand` | `Runtime/CommandTerminal/UI/TerminalUI.cs` - `Terminal.Log(TerminalLogType.Input, commandText)` then `RunCommand` |
| `CommandPaletteUI` submit | `Runtime/CommandTerminal/UI/CommandPaletteUI.cs` - same order |

So **any code running inside a command handler that reads "the newest entry" is
reading the echo of the command that is running.** `copy-last` returned the
literal text `copy-last`. The palette's own echo at submit time is in its
output window for the same reason.

`TerminalLogType.Input` has exactly two writers, both one per submit. Anything
else that logs is output, whatever its type.

## Read the log through OutputLength, never a fixed offset

`BuiltinCommands.OutputLength(window, logCount)` returns how much of the window
is not trailing command echoes, walking back from the newest end.

```csharp
int logCount = ReadLogWindow(buffer, out LogItem[] window);
int usable = OutputLength(window, logCount);      // echoes are window[usable..logCount)
int first = Math.Max(0, usable - Math.Min(lineCount, usable));
```

Why a fixed offset is wrong twice over:

- `logCount - 1` is the echo, always, from a console.
- `logCount - 2` is the message before it ONLY when the command above this one
  said something. A command that produced no output leaves its own echo there,
  so `trace` printed a command line. And dispatched programmatically
  (`shell.RunCommand("trace")`) there is no echo, so the fixed offset skipped
  the newest message.

`OutputLength` returns 0 for an all-echo window, so `usable - 1 == -1` is a
clean "nothing before the echo" sentinel.

## Two valid filter shapes, and why they differ

| Consumer | Window | Shape | Why |
| --- | --- | --- | --- |
| `BuiltinCommands` copy and trace | the whole buffer | trailing-echo walk (`OutputLength`) | an echo further back is a command the developer ran, and a transcript wants those as much as the output |
| `CommandPaletteUI.CollectOutput` | this run only, from a `Version` delta captured before the echo | whole-window `type == Input` skip | the window starts at the version delta, so it cannot contain another submit's echo |

Neither is a missed case. Pick by what the window means, and say which in a
comment. Do not unify them.

## The log view must SHOW echoes

`TerminalUI.RefreshLogs` renders every entry, echo included, and colors it via
the `--text-input-echo` class. That is correct: the developer typed it, and it
belongs on screen. The contract above is about reading the log programmatically
for a result, not about what is displayed. Do not "fix" the view by filtering
echoes out.

## The test-helper trap (this is how the bug survived two review rounds)

A test helper that calls `shell.RunCommand(line)` directly exercises a path the
product never takes, because the product echoes first. Every log-reading test
then passes against code that is broken in the console.

```csharp
// WRONG: no echo is ever written, so a copy bug is invisible.
_shell.RunCommand(line);

// RIGHT: mirrors EnterCommand's order.
Terminal.Log(TerminalLogType.Input, line);
_shell.RunCommand(line);
```

When a handler reads the log, its test helper owes the echo. When it does not,
the plain dispatch is fine - but the difference has to be a decision, not an
accident.

Symptom to recognise: a test that asserts the copied text equals the message
above, and passes, while the command returns its own name in the console.

## Sweep checklist

When a command starts reading the log:

1. Grep `Terminal.Buffer`, `CommandLog`, `CopyTo`, `Logs`, `LogItem` in the
   handler. Any hit needs `OutputLength` or an explicit type filter.
2. Grep for `- 1`, `- 2`, "second newest", "previous message" near a log read.
3. Re-read the test helper. Does it echo?
4. Check the other two consumers still agree: `CommandTrace`,
   `CommandPaletteUI.CollectOutput`, `TerminalUI.RefreshLogs`.

## Related

- [session-facade](../session-facade/SKILL.md) - the nullability boundary on
  `Terminal.Buffer` itself. This skill is about what is IN the buffer once it is
  non-null during dispatch.
- [register-terminal-command](../register-terminal-command/SKILL.md) - how the
  handler gets here.
