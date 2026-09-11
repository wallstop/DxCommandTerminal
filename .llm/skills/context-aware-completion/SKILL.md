---
name: context-aware-completion
description: Register context-aware commands with CommandDefinition, gate execution by CommandExecutionContexts, and attach CommandCompletionProvider argument completion (staged providers, TryComplete, TerminalUI token cycling) in DxCommandTerminal. Use when writing commands that need argument completion or execution-context gating, wiring chained completions like item name then item action, or debugging why a command does not run or complete.
metadata:
  category: Feature
---

# Context-Aware Execution And Completion

## Registration

```csharp
Terminal.Shell.AddCommand(new CommandDefinition
{
    Name = "pickup",
    Help = "Picks up an item",
    MinArgCount = 1,
    MaxArgCount = 2,
    Contexts = CommandExecutionContexts.Gameplay,
    Handler = (CommandExecutionContext context, BorrowedCommandArguments args) =>
    {
        string item = args[0].contents;
    },
    CompletionProvider = CommandCompletionProviders.Staged(Items, Actions),
});
```

- Exactly one of `Handler` (context + borrowed view) or `LegacyHandler`
  (`Action<CommandArg[]>`) must be set. Legacy handlers get a fresh owned
  array per invocation; they may retain it.
- `AddCommand` snapshots the definition; later edits do not affect the
  registration.
- `BorrowedCommandArguments` is valid only during the callback. Copy out
  with `ToArray()` before returning. Do not cache the view.

## Execution contexts

- `CommandExecutionContexts` flags: `EditorEditMode`, `EditorPlayMode`,
  `Player`; composites `Gameplay` and `All`.
- Definitions default to `Gameplay`; `EditorEditMode` execution is
  opt-in. `[RegisterCommand]` defaults to `Contexts = All`, so existing
  attributed commands keep their previous availability.
- Legacy `AddCommand` overloads stay unrestricted.
- Eligibility is checked at dispatch: an ineligible command queues
  `"<name> is not available in the current execution context"` and
  respects its history policy. Completion may still surface ineligible
  commands; execution re-validates.
- The current environment comes from Unity (`EditorApplication.isPlaying`
  in the Editor, `Player` otherwise). Tests can inject through the
  internal `CommandExecutionContext.AmbientContextProvider` hook; reset it
  in teardown.

## Completion providers

- `void CommandCompletionProvider(in CommandCompletionContext context,
  List<CommandCompletion> results)`; synchronous, exceptions contained.
- The context carries: `ExecutionContext`, `Input` (full line),
  `CaretIndex`, `ActiveArgumentIndex` (0 = first argument after the
  command name), `PrecedingArguments` (borrowed view, quoting preserved,
  raw variables), `Token` (active token text to the caret), the
  replacement range, and `IsQuoted`/`QuoteCharacter`.
- The shell deduplicates by insertion text (ordinal, first wins) and
  preserves provider order. Empty insertions are dropped. Zero results
  still count as provider-answered.
- `CommandCompletionProviders.Staged(...)` maps argument stage k to
  stages[k]; stages beyond the list produce nothing, and null stages
  produce nothing.
- Insertions with spaces or quotes are quoted on insertion by the UI when
  the active token is unquoted. Providers return raw values.

## Programmatic completion

`Terminal.Shell.TryComplete(context, input, caretIndex, results, out
CommandCompletionContext completionContext)` returns false when the
command is unknown, has no provider, or the caret is on the command name
itself; callers fall back to their own suggestions. Returns true
otherwise, including empty or throwing providers, so history suggestions
cannot overwrite the token.

## Terminal behavior

Tab/Shift+Tab on a provider-attached command cycles only the active token
(replacement range = the token span, quotes excluded; mid-token typed-ahead
text is replaced). Typing resets the cycle. Providerless commands keep the
legacy full-line history completion. Never show full-line suggestions for
a provider-attached command.

## Tests

PlayMode suites: `CommandCompletionProviderTests`,
`CommandContextExecutionTests`, `CommandTokenizerTests`,
`TerminalUITokenCompletionTests` (runtime-built UIDocument rig). The
tokenizer is parity-pinned against `CommandShell.TryEatArgument`; keep
that test green when touching parsing.
