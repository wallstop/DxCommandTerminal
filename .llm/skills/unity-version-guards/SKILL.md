---
name: unity-version-guards
description: Choose correct UNITY_X_Y_OR_NEWER version guards for Unity APIs in DxCommandTerminal - verify an API's true introduction and deprecation versions against per-version Unity docs, keep deprecated-API fallbacks (the Unity 6 GetInstanceID/GetEntityId entity-id migration) compiling and warning-free on every supported editor, and sweep for guard mismatches. Use when adding or reviewing #if UNITY_ version guards, calling Unity 6 entity-id APIs, or fixing compile breaks or CS0618 deprecation warnings on supported Unity versions.
metadata:
  category: Feature
---

# Unity Version Guards

## Rule

A `#if UNITY_X_Y_OR_NEWER` constant is the API's true introduction version,
verified against evidence - never the version of the editor you happen to run.
Too low breaks older editors; too high silently degrades behavior (or emits
deprecation warnings) on editors that already have the newer API.

## Verify before guarding

1. Per-version docs bisect: `https://docs.unity3d.com/{ver}/Documentation/ScriptReference/{Page}.html`.
   A 404 means the API does not exist in that version; the surviving page's
   "Added in Version" marker confirms. This is the authority when release
   notes are ambiguous.
2. Deprecation facts on the pinned host (Unity MCP `eval`): reflect over the
   method and read `ObsoleteAttribute` (`IsError` false = compile warning).
3. Known entity-id timeline (verified 2026-09, PR #138 review):
   - `Object.GetEntityId()` / `EntityId`-backed sorting: introduced **6000.4**
     (docs 404 for 6000.0-6000.3).
   - `GetInstanceID()`: deprecated starting **6000.4** (non-error CS0618,
     "use GetEntityId"); warning-free on 2021.3-6000.3.
   - `Object.FindObjectsByType` with `FindObjectsSortMode`: 2022.2+; the
     no-sort overload is documented from 6000.4.
   So a `GetInstanceID` fallback must live in the `#else` of a
   `UNITY_6000_4_OR_NEWER` guard - compiled out exactly where it warns.

## Sweep (same class of issue)

- Grep the guard constant and the deprecated/new API name together; every
  deprecated call site must sit inside the `#else` of a guard at or above the
  deprecation version, and every guarded-API reference (including private
  fields!) must sit inside a matching guard block - an unguarded field
  declaration of a new-version type breaks older editors even when all uses
  are guarded (the session-062 regression).
- Watch for inconsistent constants across files guarding the same API.

## Tripwires

- `npm run compat:check` compiles the real Runtime sources against UnityEngine
  2021.3.33 reference assemblies (tooling~/compat, warnings-as-errors):
  catches member-level leaks (CS1061, CS0200) below the minimum editor -
  the failure class the docs build cannot see. It compiles the
  legacy-input project profile (`ENABLE_LEGACY_INPUT_MANAGER`); the
  new-input profile needs a Unity.InputSystem reference assembly nuget
  does not carry (T13's real-editor matrix covers it).
- `npm run docs:build` compiles Runtime sources against UnityEngine
  2021.3.33 reference assemblies (tooling~/docs): catches type-level leaks
  below the minimum editor. It suppresses member-level errors (CS1061), so
  it is a type-level tripwire only.
- After compiling on the pinned host, check the console for CS0618 warnings;
  the repo's zero-warnings rule makes any deprecation warning a failure.
- After compiling on the pinned host, check the console for CS0618 warnings;
  the repo's zero-warnings rule makes any deprecation warning a failure.
