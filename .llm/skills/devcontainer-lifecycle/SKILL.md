---
name: devcontainer-lifecycle
description: Work on the .devcontainer agent environment - OpenCode v2 readiness, credential loading and scrubbing, host versus container Unity project paths, and the lifecycle shell scripts and their tests. Use when editing .devcontainer/*.sh, .env.example, or the devcontainer contract tests, when a lifecycle hook fails or hangs, when credentials leak or go missing for an agent CLI, or when a test passes in CI but fails inside the container.
metadata:
  category: Core
---

# Devcontainer Lifecycle and Credential Handling

The devcontainer is the agent environment for this repository. Its scripts run as
VS Code lifecycle hooks, so a bug here breaks every developer, not the package.
Full reference: `.devcontainer/README.md`. Contract test:
`tooling~/scripts/tests/test-devcontainer.sh`.

## The container deliberately exports credentials

`containerEnv` forwards `GITHUB_TOKEN`, `Z_AI_API_KEY`, `OPENROUTER_API_KEY`,
`UNITY_MCP_BEARER_TOKEN`, and `UNITY_MCP_BRIDGE_*` into every process. Anything
that runs inside the container, including a test, inherits them.

Consequences that have each caused a real failure:

- A test asserting a value that came from `.env.local` silently reads the ambient
  value instead. Unset every credential in the test's own environment.
- `.env.local` is shared with the host. A container-only variable placed there
  breaks host-side tooling; keep those in `containerEnv` only.
- Process environment wins over `.env.local` by design, so an ambient value masks
  the file everywhere. Never "fix" that by reordering.

## Credential injection rules

1. **Never set `BASH_ENV`.** It loads a file into every non-interactive Bash
   subprocess. The loader is sourced explicitly by the lifecycle scripts instead.
2. **Append the autoload block to the rc file; never prepend it.** Ubuntu's
   `~/.bashrc` returns early when the shell is not interactive. A credential block
   above that guard hands secrets to every process that sources the file. The
   block must be the last thing in the file, after any guard.
3. **Provider launchers scrub before exec.** `prepare_scrubbed_agent_environment`
   sets `DXT_ENV_AUTOLOAD_DISABLED=1` and `BASH_ENV=/dev/null` so a child agent
   cannot re-import the keys its launcher is about to replace.
4. **Generated MCP configs reference `{env:NAME}`; they never contain a literal
   token.** `configure` warns and names any reference it cannot resolve, so a user
   holding only an alias learns the canonical name instead of failing at request
   time.

## Fail-closed readiness

`verify-opencode.sh` requires OpenCode v2 and fails when absent. `post-start.sh`
runs it before anything else and `post-create.sh` aborts when it fails. Both must
stay fail-closed: a container that comes up with a v1 CLI is the failure this
guards. The `opencode2` alias is verified only when present, because non-npm
installs do not ship it.

A background OpenCode service outlives the shell that started it, so `post-start`
restarts it to hand over current credentials. Two rules govern that probe:

- **Scope every check to the row it is about.** Read the `unity-mcp` line out of
  `opencode mcp list` before testing it for an auth failure. Matching the whole
  output stops a healthy service because some other server returned 401.
- **Only destroy state on positive evidence.** Stop the service when the
  unity-mcp row is explicitly rejected. Unrecognized or empty output is reported
  and left running; a reworded log line must never cost the user their service.

## Host path versus container path

`UNITY_PROJECT_PATH` is the identity the host Unity editor and the bridge need:
the port hash and the capture output path. It usually does not exist in the
container. `UNITY_PROJECT_CONTAINER_PATH` (default `/unity-project`) is the
directory this process can read and write.

- Local writes - installing the capture script, probing which artifact layout
  applies - use the container path.
- Anything handed to Unity as a string to open - the capture output directory -
  uses the host path.

Probing the layout on a path no local filesystem can see always answers "no",
which silently selects the `Library/` fallback. Prove both paths in one run:

```sh
npm run unity:capture -- --project-container /unity-project
```

## Testing rules for this environment

The container is the hostile case, not the clean one. `test-devcontainer.sh`
scrubs credentials, `BASH_ENV`, and the loader's own `DXT_*` control variables,
because an ambient value changes what the lifecycle does. Keep that list complete
when adding a new control variable, and forward test-only flags explicitly
instead of scrubbing them.

- Keep stubs on a PATH without the host's real CLIs; a leaked `opencode2` makes
  the verifier fail for a reason the test did not intend.
- Assert behavior, not the script's text. Where a source grep remains (for
  example, that a forbidden pattern is absent), keep it minimal and anchored.
- Prove a new assertion has teeth: reintroduce the bug in a scratch copy and
  confirm the suite fails, then restore.
- Syntax-check every script: `bash -n a b` checks `a` and passes `b` as
  arguments. Loop over the files instead.
- The image build is not covered by any test. See the open issue before claiming
  the Dockerfile works.
