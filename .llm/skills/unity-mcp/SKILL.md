---
name: unity-mcp
description: Drive the host Unity editor from agents via the unity-mcp bridge - start the bridge on a deterministic per-project port, probe readiness, configure MCP servers for Claude Code/Codex/OpenCode/Nanocoder/VS Code/Cursor/Copilot, and use claude-zai or codex-zai for Z.AI subscription backends. Use when Unity MCP is unreachable, MCP configs need regenerating after .env.local edits, ports collide with multiple editors open, or setting up Z.AI model providers.
metadata:
  category: Feature
---

# Unity MCP Bridge and Agent Backends

Unity runs on the host; agents (in the devcontainer or on the host) reach it through
`scripts/mcp/unity-mcp.mjs`. Full reference: `scripts/mcp/README.md`.

## Commands

| npm script | Does |
| --- | --- |
| `npm run unity:mcp` | Host only: start the bridge (`unity-mcp.mjs bridge`). Requires `UNITY_PROJECT_PATH` in `.env.local` or `--project`. |
| `npm run unity:mcp:probe` | Discover endpoints, verify a live editor answers. |
| `npm run unity:mcp:configure` | Write all agent MCP configs (probe first). `-- --offline` writes without network. |
| `npm run unity:capture` | Capture editor/game state; see [capture-unity-state](../capture-unity-state/SKILL.md). |
| `npm run ai:backends -- install` | (Re)install the Z.AI and OpenRouter launchers. |

## Ports are project-local

The bridge defaults to a deterministic port in `27100..27999` derived from the
resolved `UNITY_PROJECT_PATH` (FNV-1a hash). Multiple open editors therefore never
collide as long as each project has its own path. Discovery probes the project port
first, then `9020`, then `9003`. Override a collision with `UNITY_MCP_BRIDGE_PORT`.
The bridge is authenticated (`UNITY_MCP_BEARER_TOKEN`, minted into `.env.local`
automatically); `GET /healthz` is the only unauthenticated endpoint.

## Multi-editor and multi-checkout rules

- One bridge per project path. Each checkout sets its own `UNITY_PROJECT_PATH` in
  `.env.local`; the same `unity-mcp` server name in each checkout then reaches that
  checkout's editor.
- Keep the bridge alive under the host's service manager (launchd on macOS) if it
  must survive terminal closure: host `node <repo>/scripts/mcp/unity-mcp.mjs bridge`.
- Backend selection: `--backend cli` (default; Unity 6 CLI `unity mcp --project-path`)
  or `--backend relay` (legacy AI Assistant under `~/.unity/relay/`). If `unity list
  --project-path <project> --format json` shows no Pipeline tools, use relay.

## configure behavior

One transaction writes seven configs with rollback and mode 0600: `.mcp.json`
(Claude), `.codex/config.toml`, `opencode.jsonc`, `.nanocoder/mcp.json`,
`.vscode/mcp.json`, `.cursor/mcp.json`, `.copilot/mcp-config.json`. All are
gitignored. Servers: `unity-mcp`, `github` (remote, PAT from
`GITHUB_TOKEN`/`GH_TOKEN`/`GITHUB_PERSONAL_ACCESS_TOKEN`/`GITHUB_PAT`), `git`,
`fetch`, `context7` (library docs), and when a Z.AI key exists (`Z_AI_API_KEY` or `ZAI_API_KEY`):
`web-search-prime`, `web-reader`, `zread`, `zai-mcp-server` (vision). No key -> the
four Z.AI entries are removed. A running bridge that rejected the token aborts
configure before writing anything: copy `UNITY_MCP_BEARER_TOKEN` from the host
`.env.local` instead of minting a second one. After editing `.env.local`, run
`npm run unity:mcp:configure -- --offline`, then reconnect MCP or restart agents.

## OpenRouter backends (API key, any model)

`ai-backends.sh install` also creates `codex-openrouter` and `claude-openrouter`.
Key: `OPENROUTER_API_KEY` in the environment or `.env.local` (resolved as data).
- `claude-openrouter`: base URL `https://openrouter.ai/api` (Anthropic Messages
  compat), `ANTHROPIC_API_KEY` explicitly empty, model slots default to
  `anthropic/claude-sonnet-5` / `claude-opus-5` / `claude-haiku-4.5` /
  `claude-fable-5.1` via `CLAUDE_OPENROUTER_{SONNET,OPUS,HAIKU,FABLE}_MODEL`,
  subagents via `CLAUDE_OPENROUTER_SUBAGENT_MODEL`, gateway model picker on by
  default (`CLAUDE_OPENROUTER_GATEWAY_DISCOVERY=0` to disable).
- `codex-openrouter`: profile `devcontainer-openrouter`, Responses API
  (`https://openrouter.ai/api/v1`, `wire_api = "responses"` - modern Codex has no
  chat wire), default `openai/gpt-5.6-sol` via `CODEX_OPENROUTER_MODEL`.

## Z.AI backends (subscription)

`.devcontainer/ai-backends.sh install` creates `codex-zai` and `claude-zai`
launchers without touching native `claude`/`codex` logins or configs:

- `claude-zai`: isolated `CLAUDE_CONFIG_DIR` (`~/.claude-zai` default), base URL
  `https://api.z.ai/api/anthropic`, models `glm-5.3[1m]`/`glm-5.3-flash[1m]`,
  container-aware sandbox handling. Overrides: `CLAUDE_ZAI_{SONNET,OPUS,HAIKU}_MODEL`,
  `CLAUDE_ZAI_CONFIG_DIR`, `ZAI_API_TIMEOUT_MS`, `AI_BACKENDS_CONTAINER_MODE`,
  `CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB`.
- `codex-zai`: profile `devcontainer-zai` + model catalog in `CODEX_HOME`, Responses
  endpoint `https://api.z.ai/api/v1`, model `glm-5.3` (override `CODEX_ZAI_MODEL`,
  `CODEX_ZAI_REASONING_EFFORT=low|high|max`). The key is exported to codex but
  filtered from MCP subprocesses via `shell_environment_policy`.

Set the key only in `.env.local` or your shell/secret store; never in generated
files or CLI arguments.

## Troubleshooting

- `probe` unreachable: is the bridge running on the host (`npm run unity:mcp`)? Is
  `UNITY_PROJECT_PATH` set? In the container, `host.docker.internal` must resolve
  (devcontainer.json adds it via `--add-host`).
- `unauthorized` on probe: token mismatch between host and container `.env.local`.
- Empty tools list from the cli backend: Pipeline is not loaded in that editor;
  check compilation conflicts, or fall back to `--backend relay`.
- Agent cannot see Z.AI servers: no key in `.env.local`, or configs not rewritten
  since the key was added (re-run offline configure).
