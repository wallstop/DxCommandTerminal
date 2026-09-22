# Install

DxCommandTerminal is a Unity package. It supports Unity 2021.3 and newer.

## Install the package

- Git URL: `Window > Package Manager > + > Add package from git URL`, then
  enter `https://github.com/wallstop/DxCommandTerminal.git`.
- Tarball: download `com.wallstop-studios.dxcommandterminal-*.tgz` from the
  repository's [Releases](https://github.com/wallstop/DxCommandTerminal/releases)
  page, then `+ > Add package from tarball`.

## Open the terminal

1. Add a `TerminalUI` component to any GameObject. Its inspector adds a
   `UIDocument` for you and assigns the package's Panel Settings asset
   (`Styles/TerminalSettings.asset`), which carries the terminal's theme
   sheet - without a themed panel, nothing renders.
2. Wiring it yourself instead? Add a `UIDocument`, and point its Panel
   Settings at the package's `TerminalSettings` asset (or set your panel's
   Theme Style Sheet to `Styles/TerminalThemeSettings-Base.tss`).
3. Enter Play Mode. Press `` ` `` (backtick) to toggle the terminal,
   `shift+backtick` for the full-height window.

Every action is rebindable, and the [new Input System](https://docs.unity3d.com/Manual/InputSystem.html)
can drive the terminal through `PlayerInput`. See the package README
("Hotkeys" and "New Input System") for bindings and precedence.

## Try it

- `help` lists registered commands.
- Up and Down cycle the history.
- Tab completes commands and argument values.
