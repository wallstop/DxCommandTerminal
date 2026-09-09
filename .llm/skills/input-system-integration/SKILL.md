---
name: input-system-integration
description: Wire DxCommandTerminal input - configurable keyboard hotkeys, Unity's new Input System / PlayerInput bindings, the HandlePrevious/HandleNext/ToggleSmall/ToggleFull/CompleteCommand/EnterCommand messages, and input precedence. Use when adding or changing terminal keybindings, PlayerInput wiring, or fixing input handling bugs (including WebGL).
metadata:
  category: Feature
---

# Input System Integration

## Two input paths

1. **Legacy keyboard hotkeys** (`Use Hotkeys` on the `Terminal` component): bindable per action,
   shift-combos expressed as `#tab`, `A` (shifted char), or `shift+tab` on the new Input System.
2. **New Input System** (`Runtime/CommandTerminal/Input/`): `ITerminalInput` implementations -
   `TerminalPlayerInputController` (PlayerInput message routing) and
   `TerminalKeyboardController`; `InputHelpers` + `DefaultTerminalInput` provide the fallback
   handler (`IInputHandler`).

## PlayerInput binding

Bind InputActions to these messages (UnityEvents/`SendMessage` style on the Terminal):

| Message | Action |
| --- | --- |
| `HandlePrevious` | Older history entry |
| `HandleNext` | Newer history entry |
| `Close` | Close if open |
| `ToggleSmall` | Open small / close |
| `ToggleFull` | Open full / close |
| `CompleteCommand` | Auto-complete forward |
| `ReverseCompleteCommand` | Auto-complete backward |
| `EnterCommand` | Execute current buffer |

When using PlayerInput, UNCHECK `Use Hotkeys` on the Terminal - otherwise both paths fire.

## Precedence (hotkey path, when InputActions are not bound)

Close -> EnterCommand -> Previous -> Next -> ToggleFull -> ToggleSmall -> AutoComplete
(backward) -> AutoComplete (forward). PlayerInput binding order ignores this list entirely.

## History navigation semantics

- Up/Down navigation past either end yields a blank command (no "sticking").
- Optional skip of duplicate consecutive entries when walking history; history is filterable by
  execution success and error status (`CommandHistory`).

## WebGL specifics

- Input handling had WebGL-specific bugs fixed in this fork (key event swallowing); when touching
  input code, verify on a WebGL build, not just in-editor.
- Keyboard focus steal/restore around open/close cycles must preserve caret width calculation -
  see `TerminalUI` caret sizing.

## When changing input code

1. Add/extend `ITerminalInput` rather than branching on input system type inside `TerminalUI`.
2. Keep the precedence table in sync with code and README in the same change.
3. PlayMode-testable: `Tests/Runtime/Components/TerminalInputHandler.cs` simulates input; extend
   it for new bindings - see [run-terminal-tests](../run-terminal-tests/SKILL.md).
