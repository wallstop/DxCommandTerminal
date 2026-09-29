---
name: log-echo-contract
description: Preserve the DxCommandTerminal log echo contract - both console surfaces write the typed line into Terminal.Buffer as TerminalLogType.Input before a command handler runs, so a handler that reads the log sees its own echo as the newest entry, and a command that answers in the log writes console text that later readers must not count as output. Covers OutputLength, why a fixed -1/-2 offset is wrong, why the palette and the copy commands filter differently, why the log view must still SHOW echoes, why a search excludes the console's own replies by text rather than by type, why EnterCommand overwrites a handler's view state, and the test-helper traps that hide all of it. Use when writing or reviewing a command that reads or counts the log, when a command returns its own name or a command line instead of a message, when touching trace/copy-last/copy-log/find/clear-filter/CommandPaletteUI.CollectOutput, or when a log-reading test passes against code the product never runs.
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

## The console's own replies are not output either

The other writer of non-game text is the console answering itself. A command
that must answer in the log (`#186`) writes ordinary log text, and that text is
in the window every later reader sees.

This bit the log search: a query that happened to be a word in the search's own
answer - `search`, `log`, `line`, `clear-filter` - matched the answer, so a
search that hit nothing reported a hit and every repeat added another. The
echo exclusion above does not help; a reply is a `Message` or a `Warning`, not
an `Input`.

**A search excludes the console's own lines by exact text, not by type.** The
types belong to the game - a `Warning` is what the developer is looking for, and
a `ShellMessage` is any `Terminal.Log` the game made - so no type means "the
console said this". `LogFilter.IgnoreOwnReply` is called from the same door
that logs (`TerminalUI.LogFindReply` / `LogFindWarning`), so a new reply cannot
be added without registering it. Register the exact string that is logged: a
message with format arguments reaches the log formatted and the filter holding
the format, and the two stop being the same line.

Do not exclude the whole `Warning` type, and do not exclude trailing replies
positionally - a positional exclusion makes the count depend on whether
anything has been logged since, so the same search reports a different number
on the frame the developer runs their next command.

`copy-log` keeps console replies in its transcript on purpose (see the table
above): a transcript wants the record of what you did. The difference is that
`copy-log` reports no count the developer decides anything from, and the search
does.

## A handler's UI intent is overwritten by the code that ran it

`TerminalUI.EnterCommand` re-attaches the log tail and asks for a scroll to the
end **after** the handler returns, because running a command is a request for
its output:

```
Terminal.Log(Input, text);  shell.RunCommand(text);   // <- the handler sets the view
_logTail.Attach();  _needsScrollToEnd = true;         // <- and then overrides it
```

A handler whose whole purpose is where the view ends up has to survive that.
`EnterCommand` skips the re-assert when a search jump is queued, and it is the
only site that can know, so the decision belongs there.

Two consequences for tests:

- **Dispatch through `EnterCommand`, not the shell**, for anything about where
  the view ends up. `shell.RunCommand` never re-attaches, so a test that uses
  it cannot see this class of bug at all.
- **Assert the state synchronously.** The jump is dropped once its budget runs
  out, so a test that yields a frame or two before looking finds the flag
  cleared on a *correct* build and passes straight over a broken one. Expose
  the flag (`TerminalUI.FindScrollQueued`, `WantsScrollToEnd`) and read it in
  the frame the call returns.

Do not expose the follower's `Detached` for this: it only flips when a scroll
is actually placed, so on a host with no laid-out view it reads the same whether
or not anything is wrong.

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

When a command starts **counting** the log, or reading it to drive a view:

5. Which of the lines in the window are the console's own? Echoes (`Input`) and
   the command's own replies. A count that includes either is a number the
   developer will act on and be wrong by.
6. Does the handler's effect on the view survive the call that ran it? See
   "A handler's UI intent is overwritten by the code that ran it".

## Related

- [session-facade](../session-facade/SKILL.md) - the nullability boundary on
  `Terminal.Buffer` itself. This skill is about what is IN the buffer once it is
  non-null during dispatch.
- [register-terminal-command](../register-terminal-command/SKILL.md) - how the
  handler gets here.
