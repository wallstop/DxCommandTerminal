---
name: terminal-theming
description: Work with DxCommandTerminal themes - TerminalThemeAsset authoring, TerminalThemePack and TerminalFontPack assets, Styles USS/TSS stylesheets, theme persistence, and the Editor custom inspectors and asset postprocessors. Use when adding or editing themes/fonts, generating theme sheets, changing theme properties, or debugging theme stylesheet resolution.
metadata:
  category: Feature
---

# Terminal Theming

## Asset model

| Asset | Class | Role |
| --- | --- | --- |
| Theme pack | `TerminalThemePack` (`Runtime/CommandTerminal/Themes/`) | Named set of UI Toolkit style values (colors, sizes) applied to `TerminalUI` |
| Font pack | `TerminalFontPack` (`Runtime/CommandTerminal/Themes/`) | Bundle of `FontAsset`s selectable per terminal |
| Theme asset | `TerminalThemeAsset` (`Runtime/CommandTerminal/Themes/`) | Authoring-only SO whose 19 color fields auto-generate a sibling `.uss` via `TerminalThemeAssetPostProcessor` (Editor) |
| Stylesheets | `Styles/*.uss`, `Styles/*.tss` | Base USS (`BaseStyles.uss`), theme settings TSS chain |
| Persistence | `TerminalThemeConfiguration(s)`, `TerminalThemePersister` | Saved/serialized theme selections and per-theme overrides |

Built-in packs ship as ScriptableObject assets under `Packs/Themes/` and `Packs/Fonts/`.

## Theme assets (generated sheets)

`TerminalThemeAsset` colors -> sibling `<AssetName>.uss` written by
`TerminalThemeAssetPostProcessor` on every import. Contracts enforced there:

- Generated sheets carry the `TerminalThemeAsset.GeneratedMarkerPrefix` first-line
  stamp; only marker-carrying files are overwritten or deleted. A hand-written sheet
  sharing the asset's name is preserved with a warning.
- Writes are content-compared first (EOL-normalized; generated output is `\n`), so
  unchanged imports never rewrite or re-import.
- Unity 6000.4 derives asset GUIDs from paths: a rename mints a fresh stylesheet
  identity no matter what (verified live - `MoveAsset` cannot preserve it), so packs
  referencing the old file need reassignment. The stale sheet is cleaned up on rename.
- Sheet reads/deletes never `File.Exists`-guard before acting: `TerminalThemeAsset.TryReadText`
  (try/catch, null on missing/unreadable) is the read; `File.Delete` no-ops for missing
  files. An exists-check before an act on the same path is a check-then-act race.
- `OnPostprocessAllAssets` batches over every imported asset: filter with
  `AssetDatabase.GetMainAssetTypeAtPath` (metadata-only) before
  `LoadAssetAtPath` (deserializes). Keep the handler allocation-light and
  exception-safe - a thrown exception aborts Unity's whole import batch.

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
- Command palette rules live in `BaseStyles.uss` under `.palette-*`. They may only consume the
  required theme variables, and each `var()` there needs a fallback value so the palette renders
  when no theme sheet is attached. `npm --prefix tooling~ run lint:theme-palette-tokens` enforces
  both contracts (and that every theme block defines the full required list).

## Editor inspection

`TerminalThemePackEditor` and `TerminalFontPackEditor` provide custom inspectors (pack contents,
available commands to ignore for terminals via `TerminalUIEditor`). When adding serialized fields
to packs, update the corresponding editor so the field stays editable and validated.

## WebGL / stripping notes

Theme packs are assets (not reflection-resolved), so they are unaffected by managed stripping;
font assets must be referenced by a pack to survive stripping in builds.
