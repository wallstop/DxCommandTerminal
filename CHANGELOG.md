# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## Unreleased

### Added

- Source-generated command registration: the package now ships a Roslyn source generator that emits an internal `CommandCatalog` into every assembly declaring `[RegisterCommand]` methods. `CommandShell` binds those catalogs without walking assembly types; assemblies without a catalog (precompiled DLLs, analyzers unavailable) fall back to the previous reflection discovery with identical results. The public `CommandShell.RegisteredCommands` surface is unchanged.

### Changed

- Registering a static command whose signature is valid but not bindable (for example a generic method definition or a method inside an open generic type) now logs a contained error instead of aborting shell initialization, matching the catalog path's handling of rejected signatures.

## [1.0.0-rc25.0] - 2026-03-10

### Added

- Per-command history opt-out: set `AddToHistory = false` on `RegisterCommandAttribute` to prevent a command from being recorded in history. Also available as the `addToHistory` parameter and field on `CommandInfo` and the `addToHistory` parameter on `CommandShell.AddCommand` (all defaulting to `true`).
- `ReadOnlyHashSet<T>` and `ReadOnlyHashSetExtensions` (with `ToReadOnlyHashSet` extension method) in `WallstopStudios.DxCommandTerminal.DataStructures`.

### Changed

- `clear-history` command no longer records itself in command history.
- `CommandShell.AutoRegisteredCommands` and `CommandShell.IgnoredCommands` changed type from `ImmutableHashSet<string>` to `ReadOnlyHashSet<string>`.

### Removed

- Bundled `System.Collections.Immutable.dll` has been removed; projects that already reference `System.Collections.Immutable` (e.g. via NuGet or another package) will no longer experience DLL conflicts.

### Fixed

- Setting `ignoreDefaultCommands` to `true` on `TerminalUI` had no effect — built-in commands were still registered. Root cause: the `RegisterCommandAttribute` internal constructor was not propagating the `isDefault` parameter to the `Default` property.
