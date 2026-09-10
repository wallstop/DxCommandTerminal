---
name: register-terminal-command
description: Register terminal commands in DxCommandTerminal via RegisterCommandAttribute or Terminal.Shell.AddCommand, including name inference, arg count bounds, hint text, editor/development-only flags, and ignoring built-in commands. Use when adding console commands/cheats, changing command signatures, or debugging why a command is not recognized.
metadata:
  category: Feature
---

# Register Terminal Command

## Option 1 - Attribute registration (static methods)

```csharp
[RegisterCommand(Help = "Adds 2 numbers", MinArgCount = 2, MaxArgCount = 2)]
static void CommandAdd(CommandArg[] args)
{
    int a = args[0].Int;
    int b = args[1].Int;
    if (Terminal.IssuedError) return;
    Terminal.Log("{0} + {1} = {2}", a, b, result);
}
```

- The method must be `static` (public or non-public) with signature
  `void Name(CommandArg[] args)`.
- Command name is inferred from the method name: the substring `COMMAND` (case-insensitive,
  prefix/infix/suffix) is removed - `CommandAdd` -> `add`, `SpawnEnemyCommand` -> `spawnenemy`.
  Override with `[RegisterCommand("myname")]` or `Name = "myname"` (spaces are stripped).
- `MinArgCount` / `MaxArgCount` (`-1` = unbounded) make the shell issue an error before your
  handler runs when arity is wrong; you may then index `args` safely.
- Discovery is catalog-first: the bundled source generator emits one internal
  `WallstopStudios.DxCommandTerminal.Generated.CommandCatalog` per assembly that declares
  `[RegisterCommand]` methods, and `CommandShell` binds those catalogs without walking types.
  Assemblies without a catalog (precompiled DLLs, or when analyzers are unavailable) fall back
  to reflection over terminal-referencing assemblies; user assemblies take precedence over
  built-ins with the same name either way.
- Registration is deferred to first use: the terminal applies its ignored/default configuration
  when enabled, but the discovery scan and delegate materialization run at the first command
  request (first `RunCommand`, first read of `Terminal.Shell.Commands`, or an explicit
  `Terminal.Shell.EnsureAutoCommandsRegistered()` call). `Terminal.Shell.AutoCommandsRegistered`
  reports whether registration has been applied.

## Attribute knobs

| Property | Meaning |
| --- | --- |
| `Name` | Explicit command name (spaces stripped) |
| `Help` | Help text shown by `help` |
| `Hint` | Auto-complete hint text (defaults to `Help`) |
| `MinArgCount` / `MaxArgCount` | Arity bounds, validated by the shell |
| `EditorOnly` | Command stripped from player builds |
| `DevelopmentOnly` | Command excluded from non-development builds |
| `AddToHistory` | Whether invocations enter command history (default true) |

## Option 2 - Manual registration (instance methods)

```csharp
Terminal.Shell.AddCommand("add", CommandAdd, 2, 2, "Adds 2 numbers");
```

Required when the handler is non-static or registered dynamically. Invalid/duplicate names make
`AddCommand` return `false` and issue a shell error - check the result in editor tooling.

## Ignoring commands

Terminals can ignore built-in or third-party commands by name (configured on the `Terminal`
component; the custom editor lists discoverable commands). Register your own conflicting command
(e.g. a custom `help`) after enabling ignoring of the default commands.

## Programmatic execution

- `Terminal.Shell.RunCommand("add 1 2")` parses and executes a full line.
- Prefer typed invocation paths when possible; programmatic runs still honor `AddToHistory`.

## Gotchas

- WebGL builds require Managed Stripping Level <= Minimal or attribute discovery breaks - see
  [webgl-command-registration](../webgl-command-registration/SKILL.md).
- Handler exceptions are not caught for you; validate inputs (`args[i].TryGet<T>`) and let the
  shell's error channel (`Terminal.IssuedError`) do the reporting.
