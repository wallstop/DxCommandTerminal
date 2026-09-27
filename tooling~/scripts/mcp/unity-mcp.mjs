#!/usr/bin/env node
// Host: bridge exposes Unity CLI (or the legacy relay) over authenticated HTTP.
// Container: configure writes MCP clients; probe checks editor readiness; capture
// pulls Unity editor/game state into .artifacts through the bridge.
import { spawn } from "node:child_process";
import { randomBytes, randomUUID, timingSafeEqual } from "node:crypto";
import fs from "node:fs";
import http from "node:http";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import process from "node:process";
import { fileURLToPath, pathToFileURL } from "node:url";
import { createRequire } from "node:module";
// The image copy keeps configuration available before workspace npm install finishes.
const require = createRequire(import.meta.url);
const BAKED_MCP_DEPS = "/opt/dxt-mcp";
const dependency = (name) =>
  import(
    pathToFileURL(
      require.resolve(name, {
        paths: [path.dirname(fileURLToPath(import.meta.url)), BAKED_MCP_DEPS]
      })
    ).href
  );
const [
  { Client },
  { StreamableHTTPClientTransport, StreamableHTTPError },
  { Server },
  { StreamableHTTPServerTransport },
  { isInitializeRequest, McpError },
  { parse: parseToml, stringify: stringifyToml },
  { parse: parseJsonc }
] = await Promise.all(
  [
    "@modelcontextprotocol/sdk/client/index.js",
    "@modelcontextprotocol/sdk/client/streamableHttp.js",
    "@modelcontextprotocol/sdk/server/index.js",
    "@modelcontextprotocol/sdk/server/streamableHttp.js",
    "@modelcontextprotocol/sdk/types.js",
    "smol-toml",
    "jsonc-parser"
  ].map(dependency)
);
// T11 baseline compare: pure stdlib, no dynamic dependencies.
import {
  compareScenario,
  emitComparisonArtifacts,
  environmentKey,
  environmentProvenance,
  provenanceOf,
  readBaselineIndex
} from "../t11/comparator.mjs";
export const REPO_ROOT = path.resolve(fileURLToPath(new URL("../../..", import.meta.url)));
export const GITHUB_MCP_URL = "https://api.githubcopilot.com/mcp/";
export const DEFAULTS = Object.freeze({
  bindHost: "0.0.0.0",
  host: "host.docker.internal",
  port: 9020,
  endpointPath: "/mcp",
  protocolVersion: "2025-11-25",
  probeTimeout: 5_000,
  connectTimeout: 750,
  requestTimeout: 300_000,
  sessionTimeout: 60_000,
  bodyLimitBytes: 1_048_576,
  // Upper bound on how long the bridge waits for a request body. Capped by the session timeout so a
  // client that sends headers and then stalls cannot hold a socket (and shutdown) open.
  bodyTimeout: 15_000,
  maxSessions: 8
});
// Bridge default and the retired supergateway port. Explicit settings replace these.
export const FALLBACK_PORTS = Object.freeze([9020, 9003]);
// Docker Desktop and same-host defaults; discovery also checks WSL and Linux gateways.
export const FALLBACK_HOSTS = Object.freeze(["host.docker.internal", "127.0.0.1"]);
// Deterministic per-project bridge port ("project-port-local"). Each Unity project
// owns one port in this range, so hosts with several open editors never collide and
// discovery can find this checkout's bridge without a shared registry file.
export const PROJECT_PORT_RANGE = Object.freeze({ base: 27100, size: 900 });
export function projectPort(projectPath) {
  if (!projectPath) return null;
  const normalized = path
    .resolve(String(projectPath))
    .replace(/\\/g, "/")
    .replace(/\/+$/, "")
    .toLowerCase();
  // FNV-1a 32-bit over the UTF-8 bytes of the normalized absolute path.
  let hash = 0x811c9dc5;
  for (const byte of Buffer.from(normalized, "utf8")) {
    hash ^= byte;
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return PROJECT_PORT_RANGE.base + (hash % PROJECT_PORT_RANGE.size);
}
// Discovery order: the deterministic project port first, then stock fallbacks.
export function resolveProjectPorts(projectPath) {
  const derived = projectPort(projectPath);
  const ports = derived === null ? [...FALLBACK_PORTS] : [derived, ...FALLBACK_PORTS];
  return [...new Set(ports)];
}
const OPTION_NAMES = new Set([
  "bind",
  "host",
  "port",
  "path",
  "project",
  "relay",
  "backend",
  "cli",
  "request-timeout",
  "session-timeout",
  "timeout",
  "connect-timeout",
  "max-sessions",
  "protocol-version",
  "log-level",
  "token",
  "no-discover",
  "offline",
  "out",
  "project-container",
  "scenarios",
  "mode",
  "filter",
  "run-timeout"
]);
const FLAG_NAMES = new Set(["no-discover", "offline", "no-install"]);
const ENV_KEYS = Object.freeze({
  bindHost: "UNITY_MCP_BIND_HOST",
  host: "UNITY_MCP_BRIDGE_HOST",
  port: "UNITY_MCP_BRIDGE_PORT",
  endpointPath: "UNITY_MCP_BRIDGE_PATH",
  projectPath: "UNITY_PROJECT_PATH",
  projectContainerPath: "UNITY_PROJECT_CONTAINER_PATH",
  relayPath: "UNITY_MCP_RELAY_PATH",
  backend: "UNITY_MCP_BACKEND",
  cliPath: "UNITY_CLI_PATH",
  requestTimeout: "UNITY_MCP_REQUEST_TIMEOUT",
  sessionTimeout: "UNITY_MCP_SESSION_TIMEOUT",
  timeout: "UNITY_MCP_PROBE_TIMEOUT",
  connectTimeout: "UNITY_MCP_CONNECT_TIMEOUT",
  maxSessions: "UNITY_MCP_MAX_SESSIONS",
  protocolVersion: "UNITY_MCP_PROTOCOL_VERSION",
  logLevel: "UNITY_MCP_LOG_LEVEL",
  bearerToken: "UNITY_MCP_BEARER_TOKEN"
});
function fail(message) {
  throw new Error(message);
}
function first(...values) {
  return values.find((value) => value !== undefined && value !== "");
}
// Argument and .env.local parsing
export function parseArgs(argv) {
  const result = { _: [] };
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (!token.startsWith("--")) {
      result._.push(token);
      continue;
    }
    const separator = token.indexOf("=");
    const name = token.slice(2, separator === -1 ? undefined : separator);
    if (!OPTION_NAMES.has(name)) fail(`Unknown option: --${name}`);
    if (FLAG_NAMES.has(name)) {
      if (separator !== -1) fail(`--${name} does not take a value`);
      result[name] = true;
      continue;
    }
    const value = separator === -1 ? argv[++index] : token.slice(separator + 1);
    if (value === undefined || value.startsWith("--")) {
      fail(`Missing value for --${name}`);
    }
    if (value === "") fail(`--${name} requires a non-empty value`);
    result[name] = value;
  }
  return result;
}
// Match through the last closing quote; allow trailing backslashes in Windows paths.
const QUOTED_VALUE = Object.freeze({
  '"': /^"((?:\\"|[^"])*)"\s*(?:#.*)?$/,
  "'": /^'((?:\\'|[^'])*)'\s*(?:#.*)?$/
});
export function parseDotEnv(raw, source = ".env.local") {
  const values = {};
  const normalized = raw.replace(/^\uFEFF/, "");
  for (const [index, original] of normalized.split(/\r?\n/).entries()) {
    const line = original.trim();
    if (!line || line.startsWith("#")) continue;
    const match = /^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/.exec(line);
    if (!match) fail(`Invalid ${source} entry on line ${index + 1}`);
    let value = match[2].trim();
    const quote = QUOTED_VALUE[value[0]] ? value[0] : undefined;
    if (quote) {
      const quoted = QUOTED_VALUE[quote].exec(value);
      if (!quoted) fail(`Invalid quoted value in ${source} on line ${index + 1}`);
      // Only double quotes carry escapes, matching POSIX shell and dotenv semantics.
      value = quote === '"' ? quoted[1].replace(/\\(["\\])/g, "$1") : quoted[1];
    } else {
      const comment = value.search(/\s+#/);
      if (comment !== -1) value = value.slice(0, comment).trimEnd();
    }
    values[match[1]] = value;
  }
  return values;
}
// Skip unrelated malformed dotenv lines, with line-number-only diagnostics.
export function readLocalEnv(repoRoot) {
  const envPath = path.join(repoRoot, ".env.local");
  if (!fs.existsSync(envPath)) return {};
  const values = {};
  for (const [index, line] of fs.readFileSync(envPath, "utf8").split(/\r?\n/).entries()) {
    try {
      Object.assign(values, parseDotEnv(line, envPath));
    } catch {
      console.warn(`unity-mcp: ignoring unparsable ${envPath} line ${index + 1}`);
    }
  }
  return values;
}
function integer(value, name, minimum, maximum) {
  if (!/^\d+$/.test(String(value))) fail(`${name} must be an integer`);
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed < minimum || parsed > maximum) {
    fail(`${name} must be between ${minimum} and ${maximum}`);
  }
  return parsed;
}
function validateText(value, name) {
  if (/[\0\r\n]/.test(value)) fail(`${name} contains an invalid control character`);
  return value;
}
export function validateHost(value, name = "Host") {
  validateText(value, name);
  if (net.isIP(value)) return value;
  const candidate = value.endsWith(".") ? value.slice(0, -1) : value;
  const labels = candidate.split(".");
  const labelPattern = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$/;
  if (
    candidate.length === 0 ||
    candidate.length > 253 ||
    !labels.every((label) => labelPattern.test(label))
  ) {
    fail(`Invalid ${name.toLowerCase()}: ${value}`);
  }
  return value;
}
export function validateEndpointPath(value) {
  const normalized = value.startsWith("/") ? value : `/${value}`;
  if (
    !/^\/[A-Za-z0-9._~!$&'()*+,;=:@%/-]*$/.test(normalized) ||
    normalized.includes("//") ||
    /%(?![0-9A-Fa-f]{2})/.test(normalized)
  ) {
    fail(`Invalid MCP endpoint path: ${value}`);
  }
  let decoded;
  try {
    // Syntactically valid escapes can still be invalid UTF-8 (for example /%FF), which throws.
    decoded = decodeURIComponent(normalized);
  } catch {
    fail(`Invalid MCP endpoint path: ${value}`);
  }
  if (decoded.includes("//") || decoded.split("/").some((part) => part === "." || part === "..")) {
    fail(`Invalid MCP endpoint path: ${value}`);
  }
  return normalized;
}
function validateToken(value) {
  if (value === undefined) return undefined;
  if (!/^[A-Za-z0-9._~-]{32,256}$/.test(value)) {
    fail("Bearer token must be 32-256 URL-safe characters");
  }
  return value;
}
function githubToken(environment, local) {
  const keys = ["GITHUB_TOKEN", "GH_TOKEN", "GITHUB_PERSONAL_ACCESS_TOKEN", "GITHUB_PAT"];
  const value = first(...keys.map((key) => environment[key]), ...keys.map((key) => local[key]));
  if (value?.length > 1_024) fail("GitHub token must not exceed 1024 characters");
  return value === undefined ? value : validateText(value, "GitHub token");
}
// Host project paths are validated only by bridge; they do not exist in the container.
export function resolveOptions(args, environment = process.env, localValues, repoRoot = REPO_ROOT) {
  const local = localValues ?? readLocalEnv(repoRoot);
  const get = (argName, key, fallback) =>
    first(args[argName], environment[ENV_KEYS[key]], local[ENV_KEYS[key]], fallback);

  const number = (arg, key, max, fallback = DEFAULTS[key]) => {
    const label = arg[0].toUpperCase() + arg.slice(1).replaceAll("-", " ");
    return integer(get(arg, key, fallback), label, 1, max);
  };
  const explicitHost = first(args.host, environment[ENV_KEYS.host], local[ENV_KEYS.host]);
  const explicitPort = first(args.port, environment[ENV_KEYS.port], local[ENV_KEYS.port]);
  const projectPath = first(
    args.project,
    environment[ENV_KEYS.projectPath],
    local[ENV_KEYS.projectPath]
  );
  const projectContainerPath = first(
    args["project-container"],
    environment[ENV_KEYS.projectContainerPath],
    local[ENV_KEYS.projectContainerPath]
  );

  // Project-port-local default: when the checkout knows its Unity project but no
  // explicit port, the deterministic project port replaces the stock bridge port.
  // An explicit port (flag, env, or .env.local) always wins and suppresses discovery.
  const derivedPort = explicitPort === undefined ? (projectPort(projectPath) ?? DEFAULTS.port) : explicitPort;

  const options = {
    repoRoot,
    bindHost: validateHost(get("bind", "bindHost", DEFAULTS.bindHost), "Bind host"),
    host: validateHost(explicitHost ?? DEFAULTS.host),
    explicitHost: explicitHost === undefined ? undefined : validateHost(explicitHost),
    port: integer(derivedPort, "Port", 1, 65_535),
    explicitPort: explicitPort === undefined ? undefined : integer(explicitPort, "Port", 1, 65_535),
    endpointPath: validateEndpointPath(get("path", "endpointPath", DEFAULTS.endpointPath)),
    projectPath: projectPath === undefined ? undefined : path.resolve(projectPath),
    projectContainerPath:
      projectContainerPath === undefined ? undefined : path.resolve(projectContainerPath),
    backend: get("backend", "backend", "cli"),
    cliPath: get("cli", "cliPath", "unity"),
    relayPath: first(args.relay, environment[ENV_KEYS.relayPath], local[ENV_KEYS.relayPath]),
    requestTimeout: number("request-timeout", "requestTimeout", 86_400_000),
    sessionTimeout: number("session-timeout", "sessionTimeout", 86_400_000),
    timeout: number("timeout", "timeout", 300_000, DEFAULTS.probeTimeout),
    connectTimeout: number("connect-timeout", "connectTimeout", 60_000),
    maxSessions: number("max-sessions", "maxSessions", 1_024),
    protocolVersion: validateText(
      get("protocol-version", "protocolVersion", DEFAULTS.protocolVersion),
      "Protocol version"
    ),
    logLevel: get("log-level", "logLevel", "info"),
    bearerToken: validateToken(get("token", "bearerToken", undefined)),
    bearerTokenFromArgument: args.token !== undefined,
    githubToken: githubToken(environment, local),
    zaiToken: first(
      environment.Z_AI_API_KEY,
      environment.ZAI_API_KEY,
      local.Z_AI_API_KEY,
      local.ZAI_API_KEY
    ),
    offline: args.offline === true,
    discover: args["no-discover"] !== true,
    noInstall: args["no-install"] === true,
    out: args.out === undefined ? undefined : path.resolve(args.out)
  };

  if (!["cli", "relay"].includes(options.backend)) fail("Backend must be cli or relay");
  if (options.zaiToken) validateText(options.zaiToken, "Z.AI key");
  if (options.relayPath) options.relayPath = path.resolve(options.repoRoot, options.relayPath);
  if (!/^(?:debug|info|none)$/.test(options.logLevel)) {
    fail("Log level must be debug, info, or none");
  }
  if (options.protocolVersion !== DEFAULTS.protocolVersion) {
    fail(`Protocol version must be ${DEFAULTS.protocolVersion}`);
  }
  return options;
}

export function requireProjectPath(options) {
  if (!options.projectPath) {
    fail(`Unity project path is required. Pass --project or set ${ENV_KEYS.projectPath}.`);
  }
  if (!fs.existsSync(options.projectPath) || !fs.statSync(options.projectPath).isDirectory()) {
    fail(`Unity project directory does not exist: ${options.projectPath}`);
  }
  return options.projectPath;
}

export function requireProjectFilesystemPath(options) {
  const usesContainerPath = options.projectContainerPath !== undefined;
  const projectPath = options.projectContainerPath ?? options.projectPath;
  const source = usesContainerPath ? "--project-container" : "--project";
  const variable = usesContainerPath ? ENV_KEYS.projectContainerPath : ENV_KEYS.projectPath;
  if (!projectPath) {
    fail(`Unity project path is required. Pass ${source} or set ${variable}.`);
  }
  if (!fs.existsSync(projectPath) || !fs.statSync(projectPath).isDirectory()) {
    fail(`Unity project directory does not exist: ${projectPath} (from ${variable})`);
  }
  return projectPath;
}

export function endpointUrl({ host, port, endpointPath }) {
  const formatted = net.isIP(host) === 6 ? `[${host}]` : host;
  return `http://${formatted}:${port}${endpointPath}`;
}

// Endpoint discovery

/** Nameserver entries in /etc/resolv.conf. Under WSL2 this is the Windows host. */
export function resolvConfHosts(raw) {
  if (!raw) {
    return [];
  }
  return raw
    .split(/\r?\n/)
    .map((line) => /^\s*nameserver\s+(\S+)\s*$/.exec(line))
    .filter(Boolean)
    .map((match) => match[1])
    .filter((address) => net.isIP(address) === 4);
}

/** Default-route gateways from /proc/net/route (little-endian hex IPv4). */
export function procNetRouteGateways(raw) {
  if (!raw) {
    return [];
  }
  const gateways = [];
  for (const line of raw.split(/\r?\n/).slice(1)) {
    const fields = line.trim().split(/\s+/);
    if (fields.length < 3 || fields[1] !== "00000000" || !/^[0-9A-Fa-f]{8}$/.test(fields[2])) {
      continue;
    }
    const value = Number.parseInt(fields[2], 16);
    if (value === 0) {
      continue;
    }
    const octets = [
      value & 0xff,
      (value >>> 8) & 0xff,
      (value >>> 16) & 0xff,
      (value >>> 24) & 0xff
    ];
    gateways.push(octets.join("."));
  }
  return gateways;
}

function readTextOrEmpty(filePath) {
  try {
    return fs.readFileSync(filePath, "utf8");
  } catch {
    return "";
  }
}

// Explicit host/port settings replace discovery defaults on that axis.
export function endpointCandidates(options, runtime = {}) {
  const readFile = runtime.readFile ?? readTextOrEmpty;
  const hosts = options.explicitHost
    ? [options.explicitHost]
    : [
        ...FALLBACK_HOSTS,
        ...resolvConfHosts(readFile("/etc/resolv.conf")),
        ...procNetRouteGateways(readFile("/proc/net/route"))
      ].filter(Boolean);
  const ports = options.explicitPort ? [options.explicitPort] : resolveProjectPorts(options.projectPath);

  const seen = new Set();
  const candidates = [];
  for (const port of ports) {
    for (const host of hosts) {
      const key = `${host}:${port}`;
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      candidates.push({ host, port, endpointPath: options.endpointPath });
    }
  }
  return candidates;
}

export function tcpReachable(host, port, timeout) {
  return new Promise((resolve) => {
    const socket = new net.Socket();
    const settle = (value) => {
      socket.removeAllListeners();
      socket.destroy();
      resolve(value);
    };
    socket.setTimeout(timeout);
    socket.once("connect", () => settle(true));
    socket.once("timeout", () => settle(false));
    socket.once("error", () => settle(false));
    socket.connect(port, host);
  });
}

// Read-only state proves the relay's advertised tools reach a live Editor (#418).
const EDITOR_READY_TOOL = "Unity_ManageEditor";
const EDITOR_READY_ARGUMENTS = { Action: "GetState" };

// Readiness: false = handshake, "tools" = registry, "editor" = live state.
/**
 * Whether a bridge error is the editor refusing new sessions because its table
 * is full ("Too many concurrent MCP sessions", HTTP 503 / JSON-RPC -32000).
 * That is a transient, server-side condition - other clients and earlier
 * commands hold the slots - so it is retried instead of reported as a dead
 * bridge, which is what the same message used to look like.
 */
export function isSessionCapError(detail) {
  return /too many concurrent mcp sessions/iu.test(String(detail ?? ""));
}

const SESSION_CAP_ATTEMPTS = 4;
const SESSION_CAP_BACKOFF_MS = [1_000, 2_000, 4_000];

/**
 * Release a bridge session with an explicit DELETE, which is the only thing
 * that frees its slot.
 *
 * Closing the SDK client is not enough: the editor's MCP server keeps every
 * session it handed out until a DELETE arrives, and it refuses new sessions
 * past a small cap (8 on Unity 6000.4.6f1, answering
 * "Too many concurrent MCP sessions"). A command that only closed its client
 * leaked a slot, so a handful of commands locked every later one out with an
 * HTTP 503 that reads like a dead bridge. Returns a warning string, or null.
 */
export async function deleteBridgeSession({
  url,
  authorization,
  sessionId,
  protocolVersion,
  fetchImpl = fetch,
  timeoutMs = 1_000
}) {
  if (!sessionId) return null;
  try {
    const response = await fetchImpl(url, {
      method: "DELETE",
      headers: {
        ...authorization,
        "Mcp-Session-Id": sessionId,
        "MCP-Protocol-Version": protocolVersion
      },
      signal: AbortSignal.timeout(Math.max(1, Math.min(timeoutMs, 5_000)))
    });
    await response.body?.cancel();
    if (!response.ok && response.status !== 405) {
      return `session cleanup returned HTTP ${response.status}`;
    }
    return null;
  } catch (error) {
    return `session cleanup failed: ${error.message}`;
  }
}

export async function probeEndpoint(candidate, options, fetchImpl = fetch, readiness = false) {
  const url = endpointUrl(candidate);
  const classify = (status, detail) => ({ ...candidate, url, ok: false, status, detail });
  const succeed = (extra) => ({ ...candidate, url, ok: true, status: "ok", ...extra });
  if (!(await tcpReachable(candidate.host, candidate.port, options.connectTimeout))) {
    return classify("unreachable", "no TCP listener");
  }
  const lifecycleSignal = AbortSignal.timeout(options.timeout);
  const authorization = options.bearerToken
    ? { Authorization: `Bearer ${options.bearerToken}` }
    : {};
  const cleanupWarnings = [];
  const cleanup = async (sessionId, protocolVersion) => {
    const warning = await deleteBridgeSession({
      url,
      authorization,
      sessionId,
      protocolVersion,
      fetchImpl,
      timeoutMs: options.timeout
    });
    if (warning !== null) cleanupWarnings.push(warning);
  };
  const failure = (error, operation) => {
    const message = error?.message ?? String(error);
    if (error?.probeStatus) return classify(error.probeStatus, message);
    if (error instanceof StreamableHTTPError) {
      const status = [401, 403].includes(error.code)
        ? "unauthorized"
        : error.code === -1
          ? "malformed"
          : "http-error";
      return classify(status, `${operation}: HTTP ${error.code}: ${message}`);
    }
    if (error instanceof McpError) {
      const status = lifecycleSignal.aborted ? "transport-error" : "jsonrpc-error";
      return classify(status, `${operation}: ${message}`);
    }
    const malformed = error instanceof SyntaxError || Array.isArray(error?.issues);
    return classify(malformed ? "malformed" : "transport-error", `${operation}: ${message}`);
  };
  let result;
  const attempts = 1 + SESSION_CAP_ATTEMPTS;
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    result = undefined;
    let operation = "initialize";
    let sessionId;
    const transport = new StreamableHTTPClientTransport(new URL(url), {
      requestInit: { headers: authorization },
      fetch: async (target, init = {}) => {
        if (lifecycleSignal.aborted) throw lifecycleSignal.reason;
        const request = init.body ? JSON.parse(init.body) : undefined;
        operation = request?.method ?? operation;
        const lastEventId = new Headers(init.headers).get("last-event-id");
        if (init.method === "GET" && !lastEventId) return new Response(null, { status: 405 });
        const signal = init.signal
          ? AbortSignal.any([init.signal, lifecycleSignal])
          : lifecycleSignal;
        const response = await fetchImpl(target, { ...init, signal });
        sessionId ||= response.headers.get("mcp-session-id") ?? undefined;
        const responseType = response.headers.get("content-type") ?? "";
        const isSse = /^text\/event-stream\s*(?:;|$)/i.test(responseType);
        const resumeOk = response.status === 200 && isSse;
        if (lastEventId && response.ok && !resumeOk) {
          await response.body?.cancel();
          const error = new Error(`resume GET returned HTTP ${response.status} ${responseType}`);
          error.probeStatus = "malformed";
          throw error;
        }
        const messages = Array.isArray(request) ? request : [request];
        const expectsResponse = messages.some((message) => message?.method && "id" in message);
        const expectedStatus = expectsResponse ? 200 : 202;
        if (request && response.ok && response.status !== expectedStatus) {
          await response.body?.cancel();
          const error = new Error(`${operation}: HTTP ${response.status}, want ${expectedStatus}`);
          error.probeStatus = "malformed";
          throw error;
        }
        return response;
      }
    });
    const client = new Client({ name: "unity-mcp-probe", version: "1.0.0" });
    let reportTransportError;
    const transportError = new Promise((_, reject) => (reportTransportError = reject));
    client.onerror = reportTransportError;
    const awaited = (promise) => Promise.race([promise, transportError]);
    let retry = false;
    try {
      await awaited(client.connect(transport, { signal: lifecycleSignal }));
      const protocolVersion = transport.protocolVersion;
      if (protocolVersion !== options.protocolVersion) {
        const error = new Error(`server negotiated unsupported protocol ${protocolVersion}`);
        error.probeStatus = "malformed";
        throw error;
      }
      if (!readiness) {
        result = succeed({ sessionId, protocolVersion });
      } else if (!client.getServerCapabilities()?.tools) {
        result = classify("not-ready", "server did not advertise MCP tools");
      } else {
        const cursors = new Set();
        let cursor;
        let toolCount = 0;
        let commandAdvertised = false;
        let editorToolAdvertised = false;
        let editorTool = EDITOR_READY_TOOL;
        operation = "tools/list";
        for (let page = 0; page < 100; page += 1) {
          const params = cursor === undefined ? {} : { cursor };
          const listed = await awaited(client.listTools(params, { signal: lifecycleSignal }));
          toolCount += listed.tools.length;
          if (listed.tools.some((t) => t.name === "editor_status")) editorTool = "editor_status";
          editorToolAdvertised ||= listed.tools.some((t) => t.name === editorTool);
          commandAdvertised ||= listed.tools.some(
            (tool) => tool.name === "Unity_RunCommand" || tool.name === "editor_status"
          );
          // Editor readiness must search later pages before settling for a registry-only result.
          if (
            commandAdvertised &&
            (readiness !== "editor" || editorToolAdvertised || listed.nextCursor === undefined)
          ) {
            result = succeed({
              sessionId,
              protocolVersion,
              toolCount,
              editorToolAdvertised,
              editorTool
            });
            break;
          }
          if (listed.nextCursor === undefined) {
            result = classify(
              "not-ready",
              "Neither Unity_RunCommand nor editor_status was advertised"
            );
            break;
          }
          if (cursors.has(listed.nextCursor)) {
            result = classify("malformed", "tools/list returned a repeated cursor");
            break;
          }
          cursors.add(listed.nextCursor);
          cursor = listed.nextCursor;
        }
        result ??= classify("malformed", "tools/list exceeded 100 pages");
        // A relay whose registry has no editor tool cannot be asked whether an editor is behind
        // it, so that stays a tools-level verdict rather than a false red.
        if (result.ok && readiness === "editor" && result.editorToolAdvertised) {
          operation = "tools/call";
          const call = await awaited(
            client.callTool(
              {
                name: editorTool,
                arguments: editorTool === EDITOR_READY_TOOL ? EDITOR_READY_ARGUMENTS : {}
              },
              undefined,
              { signal: lifecycleSignal }
            )
          );
          const reply = (call.content ?? [])
            .map((part) => part.text ?? "")
            .join(" ")
            .trim();
          if (
            call.isError ||
            !(
              editorTool === EDITOR_READY_TOOL
                ? /"IsCompiling"/
                : /"compiling"\s*:\s*(?:true|false)/
            ).test(reply)
          ) {
            result = classify(
              "not-ready",
              `${editorTool}: ${reply.slice(0, 160) || "returned no editor state"}`
            );
          }
        }
      }
    } catch (error) {
      const expired = error instanceof StreamableHTTPError && error.code === 404;
      const capped = isSessionCapError(error?.message);
      retry = attempt + 1 < attempts && (capped || (attempt === 0 && Boolean(sessionId) && expired));
      if (capped && retry) {
        console.warn(
          `${url}: ${error.message} - retrying (attempt ${attempt + 2}/${attempts})`
        );
        await new Promise((resolve) => setTimeout(resolve, SESSION_CAP_BACKOFF_MS[attempt] ?? 4_000));
      }
      result = failure(error, operation);
    } finally {
      await client.close().catch(() => {});
      await cleanup(sessionId, transport.protocolVersion ?? options.protocolVersion);
    }
    if (!retry || lifecycleSignal.aborted) break;
  }
  if (cleanupWarnings.length) result.cleanupWarning = cleanupWarnings.join("; ");
  return result;
}

/** Probe candidates in order and return the first that meets the requested readiness level. */
export async function discoverEndpoint(options, runtime = {}) {
  const fetchImpl = runtime.fetchImpl ?? fetch;
  const candidates = runtime.candidates ?? endpointCandidates(options, runtime);
  const attempts = [];
  for (const candidate of candidates) {
    log(options, "debug", `Probing ${endpointUrl(candidate)}`);
    const result = await probeEndpoint(candidate, options, fetchImpl, runtime.readiness);
    log(options, "debug", `  ${result.status}: ${result.detail ?? "ok"}`);
    if (result.cleanupWarning) console.warn(`${result.url}: ${result.cleanupWarning}`);
    attempts.push(result);
    if (result.ok) return { found: result, attempts };
  }
  return { found: undefined, attempts };
}

export function describeAttempts(attempts) {
  const interesting = attempts.filter((attempt) => attempt.status !== "unreachable");
  const shown = interesting.length > 0 ? interesting : attempts;
  return shown
    .map(
      (attempt) =>
        `  ${attempt.url} - ${attempt.status} (${[attempt.detail, attempt.cleanupWarning].filter(Boolean).join("; ") || "no detail"})`
    )
    .join("\n");
}

// Client configuration

function stageFile(filePath, content) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  const temporary = `${filePath}.${process.pid}.${randomBytes(8).toString("hex")}.tmp`;
  fs.writeFileSync(temporary, content, { encoding: "utf8", mode: 0o600, flag: "wx" });
  return temporary;
}

function atomicWrite(filePath, content, mode) {
  const temporary = stageFile(filePath, content);
  try {
    if (mode !== undefined) {
      // Rollback must restore the permissions the file had, not the 0600 staging default.
      fs.chmodSync(temporary, mode);
    }
    fs.renameSync(temporary, filePath);
  } finally {
    fs.rmSync(temporary, { force: true });
  }
}

// Stage every config first. Roll back all commits on failure, collecting rollback
// errors without hiding the original failure (Windows can hold config files open).
export function transactionalWrite(writes, beforeCommit = () => {}) {
  const changed = writes.filter(
    ([filePath, content]) =>
      !fs.existsSync(filePath) || fs.readFileSync(filePath, "utf8") !== content
  );
  const staged = [];
  const committed = [];
  try {
    for (const [filePath, content] of changed) {
      const existed = fs.existsSync(filePath);
      staged.push({
        filePath,
        existed,
        original: existed ? fs.readFileSync(filePath, "utf8") : undefined,
        mode: existed ? fs.statSync(filePath).mode & 0o777 : undefined,
        temporary: stageFile(filePath, content)
      });
    }
    for (let index = 0; index < staged.length; index += 1) {
      beforeCommit(index, staged[index].filePath);
      fs.renameSync(staged[index].temporary, staged[index].filePath);
      committed.push(staged[index]);
    }
  } catch (error) {
    const suppressed = [];
    for (const item of committed.reverse()) {
      try {
        if (item.existed) {
          atomicWrite(item.filePath, item.original, item.mode);
        } else {
          fs.rmSync(item.filePath, { force: true });
        }
      } catch (rollbackError) {
        suppressed.push(rollbackError);
      }
    }
    if (suppressed.length > 0) {
      error.cause = new AggregateError(
        suppressed,
        `Rollback failed for ${suppressed.length} file(s)`
      );
    }
    throw error;
  } finally {
    // Staging can throw part way through, so only the temporaries actually created are removed.
    for (const item of staged) {
      fs.rmSync(item.temporary, { force: true });
    }
  }
  return changed.map(([filePath]) => filePath);
}

function persistBearerToken(repoRoot, token) {
  const envPath = path.join(repoRoot, ".env.local");
  if (readLocalEnv(repoRoot)[ENV_KEYS.bearerToken] === token) return;
  const current = fs.existsSync(envPath) ? fs.readFileSync(envPath, "utf8") : "";
  const pattern = /^(\uFEFF?[ \t]*(?:export[ \t]+)?UNITY_MCP_BEARER_TOKEN[ \t]*=)[^\r\n]*/gm;
  const updated = current.replace(pattern, `$1${token}`);
  if (updated !== current) {
    atomicWrite(envPath, updated);
    return;
  }
  const prefix = current && !current.endsWith("\n") ? "\n" : "";
  atomicWrite(envPath, `${current}${prefix}${ENV_KEYS.bearerToken}=${token}\n`);
}

function ensureBearerToken(options) {
  if (options.bearerToken) {
    if (options.bearerTokenFromArgument) persistBearerToken(options.repoRoot, options.bearerToken);
    return options;
  }
  const saved = readLocalEnv(options.repoRoot)[ENV_KEYS.bearerToken];
  if (saved) return { ...options, bearerToken: validateToken(saved) };
  const bearerToken = randomBytes(32).toString("hex");
  const envPath = path.join(options.repoRoot, ".env.local");
  const current = fs.existsSync(envPath) ? fs.readFileSync(envPath, "utf8") : "";
  const prefix = current && !current.endsWith("\n") ? "\n" : "";
  atomicWrite(envPath, `${current}${prefix}${ENV_KEYS.bearerToken}=${bearerToken}\n`);
  return { ...options, bearerToken };
}

// Reject parser recovery: malformed configs must never be overwritten.
export function stripJsonComments(raw) {
  const errors = [];
  const parsed = parseJsonc(raw, errors, { allowTrailingComma: true });
  if (errors.length) fail(`Invalid JSONC at offset ${errors[0].offset}`);
  return JSON.stringify(parsed);
}

function readJsonObject(filePath) {
  if (!fs.existsSync(filePath) || !fs.readFileSync(filePath, "utf8").trim()) {
    return {};
  }
  let parsed;
  try {
    parsed = JSON.parse(stripJsonComments(fs.readFileSync(filePath, "utf8")));
  } catch (error) {
    fail(`Invalid JSON in ${filePath}: ${error.message}`);
  }
  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
    fail(`Expected a JSON object in ${filePath}`);
  }
  return parsed;
}

// `defaults` fills top-level document keys that are still undefined (existing
// user values are preserved, never stomped).
export function prepareJsonServers(filePath, collection, servers, removed = [], defaults = {}) {
  const document = readJsonObject(filePath);
  const existing = document[collection];
  if (
    existing !== undefined &&
    (!existing || typeof existing !== "object" || Array.isArray(existing))
  ) {
    fail(`Expected ${collection} to be an object in ${filePath}`);
  }
  document[collection] = { ...(existing ?? {}), ...servers };
  for (const name of removed) delete document[collection][name];
  for (const [key, value] of Object.entries(defaults)) {
    if (document[key] === undefined) document[key] = value;
  }
  return `${JSON.stringify(document, null, 2)}\n`;
}

function migrateOpenCodeOAuth(oauth) {
  if (!oauth || typeof oauth !== "object" || Array.isArray(oauth)) return oauth;
  const migrated = { ...oauth };
  for (const [legacy, native] of [
    ["clientId", "client_id"],
    ["clientSecret", "client_secret"],
    ["callbackPort", "callback_port"],
    ["redirectUri", "redirect_uri"]
  ]) {
    if (legacy in migrated && !(native in migrated)) migrated[native] = migrated[legacy];
    delete migrated[legacy];
  }
  return migrated;
}

function migrateOpenCodeServer(filePath, name, config) {
  if (!config || typeof config !== "object" || Array.isArray(config)) {
    fail(`Expected OpenCode MCP server ${name} to be an object in ${filePath}`);
  }
  const { enabled, timeout, oauth, ...server } = config;
  if (enabled !== undefined) server.disabled = !enabled;
  if (typeof timeout === "number") {
    server.timeout = { catalog: timeout, execution: timeout };
  } else {
    server.timeout = timeout;
  }
  if (server.timeout === undefined) delete server.timeout;
  server.oauth = migrateOpenCodeOAuth(oauth);
  if (server.oauth === undefined) delete server.oauth;
  return server;
}

function isOpenCodeServerConfig(config) {
  if (config === null || typeof config !== "object" || Array.isArray(config)) return false;
  if (config.type === "local") return Array.isArray(config.command);
  return config.type === "remote" && typeof config.url === "string";
}

const OPEN_CODE_SKILLS_PATH = "./.llm/skills";

function migrateOpenCodeSkills(filePath, skills) {
  let entries;
  if (Array.isArray(skills)) {
    entries = skills;
  } else {
    if (!skills || typeof skills !== "object") {
      fail(`Expected skills to be an array or object in ${filePath}`);
    }
    const { paths = [], urls = [] } = skills;
    if (!Array.isArray(paths) || !Array.isArray(urls)) {
      fail(`Expected skills.paths and skills.urls to be arrays in ${filePath}`);
    }
    entries = [...paths, ...urls];
  }
  return [...new Set([...entries, OPEN_CODE_SKILLS_PATH])];
}

function migrateOpenCodeMcpTimeout(document, mcp) {
  const experimental = document.experimental;
  if (
    !experimental ||
    typeof experimental !== "object" ||
    Array.isArray(experimental) ||
    typeof experimental.mcp_timeout !== "number"
  ) {
    return;
  }
  if (!Object.hasOwn(mcp, "timeout")) {
    mcp.timeout = { catalog: experimental.mcp_timeout, execution: experimental.mcp_timeout };
  }
  const migratedExperimental = { ...experimental };
  delete migratedExperimental.mcp_timeout;
  if (Object.keys(migratedExperimental).length === 0) {
    delete document.experimental;
  } else {
    document.experimental = migratedExperimental;
  }
}

function prepareOpenCodeConfig(filePath, servers, removed = [], defaults = {}) {
  const document = readJsonObject(filePath);
  const mcp = document.mcp === undefined ? {} : document.mcp;
  if (!mcp || typeof mcp !== "object" || Array.isArray(mcp)) {
    fail(`Expected mcp to be an object in ${filePath}`);
  }
  const serversIsNativeMap = mcp.servers !== undefined && !isOpenCodeServerConfig(mcp.servers);
  const nestedServers = serversIsNativeMap ? mcp.servers : undefined;
  if (
    nestedServers !== undefined &&
    (!nestedServers || typeof nestedServers !== "object" || Array.isArray(nestedServers))
  ) {
    fail(`Expected mcp.servers to be an object in ${filePath}`);
  }
  const legacyServers = Object.fromEntries(
    Object.entries(mcp).filter(
      ([name, config]) =>
        (name !== "servers" || !serversIsNativeMap) &&
        (name !== "timeout" || isOpenCodeServerConfig(config))
    )
  );
  const existingServers = { ...legacyServers, ...(nestedServers ?? {}) };
  const migratedServers = Object.fromEntries(
    Object.entries(existingServers).map(([name, config]) => [
      name,
      migrateOpenCodeServer(filePath, name, config)
    ])
  );
  const retainedMcp = { ...mcp };
  delete retainedMcp.servers;
  for (const name of Object.keys(legacyServers)) delete retainedMcp[name];
  migrateOpenCodeMcpTimeout(document, retainedMcp);
  document.mcp = { ...retainedMcp, servers: { ...migratedServers, ...servers } };
  for (const name of removed) delete document.mcp.servers[name];
  if (document.skills !== undefined) {
    document.skills = migrateOpenCodeSkills(filePath, document.skills);
  }
  for (const [key, value] of Object.entries(defaults)) {
    if (document[key] === undefined) document[key] = value;
  }
  return `${JSON.stringify(document, null, 2)}\n`;
}

export function mergeCodexToml(raw, url, bearerToken, serverName = "unity-mcp") {
  let document;
  try {
    document = raw.trim() ? parseToml(raw) : {};
  } catch {
    fail("Invalid TOML in .codex/config.toml; fix the syntax and re-run configure");
  }
  const servers = document.mcp_servers ?? {};
  if (typeof servers !== "object" || Array.isArray(servers)) fail("mcp_servers must be a table");
  const config =
    typeof url === "string"
      ? {
          url,
          ...(bearerToken ? { http_headers: { Authorization: `Bearer ${bearerToken}` } } : {})
        }
      : url;
  document.mcp_servers = servers;
  if (config === null) delete servers[serverName];
  else
    servers[serverName] = {
      ...config,
      startup_timeout_sec: 30,
      tool_timeout_sec: 300,
      enabled: true
    };
  return stringifyToml(document);
}

/** Every MCP client config this repository owns, keyed by the schema each client expects. */
export function clientConfigPaths(repoRoot) {
  return {
    claudeCode: path.join(repoRoot, ".mcp.json"),
    copilot: path.join(repoRoot, ".copilot", "mcp-config.json"),
    cursor: path.join(repoRoot, ".cursor", "mcp.json"),
    vscode: path.join(repoRoot, ".vscode", "mcp.json"),
    codex: path.join(repoRoot, ".codex", "config.toml"),
    openCode: path.join(repoRoot, "opencode.jsonc"),
    nanocoder: path.join(repoRoot, ".nanocoder", "mcp.json")
  };
}
const ZAI_SERVERS = ["web-search-prime", "web-reader", "zread", "zai-mcp-server"];
const OPEN_CODE_TOKEN_ENV = Object.freeze({
  "unity-mcp": "UNITY_MCP_BEARER_TOKEN",
  github: "GITHUB_TOKEN",
  "web-search-prime": "ZAI_API_KEY",
  "web-reader": "ZAI_API_KEY",
  zread: "ZAI_API_KEY"
});
export { ZAI_SERVERS };

function openCodeAuthHeaders(name, token) {
  if (!token) return undefined;
  const variable = OPEN_CODE_TOKEN_ENV[name];
  if (!variable) fail(`No OpenCode environment variable is defined for ${name}`);
  return { Authorization: `Bearer {env:${variable}}` };
}

function openCodeLocalEnvironment(environment) {
  if (!environment) return undefined;
  const result = { ...environment };
  if (result.Z_AI_API_KEY) result.Z_AI_API_KEY = "{env:ZAI_API_KEY}";
  return result;
}

/*
    A {env:NAME} reference resolves only when that exact name is exported. An
    accepted alias (Z_AI_API_KEY, GITHUB_PAT) would otherwise leave OpenCode
    sending an empty header and failing at request time.
*/
export function unresolvedOpenCodeVariables(options, values) {
  const referenced = [OPEN_CODE_TOKEN_ENV["unity-mcp"]];
  if (options.githubToken) referenced.push(OPEN_CODE_TOKEN_ENV.github);
  if (options.zaiToken) referenced.push(OPEN_CODE_TOKEN_ENV["web-search-prime"]);
  return [...new Set(referenced)].filter((variable) => !values[variable]);
}

function warnOnUnresolvedOpenCodeReferences(options) {
  const missing = unresolvedOpenCodeVariables(options, {
    ...readLocalEnv(options.repoRoot),
    ...process.env
  });
  if (missing.length === 0) return;
  console.warn(
    `[unity-mcp] OpenCode reads ${missing.join(", ")}. Export the listed names (ai-backends.sh env does) or those servers fail to authenticate.`
  );
}

// One catalog, rendered in each client's documented schema.
function clientServers(kind, options, url) {
  const catalog = {
    "unity-mcp": { url, token: options.bearerToken },
    github: { url: GITHUB_MCP_URL, token: options.githubToken },
    context7: { command: "context7-mcp", args: [] },
    git: { command: "mcp-server-git", args: ["--repository", options.repoRoot] },
    fetch: { command: "mcp-server-fetch", args: [] }
  };
  if (options.zaiToken) {
    for (const [name, endpoint] of [
      ["web-search-prime", "web_search_prime"],
      ["web-reader", "web_reader"],
      ["zread", "zread"]
    ]) {
      catalog[name] = { url: `https://api.z.ai/api/mcp/${endpoint}/mcp`, token: options.zaiToken };
    }
    catalog["zai-mcp-server"] = {
      command: "zai-mcp-server",
      args: [],
      env: { Z_AI_API_KEY: options.zaiToken, Z_AI_MODE: "ZAI" }
    };
  }
  return Object.fromEntries(
    Object.entries(catalog).map(([name, { url, token, command, args, env }]) => {
      const headers = token ? { Authorization: `Bearer ${token}` } : undefined;
      const openCodeHeaders = kind === "openCode" ? openCodeAuthHeaders(name, token) : undefined;
      let config;
      if (kind === "codex") {
        // ZAI returns an empty 200 without Content-Type for initialized notifications.
        // Codex's HTTP transport rejects it; the SDK-based adapter accepts it.
        config =
          url && ZAI_SERVERS.includes(name)
            ? {
                command: "mcp-remote",
                args: [
                  url,
                  "--transport",
                  "http-only",
                  "--header",
                  "Authorization:${ZAI_AUTH_HEADER}",
                  "--silent"
                ],
                env: { ZAI_AUTH_HEADER: `Bearer ${token}` }
              }
            : url
              ? { url, ...(headers ? { http_headers: headers } : {}) }
              : { command, args, ...(env ? { env } : {}) };
      } else if (kind === "openCode") {
        config = url
          ? { type: "remote", url, ...(openCodeHeaders ? { headers: openCodeHeaders, oauth: false } : {}) }
          : {
              type: "local",
              command: [command, ...args],
              ...(env ? { environment: openCodeLocalEnvironment(env) } : {})
            };
        Object.assign(config, {
          codemode: true,
          disabled: false,
          timeout: { catalog: 30000, execution: 300000 }
        });
      } else {
        config = url
          ? { url, ...(headers ? { headers } : {}) }
          : { command, args, ...(env ? { env } : {}) };
        config[kind === "nanocoder" ? "transport" : "type"] = url ? "http" : "stdio";
        if (kind === "copilot") config.tools = ["*"];
      }
      return [name, config];
    })
  );
}
export function configure(inputOptions, endpoint, beforeCommit) {
  const options = ensureBearerToken(inputOptions);
  warnOnUnresolvedOpenCodeReferences(options);
  const url = endpointUrl(endpoint);
  const paths = clientConfigPaths(options.repoRoot);
  const removed = options.zaiToken ? [] : ZAI_SERVERS;
  const writes = Object.entries(paths).map(([kind, file]) => {
    const servers = clientServers(kind, options, url);
    if (kind === "codex") {
      let raw = fs.existsSync(file) ? fs.readFileSync(file, "utf8") : "";
      for (const [name, config] of [
        ...Object.entries(servers),
        ...removed.map((name) => [name, null])
      ]) {
        raw = mergeCodexToml(raw, config, undefined, name);
      }
      return [file, raw];
    }
    if (kind === "openCode") {
      const defaults = {
        $schema: "https://opencode.ai/config.json",
        share: "disabled",
        skills: [OPEN_CODE_SKILLS_PATH]
      };
      return [file, prepareOpenCodeConfig(file, servers, removed, defaults)];
    }
    const collection = kind === "vscode" ? "servers" : "mcpServers";
    return [file, prepareJsonServers(file, collection, servers, removed)];
  });
  const written = transactionalWrite(writes, beforeCommit);
  for (const filePath of Object.values(paths)) fs.chmodSync(filePath, 0o600);
  return { url, written };
}

export function relayCandidates({
  platform = process.platform,
  arch = process.arch,
  home = os.homedir()
} = {}) {
  const root = path.join(home, ".unity", "relay");
  const names =
    platform === "win32"
      ? ["relay_win.exe", "relay_windows.exe", "relay.exe"]
      : platform === "darwin"
        ? [
            `relay_mac_${arch}.app/Contents/MacOS/relay_mac_${arch}`,
            `relay_macos_${arch}.app/Contents/MacOS/relay_macos_${arch}`,
            `relay_mac_${arch}`,
            "relay_mac",
            "relay"
          ]
        : platform === "linux"
          ? [`relay_linux_${arch}`, "relay_linux", "relay"]
          : [];
  return names.map((name) => path.join(root, ...name.split("/")));
}

export function findRelay(override, runtime = {}) {
  const candidates = override ? [path.resolve(override)] : relayCandidates(runtime);
  const found = candidates.find((candidate) => {
    try {
      if (!fs.statSync(candidate).isFile()) return false;
      if ((runtime.platform ?? process.platform) !== "win32")
        fs.accessSync(candidate, fs.constants.X_OK);
      return true;
    } catch {
      return false;
    }
  });
  if (!found) {
    fail(
      `Unity MCP relay not found or not executable. ${
        override ? `Checked: ${candidates[0]}` : `Searched: ${candidates.join(", ")}`
      }`
    );
  }
  return found;
}

export function buildRelayArgs(projectPath) {
  return ["--mcp", "--project-path", path.resolve(projectPath)];
}

export async function assertPortAvailable(port, host = DEFAULTS.bindHost) {
  await new Promise((resolve, reject) => {
    const server = net.createServer();
    server.unref();
    server.once("error", (error) =>
      reject(new Error(`Port ${port} is unavailable on ${host}: ${error.message}`))
    );
    server.listen({ port, host, exclusive: true }, () => server.close(resolve));
  });
}

function authorized(request, token) {
  const received = Buffer.from(request.headers.authorization ?? "");
  const expected = Buffer.from(`Bearer ${token}`);
  return received.length === expected.length && timingSafeEqual(received, expected);
}

function log(options, level, message) {
  if (options.logLevel === "none" || (level === "debug" && options.logLevel !== "debug")) {
    return;
  }
  (level === "error" ? console.error : console.log)(message);
}

// Preserve client-error status codes instead of reporting retryable HTTP 500 errors.
function bodyError(message, httpStatus, code, rpcMessage) {
  return Object.assign(new Error(message), { httpStatus, rpc: { code, message: rpcMessage } });
}

function readJsonBody(request, limitBytes, timeoutMs) {
  return new Promise((resolve, reject) => {
    let size = 0;
    const chunks = [];
    const timer = setTimeout(() => {
      // Without this a client that sends Content-Length headers and no body pins the socket (and
      // therefore shutdown) until Node's request timeout, which defaults to five minutes.
      request.pause();
      reject(bodyError("Request body timed out", 408, -32001, "Request body timed out"));
    }, timeoutMs);
    timer.unref();
    const settle = (action, value) => {
      clearTimeout(timer);
      action(value);
    };
    request.on("data", (chunk) => {
      size += chunk.length;
      if (size > limitBytes) {
        // Pause rather than destroy: the handler still has to write a 413, and destroying the socket
        // first is what turns an over-large body into an opaque ECONNRESET for the client.
        request.pause();
        settle(reject, bodyError("Request body too large", 413, -32600, "Request body too large"));
        return;
      }
      chunks.push(chunk);
    });
    request.once("error", (error) => settle(reject, error));
    request.once("end", () => {
      const raw = Buffer.concat(chunks).toString("utf8");
      if (!raw.trim()) {
        settle(resolve, undefined);
        return;
      }
      try {
        settle(resolve, JSON.parse(raw));
      } catch (error) {
        settle(
          reject,
          bodyError(`Invalid JSON body: ${error.message}`, 400, -32700, "Parse error")
        );
      }
    });
  });
}

function sendJson(response, statusCode, payload, closeConnection = false) {
  const body = JSON.stringify(payload);
  const headers = {
    "Content-Type": "application/json",
    "Content-Length": Buffer.byteLength(body)
  };
  if (closeConnection) {
    // The request body was never drained, so the connection cannot be reused.
    headers.Connection = "close";
  }
  response.writeHead(statusCode, headers);
  response.end(body);
}

export async function startBridge(inputOptions, runtime = {}) {
  const options = ensureBearerToken(inputOptions);
  const projectPath = requireProjectPath(options);
  const relayPath =
    options.backend === "relay"
      ? findRelay(options.relayPath, runtime.relayRuntime)
      : (options.cliPath ?? "unity");
  await assertPortAvailable(options.port, options.bindHost);

  const maxSessions = options.maxSessions ?? DEFAULTS.maxSessions;
  const bodyTimeout = Math.min(options.sessionTimeout ?? Infinity, DEFAULTS.bodyTimeout);
  const sessions = new Map();
  const provisionalSessions = new Set();
  let starting = 0;

  const disposeSession = async (session) => {
    if (!session || session.disposed) return;
    log(options, "debug", `Disposing session ${session.sessionId ?? "(provisional)"}`);
    session.disposed = true;
    provisionalSessions.delete(session);
    if (session.sessionId) {
      sessions.delete(session.sessionId);
    }
    clearTimeout(session.timer);
    if (!session.stopping) {
      session.stopping = true;
      if (session.child.exitCode === null && session.child.signalCode === null) {
        session.child.kill("SIGTERM");
      }
      const force = setTimeout(() => {
        if (session.child.exitCode === null && session.child.signalCode === null) {
          session.child.kill("SIGKILL");
        }
      }, 3_000);
      force.unref();
    }
    await session.transport.close().catch(() => {});
  };

  const armTimeout = (sessionId, timeout, idleOnly = false) => {
    const session = sessions.get(sessionId);
    if (!session || (idleOnly && session.pendingRequests.size)) {
      return;
    }
    clearTimeout(session.timer);
    session.timer = setTimeout(() => {
      disposeSession(session).catch(() => {});
    }, timeout);
    session.timer.unref();
  };

  const touch = (id) => armTimeout(id, options.sessionTimeout, true);
  const armRequestTimeout = (id) => armTimeout(id, options.requestTimeout);

  const createSession = async () => {
    let sessionId;
    const transport = new StreamableHTTPServerTransport({
      sessionIdGenerator: () => randomUUID(),
      enableJsonResponse: true,
      onsessioninitialized: (id) => {
        sessionId = id;
        session.sessionId = id;
        provisionalSessions.delete(session);
        sessions.set(id, session);
        log(options, "debug", `Session ${id} initialized (${sessions.size}/${maxSessions})`);
        touch(id);
      }
    });
    const server = new Server(
      { name: "dxcommandterminal-unity-mcp-bridge", version: "1.0.0" },
      { capabilities: {} }
    );
    await server.connect(transport);
    const relayArgs =
      options.backend === "relay"
        ? buildRelayArgs(projectPath)
        : ["mcp", "--project-path", projectPath];
    log(options, "debug", `Spawning relay: ${relayPath} ${relayArgs.join(" ")}`);
    const child = runtime.spawnRelay
      ? runtime.spawnRelay(relayPath, relayArgs)
      : spawn(relayPath, relayArgs, {
          stdio: ["pipe", "pipe", "pipe"],
          shell: false,
          cwd: projectPath,
          windowsHide: true
        });
    const session = {
      child,
      server,
      transport,
      timer: undefined,
      stopping: false,
      disposed: false,
      sessionId: undefined,
      pendingRequests: new Map()
    };
    provisionalSessions.add(session);
    // A session that never reaches `onsessioninitialized` holds a live relay child, so it gets the
    // short idle timeout rather than the multi-minute active-request budget.
    session.timer = setTimeout(() => {
      disposeSession(session).catch(() => {});
    }, options.sessionTimeout);
    session.timer.unref();

    let buffer = "";
    child.stdout.on("data", (chunk) => {
      buffer += chunk.toString("utf8");
      const lines = buffer.split(/\r?\n/);
      buffer = lines.pop() ?? "";
      for (const line of lines) {
        if (!line.trim()) continue;
        try {
          const message = JSON.parse(line);
          if (message.id !== undefined && !message.method) {
            const requestKey = `${typeof message.id}:${message.id}`;
            if (
              session.pendingRequests.get(requestKey) === "tools/list" &&
              message.result?.tools?.length === 0 &&
              message.result.nextCursor === undefined
            ) {
              delete message.result;
              message.error = {
                code: -32002,
                message: `Unity has no tools for ${projectPath}. Check that Pipeline is loaded and running in this Editor, then reconnect. Run unity:mcp:probe to verify editor readiness.`
              };
            }
            session.pendingRequests.delete(requestKey);
          }
          if (sessionId) touch(sessionId);
          Promise.resolve(transport.send(message)).catch((error) =>
            log(options, "error", `Relay response failed: ${error.message}`)
          );
        } catch {
          log(options, "error", `Unity relay emitted non-JSON output: ${line.slice(0, 200)}`);
        }
      }
    });
    child.stderr.on("data", (chunk) =>
      log(options, "error", `Unity relay: ${chunk.toString("utf8").trimEnd()}`)
    );
    child.stdin.on("error", (error) => {
      log(options, "error", `Unity relay input failed: ${error.message}`);
      disposeSession(session).catch(() => {});
    });
    child.once("error", (error) => {
      log(options, "error", `Unity relay failed: ${error.message}`);
      disposeSession(session).catch(() => {});
    });
    child.once("exit", () => {
      if (!session.stopping) disposeSession(session).catch(() => {});
    });

    transport.onmessage = (message) => {
      const startsRequest = message.id !== undefined && message.method;
      const wasIdle = session.pendingRequests.size === 0;
      if (startsRequest) {
        session.pendingRequests.set(
          `${typeof message.id}:${message.id}`,
          message.method === "tools/list" && message.params?.cursor !== undefined
            ? "tools/list/page"
            : message.method
        );
      }
      child.stdin.write(`${JSON.stringify(message)}\n`);
      if (sessionId && startsRequest && wasIdle && session.pendingRequests.size) {
        armRequestTimeout(sessionId);
      } else if (sessionId) {
        touch(sessionId);
      }
    };
    transport.onclose = () => {
      disposeSession(session).catch(() => {});
    };
    transport.onerror = (error) => {
      log(options, "error", `MCP transport error: ${error.message}`);
      disposeSession(session).catch(() => {});
    };
    return transport;
  };

  const handle = async (request, response) => {
    try {
      const url = new URL(request.url ?? "/", `http://${request.headers.host ?? "localhost"}`);
      // Liveness only; it reveals nothing, so it is deliberately outside the bearer check. A probe
      // that has to hold the token is not a probe an orchestrator can run.
      if (url.pathname === "/healthz") {
        response.writeHead(200, { "Content-Type": "text/plain" });
        response.end("ok");
        return;
      }
      if (!authorized(request, options.bearerToken)) {
        response.setHeader("WWW-Authenticate", "Bearer");
        sendJson(response, 401, { error: "Unauthorized" });
        return;
      }
      if (
        url.pathname !== options.endpointPath ||
        !["POST", "GET", "DELETE"].includes(request.method ?? "")
      ) {
        sendJson(response, 404, { error: "Not found" });
        return;
      }

      const body =
        request.method === "POST"
          ? await readJsonBody(request, DEFAULTS.bodyLimitBytes, bodyTimeout)
          : undefined;
      const sessionId = request.headers["mcp-session-id"];
      let transport = sessionId ? sessions.get(sessionId)?.transport : undefined;
      if (!transport && request.method === "POST" && !sessionId && isInitializeRequest(body)) {
        // Every session owns a relay child process, so the count is capped rather than unbounded.
        // `starting` is bumped synchronously because createSession awaits before it registers.
        if (sessions.size + provisionalSessions.size + starting >= maxSessions) {
          sendJson(response, 503, {
            jsonrpc: "2.0",
            id: null,
            error: {
              code: -32000,
              message: `Too many concurrent MCP sessions (limit ${maxSessions}); close one or raise --max-sessions`
            }
          });
          return;
        }
        starting += 1;
        try {
          transport = await createSession();
        } finally {
          starting -= 1;
        }
      }
      if (!transport) {
        sendJson(response, sessionId ? 404 : 400, {
          jsonrpc: "2.0",
          id: null,
          error: {
            code: -32001,
            message: sessionId ? "Session not found" : "Initialize request required"
          }
        });
        return;
      }
      if (sessionId) touch(sessionId);
      await transport.handleRequest(request, response, body);
    } catch (error) {
      const status = error.httpStatus ?? 500;
      if (!response.headersSent) {
        // 413 and 408 both leave the request body undrained, so the socket cannot be reused.
        sendJson(
          response,
          status,
          {
            jsonrpc: "2.0",
            id: null,
            error: error.rpc ?? { code: -32603, message: "Bridge failure" }
          },
          status === 413 || status === 408
        );
      }
      log(options, status === 500 ? "error" : "debug", `Bridge request failed: ${error.message}`);
    }
  };

  const httpServer = http.createServer((request, response) => {
    // The catch inside `handle` can itself throw (a socket that died mid-response), and an unhandled
    // rejection is fatal to the process by default, so the outer promise is always caught.
    handle(request, response).catch((error) => {
      log(options, "error", `Bridge handler crashed: ${error.message}`);
      response.destroy();
    });
  });

  await new Promise((resolve, reject) => {
    httpServer.once("error", reject);
    httpServer.listen(options.port, options.bindHost, resolve);
  });

  let closeResolve;
  const closed = new Promise((resolve) => {
    closeResolve = resolve;
  });
  let closing = false;
  const close = async () => {
    if (closing) {
      return closed;
    }
    closing = true;
    await Promise.all(
      [...new Set([...sessions.values(), ...provisionalSessions])].map(disposeSession)
    );
    const stopped = new Promise((resolve) => httpServer.close(resolve));
    // Without this, an idle keep-alive socket or a client that stalled mid-body keeps `close()`
    // pending until Node's 300s request timeout expires.
    httpServer.closeAllConnections();
    await stopped;
    closeResolve();
    return closed;
  };
  return { close, closed, httpServer, options, bearerToken: options.bearerToken };
}

// Commands

// --no-discover narrows candidates but still checks readiness.
async function resolveEndpoint(options, runtime = {}) {
  const configured = {
    host: options.host,
    port: options.port,
    endpointPath: options.endpointPath
  };
  const narrowed = options.discover
    ? runtime
    : { ...runtime, candidates: runtime.candidates ?? [configured] };

  const { found, attempts } = await discoverEndpoint(options, narrowed);
  if (found) {
    return {
      endpoint: { host: found.host, port: found.port, endpointPath: found.endpointPath },
      attempts,
      found
    };
  }
  // With discovery off the caller named the endpoint, so keep it: `configure` still writes it, and
  // `probe` reports the attempt that failed rather than an empty list.
  return { endpoint: options.discover ? undefined : configured, attempts };
}

export async function runProbe(options, runtime = {}) {
  const { found, attempts } = await resolveEndpoint(options, { ...runtime, readiness: "editor" });
  if (!found) {
    fail(
      `No Unity MCP endpoint is ready for editor-backed calls. Attempts:\n${describeAttempts(attempts)}`
    );
  }
  // Report only the readiness level actually verified.
  const proven = found.editorToolAdvertised
    ? `is ready for editor-backed calls (${found.editorTool} answered)`
    : `advertises Unity_RunCommand, but has no ${EDITOR_READY_TOOL} to prove an editor is behind it`;
  console.log(`Unity MCP at ${found.url} ${proven} (protocol ${found.protocolVersion}).`);
  return found;
}

export async function runConfigure(options, runtime = {}) {
  const { endpoint, attempts, found } = options.offline
    ? { endpoint: options, attempts: [] }
    : await resolveEndpoint(options, runtime);
  // Never mint a replacement token when a running bridge rejected the current one.
  const unauthorized = found ? undefined : attempts.find((a) => a.status === "unauthorized");
  if (unauthorized) {
    fail(
      `A Unity MCP bridge is running at ${unauthorized.url} but rejected the bearer token ` +
        `(${unauthorized.detail}). Nothing was written and no token was generated: copy ` +
        `${ENV_KEYS.bearerToken} from the host's .env.local into ` +
        `${path.join(options.repoRoot, ".env.local")}, or pass --token, then re-run configure.`
    );
  }
  const target = endpoint ?? {
    host: options.host,
    port: options.port,
    endpointPath: options.endpointPath
  };
  if (!found && !options.offline) {
    console.warn(
      `No Unity MCP endpoint completed initialization; configuring ${endpointUrl(target)} anyway. Attempts:\n${describeAttempts(attempts)}`
    );
  }
  const { url, written } = configure(options, target);
  const summary = written.length
    ? written.map((filePath) => path.relative(options.repoRoot, filePath)).join(", ")
    : "no changes";
  console.log(`Configured agent MCP servers; Unity endpoint ${url} (${summary}).`);
  if (!options.zaiToken)
    console.log("Z.AI servers need Z_AI_API_KEY or ZAI_API_KEY in .env.local.");
  return url;
}

export async function runBridge(options) {
  const running = await startBridge(options);
  console.log(`Unity project: ${running.options.projectPath}`);
  console.log(
    `Unity MCP bridge: http://${running.options.bindHost}:${running.options.port}${running.options.endpointPath} (bearer authentication required)`
  );
  const stop = () => {
    running.close().catch((error) => console.error(`Bridge shutdown failed: ${error.message}`));
  };
  process.once("SIGINT", stop);
  process.once("SIGTERM", stop);
  await running.closed;
  process.removeListener("SIGINT", stop);
  process.removeListener("SIGTERM", stop);
}

// ---------------------------------------------------------------------------
// Unity state capture (agentic harness support)
// ---------------------------------------------------------------------------

export const CAPTURE_PACKAGE_NAME = "com.wallstop-studios.dxcommandterminal";
const CAPTURE_SOURCE_NAME = "DxTerminalStateCapture.cs.txt";
const CAPTURE_TARGET_NAME = "DxTerminalStateCapture.cs";
const REPORTER_SOURCE_NAME = "DxTerminalTestRunReporter.cs.txt";
const REPORTER_TARGET_NAME = "DxTerminalTestRunReporter.cs";
// Both installed dev tools ship as maintained sources outside Unity compilation.
export const CAPTURE_SCRIPTS = Object.freeze([
  Object.freeze({ source: CAPTURE_SOURCE_NAME, target: CAPTURE_TARGET_NAME }),
  Object.freeze({ source: REPORTER_SOURCE_NAME, target: REPORTER_TARGET_NAME })
]);
// The eval compiler does not reference Assembly-CSharp-Editor, so the capture
// type is unreachable by name; only assembly-qualified reflection resolves it
// (issue #127). The simple name does not resolve: the namespace is required.
const CAPTURE_TYPE_NAME = "DxTerminalDevTools.DxTerminalStateCapture, Assembly-CSharp-Editor";
const CAPTURE_TYPE_PROBE = `return (System.Type.GetType("${CAPTURE_TYPE_NAME}") != null);`;
const CAPTURE_REFRESH_EXPRESSION = "UnityEditor.AssetDatabase.Refresh();";

export function captureScriptFiles(repoRoot = REPO_ROOT) {
  return CAPTURE_SCRIPTS.map(({ source, target }) => ({
    source: path.join(repoRoot, "tooling~", "scripts", "mcp", source),
    target
  }));
}

export function captureScriptSourcePath(repoRoot = REPO_ROOT) {
  return captureScriptFiles(repoRoot)[0].source;
}

export function captureInstallTarget(projectPath, target = CAPTURE_TARGET_NAME) {
  return path.join(path.resolve(projectPath), "Assets", "Editor", target);
}

export function captureArtifactRoot(projectPath, layoutPath = projectPath) {
  const project = path.resolve(projectPath);
  // The package probe needs a locally visible root: a host path does not exist
  // in a container, which would silently select the Library fallback.
  const packageRoot = path.join(path.resolve(layoutPath), "Packages", CAPTURE_PACKAGE_NAME);
  return fs.existsSync(packageRoot)
    ? path.join(project, "Packages", CAPTURE_PACKAGE_NAME, ".artifacts", "unity-state")
    : path.join(project, "Library", "DxTerminalStateCapture");
}

export function captureOutputDir(projectPath, utcStamp, layoutPath = projectPath) {
  return path.join(captureArtifactRoot(projectPath, layoutPath), utcStamp);
}

function captureStamp(date = new Date()) {
  return date.toISOString().replace(/[:.]/g, "-");
}

/**
 * Statement-form eval expression that invokes a public static
 * DxTerminalStateCapture method through assembly-qualified reflection. The
 * eval compiler cannot name the host's Assembly-CSharp-Editor types directly.
 */
export function captureInvocationExpression(methodName, outputDirectory) {
  const directory = outputDirectory.replace(/\\/g, "/").replace(/"/g, '""');
  return [
    `var captureType = System.Type.GetType("${CAPTURE_TYPE_NAME}");`,
    "if (captureType == null) return null;",
    `var captureMethod = captureType.GetMethod("${methodName}", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);`,
    "if (captureMethod == null) return null;",
    `return (string)captureMethod.Invoke(null, new object[] { @"${directory}" });`
  ].join("\n");
}

function parseEnvelope(text) {
  try {
    const parsed = JSON.parse(text);
    if (parsed !== null && typeof parsed === "object" && !Array.isArray(parsed)) {
      return parsed;
    }
  } catch {}
  return null;
}

/**
 * Decode an eval tool answer. The current backend wraps results in a JSON
 * envelope ({output, diagnostics, success, result}); matching the raw text
 * false-positives on `"success": true`. Returns the decoded result when the
 * envelope carries one, else the untouched text.
 */
export function evalResultText(text) {
  const parsed = parseEnvelope(text);
  if (parsed !== null && Object.hasOwn(parsed, "result")) {
    const result = parsed.result;
    if (result === null || result === undefined) return "";
    return typeof result === "string" ? result : JSON.stringify(result);
  }
  return text;
}

/**
 * The eval backend reports runtime failures as success:false envelopes
 * without raising a tool error, so the failure text must be surfaced
 * explicitly or capture failures become deadline timeouts. Returns the
 * failure message, or null when the answer is not a failed envelope.
 */
export function evalFailure(text) {
  const parsed = parseEnvelope(text);
  if (parsed === null || parsed.success !== false) return null;
  const diagnostics = (Array.isArray(parsed.diagnostics) ? parsed.diagnostics : [])
    .map((entry) => (typeof entry === "string" ? entry : entry?.message))
    .filter((message) => typeof message === "string" && 0 < message.length);
  const message = [parsed.errorDetails, parsed.error, ...diagnostics].find(
    (candidate) => typeof candidate === "string" && 0 < candidate.length
  );
  return message ?? "eval failed";
}

/** Decoded-answer predicates; exported for the envelope-regression tests. */
export function evalAnswerIsTrue(text) {
  return /^true$/i.test(evalResultText(text).trim());
}

export function evalAnswerIsFalse(text) {
  return /^false$/i.test(evalResultText(text).trim());
}

/**
 * Host-side installation of the maintained dev-tool sources. An existing
 * different file is backed up under the artifact root; never silently
 * clobbered. Returns one result per script, in install order.
 */
export function ensureCaptureScripts(projectPath, repoRoot = REPO_ROOT) {
  return captureScriptFiles(repoRoot).map(({ source, target: name }) => {
    const sourceText = fs.readFileSync(source, "utf8");
    const target = captureInstallTarget(projectPath, name);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    let existing = null;
    try {
      existing = fs.readFileSync(target, "utf8");
    } catch {}
    if (existing === sourceText) return { target, changed: false, backup: undefined };
    let backup;
    if (existing !== null) {
      // projectPath is always the locally writable root here, so the default
      // layout probe is the correct one.
      const backupDir = path.join(captureArtifactRoot(projectPath), "backup");
      fs.mkdirSync(backupDir, { recursive: true });
      backup = path.join(backupDir, `${name}.${captureStamp()}.bak`);
      fs.copyFileSync(target, backup);
    }
    atomicWrite(target, sourceText);
    return { target, changed: true, backup };
  });
}

export async function runInstallCapture(options) {
  const projectPath = requireProjectFilesystemPath(options);
  const results = ensureCaptureScripts(projectPath, options.repoRoot);
  for (const result of results) {
    if (result.changed) {
      console.log(
        `Installed ${result.target}${result.backup ? ` (previous copy saved to ${result.backup})` : ""}.`
      );
    } else {
      console.log(`Dev tool already current: ${result.target}`);
    }
  }
  console.log("Unity will import it on the next refresh; then run npm run unity:capture.");
  return results.map((result) => result.target);
}

// Persistent MCP session for editor-backed commands (unlike the disposable probe session).
async function withMcpSession(options, endpoint, run, fetchImpl = fetch) {
  const url = endpointUrl(endpoint);
  const authorization = options.bearerToken
    ? { Authorization: `Bearer ${options.bearerToken}` }
    : {};
  const connect = async () => {
    let sessionId;
    const transport = new StreamableHTTPClientTransport(new URL(url), {
      requestInit: { headers: authorization },
      // The session id only appears on the initialize response, so the
      // transport's fetch is the one place that can capture it.
      fetch: async (target, init = {}) => {
        const response = await fetchImpl(target, init);
        sessionId ||= response.headers.get("mcp-session-id") ?? undefined;
        return response;
      }
    });
    const connected = new Client({ name: "unity-mcp-capture", version: "1.0.0" });
    await connected.connect(transport);
    const release = async () => {
      await connected.close().catch(() => {});
      const warning = await deleteBridgeSession({
        url,
        authorization,
        sessionId,
        protocolVersion: transport.protocolVersion ?? options.protocolVersion,
        fetchImpl,
        timeoutMs: options.timeout
      });
      if (warning !== null) console.warn(`${url}: ${warning}`);
    };
    return { client: connected, release };
  };
  const client = reconnectingSession(connect);
  try {
    return await run(client);
  } finally {
    await client.close().catch(() => {});
  }
}

/**
 * A session that rebuilds its transport once when a call throws.
 *
 * The editor serves MCP calls on its main thread, so a request that lands
 * while the editor is busy (a test run winding down, a domain reload) can time
 * the session's stream out. The SDK then rejects every later call on that
 * session instantly with the recorded reason, so one unlucky request kills the
 * rest of the command: the observed symptom was `test_status` failing in 0 ms
 * with "The operation was aborted due to timeout" while the editor answered
 * the same call fine from a new session. Reconnecting turns a dead session
 * into one retry.
 */
function reconnectingSession(connect) {
  let session = null;
  let pending = null;

  const live = async () => {
    if (session !== null) return session;
    pending ??= connect().then(
      (connected) => {
        session = connected;
        pending = null;
        return connected;
      },
      (error) => {
        pending = null;
        throw error;
      }
    );
    return pending;
  };

  const withReconnect = async (invoke) => {
    for (let attempt = 0; attempt < 2; ++attempt) {
      const current = await live();
      try {
        return await invoke(current.client);
      } catch (error) {
        if (attempt == 1) throw error;
        // A dropped transport would fail every later call on this session, so
        // release it (the DELETE is what frees the editor's slot) and rebuild.
        await current.release().catch(() => {});
        session = null;
      }
    }
    fail("The bridge session could not complete a call after reconnecting.");
  };

  return {
    callTool: (...args) => withReconnect((current) => current.callTool(...args)),
    listTools: (...args) => withReconnect((current) => current.listTools(...args)),
    close: async () => {
      const current = session;
      session = null;
      await current?.release().catch(() => {});
    }
  };
}

/** Beta backend tool schemas drift; try each documented argument shape until one answers. */
async function callFirstWorking(client, candidates, signal) {
  const tried = [];
  for (const candidate of candidates) {
    try {
      const call = await client.callTool(candidate, undefined, { signal });
      if (call.isError) throw new Error(extractText(call).slice(0, 200) || "tool reported an error");
      return { candidate, call };
    } catch (error) {
      tried.push(`${candidate.name}: ${error.message}`);
    }
  }
  fail(`No backend tool variant answered. Tried:\n  ${tried.join("\n  ")}`);
}

function extractText(call) {
  return (call.content ?? [])
    .map((part) => part.text ?? "")
    .join(" ")
    .trim();
}

async function listAllTools(client, signal) {
  const names = [];
  let cursor;
  for (let page = 0; page < 100; page += 1) {
    const listed = await client.listTools(cursor === undefined ? {} : { cursor }, { signal });
    names.push(...listed.tools.map((tool) => tool.name));
    if (listed.nextCursor === undefined) return names;
    cursor = listed.nextCursor;
  }
  fail("tools/list exceeded 100 pages");
}

/**
 * Editor state capture through the bridge: install (when local), refresh, invoke
 * DxTerminalStateCapture.CaptureAll, and wait for the manifest to complete.
 */
export async function runCapture(options, runtime = {}) {
  const fetchImpl = runtime.fetchImpl ?? fetch;
  // This budget covers discovery, the session connect, and the test run, so it
  // is shorter than the number suggests by however long those take.
  const captureTimeout = Math.max(options.timeout, 120_000);
  const deadline = Date.now() + captureTimeout;
  const { found } = await discoverEndpoint(options, { ...runtime, readiness: "tools" });
  if (!found) fail("No Unity MCP endpoint with tools found; run npm run unity:mcp:probe for detail.");
  console.log(`Capturing via ${found.url}.`);

  return withMcpSession(options, found, async (client) => {
    const signal = AbortSignal.timeout(captureTimeout);
    const tools = await listAllTools(client, signal);
    const available = new Set(tools);
    const hasEval = available.has("eval") || available.has("Unity_RunCommand");
    if (!hasEval) fail("The backend advertises no eval/Unity_RunCommand tool; cannot drive capture.");

    const evalCall = (expression) =>
      callFirstWorking(
        client,
        [
          { name: "eval", arguments: { code: expression } },
          { name: "eval", arguments: { expression } },
          { name: "Unity_RunCommand", arguments: { Command: expression } },
          { name: "Unity_RunCommand", arguments: { command: expression } }
        ],
        signal
      );

    // Install through the container bind mount when one is configured. The
    // bridge still receives the host project path for deterministic port and
    // artifact paths because Unity runs on the host.
    const projectPath = options.projectPath;
    const filesystemProjectPath = options.projectContainerPath ?? options.projectPath;
    const install = () => {
      const results = ensureCaptureScripts(filesystemProjectPath, options.repoRoot);
      for (const result of results) {
        if (result.changed) console.log(`Installed dev tool: ${result.target}`);
      }
      return results.some((result) => result.changed);
    };
    let installed = false;
    if (filesystemProjectPath && !options.noInstall && fs.existsSync(filesystemProjectPath)) {
      installed = install();
    }

    const typePresent = async () => {
      const { call } = await evalCall(CAPTURE_TYPE_PROBE);
      return evalAnswerIsTrue(extractText(call));
    };
    if (!(await typePresent())) {
      if (!installed && filesystemProjectPath && fs.existsSync(filesystemProjectPath)) {
        install();
        await evalCall(CAPTURE_REFRESH_EXPRESSION);
        await waitForEditorIdle(client, evalCall, deadline);
      }
      if (!(await typePresent())) {
        fail(
          `DxTerminalStateCapture is not compiled in the editor. Run ` +
            `\`npm run unity:mcp:install-capture -- --project ${projectPath ?? "<host-project>"}\` ` +
            "on the host, let Unity recompile, then re-run capture."
        );
      }
    }

    // Wait for a quiescent editor before touching the asset database.
    await waitForEditorIdle(client, evalCall, deadline);

    const refresh = await callFirstWorking(
      client,
      [
        { name: "menu", arguments: { menuPath: "Assets/Refresh" } },
        { name: "menu", arguments: { path: "Assets/Refresh" } },
        { name: "Unity_ManageMenuItem", arguments: { MenuPath: "Assets/Refresh" } },
        { name: "eval", arguments: { code: CAPTURE_REFRESH_EXPRESSION } },
        { name: "eval", arguments: { expression: CAPTURE_REFRESH_EXPRESSION } }
      ],
      signal
    );
    log(options, "debug", `Refresh via ${refresh.candidate.name}`);
    await waitForEditorIdle(client, evalCall, deadline);

    const outputDirectory =
      options.out ??
      captureOutputDir(projectPath ?? ".", captureStamp(), filesystemProjectPath ?? projectPath);
    const summary = await evalCall(
      captureInvocationExpression("CaptureAll", outputDirectory)
    );
    assertEvalAnswer(summary, "CaptureAll");
    const manifest = parseCaptureSummary(
      evalResultText(extractText(summary.call)),
      outputDirectory
    );

    // Game-view pixels are written a few frames later; poll the manifest to completion.
    const complete = await pollCaptureCompletion(
      client,
      evalCall,
      manifest.outputDirectory ?? outputDirectory,
      deadline
    );
    console.log(`Unity state captured: ${manifest.outputDirectory ?? outputDirectory}`);
    for (const artifact of complete.artifacts ?? manifest.artifacts ?? []) {
      console.log(`  ${artifact}`);
    }
    return complete;
  }, fetchImpl);
}

function parseCaptureSummary(text, fallbackDirectory) {
  try {
    const parsed = JSON.parse(text);
    if (parsed && typeof parsed === "object") return parsed;
  } catch {}
  return { outputDirectory: fallbackDirectory, artifacts: [], complete: false, raw: text };
}

async function pollCaptureCompletion(client, evalCall, outputDirectory, deadline) {
  const expression = captureInvocationExpression("CaptureStatus", outputDirectory);
  while (Date.now() < deadline) {
    const { call } = await evalCall(expression);
    const answer = extractText(call);
    const failure = evalFailure(answer);
    if (failure !== null) fail(`Capture status poll failed: ${failure}`);
    const status = parseCaptureSummary(evalResultText(answer), outputDirectory);
    if (status.complete) return status;
    if (status.gameViewError) fail(`Game view capture failed: ${status.gameViewError}`);
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  fail(`Capture did not complete within the deadline; inspect ${outputDirectory}`);
}

/** Fail fast when an eval answer reports a runtime failure envelope. */
function assertEvalAnswer(evalAnswer, action) {
  const failure = evalFailure(extractText(evalAnswer.call));
  if (failure !== null) fail(`${action} failed: ${failure}`);
}

async function waitForEditorIdle(client, evalCall, deadline) {
  const expression =
    "return (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating);";
  while (Date.now() < deadline) {
    /*
        A call that lands while the editor is busy does not answer at all: the
        bridge reports a main-thread timeout and the SDK poisons the session. That
        is the state this wait exists for, so it counts as busy and keeps asking.
     */
    try {
      const { call } = await evalCall(expression);
      if (evalAnswerIsFalse(extractText(call))) return;
    } catch {}

    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
  fail("The editor did not reach an idle (non-compiling) state before the deadline.");
}

/*
    A test run additionally needs play mode over: a PlayMode run leaves the
    editor tearing the session down, and the editor does not answer status
    calls while it does. The capture path must not wait for this - capturing in
    play mode is a supported flow - so the wait lives with the test path.

    Exported for the idle-wait tests: a call the busy editor refuses is the
    expected answer here, not a reason to fail the command.
 */
export async function waitForTestIdle(client, evalCall, deadline, now = () => Date.now()) {
  const expression =
    "return (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode);";
  while (now() < deadline) {
    try {
      const { call } = await evalCall(expression);
      if (evalAnswerIsFalse(extractText(call))) return;
    } catch {}

    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
  fail(
    "The editor stayed busy (compiling or in play mode) until the deadline; "
      + "cannot start a test run."
  );
}

// ---------------------------------------------------------------------------
// T04 fixture capture: run the capture tests over the bridge and validate the
// manifests they write under .artifacts/t4/.
// ---------------------------------------------------------------------------

// The scenario registries live in ../t11/scenarios.mjs (pure data, stdlib-free)
// so Unity-free consumers import them without this SDK-coupled module.
import {
  T4_ALL_SCENARIOS,
  T4_DEFAULT_SCENARIOS,
  T4_EXPECTED_INCOMPLETE,
  T4_TEST_FILTER,
  T4_VARIANT_SCENARIOS
} from "../t11/scenarios.mjs";
export {
  T4_ALL_SCENARIOS,
  T4_DEFAULT_SCENARIOS,
  T4_EXPECTED_INCOMPLETE,
  T4_TEST_FILTER,
  T4_VARIANT_SCENARIOS
};

export function parseT4Scenarios(raw) {
  if (raw === undefined || raw === null) return [...T4_ALL_SCENARIOS];
  // Idempotent: callers may pass the comma-separated CLI string or an
  // already-parsed array (main parses once; runT4Capture re-validates).
  const names = (Array.isArray(raw) ? raw : String(raw).split(","))
    .map((name) => String(name).trim())
    .filter((name) => name.length > 0);
  if (names.length === 0) fail("--scenarios lists at least one scenario name");
  const invalid = names.filter((name) => !/^[A-Za-z][A-Za-z0-9]*$/.test(name));
  if (invalid.length > 0) fail(`Invalid scenario name(s): ${invalid.join(", ")}`);
  return names;
}

/** Schema + completeness check for one capture manifest object. */
export function validateT4Manifest(manifest, expectComplete) {
  const problems = [];
  if (manifest === null || typeof manifest !== "object" || Array.isArray(manifest)) {
    return ["manifest is not a JSON object"];
  }
  for (const field of ["scenario", "capturedUtc", "unityVersion", "graphicsApi", "png"]) {
    if (typeof manifest[field] !== "string" || manifest[field].length === 0) {
      problems.push(`missing ${field}`);
    }
  }
  const resolution = manifest.resolution;
  if (
    resolution === null ||
    typeof resolution !== "object" ||
    !Number.isInteger(resolution.width) ||
    !Number.isInteger(resolution.height) ||
    resolution.width < 1 ||
    resolution.height < 1
  ) {
    problems.push("resolution must record positive integer width/height");
  }
  const metrics = manifest.metrics;
  if (
    metrics === null ||
    typeof metrics !== "object" ||
    !Number.isInteger(metrics.distinctColors) ||
    typeof metrics.backgroundFraction !== "number" ||
    !Number.isInteger(metrics.pngBytes) ||
    metrics.pngBytes < 1
  ) {
    problems.push("metrics must record distinctColors, backgroundFraction, and pngBytes");
  }
  if (!Array.isArray(manifest.violations)) problems.push("violations must be an array");
  if (manifest.complete === true && manifest.violations?.length > 0) {
    problems.push("complete manifest must have no violations");
  }
  if (expectComplete && manifest.complete !== true) {
    problems.push(`capture incomplete: ${(manifest.violations ?? []).join("; ") || "unknown"}`);
  }
  if (!expectComplete && manifest.complete === true) {
    problems.push("expected an incomplete manifest (negative control must fail bounds)");
  }
  return problems;
}

/**
 * Manifests of the capture runs that started at or after `sinceEpochMs`, oldest
 * first, one per scenario (the newest copy wins).
 *
 * A run mints one UTC-stamped directory per capture test
 * (`TerminalSurfaceCapture.CreateRunDirectory`), so the directory name is the
 * run's own start time and scopes collection to the run at hand. A flat
 * time-window over file mtimes instead collects the previous run's manifests
 * when the two are seconds apart - the full PlayMode suite runs these same
 * capture tests - which compared 16 scenarios twice and could fail the command
 * on evidence from a run that was already over, or satisfy a scenario's
 * coverage check with a stale copy.
 *
 * The scenario comes from the filename, which is the same string the capture
 * wrote, so this stays one read per file.
 */
export function collectT4ManifestPaths(artifactRoot, sinceEpochMs) {
  const root = path.resolve(artifactRoot);
  if (!fs.existsSync(root)) return [];
  const manifests = [];
  for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
    if (!entry.isDirectory() || !isCaptureRunDirectory(entry.name, sinceEpochMs)) continue;
    const directory = path.join(root, entry.name);
    for (const file of fs.readdirSync(directory)) {
      if (!file.endsWith(".manifest.json")) continue;
      const filePath = path.join(directory, file);
      manifests.push({ filePath, mtimeMs: fs.statSync(filePath).mtimeMs });
    }
  }
  manifests.sort((a, b) => a.mtimeMs - b.mtimeMs);
  return newestPerScenario(manifests);
}

/**
 * A capture run directory is stamped `yyyy-MM-ddTHH-mm-ss-fffZ` in UTC, so the
 * name parses back to the run's start instant. Anything else is not a directory
 * this harness made and is left alone.
 */
function isCaptureRunDirectory(name, sinceEpochMs) {
  const match =
    /^(\d{4})-(\d{2})-(\d{2})T(\d{2})-(\d{2})-(\d{2})-(\d{3})Z$/u.exec(name);
  if (match === null) {
    return false;
  }

  const stamp = Date.parse(
    `${match[1]}-${match[2]}-${match[3]}T${match[4]}:${match[5]}:${match[6]}.${match[7]}Z`
  );
  return Number.isFinite(stamp) && stamp >= sinceEpochMs;
}

/** One entry per scenario, the newest copy kept, oldest first. */
export function newestPerScenario(entries) {
  const newest = new Map();
  for (const entry of entries) {
    const scenario = path.basename(entry.filePath, ".manifest.json");
    const current = newest.get(scenario);
    if (current === undefined || entry.mtimeMs >= current.mtimeMs) {
      newest.set(scenario, entry);
    }
  }
  return [...newest.values()].sort((left, right) => left.mtimeMs - right.mtimeMs);
}


/**
 * T04 fixture capture through the bridge: wait for an idle editor, run the
 * capture PlayMode tests, then validate every manifest they wrote. Fails the
 * command when a capture is missing, incomplete, or schema-invalid, so
 * blank or broken renders can never pass silently.
 */
export async function runT4Capture(options, runtime = {}) {
  const fetchImpl = runtime.fetchImpl ?? fetch;
  const captureTimeout = Math.max(options.timeout, 240_000);
  const startedAt = Date.now();
  const deadline = startedAt + captureTimeout;
  const scenarios = parseT4Scenarios(runtime.scenarios);
  const { found } = await discoverEndpoint(options, { ...runtime, readiness: "tools" });
  if (!found) fail("No Unity MCP endpoint with tools found; run npm run unity:mcp:probe for detail.");
  console.log(`T04 capture via ${found.url} (scenarios: ${scenarios.join(", ")})`);

  return withMcpSession(options, found, async (client) => {
    const signal = AbortSignal.timeout(captureTimeout);
    const evalCall = (expression) =>
      callFirstWorking(
        client,
        [
          { name: "eval", arguments: { code: expression } },
          { name: "eval", arguments: { expression } }
        ],
        signal
      );

    await waitForEditorIdle(client, evalCall, deadline);
    const reporter = await openRunReporter(options, evalCall);

    /*
        Counters read before the request are the previous capture run's, and the
        manifests validated below come from a window that can still contain
        them, so a run is only waited for when its result is attributable. The
        reporter's owner token carries that attribution without the pre-request
        poll.
     */
    let previousKey = null;
    let token = null;
    let requestedAt = null;
    if (reporter !== null) {
      token = newRunToken("t4");
      beginRunRequest(reporter, token);
      requestedAt = Date.now();
    } else {
      previousKey = readSummaryKey(await pollTestStatus(client, signal));
    }
    const runTests = await callFirstWorking(
      client,
      [
        {
          name: "run_tests",
          arguments: {
            mode: "playmode",
            filter: T4_TEST_FILTER,
            async_tests: true
          }
        },
        {
          name: "run_tests",
          arguments: { mode: "playmode", filter: T4_TEST_FILTER }
        }
      ],
      signal
    );
    log(options, "debug", `run_tests via ${runTests.candidate.name}`);

    const { summary, detail } = await awaitTestRunResult({
      label: "capture",
      client,
      signal,
      reporter,
      token,
      expectMode: "playmode",
      initial: extractText(runTests.call),
      previousKey,
      requestedAt,
      deadline
    });

    if (summary === null) {
      fail(`The capture test run produced no result: ${detail}. Inspect the editor.`);
    }

    console.log(testSummaryLine(summary));

    const artifactRoot = path.join(options.repoRoot, ".artifacts", "t4");
    const manifestPaths = collectT4ManifestPaths(artifactRoot, startedAt - RUN_DIR_SLACK_MS);
    const problems = [];
    const validated = new Set();
    const validManifests = [];
    for (const { filePath: manifestPath } of manifestPaths) {
      let manifest;
      try {
        manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
      } catch (error) {
        problems.push(`${manifestPath}: unreadable (${error.message})`);
        continue;
      }

      const scenario = manifest.scenario;
      // The negative control is expected incomplete regardless of what the
      // manifest claims; a complete:true blank capture is itself a failure
      // (it would mean the harness approved a blank render).
      const expectComplete = !T4_EXPECTED_INCOMPLETE.includes(scenario);
      const scenarioProblems = validateT4Manifest(manifest, expectComplete);
      if (scenarioProblems.length > 0) {
        problems.push(`${manifestPath}: ${scenarioProblems.join("; ")}`);
        continue;
      }
      validated.add(scenario);
      if (T4_ALL_SCENARIOS.includes(scenario)) {
        validManifests.push({ scenario, manifest, manifestPath });
      }
      console.log(`  ok ${scenario} (${manifest.resolution.width}x${manifest.resolution.height})`);
    }

    for (const scenario of scenarios) {
      if (!validated.has(scenario)) {
        problems.push(`no valid manifest captured for ${scenario}`);
      }
    }

    compareT4Baselines(options, validManifests, problems);

    if (summary.failed > 0) {
      problems.push(`${summary.failed} capture test(s) failed in the editor`);
    }
    if (problems.length > 0) {
      fail(`T04 capture rejected:\n  - ${problems.join("\n  - ")}`);
    }
    console.log(`T04 capture approved: ${validated.size} manifest(s) validated.`);
    return { validated: [...validated], summary };
  }, fetchImpl);
}

/**
 * T11 gate on top of the T04 harness: compare every validated capture
 * against its committed baseline (never across environments - provenance
 * must match exactly). A missing store or missing baseline is reported as
 * pending, not failed (first capture on a new environment); a mismatching
 * baseline fails the command and emits review artifacts.
 */
export function compareT4Baselines(options, validManifests, problems) {
  if (validManifests.length === 0) return;
  const storeDir = path.join(
    options.repoRoot,
    "Tests",
    "Runtime",
    "Capture",
    "Baselines~"
  );
  if (!fs.existsSync(storeDir)) {
    console.log("No T11 baseline store yet; run npm run t11:update to seed one.");
    return;
  }

  const stamp = new Date().toISOString().replace(/[:.]/g, "-");
  const artifactsRoot = path.join(options.repoRoot, ".artifacts", "t11", `t4-${stamp}`);
  let compared = 0;
  let pending = 0;
  for (const { scenario, manifest, manifestPath } of validManifests) {
    let envKey = "unknown-environment";
    let index;
    let baselinePng;
    let actualPng;
    let outcome;
    try {
      envKey = environmentKey(provenanceOf(manifest));
      index = readBaselineIndex(storeDir, envKey);
      if (index !== null) {
        const entry = index.scenarios[scenario];
        if (entry === null) {
          throw new Error(`baseline index entry for ${scenario} is corrupt`);
        }
        if (entry !== undefined) {
          if (entry.png !== `${scenario}.png`) {
            throw new Error(`baseline index png must be the bare filename ${scenario}.png`);
          }
          baselinePng = fs.readFileSync(path.join(storeDir, envKey, entry.png));
          actualPng = fs.readFileSync(path.join(path.dirname(manifestPath), manifest.png));
          outcome = compareScenario(
            baselinePng,
            actualPng,
            environmentProvenance(index.environment, entry.theme, entry.font),
            provenanceOf(manifest)
          );
        }
      }
    } catch (error) {
      problems.push(`${scenario}: baseline compare failed (${error.message})`);
      continue;
    }
    if (index === null || outcome === undefined) {
      ++pending;
      console.log(`  pending baseline ${scenario} (${envKey}); run npm run t11:update`);
      continue;
    }

    ++compared;
    if (outcome.pass) {
      console.log(
        `  baseline ok ${scenario}`
          + (outcome.result?.identical ? " (byte-identical)" : " (within tolerance)")
      );
      continue;
    }
    const artifactsDir = emitComparisonArtifacts(
      path.join(artifactsRoot, scenario),
      baselinePng,
      actualPng,
      outcome
    );
    const problemText = outcome.problems.join("; ");
    problems.push(
      `${scenario}: `
        + (outcome.result === null
          ? `baseline provenance mismatch (${problemText})`
          : `pixels differ from the baseline (${problemText})`)
        + `; review artifacts at ${artifactsDir}`
    );
  }
  if (compared > 0 || pending > 0) {
    console.log(`T11 baseline compare: ${compared} compared, ${pending} pending.`);
  }
}

// ---------------------------------------------------------------------------
// Unity test runner over the bridge: the one-command entry point per test
// suite category (functional / allocation / performance; graphics is
// t4-capture, tooling is `npm test`).
// ---------------------------------------------------------------------------

export const TEST_MODES = Object.freeze(["all", "editmode", "playmode"]);

/*
    Slack between the command's start clock and a capture run's own directory
    stamp: clock and filesystem granularity, nothing more. Anything older
    belongs to a previous run.
 */
const RUN_DIR_SLACK_MS = 2_000;
export const DEFAULT_TEST_RUN_TIMEOUT = 600_000;

/*
    Claim file: the editor's own report of one test run, so the caller reads a
    file instead of polling a main thread a Play Mode run occupies. The
    reporter (DxTerminalTestRunReporter, installed by install-capture) writes
    one line whose first token is `running`, `pass=`, or `did-not-run`, and
    every line carries the owner token of the run request the caller wrote to
    the request file. Falls back to bridge polling when the reporter is absent
    or its artifact directory is not writable from here (issue #162).
 */
export const RUN_CLAIM_FILE = "test-run.txt";
export const RUN_REQUEST_FILE = "test-run-request.txt";
/*
    Token the editor reports when it started a run it has no request for. It is
    written in exactly one place, so it always means "this run belongs to
    nobody" - a diagnosis, not a result to wait on.
 */
export const RUN_UNATTRIBUTED_TOKEN = "none";
/*
    How long the editor may take to acknowledge the request at all before the
    run counts as one that never began. Generous against a cold Play Mode start
    (compile plus domain reload), and far below the run timeout: a refusal must
    read as a refusal instead of as patience. It stops the moment a claim with
    our own token arrives, so a long run is never cut off by it.
 */
export const RUN_START_GRACE_MS = 120_000;
const RUN_REPORTER_TYPE_NAME = "DxTerminalDevTools.DxTerminalTestRunReporter, Assembly-CSharp-Editor";
const RUN_REPORTER_PROBE = `return (System.Type.GetType("${RUN_REPORTER_TYPE_NAME}") != null);`;

export function runClaimPaths(artifactRoot) {
  return {
    request: path.join(artifactRoot, RUN_REQUEST_FILE),
    claim: path.join(artifactRoot, RUN_CLAIM_FILE)
  };
}

/**
 * Owner token of one run request. Unique per call, so a claim written by an
 * earlier run (or by a run this command never asked for) cannot be read as
 * this run's result.
 */
export function newRunToken(label, now = Date.now()) {
  return `${label}-${now.toString(36)}-${randomBytes(4).toString("hex")}`;
}

/**
 * Decode one claim line. The first token decides the state:
 *
 *   running  token=<owner|none> mode=<mode> started=<o>
 *   pass=<n> fail=<n> skipped=<n> inconclusive=<n> duration=<s> token=<owner|none> mode=<mode> finished=<o>
 *           [failed-names=<a,b,c> [failed-more=<n>]]
 *   did-not-run token=<owner|none> mode=<mode> reason=<free text>
 *
 * `total` is summed here rather than reported by the editor, so the summary
 * cannot drift from the counters, and a refusal's reason is the rest of the
 * line so it stays readable English. A line without a readable token, with a
 * state word that is not one of the three, or with a counter that is not a
 * count, is `unreadable`: never a result.
 */
export function parseRunClaim(text) {
  const line = (text ?? "").trim();
  const fields = new Map();
  const parts = line.split(/\s+/u).filter((part) => part.length > 0);
  if (parts.length === 0) return { state: "unreadable", token: null };
  const head = parts.shift();
  for (const part of parts) {
    const separator = part.indexOf("=");
    if (separator <= 0) continue;
    fields.set(part.slice(0, separator), part.slice(separator + 1));
  }
  // An absent or empty token means a torn or hand-written line: never a result,
  // and never "another run's claim" that would suppress the start grace.
  const token = fields.get("token") ?? null;
  if (token === null || token === "") return { state: "unreadable", token: null };
  if (head === "running") return { state: "running", token, mode: fields.get("mode") ?? null };
  if (head === "did-not-run") {
    // The reason is the rest of the line, so it stays readable English; a
    // refusal with no reason text is still a refusal.
    const marker = line.indexOf("reason=");
    const reason = marker < 0 ? "" : line.slice(marker + "reason=".length);
    return {
      state: "refused",
      token,
      mode: fields.get("mode") ?? null,
      reason: reason.length === 0 ? "unspecified" : reason
    };
  }
  if (!head.startsWith("pass=")) return { state: "unreadable", token };
  fields.set("pass", head.slice("pass=".length));
  // A counter that is not a non-negative integer makes the summary NaN, and NaN
  // passes both exit guards (total === 0, failed > 0): a green run out of a
  // malformed line. Such a line is unreadable instead.
  const count = (name) => {
    const value = Number(fields.get(name) ?? 0);
    return Number.isSafeInteger(value) && 0 <= value ? value : null;
  };
  const passed = count("pass");
  const failed = count("fail");
  const skipped = count("skipped");
  const inconclusive = count("inconclusive");
  if (passed === null || failed === null || skipped === null || inconclusive === null) {
    // A line we cannot count names nothing: it must not suppress the start
    // grace as if it were our run's acknowledgement.
    return { state: "unreadable", token: null };
  }
  /*
      `failed-more` says how many failures the editor's cap left unnamed. It is a
      count, not a gate, so it can never decide whether a run is a result - but it
      is still bounded by the failures this same line reports, because a count the
      editor could not have written must not be printed as if it were one.

      `failedNames` is empty on a green run, on an editor whose reporter predates
      the field, and on the bridge fallback: that is the honest answer, not a
      silent "none failed".
   */
  const names = readFailureNames(fields.get("failed-names"));
  const more = Number(fields.get("failed-more"));
  const counted = /^\d+$/u.test(String(fields.get("failed-more") ?? "").trim())
    ? Math.min(more, Math.max(0, failed - names.length))
    : 0;
  return {
    state: "finished",
    token,
    mode: fields.get("mode") ?? null,
    summary: {
      total: passed + failed + skipped + inconclusive,
      passed,
      failed,
      skipped,
      inconclusive,
      failedNames: names,
      failedMore: counted
    }
  };
}

/*
    The failed test names the editor reported. The editor percent-encodes every
    byte the claim grammar reserves, so the decode is a split followed by a
    percent-decode. A `%` the editor could not have written - a hand-edited
    claim, or a name encoded by a future reporter - decodes to itself instead
    of throwing, because a claim that cannot be read must not take the run's
    result with it.
 */
function readFailureNames(value) {
  if (value === undefined || value === "") return [];
  const names = [];
  for (const part of value.split(",")) {
    if (part.length === 0) continue;
    try {
      names.push(decodeURIComponent(part));
    } catch {
      names.push(part);
    }
  }
  return names;
}

function readClaimFile(claimPath) {
  try {
    return fs.readFileSync(claimPath, "utf8");
  } catch {
    return "";
  }
}

function consumeRequestFile(requestPath) {
  if (requestPath === null) return;
  try {
    fs.rmSync(requestPath, { force: true });
  } catch {
    // A request that survives is overwritten by the next run; losing it is not
    // worth failing a result we already have.
  }
}

/*
    A claim that carries our token but a different mode is a run the editor
    started for something else. Reported loudly instead of accepted: the token
    is the attribution, and a mode that disagrees with the leg means the
    attribution is wrong.
 */
function assertClaimMode(mode, expected) {
  if (expected === null || mode === null) return;
  if (mode.toLowerCase() !== expected.toLowerCase()) {
    fail(`The editor reported a ${mode} run while the ${expected} leg was requested.`);
  }
}

/**
 * Wait for the claim of one run, reading only the file.
 *
 * Inspects every payload handed to it, including one read after the deadline
 * passed (the same rule the bridge wait follows: a loop that tests the deadline
 * before inspecting drops the result its last poll fetched).
 *
 * A claim is only ours when its token is the request token. The start grace only
 * applies until the editor acknowledges that token, so a long run is never cut
 * off; an editor that never acknowledges it fails with a reason instead of
 * sitting out the whole run timeout.
 *
 * A missing claim path or token is refused here instead of reading as "no claim
 * yet": a wiring mistake must never look like patience.
 *
 * `now` and `sleep` cover this loop only.
 */
export async function awaitRunClaim({
  claim,
  request = null,
  token,
  expectMode = null,
  requestedAt = null,
  deadline,
  read = readClaimFile,
  consume = consumeRequestFile,
  startGraceMs = RUN_START_GRACE_MS,
  pollIntervalMs = 2_000,
  now = () => Date.now(),
  sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))
}) {
  if (typeof claim !== "string" || claim === "" || typeof token !== "string" || token === "") {
    fail(`The run claim wait needs a claim path and an owner token; got ${claim} / ${token}.`);
  }
  const requested = requestedAt ?? now();
  let started = false;
  let sawForeign = false;
  let text = read(claim);
  for (;;) {
    const observed = parseRunClaim(text);
    if (observed.token === token) {
      /*
          Acknowledged: the editor is running our request, so the start grace is
          spent and the token is dead. Consuming the request file here is what
          keeps a run nobody asked for (a person in the Test Runner window, a
          second agent) from stamping itself with our token later.
       */
      if (!started) {
        started = true;
        consume(request);
      }
      if (observed.state === "finished") {
        assertClaimMode(observed.mode, expectMode);
        return { state: "finished", summary: observed.summary };
      }
      if (observed.state === "refused") {
        return { state: "refused", reason: observed.reason };
      }
    } else if (observed.state === "refused" && observed.token === RUN_UNATTRIBUTED_TOKEN) {
      // The editor found no request at all: the two sides disagree about the
      // claim directory, or a run nobody requested is in flight. Its own reason
      // is the diagnosis, so it must not wait out the deadline.
      return { state: "refused", reason: observed.reason };
    } else if (observed.state !== "unreadable") {
      sawForeign = true;
    }

    if (!started && startGraceMs <= now() - requested) {
      return {
        state: "no-start",
        detail: `the editor wrote no claim for this run within ${startGraceMs} ms`
      };
    }
    const remainingMs = deadline - now();
    if (remainingMs <= 0) {
      return {
        state: "no-claim",
        detail: sawForeign
          ? "the only claim on disk belongs to another run"
          : "the editor wrote no claim for this run"
      };
    }

    await sleep(Math.max(1, Math.min(pollIntervalMs, remainingMs)));
    text = read(claim);
  }
}

/**
 * The bridge's own "nothing matched" answer: a finished summary whose total is
 * zero. Only a zero total is accepted, so the previous run's counts can never be
 * read as this run's result. Null when the bridge says anything else, or does
 * not answer.
 */
async function noTestsMatchedSummary(client, signal, initial) {
  const candidates = [initial];
  try {
    candidates.push(
      extractText((await callFirstWorking(client, [{ name: "test_status", arguments: {} }], signal)).call)
    );
  } catch {}

  for (const candidate of candidates) {
    const status = parseTestStatus(candidate, true);
    if (status.finished && Number(status.summary?.total ?? -1) === 0) {
      return status.summary;
    }
  }
  return null;
}

/**
 * Claim paths when the reporter is installed in the editor and its artifact
 * directory can be written from here, or null to poll the bridge instead. The
 * probe is one eval while the editor is quiet, and every failure (no eval tool,
 * no reporter, no local project, an unwritable directory) means the fallback,
 * never a failed command.
 */
async function openRunReporter(options, evalCall) {
  const localProjectPath = options.projectContainerPath ?? options.projectPath;
  if (localProjectPath === undefined || !fs.existsSync(localProjectPath)) return null;
  let installed = false;
  try {
    const { call } = await evalCall(RUN_REPORTER_PROBE);
    installed = evalAnswerIsTrue(extractText(call));
  } catch {}
  const root = captureArtifactRoot(localProjectPath, localProjectPath);
  // The request has to be written before every run, so the directory is created
  // if this project never captured, and a read-only one is a fallback rather
  // than a crash on the first leg.
  try {
    fs.mkdirSync(root, { recursive: true });
    fs.accessSync(root, fs.constants.W_OK);
  } catch {
    installed = false;
  }
  if (!installed) {
    log(
      options,
      "debug",
      "The editor-side test run reporter is unusable here; polling the bridge instead."
    );
    return null;
  }

  return runClaimPaths(root);
}

/**
 * Claim the next run: write the owner token the editor will echo, and clear
 * the previous claim so a leftover line is never the first thing read. The wait
 * removes the request again once the editor acknowledges the token.
 */
function beginRunRequest(paths, token) {
  fs.mkdirSync(path.dirname(paths.request), { recursive: true });
  // World readable: the editor runs as the host user, this writes as the
  // container user.
  atomicWrite(paths.request, token, 0o644);
  fs.rmSync(paths.claim, { force: true });
}

/*
    Concrete bridge legs per mode. `all` expands to both suites instead of
    being sent as-is: on Unity 6000.4.6f1 the bridge answers `run_tests` with
    mode "all" by echoing the previous run's result without starting a run, so
    the command would report the last run's numbers. Two explicit legs each
    start a real run.
 */
export function testRunLegs(mode) {
  return mode === "all" ? ["editmode", "playmode"] : [mode];
}

/**
 * Session signal budget for a run: the per-leg timeout times the leg count, so
 * the cap never fires before a leg's own deadline. Sized for one leg, the
 * signal ends the command early and a per-leg deadline is unreachable.
 */
export function sessionSignalBudgetMs(runTimeoutMs, legCount) {
  return runTimeoutMs * Math.max(1, legCount);
}

/**
 * Validate and normalize `tests` subcommand options. Values arrive raw from
 * argv; a bad mode or non-numeric timeout fails before any endpoint contact.
 */
export function resolveTestRunOptions(runtime = {}) {
  const mode = runtime.mode ?? "all";
  if (!TEST_MODES.includes(mode)) {
    fail(`Unknown --mode: ${mode} (expected ${TEST_MODES.join(", ")})`);
  }
  const filter = runtime.filter ?? "";
  const parsedTimeout = Number(runtime.runTimeout ?? DEFAULT_TEST_RUN_TIMEOUT);
  if (!Number.isFinite(parsedTimeout) || parsedTimeout <= 0) {
    fail(`--run-timeout must be a positive number of milliseconds, got: ${runtime.runTimeout}`);
  }
  if (parsedTimeout < 30_000) {
    fail(`--run-timeout must be at least 30000ms (got ${runtime.runTimeout}); editor runs need startup room`);
  }
  return { mode, filter, runTimeout: parsedTimeout };
}

/**
 * Decode run_tests/test_status payloads across bridge generations: the
 * summary may sit under `Summary` or `summary`, and the run state may be a
 * bare status string. A payload that is still running, or `idle` before the
 * run was ever seen in flight, is never finished: it can still be carrying the
 * previous run's counts, and reading one as this run's result is the false
 * green gate. Absent or `completed` statuses may finish on the summary alone
 * so a blocking run_tests response terminates.
 */
export function parseTestStatus(text, seenRunning = false) {
  let parsed = null;
  try {
    parsed = JSON.parse(text);
  } catch {}
  if (!parsed || typeof parsed !== "object") {
    return { finished: false, running: false, summary: null };
  }
  const summary = parsed.Summary ?? parsed.summary ?? null;
  const status = typeof parsed.status === "string" ? parsed.status.toLowerCase() : "";
  const running = /in_progress|^running$|started/u.test(status);
  if (running || (status === "idle" && !seenRunning)) {
    return { finished: false, running, summary };
  }
  if (summary && Number(summary.total ?? 0) > 0) {
    return { finished: true, running, summary };
  }
  if ((status === "completed" || status === "idle") && summary) {
    return { finished: true, running, summary };
  }
  return { finished: false, running, summary };
}

/**
 * One leg's result, counters plus the names of the tests it failed. A red leg
 * names itself here: a counter alone leaves the reader to dig through the editor
 * console to learn which tests broke, which is the whole cost of a red run. The
 * names are printed once, here, so nothing downstream has to repeat them.
 */
export function testSummaryLine(summary) {
  return (
    `Tests: ${summary.total} total, ${summary.passed} passed, `
    + `${summary.failed} failed, ${summary.skipped} skipped.`
    + failureNameLines(summary)
  );
}

/*
    Characters that must not reach the output raw: the C0 and C1 controls, DEL,
    the zero-width and bidi formatting characters, and the BOM. The reporter
    percent-encodes all of them, so a decoded name can carry any of them - a
    `[TestCase("a\nb")]` is real in this repository - and one printed raw would
    split its own line, reorder one, or hide a run's result behind an override.
    Replaced with a visible escape rather than dropped, so a reader can tell the
    name was changed.
 */
const UNPRINTABLE_NAME_CHARACTER = /[\u0000-\u001f\u007f-\u009f\u00ad\u200b-\u200f\u202a-\u202e\u2060-\u2069\ufeff]/gu;

/** A decoded name, safe to print on one line. */
function printableName(name) {
  return name.replace(
    UNPRINTABLE_NAME_CHARACTER,
    (character) =>
      `\\u${character.codePointAt(0).toString(16).toUpperCase().padStart(4, "0")}`
  );
}

/**
 * One indented line per reported name, then one line for the failures the
 * editor's cap left unnamed. No deduplication: a parameterized test that failed
 * on several cases has several names, and the reader's only other count is the
 * `fail=` counter.
 */
function failureNameLines(summary) {
  const names = summary?.failedNames ?? [];
  const more = Number(summary?.failedMore ?? 0);
  const lines = names.map((name) => `\n  failed: ${printableName(name)}`);
  if (0 < more) {
    lines.push(`\n  ... and ${more} more the cap did not list`);
  } else if (0 < Number(summary?.failed ?? 0) && names.length === 0) {
    /*
        A red run the editor named nothing: a reporter older than this field, or
        the bridge fallback. Without this line the output is exactly the one the
        reader filed the issue about, with nothing to say the feature is absent.
     */
    lines.push("\n  no failed test names were reported (bridge fallback, or a reporter older than this change)");
  }
  return lines.join("");
}

/**
 * Canonical identity of one run: its counters plus its duration. Two payloads
 * with the same key are the same result, which is what makes a previous run's
 * answer recognizable - including a legitimate re-run of the same suite, whose
 * counters repeat but whose duration does not.
 */
export function summaryKey(summary, duration) {
  const counters = ["total", "passed", "failed", "skipped", "inconclusive"]
    .map((field) => {
      const value = Number(summary?.[field]);
      // Keep the raw value when it is not a number, so two malformed payloads
      // cannot collapse into one key.
      return `${field}=${Number.isFinite(value) ? value : JSON.stringify(summary?.[field] ?? null)}`;
    })
    .join(",");
  return `duration=${duration ?? "?"};${counters}`;
}

/** Run key carried by a run_tests/test_status payload, or null. */
export function readSummaryKey(text) {
  let parsed = null;
  try {
    parsed = JSON.parse(text);
  } catch {}
  if (!parsed || typeof parsed !== "object") {
    return null;
  }
  const summary = parsed.Summary ?? parsed.summary ?? null;
  if (!summary || typeof summary !== "object") {
    return null;
  }
  return summaryKey(summary, parsed.duration);
}

/**
 * Whether a completed payload carries a result attributable to the run we asked
 * for. The bridge answers `run_tests` with the *previous* run's result when it
 * declines to start one (observed with `mode: all` on Unity 6000.4.6f1), so a
 * result that still reads exactly like the pre-request one is never accepted:
 * a run is only believed once it was seen in flight or reported a different
 * key. Without this, a run that never started prints the last green run.
 */
export function summaryIsFresh(text, previousKey, seenRunning) {
  const key = readSummaryKey(text);
  if (key === null) {
    return false;
  }
  return seenRunning || previousKey === null || key !== previousKey;
}

/**
 * Run Unity tests over the bridge and report the summary. Exit code 1 when
 * any test fails or nothing matches; the command never parses test output
 * beyond the runner's own summary payload.
 */
export async function runUnityTests(options, runtime = {}) {
  const fetchImpl = runtime.fetchImpl ?? fetch;
  const { mode, filter, runTimeout } = resolveTestRunOptions(runtime);
  const legs = testRunLegs(mode);
  const { found } = await discoverEndpoint(options, { ...runtime, readiness: "tools" });
  if (!found) fail("No Unity MCP endpoint with tools found; run npm run unity:mcp:probe for detail.");
  console.log(`Unity tests via ${found.url} (mode: ${mode}${filter ? `, filter: ${filter}` : ""})`);

  return withMcpSession(options, found, async (client) => {
    /*
        The session signal is the hard cap for the whole command, so it has to
        cover every leg: sized for one leg it fires before the per-leg deadlines
        and a slow first suite still starves the second one.
     */
    const signal = AbortSignal.timeout(sessionSignalBudgetMs(runTimeout, legs.length));
    const evalCall = (expression) =>
      callFirstWorking(
        client,
        [
          { name: "eval", arguments: { code: expression } },
          { name: "eval", arguments: { expression } }
        ],
        signal
      );

    const summaries = [];
    // Probed once, on the first leg's quiet editor: a busy main thread is the
    // one moment the probe must not land on, and the answer cannot change
    // mid-command (nothing installs the reporter while a run is in flight).
    let reporter = null;
    let reporterProbed = false;
    for (const leg of legs) {
      /*
          Each leg gets its own deadline: the flag reads as how long a run may
          take, and a slow first suite must not starve the second one.
       */
      const legDeadline = Date.now() + runTimeout;
      if (signal.aborted) {
        fail(
          `The run timeout (${runTimeout} ms per leg) is spent after `
            + `${summaries.length} of ${legs.length} legs; raise --run-timeout.`
        );
      }

      // Each leg waits for its own quiet editor: a PlayMode leg leaves the
      // session tearing down, and the next leg must not race that.
      await waitForTestIdle(client, evalCall, legDeadline);
      if (!reporterProbed) {
        reporter = await openRunReporter(options, evalCall);
        reporterProbed = true;
      }

      const summary = await runTestLeg(
        client,
        signal,
        leg,
        filter,
        legDeadline,
        reporter,
        evalCall
      );
      console.log(`${leg}: ${testSummaryLine(summary)}`);
      summaries.push(summary);
    }

    const total = summaries.reduce((sum, summary) => sum + Number(summary.total ?? 0), 0);
    if (total === 0) {
      fail(`No tests matched (mode: ${mode}${filter ? `, filter: ${filter}` : ""}).`);
    }
    const failed = summaries.reduce((sum, summary) => sum + Number(summary.failed ?? 0), 0);
    if (failed > 0) {
      // The names are already on each leg's line; the count is what is left to
      // say, and it covers every leg.
      fail(`${failed} test(s) failed.`);
    }
    return { summaries, summary: summaries[summaries.length - 1] };
  }, fetchImpl);
}

/**
 * Read the bridge's test status, retrying a transient failure. The editor can
 * still be unwinding a previous run when a poll lands, and one timed-out
 * request must not fail a command that has not run yet; a status that never
 * answers at all is still fatal, because the freshness check needs it.
 */
async function pollTestStatus(client, signal, attempts = 5) {
  let lastFailure = null;
  for (let attempt = 0; attempt < attempts; ++attempt) {
    try {
      return extractText(
        (await callFirstWorking(client, [{ name: "test_status", arguments: {} }], signal)).call
      );
    } catch (error) {
      lastFailure = error;
      if (attempt + 1 < attempts) {
        await new Promise((resolve) => setTimeout(resolve, 2_000));
      }
    }
  }
  fail(`The bridge did not answer test_status: ${lastFailure?.message ?? "unknown error"}`);
}

/*
    The mid-run variant of the idle wait: the editor being busy is the expected
    state while a run winds down, so a stalled answer ends the wait instead of
    failing the command.
 */
async function waitForTestIdleQuietly(client, evalCall, deadline) {
  const expression =
    "return (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode);";
  for (let attempt = 0; attempt < 30; ++attempt) {
    if (Date.now() >= deadline) return;
    try {
      const { call } = await evalCall(expression);
      if (evalAnswerIsFalse(extractText(call))) return;
    } catch {
      return;
    }

    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
}

/**
 * Wait for a run result, inspecting every payload handed to it - including the
 * one fetched after the deadline has already passed.
 *
 * The loop must inspect before it re-tests the deadline. Shaped as
 * `while (now < deadline) { inspect; sleep; fetch }` the deadline is checked
 * first, so a poll that lands late is dropped and a run that finished during
 * that poll reads as missing (found by Cursor Bugbot on PR #161). Here the
 * deadline only decides whether another poll is worth starting.
 *
 * Returns the attributable summary, or null when the deadline passed with
 * nothing acceptable in hand.
 *
 * `now` and `sleep` cover this loop only. The editor-idle wait and the status
 * retry it calls read the real clock, so a test that injects a clock bounds this
 * loop and nothing below it.
 */
export async function awaitRunResult({
  initial,
  previousKey,
  deadline,
  read,
  beforePoll = null,
  pollIntervalMs = 2_000,
  now = () => Date.now(),
  sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))
}) {
  let payload = initial;
  /*
      "Seen in flight" is only recorded from a poll. The answer to our own
      `run_tests` request is the bridge's word about that request, and taking it
      as proof would let a bridge that reports in_progress without starting
      anything unlock the key check below.
   */
  let seenRunning = false;
  for (;;) {
    const observed = parseTestStatus(payload, seenRunning);
    /*
        Only a finished, non-in-flight payload can be a result - one still
        running, or idle before the run was ever seen, can carry the previous
        run's numbers - and only when its run key differs from the pre-request
        key, unless the run was seen in flight.
     */
    if (observed.finished && summaryIsFresh(payload, previousKey, seenRunning)) {
      return observed.summary;
    }

    const remainingMs = deadline - now();
    if (remainingMs <= 0) {
      return null;
    }

    // Clamped, so the last poll lands on the deadline instead of a full
    // interval past it: that round-trip is the one most likely to time out on a
    // busy editor, and it buys nothing once the budget is spent.
    await sleep(Math.max(1, Math.min(pollIntervalMs, remainingMs)));
    if (beforePoll !== null) {
      await beforePoll();
    }

    payload = await read();
    seenRunning ||= parseTestStatus(payload, false).running;
  }
}

/**
 * Wait for one run's result through the editor's claim file, or through bridge
 * polling when no reporter is installed.
 *
 * Returns the attributable summary, or `{ summary: null, detail }` when the
 * deadline passed with nothing attributable. A refusal is the editor's own
 * answer, so it fails the command here instead of returning as a missing
 * result: `did-not-run` must never read as patience.
 */
async function awaitTestRunResult({
  label,
  client,
  signal,
  reporter = null,
  token = null,
  expectMode = null,
  initial = null,
  previousKey = null,
  requestedAt = null,
  deadline,
  beforePoll = null
}) {
  const paths = reporter ?? null;
  if (paths === null) {
    const summary = await awaitRunResult({
      initial,
      previousKey,
      deadline,
      beforePoll,
      read: () => pollTestStatus(client, signal)
    });
    return {
      summary,
      detail:
        "the bridge may not have started a run, or it answered with the previous run's result"
    };
  }

  // Both paths are passed by name, never spread: a spread of the reporter
  // object once read a file that was never written, for the whole deadline.
  const claim = await awaitRunClaim({
    claim: paths.claim,
    request: paths.request,
    token,
    expectMode,
    requestedAt,
    deadline
  });
  if (claim.state === "finished") {
    return { summary: claim.summary, detail: null };
  }
  if (claim.state === "refused") {
    fail(
      `The ${label} test run was refused by the editor: ${claim.reason} `
        + `(this command's run request was ${paths.request})`
    );
  }
  if (claim.state === "no-start") {
    /*
        A run that matches no test never starts, so the editor has nothing to
        report. The bridge does answer that case, with a finished zero-total
        summary, and a mode-specific filter is not an error by itself: only a
        zero total across legs fails. Nothing else is taken from the bridge here,
        because a non-zero summary on this path is unverified by construction.
     */
    const empty = await noTestsMatchedSummary(client, signal, initial);
    if (empty !== null) {
      return { summary: empty, detail: null };
    }
  }
  // No claim is deleted here: the next run clears it, and a claim on disk may
  // be the evidence the failure message names.
  return { summary: null, detail: claim.detail };
}

/**
 * Run one mode's suite over the bridge and return its summary.
 *
 * With the reporter installed, the owner token written before the request is
 * what makes the result attributable; the token replaces the pre-request poll
 * entirely, so the request is made while the editor is still free. Without it,
 * the counters observed before the request belong to the previous run, so a
 * summary that still reads exactly like them is never accepted as this run's
 * result: the bridge can answer `run_tests` without starting anything, and
 * without that guard the command would print the last run's numbers.
 */
async function runTestLeg(client, signal, leg, filter, deadline, reporter = null, evalCall = null) {
  const paths = reporter ?? null;
  let previousKey = null;
  let token = null;
  let requestedAt = null;
  if (paths !== null) {
    token = newRunToken(leg);
    beginRunRequest(paths, token);
    requestedAt = Date.now();
  } else {
    previousKey = readSummaryKey(await pollTestStatus(client, signal));
  }
  const runArguments = { mode: leg, async_tests: true };
  if (filter !== "") {
    runArguments.filter = filter;
  }
  const run = await callFirstWorking(
    client,
    [
      { name: "run_tests", arguments: runArguments },
      {
        name: "run_tests",
        arguments: filter === "" ? { mode: leg } : { mode: leg, filter }
      }
    ],
    signal
  );

  const { summary, detail } = await awaitTestRunResult({
    label: leg,
    client,
    signal,
    reporter: paths,
    token,
    expectMode: leg,
    initial: extractText(run.call),
    previousKey,
    requestedAt,
    deadline,
    beforePoll:
      paths === null && evalCall !== null
        ? () =>
            waitForTestIdleQuietly(client, evalCall, deadline)
        : null
  });
  if (summary !== null) {
    return summary;
  }

  fail(
    `The ${leg} test run produced no result`
      + `${filter ? ` (filter: ${filter})` : ""}: ${detail}. Inspect the editor.`
  );
}

function usage() {
  return [
    "Usage: node tooling~/scripts/mcp/unity-mcp.mjs <probe|configure|bridge|install-capture|capture|t4-capture|tests> [options]",
    "",
    "  probe           Discover Unity tools and check editor readiness.",
    "  configure       Configure agent MCP servers, discovering Unity unless --offline is set.",
    "  bridge          Serve Unity CLI or the legacy relay over authenticated HTTP on the host.",
    "  install-capture Install the editor dev tools (state capture, test run reporter)\n" +
      "                  into the host project (host side).",
    "  capture         Capture editor/game state into .artifacts through the bridge.",
    "  t4-capture      Run the T04 fixture-capture tests, validate their manifests,\n" +
    "                  and compare pixels against the T11 baseline store.",
    "  tests           Run Unity tests over the bridge and report one result block",
    "                  per leg; 'all' runs the EditMode suite then the PlayMode suite.",
    "                  A red leg names the tests it failed when the run reporter",
    "                  compiled in the editor named any.",
    "                  With the run reporter installed, the result is read from the",
    "                  editor's claim file instead of polling its main thread.",
    "",
    "Options:",
    "  --mode MODE                 Test mode: all, editmode, playmode (tests only; default all,",
    "                              which runs both suites, each with its own run timeout)",
    "  --filter TEXT               Test name filter (tests only)",
    "  --run-timeout MS            Per-leg test run deadline (tests only; default 600000)",
    "  --host HOST                 Endpoint host; the only host discovery probes",
    "  --port PORT                 Endpoint port; the only port discovery probes",
    "  --path PATH                 Streamable HTTP path (default: /mcp)",
    "  --offline                   Write configs without network access (configure only)",
    "  --no-discover               Probe only the configured host/port, not the fallbacks",
    "  --bind HOST                 Bridge bind interface (default: 0.0.0.0)",
    "  --project PATH              Unity project directory (bridge, install-capture, capture)",
    "  --project-container PATH    Container-visible project directory for local capture files",
    "  --out DIR                   Capture output directory (capture only)",
    "  --scenarios LIST            Comma-separated scenario names (t4-capture only)",
    "  --no-install                Skip local capture-script installation (capture only)",
    "  --backend cli|relay         Host backend (default: cli; relay supports Assistant)",
    "  --cli PATH                  Unity CLI executable (default: unity on host PATH)",
    "  --relay PATH                Unity relay executable override",
    "  --token TOKEN               32-256 character bearer token (stored in .env.local)",
    "  --timeout MS                Per-endpoint MCP lifecycle deadline (default: 5000)",
    "  --connect-timeout MS        Per-endpoint TCP connect timeout (default: 750)",
    "  --session-timeout MS        Idle session timeout (default: 60000)",
    "  --request-timeout MS        Active-request hard limit (default: 300000)",
    "  --max-sessions COUNT        Concurrent bridge sessions, one relay each (default: 8)",
    "  --protocol-version VERSION  MCP protocol version (default: 2025-11-25)",
    "  --log-level LEVEL           debug, info, or none"
  ].join("\n");
}

export async function main(argv = process.argv.slice(2)) {
  const commands = {
    probe: runProbe,
    configure: runConfigure,
    bridge: runBridge,
    "install-capture": runInstallCapture,
    capture: runCapture,
    "t4-capture": runT4Capture,
    tests: runUnityTests
  };
  const [command, ...rest] = argv;
  if (!command || command === "--help" || command === "-h") {
    console.log(usage());
    return;
  }
  if (!Object.hasOwn(commands, command)) {
    fail(`Unknown command: ${command}`);
  }
  if (rest.includes("--help") || rest.includes("-h")) {
    console.log(usage());
    return;
  }
  const args = parseArgs(rest);
  if (args._.length) {
    fail(`Unexpected argument: ${args._[0]}`);
  }
  const options = resolveOptions(args);
  if (options.offline && command !== "configure") fail("--offline is only valid for configure");
  if (command === "t4-capture") {
    await commands[command](options, { scenarios: parseT4Scenarios(args.scenarios) });
    return;
  }
  if (command === "tests") {
    await commands[command](options, {
      mode: args.mode,
      filter: args.filter,
      runTimeout: args["run-timeout"]
    });
    return;
  }
  await commands[command](options);
}

const entry = process.argv[1] ? pathToFileURL(path.resolve(process.argv[1])).href : "";
if (import.meta.url === entry) {
  main().catch((error) => {
    console.error(`unity-mcp: ${error.message}`);
    process.exitCode = 1;
  });
}
