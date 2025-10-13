# Repository Guidelines

## Project Structure & Module Organization

- `Runtime/` — core C# code (`WallstopStudios.DxCommandTerminal.asmdef`).
- `Editor/` — editor tooling and drawers (`*.Editor.asmdef`).
- `Tests/Runtime/` — Unity Test Framework (NUnit) tests (`*Tests.cs`, `*.asmdef`).
- `Styles/` — UI Toolkit styles (`.uss`) and theme assets (`.tss`).
- `Fonts/`, `Media/`, `Packs/` — assets used by the terminal.
- `package.json` — Unity package manifest + npm metadata. See `README.md` for usage.

## Build, Test, and Development Commands

- Install tools: `dotnet tool restore`
- Format C#: `dotnet tool run csharpier .`
- Optional hooks: install pre-commit — `pre-commit install`
- Unity tests (CLI example, Windows):
  `"C:\\Program Files\\Unity\\Hub\\Editor\\2021.3.x\\Editor\\Unity.exe" -batchmode -projectPath <your-unity-project> -runTests -testPlatform playmode -assemblyNames WallstopStudios.DxCommandTerminal.Tests.Runtime -logfile - -quit`
  Or use Unity’s Test Runner UI.

## Coding Style & Naming Conventions

- Follow `.editorconfig`:
  - Indentation: spaces (C# 4), JSON/YAML/asmdef 2.
  - Line endings: CRLF; encoding: UTF-8 BOM.
  - C#: prefer braces; explicit types over `var` unless obvious; `using` inside namespace.
  - Naming: Interfaces `IType`, type params `TType`, events/types/methods PascalCase; tests end with `Tests`.
- Use CSharpier for formatting before committing.
- Do not use underscores in function names, especially test function names.
- Do not use regions, anywhere, ever.

## Testing Guidelines

- Framework: Unity Test Framework (NUnit) under `Tests/Runtime`.
- Conventions: file names `*Tests.cs`, one feature per fixture, deterministic tests.
- Run via Unity CLI (above) or Test Runner. Add/adjust tests when changing parsing, history, UI behavior, or input handling.
- Do not use regions.
- Try to use minimal comments and instead rely on expressive naming conventions and assertions.
- Do not use Description annotations for tests.
- Do not create `async Task` test methods - the Unity test runner does not support this. Make do with `IEnumerator` based UnityTestMethods.
- Do not use `Assert.ThrowsAsync`, it does not exist.

## Commit & Pull Request Guidelines

- Commits: imperative, concise subject (≤72 chars), explain “what/why”. Link issues/PRs (`#123`).
- PRs: include description, motivation, and test coverage. For UI/USS changes, add before/after screenshots.
- Versioning/CI: do not change `package.json` version unless preparing a release; npm publishing is automated via GitHub Actions on version bumps.

## Security & Configuration Tips

- No secrets in repo; publishing uses `NPM_TOKEN` in GitHub secrets.
- Target Unity `2021.3+`. Keep `asmdef` names and folder layout intact to preserve assembly boundaries.
