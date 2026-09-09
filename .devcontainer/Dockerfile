# syntax=docker/dockerfile:1.4

# =============================================================================
# DxCommandTerminal Development Container
# =============================================================================
# Base: .NET 9.0 on Debian Bookworm (Unity UPM package development; the Unity
# Editor itself stays on the host and is driven through the unity-mcp bridge).
# Design goals: fast reliable opens, zero sudo for the container user, MCP
# dependencies baked into the image so agent configuration works offline.
# =============================================================================
FROM mcr.microsoft.com/devcontainers/dotnet:1-9.0-bookworm

# BuildKit's automatic target architecture (amd64 or arm64). No default: it
# would shadow the native target on ARM64.
ARG TARGETARCH

LABEL org.opencontainers.image.source="https://github.com/wallstop/DxCommandTerminal"
LABEL org.opencontainers.image.description="DxCommandTerminal development container"
LABEL org.opencontainers.image.licenses="MIT"
LABEL unity.note="Unity runs on the host; the container drives it via the unity-mcp bridge"

ENV DEBIAN_FRONTEND=noninteractive
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1

# ------------------------------------------------------------------------------
# APT: fix expired Yarn key, then install essentials (lean for fast builds)
# ------------------------------------------------------------------------------
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    rm -f /etc/apt/sources.list.d/yarn.list \
    && rm -f /usr/share/keyrings/yarn-keyring.gpg \
    && rm -f /etc/apt/keyrings/yarn.gpg \
    && rm -f /usr/share/keyrings/yarn.gpg \
    && rm -f /etc/apt/trusted.gpg.d/yarn.gpg \
    && find /etc/apt/sources.list.d/ -name "*yarn*" -delete || true; \
    apt-get update && apt-get install -y --no-install-recommends \
        ripgrep \
        jq \
        tree \
        htop \
        ncdu \
        wget \
        curl \
        ca-certificates \
        unzip \
        shellcheck \
        git-lfs \
        fd-find \
        bat \
        fzf \
        python3 \
        python3-pip \
        python3-venv \
    && ln -sf /usr/bin/fdfind /usr/local/bin/fd \
    && ln -sf /usr/bin/batcat /usr/local/bin/bat \
    && apt-get autoremove -y

# ------------------------------------------------------------------------------
# .NET 10 SDK side-by-side (required by the C# Dev Kit extension). The base
# image's 9.0 SDK stays primary for the repo's CSharpier tool manifest.
# ------------------------------------------------------------------------------
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh

# ------------------------------------------------------------------------------
# PowerShell: the repository's lint/skill scripts are PowerShell.
# ------------------------------------------------------------------------------
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    wget -q https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb \
    && dpkg -i /tmp/packages-microsoft-prod.deb \
    && rm /tmp/packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends powershell \
    && apt-get autoremove -y \
    && pwsh --version

# ------------------------------------------------------------------------------
# Git: Unity-friendly defaults + LFS
# ------------------------------------------------------------------------------
RUN git config --system core.longpaths true \
    && git config --system core.preloadindex true \
    && git config --system gc.auto 256 \
    && git config --system init.defaultBranch master \
    && git config --system lfs.concurrenttransfers 10

# ------------------------------------------------------------------------------
# Node.js LTS via NodeSource. Global npm installs happen either as root into
# /usr/local (image fallback) or as vscode into ~/.local (lifecycle refresh);
# the user workspace prefix makes plain `npm install -g` work without sudo.
# ------------------------------------------------------------------------------
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    curl -fsSL https://deb.nodesource.com/setup_lts.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && apt-get autoremove -y \
    && node --version \
    && npm --version

# ------------------------------------------------------------------------------
# Agent CLIs + MCP adapters baked into the image. The user-scoped lifecycle
# refresh moves them to npm's latest tags; these copies keep offline launches
# working.
# ------------------------------------------------------------------------------
RUN npm install --global --prefix /usr/local \
        @openai/codex@latest \
        opencode-ai@latest \
        @nanocollective/nanocoder@latest \
        @anthropic-ai/claude-code@latest \
        @z_ai/mcp-server@latest \
        @upstash/context7-mcp@latest \
        mcp-remote@latest \
        --allow-scripts=opencode-ai,@anthropic-ai/claude-code \
        --no-fund \
        --no-audit \
    && claude --version \
    && codex --version \
    && opencode --version \
    && nanocoder --version

# MCP configuration must work before the workspace volume has dependencies.
# jq extracts exactly the devDependencies the MCP script needs; the length guard
# fails the build when the manifest drops a required package.
COPY tooling~/package.json /tmp/dxt-tooling-package.json
RUN mkdir -p /opt/dxt-mcp \
    && jq '{private: true, dependencies: (.devDependencies | with_entries(select(.key as $k | ["@modelcontextprotocol/sdk", "smol-toml", "jsonc-parser"] | index($k))))}' /tmp/dxt-tooling-package.json > /opt/dxt-mcp/package.json \
    && test "$(jq '.dependencies | length' /opt/dxt-mcp/package.json)" -eq 3 \
    && npm install --prefix /opt/dxt-mcp --no-fund --no-audit \
    && rm /tmp/dxt-tooling-package.json

# All user installs use this prefix; image copies above are system fallbacks.
ENV NPM_CONFIG_PREFIX="/home/vscode/.local"
ENV NPM_CONFIG_CACHE="/home/vscode/.npm"
ENV NPM_CONFIG_FUND=false \
    NPM_CONFIG_UPDATE_NOTIFIER=false \
    NPM_CONFIG_FETCH_RETRIES=5 \
    NPM_CONFIG_FETCH_TIMEOUT=300000

# ------------------------------------------------------------------------------
# Python tooling: pre-commit (repo hooks), yamllint, and the git/fetch MCP
# servers via uv. The MCP executables land in /usr/local/bin so MCP startup
# never needs to download anything.
# ------------------------------------------------------------------------------
RUN pip3 install --no-cache-dir --break-system-packages pre-commit yamllint \
    && pre-commit --version \
    && yamllint --version
RUN pip3 install --no-cache-dir --break-system-packages uv \
    && UV_TOOL_DIR=/opt/dxt-mcp/uv-tools UV_TOOL_BIN_DIR=/usr/local/bin uv tool install mcp-server-git \
    && UV_TOOL_DIR=/opt/dxt-mcp/uv-tools UV_TOOL_BIN_DIR=/usr/local/bin uv tool install mcp-server-fetch \
    && mcp-server-git --help >/dev/null \
    && mcp-server-fetch --help >/dev/null

# ------------------------------------------------------------------------------
# Pre-create the volume mount targets with vscode ownership. Named volumes copy
# permissions from the image only on first attach; rebuilds repair via the
# lifecycle scripts. Targets MUST stay aligned with .devcontainer/cache-contract.sh.
# ------------------------------------------------------------------------------
RUN mkdir -p /home/vscode/.nuget \
    && mkdir -p /home/vscode/.dotnet/tools \
    && mkdir -p /home/vscode/.local/share/powershell \
    && mkdir -p /home/vscode/.cache/pip \
    && mkdir -p /home/vscode/.npm \
    && mkdir -p /home/vscode/.local/bin \
    && mkdir -p /workspaces/package/node_modules \
    && chown -R vscode:vscode /home/vscode/.nuget /home/vscode/.dotnet \
    && chown -R vscode:vscode /home/vscode/.local /home/vscode/.cache \
    && chown -R vscode:vscode /home/vscode/.npm /home/vscode/.config \
    && chown -R vscode:vscode /workspaces

# ------------------------------------------------------------------------------
# Dev-friendly shell: aliases, zoxide-less prompt basics, VS Code integration.
# ------------------------------------------------------------------------------
RUN cat >> /etc/bash.bashrc <<'EOF'

# DxCommandTerminal CLI aliases
alias ls='ls --color=auto --group-directories-first'
alias ll='ls -lah --color=auto'
alias lt='tree -a -I ".git|node_modules"'
if command -v bat >/dev/null 2>&1; then alias cat='bat --paging=never'; fi
if command -v fd >/dev/null 2>&1; then alias find='fd'; fi
alias grep='rg'
alias ports='ss -tulpn 2>/dev/null || netstat -tulpn 2>/dev/null'

# VS Code shell integration
if [[ "$TERM_PROGRAM" == "vscode" ]]; then
    if [[ -f "$VSCODE_SHELL_INTEGRATION" ]]; then
        . "$VSCODE_SHELL_INTEGRATION"
    fi
fi
EOF

# ------------------------------------------------------------------------------
# CSharpier for the vscode user (matches .config/dotnet-tools.json).
# ------------------------------------------------------------------------------
USER vscode
RUN dotnet tool install --global CSharpier --version 1.1.2
ENV PATH="${PATH}:/home/vscode/.dotnet/tools"

# ------------------------------------------------------------------------------
# Verification: fail the build when anything required is missing.
# ------------------------------------------------------------------------------
RUN echo "=== Environment Verification ===" \
    && echo "--- .NET SDKs ---" && dotnet --list-sdks \
    && echo "--- Node tooling ---" && node --version && npm --version \
    && echo "--- Agent CLIs ---" \
    && claude --version && codex --version && opencode --version && nanocoder --version \
    && echo "--- MCP executables ---" \
    && command -v mcp-server-git && command -v mcp-server-fetch \
    && command -v zai-mcp-server && command -v mcp-remote \
    && command -v context7-mcp \
    && echo "--- MCP script dependencies ---" \
    && test -d /opt/dxt-mcp/node_modules/@modelcontextprotocol/sdk \
    && test -d /opt/dxt-mcp/node_modules/smol-toml \
    && test -d /opt/dxt-mcp/node_modules/jsonc-parser \
    && echo "--- Repo tooling ---" \
    && pwsh --version && pre-commit --version \
    && echo "--- Core CLIs ---" \
    && rg --version | head -n1 && jq --version && fd --version && bat --version \
    && echo "=== Verification Complete ==="

WORKDIR /workspaces
USER vscode
