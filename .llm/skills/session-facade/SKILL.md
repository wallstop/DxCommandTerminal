---
name: session-facade
description: Explain and preserve the DxCommandTerminal backend session nullability contract - TerminalSession.Current is never null, the Terminal facade getters (Buffer/Shell/History/AutoComplete) read null before bootstrap and after play-session reset, TerminalUI and CommandPaletteUI apply or bootstrap the session on enable, and command registration stays deferred to first use. Use when touching TerminalUI/CommandPaletteUI lifecycle, adding session consumers, writing bootstrap code, reviewing null-chain questions on Terminal accessors, or debugging why Terminal.Log or Terminal.Shell is null.
metadata:
  category: Feature
---

# Session Facade Nullability

## The nullability boundary

- `TerminalSession.Current` is NEVER null: `public static TerminalSession Current { get; } = new();` - one instance per domain, immutable property. Never write a null guard for it. `ResetState()` clears the session's backend objects, not the session.
- The facade getters are the null boundary: `Terminal.Buffer`, `Terminal.Shell`, `Terminal.History`, `Terminal.AutoComplete` read null (a) before the first `Apply` and (b) after the play-session reset. Every consumer outside a proven-ready scope guards them.
- Guard pattern: capture into a local ONCE per method and check it (`CommandLog buffer = Terminal.Buffer; if (buffer == null) { ... }`). Never mix `?.` reads with unguarded reads of the same chain in one method (PR #122 review: `Terminal.Buffer?.Logs` next to `Terminal.Buffer.Version`).
- `Terminal.Buffer?.X` / `x = Terminal.Shell` + null-check are legal here: these are plain C# objects, not UnityEngine.Object (rule 25 does not apply). The singleton COMPONENTS (`TerminalUI.Instance`, `CommandPaletteUI.Instance`) ARE Unity objects - guard them only with the Unity `== null` / `!= null` operators.

## Who applies the session, and when

- `TerminalUI.OnEnable` and `.Start` call `RefreshStaticState(force: resetStateOnInit)`: `Apply` reuses/resizes existing backends when false, recreates when true. The enabling terminal becomes `_configOwner` (newest enabled claim wins).
- `CommandPaletteUI.OnEnable` bootstraps ONLY when the session is cold (`Buffer == null`): config from its `TerminalSettings` asset, else `TerminalSession.Config.Default`. A palette NEVER reconfigures an existing session, never takes `_configOwner`/`Instance`, and never forces.
- Command registration stays deferred to first use on every path (`RunCommand`, `Commands` read, `TryComplete`, or explicit `EnsureAutoCommandsRegistered`). Do not call `EnsureReady` on enable paths - that reintroduces enable-frame discovery (readiness p95 gate). `TerminalSession.EnsureReady` exists only as an explicit headless bootstrap helper.
- `ResetForNextPlaySession` runs at `SubsystemRegistration` (before any scene Awake) and nulls the backends; the next component enable - terminal apply or palette bootstrap - recreates them. With disabled domain reload this is what clears the previous session.

## Reviewing null-chain findings

When a reviewer asks "can this accessor chain be null?": answer per link. Static singletons initialized `= new()` (`TerminalSession.Current`, `DefaultTerminalInput.Instance`, `TypeCacheCommandDiscovery.Instance`) are non-null by construction; struct properties with `?? new(...)` fallbacks (`CommandExecutionContext.Current`) too. Nullable links are the facade getters and Unity-object component instances. Sweep the whole chain, not just the flagged site: grep facade consumers in `Runtime/`, confirm each either captures-and-checks or is inside a proven-ready scope (builtin command handlers run through a live shell, so all four backends exist there).
