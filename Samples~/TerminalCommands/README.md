# Typed Command Builder Examples

Runnable examples for the typed command builder. Every component registers
its commands on enable and disposes them on disable.

## Import

Package Manager > DxCommandTerminal > Samples > Import. Requires a scene with
a `TerminalUI` component (the commands register into its shell). Component
enable order is undefined: if a sample enables before the TerminalUI builds
its shell, it logs an error and skips registration - keep the sample
components in a scene with a TerminalUI.

## Components

| Component | Commands | Shows |
| --- | --- | --- |
| `SimpleCommands` | `heal <amount> [target]` | Required int with a range, optional default, static choices |
| `ArgumentKinds` | `god`, `weather`, `timescale` | Bool, enum, and float range arguments |
| `InventoryCommands` | `inventory add/remove/list`, `pickup` | Subcommands, bare-invocation fallback, live dynamic choices |
| `GameTimeCommands` | `game time set/scale`, `game pause` | Subcommands nested to any depth |
| `AnnounceCommands` | `say <message...>` | The unbounded trailing argument |
| `SceneCommands` | `scene-reload`, `scene-info` | Execution contexts and Edit Mode opt-in |
| `LifecycleCommands` | `ping` | Raw handle lifetime: register, dispose, re-register |
| `SceneObjectCommands` | `object-info <name>`, `object-position <name>` | Opt-in object/component parsers and fresh name completion |

## Try it

Open the terminal (default hotkey: backtick), then:

- `heal 50 ally`
- `god true`, `weather storm` - bool and enum arguments Tab-complete their values
- `inventory add gem 3`, then `pickup ` + Tab lists the current inventory
- `game time scale 0.5`
- `say hello world`
- `ping`
- Add `SceneObjectCommands`, create an active object named `Demo Target`, then run `object-info "Demo Target"`.
- `object-position "Demo Target"` resolves its `Transform`; duplicate names reject execution.
- Type `object-info ` or `object-position ` and press Tab for current names.

`SceneObjectArgumentAdapter<T>` supports `GameObject`, `Component`, and component subclasses.
Use `.RawParser(adapter.TryParse).Choices(adapter.GetChoices, adapter.FormatChoice)` to opt into name parsing and completion.
Raw parsing preserves CR and LF in object names instead of removing them before lookup.
The formatter affects dynamic completions only; existing choices and error messages retain their original formatting.
Names match exactly, ignoring case. Paths and instance IDs have no special syntax.
The default `FirstMatch` selects the first result in Unity's instance-ID order, not hierarchy order.
Use `SceneObjectAmbiguityPolicy.RequireUnique` to reject duplicate matches, including multiple components on one object.
Completions remain suggestions; execution checks the name again and reports the standard parser error on failure.
Queries run on Unity's main thread for every parse and completion request, without caching.
They search loaded objects, exclude assets and `HideFlags.DontSave`, and allocate Unity's result array each time.
Inactive objects are excluded unless `includeInactive: true` is passed to the adapter constructor.
Unity sorts each query; large scenes can make per-keystroke completion expensive.
No parsers or commands are registered globally by the adapter.

If `Reset State On Init` is enabled on the `TerminalUI`, the shell is rebuilt
during the terminal's own startup (its OnEnable and Start); register from
`Start` or after the terminal is ready, or the registration can be discarded.
See the note in the package README.
