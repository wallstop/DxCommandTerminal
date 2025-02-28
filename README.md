Unity Command Terminal
======================

# Notice

This is a fork of [Command Terminal](https://github.com/stillwwater/command_terminal) for Unity, mainly to address usability gaps and add maintenance

# To Install as Unity Package
1. Open Unity Package Manager
2. (Optional) Enable Pre-release packages to get the latest, cutting-edge builds
3. Open the Advanced Package Settings
4. Add an entry for a new "Scoped Registry"
    - Name: `NPM`
    - URL: `https://registry.npmjs.org`
    - Scope(s): `com.wallstop-studios.dxcommandterminal`
5. Resolve the latest `com.wallstop-studios.dxcommandterminal`

## Improvements Over Baseline
- Add ability to ignore commands that have been annotated with `RegisterCommandAttribute`. In this way, your terminals can ignore any built-in commands, for cleanliness. A custom editor has been added to provide users with the ability to identify what commands are available to ignore, and selectively ignore them.
- Add ability to ignore certain (or all) log levels, such that unwanted logs do not clutter terminal output
- Add ability to optionally have Unity log messages routed to the terminal, default on, but can be turned off
- Add ability to optionally ignore all default commands, such that you can write your own commands that conflict with the default provided ones, such as `HELP`, `TIME`, etc
- CommandArg value parsing has been overhauled. Instead of specific properties like `Float` and `Int` that always return a value, regardless of if parsing succeeded or not, a singular `bool TryGet<T>(out T parsed)` method is supplied. This method provides robust parsing for all common types and has the ability to take in user-specified custom parsers for any fancy logic. See [Custom Parsing](#custom-parsing) for details.
- CommandArgs can now have arguments with spaces passed to them, similar to how real terminals work. Any string values surrounded by single or double quotes will be interpreted as a single input (including spaces within those quotes). Single quotes must match single quotes and double quotes must match double quotes. If there is an unmatched (hanging) quote, the entire rest of the input will be interpreted as the command argument value.
- Dynamic console resizing when Screen Size change is detected, implemented in a very performant fashion
- Added Assembly Definitions, so that this project can be used cleanly in projects that utilize Assembly Definitions
- Fixed a bug where commands annotated with `RegisterCommandAttribute` in other assemblies failed to be recognized. User assemblies have higher precedence.
- Fixed a bug where only the latest error message was preserved - errors are now queued
- Fixed a bug where attempting to access static `Terminal` properties would throw if the Terminal had been enabled yet.
- Minor performance benefits if there are terminals in multiple scenes
- Minor performance benefits (O(n) -> O(1)) when the terminal buffer becomes full
- Minor performance benefits around multiple-indexing into dictionary issues
- Minor performance benefits around using string interpolation and intelligent checking of parameters to only force `string.Format` when relevant in logging paths
- Better invalid command identification and error messages
- All string comparisons are now `OrdinalIgnoreCase` instead of relying on CurrentCulture
- `Terminal` has been made to be Editor-Aware. If the Editor is in Play mode, changes to the current terminal will take effect immediately. If the terminal is open, it will be closed, to prevent bugs.
- Extra input validation has been added on all public methods, such that user code is sanitized where appropriate, or rejected if invalid.
- The concept of "FrontCommands" has been exterminated

## Code changes
- All code is formatted via [Csharpier](https://csharpier.com/)
- All variables are now consistently named
- Access modifiers have been explicitly applied to every field
- All warnings have been gotten rid of
- Collections / properties are now exposed as immutable by default. Mutable fields / properties are only exposed as necessary
- Conversion to capitalization/lowercase is now only done where absolutely required
- Annotated all logging methods with Jetbrain's `StringFormatMethod` attribute to aid in intellisense and help identify formatting issues
- All classes / structs have been made sealed / readonly where possible to promote immutability
- Validation around command ignoring and log level ignoring has been added to Terminal, to prevent invalid data

## Custom Parsing
TODO TODO

## The Future
More improvements coming soon, stick around :)

Planned improvements:
- Integration with the new Input System
- Ensure working on WebGL builds
- Command Groups
- Background not being rendered bug
- Up key support (move cursor to end)
- Support for more out of the box command argument types
- Ensure working in Mobile builds

---

A simple and highly performant in-game drop down Console.

![gif](./demo.gif)

Command Terminal is based on [an implementation by Jonathan Blow](https://youtu.be/N2UdveBwWY4) done in the Jai programming language.

## Usage

Copy the contents from [CommandTerminal](./CommandTerminal) to your Assets folder. Attach a `Terminal` Component to a game object. The console window can be toggled with a hotkey (default is backtick), and another hotkey can be used to toggle the full size window (default is shift+backtick).

Enter `help` in the console to view all available commands, use the up and down arrow keys to traverse the command history, and the tab key to autocomplete commands.

## Registering Commands

There are 3 options to register commands to be used in the Command Terminal.

### 1. Using the RegisterCommand attribute:

The command method must be static (public or non-public).

```csharp
[RegisterCommand(Help = "Adds 2 numbers", MinArgCount = 2, MaxArgCount = 2)]
static void CommandAdd(CommandArg[] args) {
    int a = args[0].Int;
    int b = args[1].Int;

    if (Terminal.IssuedError) return; // Error will be handled by Terminal

    int result = a + b;
    Terminal.Log("{0} + {1} = {2}", a, b, result);
}
```
`MinArgCount` and `MaxArgCount` allows the Command Interpreter to issue an error if arguments have been passed incorrectly, this way you can index the `CommandArg` array, knowing the array will have the correct size.

In this case the command name (`add`) will be inferred from the method name, you can override this by setting `Name` in `RegisterCommand`.

```csharp
[RegisterCommand(Name = "MyAdd", Help = "Adds 2 numbers", MinArgCount = 2, MaxArgCount = 2)]
```

### 2. Using a FrontCommand method:

Here you still use the `RegisterCommand` attribute, but the arguments are handled in a separate method, prefixed with `FrontCommand`. This way, `MaxArgCount` and `MinArgCount` are automatically inferred.

This also allows you to keep the argument handling `FrontCommand` methods in another file, or even generate them procedurally during a pre-build.

```csharp
[RegisterCommand(Help = "Adds 2 numbers")]
static void CommandAdd(int a, int b) {
    int result = a + b;
    Terminal.Log("{0} + {1} = {2}", a, b, result);
}

static void FrontCommandAdd(CommandArg[] args) {
    int a = args[0].Int;
    int b = args[1].Int;

    if (Terminal.IssuedError) return;

    CommandAdd(a, b);
}
```

### 3. Manually adding Commands:

`RegisterCommand` only works for static methods. If you want to use a non-static method, you may add the command manually.

```csharp
Terminal.Shell.AddCommand("add", CommandAdd, 2, 2, "Adds 2 numbers");
```
