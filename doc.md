*Documentation for internal features of the Terminal. For documentation on how to register commands, please refer to the [README](./README.md).*

### Terminal structure:

|              | Description                                                        |
|:-------------|:-------------------------------------------------------------------|
| Buffer       | Handles incoming logs                                              |
| Autocomplete | Keeps a list of known words and uses it to autocomplete text       |
| Shell        | Responsible for parsing and executing commands                     |
| History      | Keeps a list of issued commands and can traverse through that list |

### Variables:

```csharp
Terminal.Shell.SetVariable("level", SceneManager.GetActiveScene().name);
```

In the console:

```
> log-terminal $level
Main

> set-variable greet Hello World!
Variable 'greet' set to 'Hello World!' successfully.

> log-terminal $greet
Hello World!

> list-variables
Variable 'greet' is set to 'Hello World!'.
Variable 'level' is set to 'Main'.
```

`set-variable` takes exactly two arguments, so a value with spaces in it has to be
quoted: `set-variable greet "Hello World!"`.

### Run a command:

```csharp
Terminal.Shell.RunCommand("set-variable greet Hello World!");
```

### Log without adding to Unity debug logs:

```csharp
Terminal.Log("Value of foo: {0}", foo);
```

### Clear logs:

```csharp
Terminal.Buffer.Clear();
```

### Modify the command history:

```csharp
Terminal.History.Clear();     // Clear history
Terminal.History.Push("foo", success: true, errorFree: true); // Add item to history

string a = Terminal.History.Next(skipSameCommands: false);     // Get next item
string b = Terminal.History.Previous(skipSameCommands: false); // Get previous item
```

`Next` and `Previous` take no optional parameters; both arguments are required.
