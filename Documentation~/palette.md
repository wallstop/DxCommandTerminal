# Quick-launch bar

`CommandPaletteUI` is a launcher-style command bar. It runs commands through
the same shell as the terminal, without the terminal animation.

## Set it up

1. Add a `UIDocument` component to a GameObject and give it Panel Settings
   (the package's `TerminalSettings` asset works; see [Install](install.md)).
2. Add a `CommandPaletteUI` component next to it.
3. Enter Play Mode and press `Ctrl+Space` to open the bar (configurable via
   the component's `toggleHotkey` or a `TerminalSettings` asset).

## Drive it

- Type to search. Results rank exact-first, then prefix, then fuzzy.
- Up and Down select a result; Tab commits the selected name; Enter runs it.
- Once the input names a command, the bar shows that command's completion
  candidates for the active argument - static choices and dynamic
  providers alike. Tab applies the selected candidate.
- A failed command keeps the bar open and shows the error.
- Escape closes the bar. Opening the terminal closes the bar, so a hotkey
  press can never execute twice.

The bar follows the terminal's theme, and the `Opened`/`Closed` events let
gameplay code release its own input maps while it is up.
