---
name: terminal-theming
description: Work with DxCommandTerminal themes - TerminalThemePack and TerminalFontPack assets, Styles USS/TSS stylesheets, theme persistence, and the Editor custom inspectors. Use when adding or editing themes/fonts, changing theme properties, or debugging theme stylesheet resolution.
metadata:
  category: Feature
---

# Terminal Theming

## Asset model

| Asset | Class | Role |
| --- | --- | --- |
| Theme pack | `TerminalThemePack` (`Runtime/CommandTerminal/Themes/`) | Named set of UI Toolkit style values (colors, sizes) applied to `TerminalUI` |
| Font pack | `TerminalFontPack` (`Runtime/CommandTerminal/Themes/`) | Bundle of `FontAsset`s selectable per terminal |
| Stylesheets | `Styles/*.uss`, `Styles/*.tss` | Base USS (`BaseStyles.uss`), theme settings TSS chain |
| Persistence | `TerminalThemeConfiguration(s)`, `TerminalThemePersister` | Saved/serialized theme selections and per-theme overrides |

Built-in packs ship as ScriptableObject assets under `Packs/Themes/` and `Packs/Fonts/`.

## How styling flows

1. `TerminalUI` (UI Toolkit) resolves the active `TerminalThemePack`.
2. `TerminalThemeStyleSheetHelper` (Editor helper) builds/patches USS variables from pack data;
   runtime styling reads the resolved `Styles/TerminalSettings.asset` + TSS chain
   (`TerminalThemeSettings-Base.tss` -> `UnityDefaultRuntimeTheme.tss`).
3. `TerminalThemePersister` loads/saves user theme configuration; `TerminalThemeConfigurations`
   holds the known set. Theme changes in-editor apply live when `Track Changes In Editor` is on.

## Adding a theme pack

1. Create a `TerminalThemePack` asset under `Packs/Themes/` (follow existing asset naming).
2. Populate the style values; give the theme a unique, user-facing name
   (`ThemeNameHelper` guards collisions).
3. Add the asset to the pack list references as needed by your setup.
4. Create `.meta` files alongside the asset - see
   [create-unity-meta](../create-unity-meta/SKILL.md).

## Editing stylesheets

- Prefer adding/overriding USS variables in the theme pack over editing `BaseStyles.uss`; base
  styles are shared by all themes.
- Keep selectors minimal and stable - `TerminalUI` queries elements by name; renaming UI elements
  requires updating selectors in the same change.

## Editor inspection

`TerminalThemePackEditor` and `TerminalFontPackEditor` provide custom inspectors (pack contents,
available commands to ignore for terminals via `TerminalUIEditor`). When adding serialized fields
to packs, update the corresponding editor so the field stays editable and validated.

## WebGL / stripping notes

Theme packs are assets (not reflection-resolved), so they are unaffected by managed stripping;
font assets must be referenced by a pack to survive stripping in builds.
