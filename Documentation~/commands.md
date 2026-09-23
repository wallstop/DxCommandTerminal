# Registering commands

Three ways to register a command: the `[RegisterCommand]` attribute on a
static method, `Terminal.Shell.AddCommand` by hand, or the typed builder.
All three land in the same shell and complete the same way.

## The attribute (static methods)

```csharp
[RegisterCommand(Help = "Adds 2 numbers", MinArgCount = 2, MaxArgCount = 2)]
static void CommandAdd(CommandArg[] args)
{
    int a = args[0].Int;
    int b = args[1].Int;
    Terminal.Log("{0} + {1} = {2}", a, b, a + b);
}
```

- The name is inferred from the method name (`Command` infix, suffix, or
  prefix stripped) - here `add`. Override with `Name = "..."`.
- The source generator finds the method at compile time and emits a
  catalog, so the command survives managed code stripping (IL2CPP/WebGL).
- The method must be static. `MinArgCount`/`MaxArgCount` let the shell
  reject wrong-arity input before the handler runs.

## Manual (instance handlers)

```csharp
Terminal.Shell.AddCommand("reset-score", ResetScore, 0, 0, "Resets the score");
```

Use this for non-static handlers or registrations that need runtime
state. Returns `false` (without throwing) when the name is taken.

## The typed builder

The builder derives argument bounds, the usage hint, validation, and
completion from one declaration, and returns a handle that removes
exactly its own registration:

[!code-csharp[SimpleCommands](samples/SimpleCommands.cs)]

- `.Required()`, `.Default(...)`, `.Range(...)`, and `.Choices(...)` are
  per-argument validation; bad input is rejected with a shell error
  before the handler runs.
- Subcommands route one command through its first argument, each with its
  own arguments and completion (`inventory add pickaxe 3`); see the
  [quick-launch bar](palette.md) and the shipped samples.
- Misconfiguration (missing handler, duplicate names, required after
  optional) throws `CommandConfigurationException` at definition time.

## Where next

- [Arguments](arguments.md) - typed parsing and custom parsers.
- [Lifetime](lifecycle.md) - when to register, and how to unregister.
- [API Reference](xref:WallstopStudios.DxCommandTerminal.Backend.CommandBuilder) -
  `CommandBuilder`, `RegisterCommandAttribute`, `CommandShell`.
