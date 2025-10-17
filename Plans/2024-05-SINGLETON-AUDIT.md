# Singleton Audit Notes

## Candidates to Replace or Wrap

- `TerminalUI.Instance` – now backed by `ITerminalProvider`; further work: remove direct usages in tests and utilities.
- `Terminal.ActiveRuntime` – consider exposing through provider or runtime service locator.
- `DefaultTerminalInput.Instance` – expose through `ITerminalInputProvider` when wiring `TerminalUI`.
- `TerminalRuntimeConfig` static helpers – evaluate whether configuration can be injected per terminal or via ScriptableObject references.

## Follow-up Ideas

- Provide onboarding docs demonstrating how to swap out the provider in multi-terminal scenes.
- Add editor diagnostics to highlight when multiple terminals are active and which one is considered primary.

- Added ITerminalRuntimeConfigurator to make runtime config injectable; remaining statics to evaluate.