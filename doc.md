_Documentation for internal features of the Terminal. For documentation on how to register commands, please refer to the [README](./README.md)._

### Terminal structure

|              | Description                                                        |
| :----------- | :----------------------------------------------------------------- |
| Buffer       | Handles incoming logs                                              |
| Autocomplete | Keeps a list of known words and uses it to autocomplete text       |
| Shell        | Responsible for parsing and executing commands                     |
| History      | Keeps a list of issued commands and can traverse through that list |

### Variables

```csharp
Terminal.Shell.SetVariable("level", SceneManager.GetActiveScene().name);
```

In the console:

```text
> print $level
Main

> set greet Hello World!
> print $greet
Hello World!

> set
LEVEL  : Main
GREET  : Hello World!
```

### Add words to autocomplete

```csharp
Terminal.Autocomplete.Register("foo");
```

### Run a command

```csharp
Terminal.Shell.RunCommand("print Hello World!"));
```

### Log without adding to Unity debug logs

```csharp
Terminal.Log("Value of foo: {0}", foo);
```

### Clear logs

```csharp
Terminal.Buffer.Clear();
```

### Modify the command history

```csharp
Terminal.History.Clear();     // Clear history
Terminal.History.Push("foo"); // Add item to history

string a = Terminal.History.Next();     // Get next item
string b = Terminal.History.Previous(); // Get previous item
```

### Argument-aware completion (new)

Commands can provide dynamic argument suggestions that the UI uses for tab completion and hints.

- Implement `Backend.IArgumentCompleter` and return context-aware suggestions.
- Attach with `[CommandCompleter(typeof(YourCompleter))]` on the same method that has `[RegisterCommand]`.
- Existing commands without completers keep working; history/known words remain as fallback.
- Argument suggestions are only triggered after the command name is complete and followed by a space, or when editing a specific argument token. If you press Tab while the caret is still inside the command name (no trailing space), the system completes command names as before.

Example:

```csharp
using WallstopStudios.DxCommandTerminal.Attributes;
using WallstopStudios.DxCommandTerminal.Backend;

public static class MyCommands
{
    [RegisterCommand("warp", MinArgCount = 1, MaxArgCount = 1, Help = "Warp to a target", Hint = "warp <target>")]
    [CommandCompleter(typeof(WarpCompleter))]
    private static void Warp(CommandArg[] args)
    {
        // Execute warp to args[0]...
    }
}

public sealed class WarpCompleter : IArgumentCompleter
{
    public IEnumerable<string> Complete(CommandCompletionContext ctx)
    {
        // Complete only first argument
        if (ctx.ArgIndex != 0)
        {
            return Array.Empty<string>();
        }

        // Domain-specific query. Filter by ctx.PartialArg
        var targets = new[] { "alpha", "beta-station", "gamma" };
        return targets.Where(t => string.IsNullOrEmpty(ctx.PartialArg)
                                  || t.StartsWith(ctx.PartialArg, StringComparison.OrdinalIgnoreCase));
    }
}
```
