# Unity MCP tooling

`unity-mcp.mjs` is the single entry point for agent access to the host Unity editor:

```
node tooling~/scripts/mcp/unity-mcp.mjs <probe|configure|bridge|install-capture|capture> [options]
```

Unity stays on the host. The `bridge` command (host) exposes the editor through an
authenticated HTTP MCP endpoint; `configure`/`probe`/`capture` (container or host)
talk to that endpoint. See `.devcontainer/README.md` for the full picture and
[unity-mcp](../../.llm/skills/unity-mcp/SKILL.md) for the agent workflow.

## Commands

| Command | Where | Behavior |
| --- | --- | --- |
| `bridge` | host | Serve Unity over MCP HTTP. Needs `--project` or `UNITY_PROJECT_PATH`. `--backend cli` (Unity 6 CLI) or `relay` (legacy Assistant). |
| `probe` | either | Discover endpoints; verify a live editor answers (`editor_status` / `Unity_ManageEditor`). |
| `configure` | either | Probe (unless `--offline`) then write all agent MCP configs in one transaction. |
| `install-capture` | host | Copy `DxTerminalStateCapture.cs.txt` to `<project>/Assets/Editor/` (backs up prior copies). |
| `capture` | either | Probe, install when possible, refresh, invoke capture, poll to completion. |

## Ports and discovery

- The bridge binds `0.0.0.0` and defaults to a **deterministic per-project port**:
  FNV-1a of the resolved project path mapped into `27100..27999`. Several editors
  on one host never collide as long as each project has its own path. Override
  with `UNITY_MCP_BRIDGE_PORT` or `--port`.
- Discovery probes, in order: the project port, `9020`, `9003`, across
  `host.docker.internal`, `127.0.0.1`, WSL nameservers, and Linux gateways.
  `--host`/`--port` restrict probing to one endpoint; `--no-discover` skips fallbacks.
- Auth: every MCP request needs `Authorization: Bearer <UNITY_MCP_BEARER_TOKEN>`.
  The token is minted into `.env.local` on first bridge start. `GET /healthz` is
  the only unauthenticated endpoint (liveness only).

## Credentials

`.env.local` at the repository root, parsed as data (never sourced). Process env
beats the file; empty env vars are ignored. Aliases: `GITHUB_TOKEN`, `GH_TOKEN`,
`GITHUB_PERSONAL_ACCESS_TOKEN`, `GITHUB_PAT`; `Z_AI_API_KEY`, `ZAI_API_KEY`;
`UNITY_PROJECT_PATH`, `UNITY_PROJECT_CONTAINER_PATH`, `UNITY_MCP_BEARER_TOKEN`.
`UNITY_PROJECT_PATH` stays the host identity the bridge needs for the port and
the capture output path. `UNITY_PROJECT_CONTAINER_PATH` (container only) is the
project directory this process can read and write, used to install the capture
script and to pick the artifact layout.

## Generated client configs

| Client | File | Collection |
| --- | --- | --- |
| Claude Code | `.mcp.json` | `mcpServers` |
| Codex | `.codex/config.toml` | `mcp_servers` |
| OpenCode | `opencode.jsonc` | `mcp.servers` |
| Nanocoder | `.nanocoder/mcp.json` | `mcpServers` |
| VS Code / Copilot Chat | `.vscode/mcp.json` | `servers` |
| Cursor | `.cursor/mcp.json` | `mcpServers` |
| Copilot CLI | `.copilot/mcp-config.json` | `mcpServers` |

All seven are written as one transaction with rollback, mode `0600`, and are
gitignored. Unrelated keys and servers survive; malformed files abort configure
before anything is written. OpenCode receives the published schema, native v2
`mcp.servers` entries, a 30-second catalog timeout, a 300-second execution
timeout, Code Mode, and the shared `.llm/skills` catalog. The execution timeout
is raised because a Play Mode suite over this bridge outlasts the 30-second
default. Generated OpenCode credentials use `{env:NAME}` references.
`GITHUB_TOKEN` and `ZAI_API_KEY` are the canonical names; the devcontainer
lifecycle and interactive shell normalize accepted aliases before OpenCode
starts, and `configure` names any reference it cannot resolve. Existing v1 MCP
fields and skill sources convert in place. The catalog also includes `context7`
(library docs, `@upstash/context7-mcp`). Z.AI remote servers go through
`mcp-remote` for Codex only (its HTTP client rejects Z.AI's empty 200s on
`initialized` notifications); the key travels via the child's
`ZAI_AUTH_HEADER` env var, never argv.

## State capture

`install-capture` + `capture` automate the DxMessaging-style observation loop with
`DxTerminalStateCapture`: hierarchy, console ring buffer, terminal buffer, passive
1 Hz editor-state snapshots, and play-mode Game View screenshots, written under
`<project>/Packages/com.wallstop-studios.dxcommandterminal/.artifacts/unity-state/`.
Details: [capture-unity-state](../../.llm/skills/capture-unity-state/SKILL.md).

## Tests

```bash
npm test   # node --test tooling~/scripts/mcp/__tests__/ (dotenv, ports, configs, capture paths)
```
