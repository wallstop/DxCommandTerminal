# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## Unreleased

### Added

- Deferred first-use command registration: `CommandShell.InitializeAutoRegisteredCommands` accepts a `deferRegistration` flag. The terminal applies its ignored/default command configuration when enabled and defers the discovery scan and delegate materialization to the first command request, the first read of `CommandShell.Commands`, or an explicit `CommandShell.EnsureAutoCommandsRegistered` call. `CommandShell.AutoCommandsRegistered` reports whether registration has been applied. Clearing auto commands cancels a pending registration. Note that `CommandShell.AutoRegisteredCommands` reflects only applied registration, so it stays empty until first use for deferred shells.
- Source-generated command registration: the package now ships a Roslyn source generator that emits an internal `CommandCatalog` into every assembly declaring `[RegisterCommand]` methods. `CommandShell` binds those catalogs without walking assembly types; assemblies without a catalog (precompiled DLLs, analyzers unavailable) fall back to the previous reflection discovery with identical results. The public `CommandShell.RegisteredCommands` surface is unchanged.

### Changed

- `TerminalUI` no longer runs command discovery on its enable frame. Commands register at the first command request (typically the first command run or completion query), bounded by the same discovery path measured at 1.5-1.8 ms for 25 commands in the test project. The terminal re-applies its command configuration on every refresh, so auto commands cleared through `CommandShell.ClearAutoRegisteredCommands` return on the next first use after a terminal enable, instead of the previous enable-time registration.
- Registering a static command whose signature is valid but not bindable (for example a generic method definition, a non-void handler, or a method inside an open generic type) now logs a contained error instead of aborting shell initialization, matching the catalog path's handling of rejected signatures.
- An assembly holding commands inside private nested or file-local classes gets no generated catalog; the shell falls back to reflection for that assembly so no command is lost.

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
