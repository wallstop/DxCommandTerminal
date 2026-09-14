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

## Try it

Open the terminal (default hotkey: backtick), then:

- `heal 50 ally`
- `god true`, `weather storm` - bool and enum arguments Tab-complete their values
- `inventory add gem 3`, then `pickup ` + Tab lists the current inventory
- `game time scale 0.5`
- `say hello world`
- `ping`

If `Reset State On Init` is enabled on the `TerminalUI`, the shell is rebuilt
during the terminal's own startup (its OnEnable and Start); register from
`Start` or after the terminal is ready, or the registration can be discarded.
See the note in the package README.
