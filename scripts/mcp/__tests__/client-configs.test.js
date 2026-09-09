import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import {
  configure,
  clientConfigPaths,
  mergeCodexToml,
  prepareJsonServers,
  stripJsonComments,
  transactionalWrite,
  ZAI_SERVERS,
  GITHUB_MCP_URL
} from "../unity-mcp.mjs";

const BEARER = "a".repeat(64);
const ZAI_KEY = "z".repeat(48);
const ENDPOINT = { host: "host.docker.internal", port: 9020, endpointPath: "/mcp" };

function tempRepo(initial = {}) {
  const repoRoot = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-repo-"));
  for (const [relative, content] of Object.entries(initial)) {
    const target = path.join(repoRoot, relative);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, content);
  }
  fs.writeFileSync(
    path.join(repoRoot, ".env.local"),
    `UNITY_MCP_BEARER_TOKEN=${BEARER}\n`
  );
  return repoRoot;
}

function options(repoRoot, overrides = {}) {
  return {
    repoRoot,
    bearerToken: BEARER,
    githubToken: "gh-token-123",
    zaiToken: undefined,
    host: ENDPOINT.host,
    port: ENDPOINT.port,
    endpointPath: ENDPOINT.endpointPath,
    ...overrides
  };
}

test("configure writes every client schema with the unity endpoint", () => {
  const repoRoot = tempRepo();
  try {
    const { url, written } = configure(options(repoRoot), ENDPOINT);
    assert.equal(url, "http://host.docker.internal:9020/mcp");
    const paths = clientConfigPaths(repoRoot);
    assert.equal(written.length, Object.keys(paths).length);

    const claude = JSON.parse(fs.readFileSync(paths.claudeCode, "utf8"))["mcpServers"];
    assert.deepEqual(claude["unity-mcp"], {
      type: "http",
      url,
      headers: { Authorization: `Bearer ${BEARER}` }
    });
    assert.deepEqual(claude["git"], {
      type: "stdio",
      command: "mcp-server-git",
      args: ["--repository", repoRoot]
    });
    assert.deepEqual(claude["fetch"], { type: "stdio", command: "mcp-server-fetch", args: [] });
    assert.equal(claude["github"].url, GITHUB_MCP_URL);
    assert.equal(claude["github"].headers.Authorization, "Bearer gh-token-123");
    assert.deepEqual(claude["context7"], {
      type: "stdio",
      command: "context7-mcp",
      args: []
    });
    assert.ok(!claude["web-search-prime"], "no Z.AI servers without a key");

    const vscode = JSON.parse(fs.readFileSync(paths.vscode, "utf8"));
    assert.ok(vscode["servers"], "vscode schema uses servers");
    assert.equal(vscode["servers"]["unity-mcp"].type, "http");

    const nanocoder = JSON.parse(fs.readFileSync(paths.nanocoder, "utf8"));
    assert.equal(nanocoder["mcpServers"]["unity-mcp"].transport, "http");

    const opencode = JSON.parse(
      fs.readFileSync(paths.openCode, "utf8").replace(/^\/\/.*$/gm, "")
    );
    assert.deepEqual(opencode["mcp"]["unity-mcp"], {
      type: "remote",
      url,
      headers: { Authorization: `Bearer ${BEARER}` },
      oauth: false,
      enabled: true,
      timeout: 30000
    });
    assert.equal(opencode["mcp"]["git"].type, "local");
    assert.deepEqual(opencode["mcp"]["git"].command, ["mcp-server-git", "--repository", repoRoot]);
    assert.equal(opencode["share"], "disabled", "session sharing must default to disabled");

    const copilot = JSON.parse(fs.readFileSync(paths.copilot, "utf8"));
    assert.deepEqual(copilot["mcpServers"]["unity-mcp"].tools, ["*"]);

    const codex = fs.readFileSync(paths.codex, "utf8");
    assert.match(codex, /\[mcp_servers\.unity-mcp\]/);
    assert.match(codex, /url = "http:\/\/host\.docker\.internal:9020\/mcp"/);
    assert.match(codex, /Authorization = "Bearer a{64}"/);
    assert.match(codex, /startup_timeout_sec = 30/);
    assert.match(codex, /tool_timeout_sec = 300/);

    for (const filePath of Object.values(paths)) {
      const mode = fs.statSync(filePath).mode & 0o777;
      assert.equal(mode, 0o600, `${filePath} must be 0600`);
    }
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("configure adds Z.AI servers when a key exists and removes them when it does not", () => {
  const repoRoot = tempRepo();
  try {
    configure(options(repoRoot, { zaiToken: ZAI_KEY }), ENDPOINT);
    const paths = clientConfigPaths(repoRoot);
    let claude = JSON.parse(fs.readFileSync(paths.claudeCode, "utf8"))["mcpServers"];
    for (const name of ZAI_SERVERS) {
      assert.ok(claude[name], `${name} should be configured`);
    }
    assert.equal(claude["web-search-prime"].url, "https://api.z.ai/api/mcp/web_search_prime/mcp");
    assert.equal(claude["web-search-prime"].headers.Authorization, `Bearer ${ZAI_KEY}`);
    assert.deepEqual(claude["zai-mcp-server"], {
      type: "stdio",
      command: "zai-mcp-server",
      args: [],
      env: { Z_AI_API_KEY: ZAI_KEY, Z_AI_MODE: "ZAI" }
    });

    const codexWithZai = fs.readFileSync(paths.codex, "utf8");
    assert.match(codexWithZai, /command = "mcp-remote"/);
    assert.match(codexWithZai, /"Authorization:\$\{ZAI_AUTH_HEADER\}"/);
    assert.match(codexWithZai, /ZAI_AUTH_HEADER = "Bearer z{48}"/);
    for (const remote of ["web-search-prime", "web-reader", "zread"]) {
      assert.match(codexWithZai, new RegExp(`\\[mcp_servers\\.${remote}\\.env\\]`));
    }

    configure(options(repoRoot, { zaiToken: undefined }), ENDPOINT);
    claude = JSON.parse(fs.readFileSync(paths.claudeCode, "utf8"));
    for (const name of ZAI_SERVERS) {
      assert.ok(!claude[name], `${name} should be removed without a key`);
    }
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("configure preserves unrelated keys and servers in JSON clients", () => {
  const repoRoot = tempRepo({
    ".mcp.json": `{
      // user comment
      "otherTool": { "command": "foo" },
      "mcpServers": { "custom": { "command": "bar" } },
      "topLevel": true
    }`
  });
  try {
    configure(options(repoRoot), ENDPOINT);
    const document = JSON.parse(fs.readFileSync(path.join(repoRoot, ".mcp.json"), "utf8"));
    assert.deepEqual(document["otherTool"], { command: "foo" });
    assert.deepEqual(document["mcpServers"]["custom"], { command: "bar" });
    assert.equal(document["topLevel"], true);
    assert.ok(document["mcpServers"]["unity-mcp"]);
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("configure defaults opencode share to disabled and preserves an explicit value", () => {
  const repoRoot = tempRepo({
    "opencode.jsonc": `{ "mcp": {}, "share": "auto" }`
  });
  try {
    configure(options(repoRoot), ENDPOINT);
    const document = JSON.parse(fs.readFileSync(clientConfigPaths(repoRoot).openCode, "utf8"));
    assert.equal(document["share"], "auto", "an explicit share setting must not be stomped");
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }

  const untouched = tempRepo();
  try {
    configure(options(untouched), ENDPOINT);
    const document = JSON.parse(fs.readFileSync(clientConfigPaths(untouched).openCode, "utf8"));
    assert.equal(document["share"], "disabled", "absent share must default to disabled");
  } finally {
    fs.rmSync(untouched, { recursive: true, force: true });
  }
});

test("configure refuses to replace malformed client configs", () => {
  const repoRoot = tempRepo({ ".mcp.json": "{ not json" });
  try {
    assert.throws(() => configure(options(repoRoot), ENDPOINT), /Invalid JSON/);
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("mergeCodexToml refuses invalid TOML and deletes null entries", () => {
  assert.throws(() => mergeCodexToml("[broken", "http://x", undefined, "unity-mcp"));
  const raw = mergeCodexToml("[mcp_servers.old]\nurl = \"http://old\"\n", null, undefined, "old");
  assert.ok(!raw.includes("old"), "null config must delete the entry");
});

test("transactionalWrite rolls back committed files when a later write fails", () => {
  const repoRoot = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-txn-"));
  try {
    const fileA = path.join(repoRoot, "a.json");
    const fileB = path.join(repoRoot, "b.json");
    const fileC = path.join(repoRoot, "c.json");
    fs.writeFileSync(fileB, "{}\n");
    assert.throws(() =>
      transactionalWrite(
        [
          [fileA, "{}\n"],
          [fileB, "{\"changed\":true}\n"],
          [fileC, "{}\n"]
        ],
        (index) => {
          if (index === 2) throw new Error("boom");
        }
      )
    );
    assert.ok(!fs.existsSync(fileA), "newly committed file must be rolled back");
    assert.ok(!fs.existsSync(fileC));
    assert.equal(fs.readFileSync(fileB, "utf8"), "{}\n", "pre-existing file must be restored");
    assert.equal(
      fs.readdirSync(repoRoot).filter((name) => name.includes(".tmp")).length,
      0,
      "no staging temporaries may leak"
    );
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("prepareJsonServers keeps unrelated collections and merges over existing servers", () => {
  const repoRoot = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-json-"));
  try {
    const filePath = path.join(repoRoot, "mcp.json");
    fs.writeFileSync(
      filePath,
      '{"mcpServers":{"keep":{"command":"k"}},"servers":{"vs":"x"}}'
    );
    const output = prepareJsonServers(filePath, "mcpServers", {
      "unity-mcp": { url: "http://u", type: "http" }
    });
    const parsed = JSON.parse(output);
    assert.deepEqual(parsed["mcpServers"]["keep"], { command: "k" });
    assert.deepEqual(parsed["servers"], { vs: "x" });
    assert.equal(parsed["mcpServers"]["unity-mcp"].url, "http://u");
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("stripJsonComments rejects parse errors instead of recovering", () => {
  assert.throws(() => stripJsonComments("{a:1,,}"), /Invalid JSONC/);
  assert.equal(stripJsonComments('{"a":1} // ok'), '{"a":1}');
});
