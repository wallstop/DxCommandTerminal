# Themes and fonts

Themes are style sheets; fonts are TrueType assets. Both ship as
ScriptableObject packs, and the terminal reads everything through them -
no per-code style mutations.

## Theme and font packs

A `TerminalThemePack` asset lists `Themes` (style sheets) and their
`ThemeNames`. A `TerminalFontPack` asset lists `Fonts`. The built-in
packs live under `Packs/`; add your own assets to extend either list.

`list-themes` and `list-fonts` print what the terminal currently has.

## Switching at runtime

`TerminalUI` exposes both switches. `persist: true` stores the choice
for next session:

```csharp
terminal.SetTheme("Wallstop", persist: true);
terminal.SetFont(myFont, persist: true);
```

`SetRandomTheme()` and `SetRandomFont()` pick a different entry from the
pack and return what they picked. `CurrentTheme`,
`CurrentFriendlyTheme`, and `CurrentFont` read the current state.

Theme names are matched with `ThemeNameHelper`, so case does not matter.

## Persisting across sessions

Add a `TerminalThemePersister` component next to the `TerminalUI`:

- It saves the terminal's font and theme to
  `Application.persistentDataPath/DxCommandTerminal/TerminalTheme.json`.
- On start it hydrates the terminal from that file.
- With `Save Periodically` on, it writes within `Save Period` seconds of
  any change.

Each terminal persists by its `id`, so several terminals keep separate
choices.

## Where next

- [Quick-launch bar](palette.md) - the palette follows the same theme.
- [API Reference](xref:WallstopStudios.DxCommandTerminal.UI.TerminalUI) -
  `TerminalUI.SetTheme`, `SetFont`, and friends.
