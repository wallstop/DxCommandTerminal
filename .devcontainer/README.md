# DxCommandTerminal Dev Container

A fast, reliable VS Code devcontainer for this Unity UPM package. The Unity Editor
stays on the **host** (licensing + GUI); the container ships every CLI tool, drives the
host editor through the [unity-mcp bridge](../tooling~/scripts/mcp/README.md), and needs **zero
sudo** anywhere.

```
┌──────────────────────────── macOS / Windows host ────────────────────────────┐
│  Unity Editor (host project)          npm run unity:mcp (bridge, host)      │
│   └─ com.wallstop-studios.             └─ HTTP :27xxx (bearer auth)          │
│      dxcommandterminal package              ▲                                │
├─────────────────────────────────────────────│────────────────────────────────┤
│  Dev container (this repo is the workspace) │                                │
│   claude · codex · opencode · nanocoder     │                                │
│   claude-zai · codex-zai (Z.AI plans)       │                                │
│   claude-openrouter · codex-openrouter      │                                │
│   MCP: unity-mcp · github · git · fetch ────┘                                │
│        web-search-prime · web-reader · zread · zai-mcp-server (Z.AI)         │
└──────────────────────────────────────────────────────────────────────────────┘
```

## Quick start

1. Open this repository in VS Code and choose **Reopen in Container** (or
   `> Dev Containers: Reopen in Container`). First build installs everything;
   later opens attach in seconds from cache.
2. Create `.env.local` at the repository root (gitignored, mode 0600 on generated
   configs):

   ```dotenv
   # Z.AI subscription key (GLM coding plan). ZAI_API_KEY also works.
   Z_AI_API_KEY=your_zai_key
   # GitHub PAT for the github MCP server (or set GITHUB_TOKEN on the host).
   GITHUB_PERSONAL_ACCESS_TOKEN=your_github_token
   # Absolute host path to the Unity project that embeds this package.
   UNITY_PROJECT_PATH=/absolute/host/project/path
   ```

3. On the **host**, with the Unity project open, start the bridge (deterministic
   per-project port, so multiple editors never collide):

   ```bash
   npm --prefix tooling~ install   # host checkout of this repo (one time)
   npm run unity:mcp  # bridge: host editor -> authenticated HTTP
   ```

4. Back in the container:

   ```bash
   npm run unity:mcp:probe   # readiness check (editor answered)
   npm run unity:capture     # capture editor/game state into .artifacts/
   claude-zai                # Claude Code on the Z.AI subscription
   codex-zai                 # Codex on the Z.AI subscription
   claude-openrouter         # Claude Code on any OpenRouter model
   codex-openrouter          # Codex on any OpenRouter model
   ```

## What the image bakes in

| Layer | Contents |
| --- | --- |
| Base | `mcr.microsoft.com/devcontainers/dotnet:1-9.0-bookworm` + .NET 10 side-by-side (C# Dev Kit) |
| Repo tooling | PowerShell (lint scripts), CSharpier 1.1.2, pre-commit, yamllint, git-lfs |
| Node LTS | NodeSource LTS; user-global npm prefix is `~/.local` (no sudo, ever) |
| Agent CLIs | `@anthropic-ai/claude-code`, `@openai/codex`, `@opencode/cli` v2, `@nanocollective/nanocoder` (configured npm lines; refreshed on every start) |
| MCP servers | `mcp-server-git`, `mcp-server-fetch` (uv), `@z_ai/mcp-server` (vision), `@upstash/context7-mcp` (docs), `mcp-remote` (Z.AI↔Codex adapter), baked `@modelcontextprotocol/sdk` + `smol-toml` + `jsonc-parser` under `/opt/dxt-mcp` |

Offline launches keep working: configure uses the baked dependencies, and the
image copies of the CLIs remain until a refresh succeeds.

## Lifecycle

| Hook | Script | Work |
| --- | --- | --- |
| `updateContentCommand` | `post-start.sh --prepare` | Repair cache ownership, configure MCP offline, then allow attach |
| `postCreateCommand` | `post-create.sh` | npm prefix, agent CLI refresh, `dotnet tool restore`, `npm install`, MCP configure, Z.AI launcher install, pre-commit, welcome panel |
| `postStartCommand` / `postAttachCommand` | `post-start.sh` | Ownership repair, offline MCP configure, background CLI refresh to configured npm lines |

Logs for the background refresh: `/tmp/dxt-agent-cli-refresh.log`.

## No sudo, by construction

- npm global prefix is `/home/vscode/.local`, first on `PATH`.
- `cache-contract.sh` repairs volume ownership before anything runs, so `npm
  install` (workspace **and** `-g`) always works as `vscode`. If a file is still
  unwritable, the scripts fail loudly with its path instead of suggesting sudo.
- The one permitted `sudo -n` use is `chown` of files that earlier `sudo npm`
  runs broke; it is non-interactive and cannot be prompted.

## Credentials

`.env.local` (gitignored) is the preferred source; process environment wins over
the file; empty forwarded variables never hide file values. Supported aliases:

- GitHub: `GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_PERSONAL_ACCESS_TOKEN`, `GITHUB_PAT`
- Z.AI: `Z_AI_API_KEY`, `ZAI_API_KEY`
- OpenRouter: `OPENROUTER_API_KEY`
- Unity bridge: `UNITY_PROJECT_PATH`, `UNITY_MCP_BRIDGE_PORT`, `UNITY_MCP_BEARER_TOKEN`

Every launcher reads `.env.local` (parsed as data, never sourced), so a key
placed in the file works for `claude-zai`, `codex-zai`, `claude-openrouter`,
`codex-openrouter`, and the MCP config generator alike.

**Automatic shell loading:** post-create installs a guarded block into
`~/.bashrc` and `~/.profile` that sources `.devcontainer/env-autoload.sh`,
which evaluates the managed exports in every new interactive shell. Native
agents (`claude`, `codex`, `opencode` — opencode's Z.AI Coding Plan provider
reads `ZHIPU_API_KEY`, which the loader mirrors automatically), `nanocoder`,
`gh`, and MCP tooling therefore pick up `.env.local` credentials with no manual
step. Non-interactive contexts can still evaluate the exports explicitly:

```bash
eval "$(bash .devcontainer/ai-backends.sh env)"
```

The loader prints one `export KEY='value'` line per credential found
(single-quote escaped) and omits keys absent from both the environment and
`.env.local`.

`configure --offline` writes all seven client configs (Claude Code, Codex,
OpenCode, Nanocoder, VS Code, Cursor, Copilot CLI) in one transaction with
rollback, mode 0600. Generated configs are gitignored. The OpenCode v2 config
uses native `mcp.servers` entries, enables Code Mode, and registers the shared
`.llm/skills` catalog. It also converts existing v1 MCP and skill fields without
replacing explicit values. Session `share` stays `disabled` so transcripts never
sync to a public URL. After editing `.env.local`, run
`npm run unity:mcp:configure -- --offline` and restart the agents' MCP
connections.

## Alternate agent backends (`claude-zai` / `codex-zai` / `claude-openrouter` / `codex-openrouter`)

`.devcontainer/ai-backends.sh install` installs process-scoped launchers that never
touch the native `claude`/`codex` defaults:

- `claude-zai` — isolated `CLAUDE_CONFIG_DIR`, `ANTHROPIC_BASE_URL=https://api.z.ai/api/anthropic`,
  model defaults `glm-5.3[1m]` / `glm-5.3-flash[1m]`, sandbox handling tuned for
  running inside (sandbox off, container is the boundary) vs outside (scrubber on).
  `ZAI_API_KEY`/`Z_AI_API_KEY` resolve from the environment or `.env.local`.
- `codex-zai` — dedicated Codex profile + model catalog (`glm-5.3`, Responses API
  `https://api.z.ai/api/v1`), shell environment filtered so the key never reaches
  subprocesses or stdio MCP servers.

Z.AI overrides: `CODEX_ZAI_MODEL`, `CODEX_ZAI_REASONING_EFFORT`, `CLAUDE_ZAI_*_MODEL`,
`ZAI_API_TIMEOUT_MS` (default 3000000, matching the GLM coding-plan guidance),
`CLAUDE_ZAI_CONFIG_DIR`, `AI_BACKENDS_CONTAINER_MODE`,
`CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB`. The launchers are regression-tested
(`tooling~/scripts/tests/test-ai-backends.sh`).

### OpenRouter (API key, any model)

Put `OPENROUTER_API_KEY` in the environment or `.env.local` (the launcher parses
`.env.local` as data), then:

- `claude-openrouter` — OpenRouter's Anthropic Messages-compatible endpoint
  (`ANTHROPIC_BASE_URL=https://openrouter.ai/api`, `ANTHROPIC_API_KEY` explicitly
  empty per OpenRouter's Claude Code guide, gateway model picker enabled). Model
  slots default to `anthropic/claude-sonnet-5` (sonnet), `anthropic/claude-opus-5`
  (opus), `anthropic/claude-haiku-4.5` (haiku), `anthropic/claude-fable-5.1`
  (fable) and `anthropic/claude-sonnet-5` (subagents); override with
  `CLAUDE_OPENROUTER_{SONNET,OPUS,HAIKU,FABLE,SUBAGENT}_MODEL` — any OpenRouter
  slug works.
- `codex-openrouter` — Codex profile `devcontainer-openrouter` against
  OpenRouter's Responses API (`https://openrouter.ai/api/v1`, `wire_api="responses"`;
  chat was removed in codex 0.15x). Authentication uses a command-backed auth
  block (per OpenRouter's Codex guide) so Codex fetches the live model catalog —
  `env_key` would run non-OpenAI models on fallback metadata. The key still
  never reaches agent subprocesses (shell environment filter). Default model
  `openai/gpt-5.6-sol`; override with `CODEX_OPENROUTER_MODEL`.

More overrides: `OPENROUTER_API_TIMEOUT_MS`, `CLAUDE_OPENROUTER_CONFIG_DIR`,
`CLAUDE_OPENROUTER_GATEWAY_DISCOVERY` (0/1), `CLAUDE_OPENROUTER_SUBPROCESS_ENV_SCRUB`,
`AI_BACKENDS_CONTAINER_MODE`.

## Caches and mounts

Deterministic named volumes survive full rebuilds: NuGet, dotnet tools, PowerShell
modules, pip, npm cache, and the Linux `node_modules` tree. The enclosing Unity
project is bind-mounted read-write at `/unity-project` so agents can inspect host
code and capture tooling can be installed into `Assets/Editor`. It is excluded
from workspace watching; the workspace itself remains the package repository.

## Verify

```bash
npm test                                   # node --test tooling~/scripts/mcp/__tests__
bash tooling~/scripts/tests/test-ai-backends.sh     # Z.AI launcher regression suite
npm run unity:mcp:probe                    # host editor readiness (bridge running)
```
