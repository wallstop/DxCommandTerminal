import test from "node:test";
import assert from "node:assert/strict";
import path from "node:path";
import { projectPort, resolveProjectPorts, resolveOptions, parseArgs } from "../unity-mcp.mjs";

test("projectPort is deterministic across calls and path spellings", () => {
  const direct = projectPort("/Users/dev/Code/Proj");
  assert.equal(projectPort("/Users/dev/Code/Proj"), direct);
  assert.equal(projectPort("/Users/dev/./Code/../Code/Proj"), direct);
  assert.equal(projectPort("/Users/dev/Code/Proj/"), direct);
});

test("projectPort lands inside the dedicated project range", () => {
  for (const candidate of ["/a", "/b/bb", "/c/cc/ccc", "/Users/wallstop/Code/DxCommandTerminal"]) {
    const port = projectPort(candidate);
    assert.ok(
      Number.isInteger(port) && port >= 27100 && port <= 27999,
      `${port} outside 27100..27999 for ${candidate}`
    );
  }
});

test("distinct projects map to distinct ports for realistic inputs", () => {
  const projects = [
    "/Users/dev/Code/DxCommandTerminal",
    "/Users/dev/Code/DxMessaging",
    "/Users/dev/Code/NovaSharp",
    "/Users/dev/Code/unity-helpers",
    "/Users/dev/Work/AnotherGame",
    "/Users/other/Code/DxCommandTerminal"
  ];
  const ports = new Set(projects.map(projectPort));
  assert.equal(ports.size, projects.length, `collision among: ${[...ports].join(", ")}`);
});

test("projectPort returns null without a path", () => {
  assert.equal(projectPort(undefined), null);
  assert.equal(projectPort(""), null);
});

test("resolveProjectPorts puts the project port first and dedupes fallbacks", () => {
  const ports = resolveProjectPorts("/Users/dev/Code/Proj");
  assert.ok(ports.length >= 2);
  assert.ok(ports.includes(projectPort("/Users/dev/Code/Proj")));
  assert.ok(ports.includes(9020));
  assert.equal(new Set(ports).size, ports.length, "ports must be unique");
});

test("resolveProjectPorts without a project uses the stock fallbacks", () => {
  assert.deepEqual(resolveProjectPorts(undefined), [9020, 9003]);
});

const ENV_KEYS = {
  projectPath: "UNITY_PROJECT_PATH",
  port: "UNITY_MCP_BRIDGE_PORT",
  host: "UNITY_MCP_BRIDGE_HOST"
};

test("bridge port precedence: flag beats env beats .env.local beats project port", () => {
  const projectPath = "/Users/dev/Code/DxCommandTerminal";
  const base = { "no-discover": true };
  const repoRoot = "/tmp/does-not-matter-for-options";

  const flag = resolveOptions(
    { ...base, port: "27500", project: projectPath },
    {},
    {},
    repoRoot
  );
  assert.equal(flag.port, 27500);
  assert.equal(flag.explicitPort, 27500);

  const env = resolveOptions({ ...base }, { [ENV_KEYS.port]: "27501" }, {}, repoRoot);
  assert.equal(env.port, 27501);

  const local = resolveOptions({ ...base }, {}, { [ENV_KEYS.port]: "27502" }, repoRoot);
  assert.equal(local.port, 27502);

  const derived = resolveOptions(
    { ...base },
    {},
    { [ENV_KEYS.projectPath]: projectPath },
    repoRoot
  );
  assert.equal(derived.port, projectPort(projectPath));
  assert.equal(derived.explicitPort, undefined, "derived port must not suppress discovery");

  const stock = resolveOptions({ ...base }, {}, {}, repoRoot);
  assert.equal(stock.port, 9020);
});

test("resolveOptions keeps project path from .env.local when no flag is given", () => {
  const options = resolveOptions(
    {},
    {},
    { UNITY_PROJECT_PATH: "/Users/dev/Code/DxCommandTerminal" },
    "/tmp/whatever"
  );
  assert.equal(options.projectPath, path.resolve("/Users/dev/Code/DxCommandTerminal"));
});

test("parseArgs rejects unknown options and empty values", () => {
  assert.throws(() => parseArgs(["--bogus"]));
  assert.throws(() => parseArgs(["--port="]));
  assert.deepEqual(parseArgs(["--port", "9020", "--offline"]), {
    port: "9020",
    offline: true,
    _: []
  });
  assert.deepEqual(parseArgs(["--port=9020"]), { port: "9020", _: [] });
});
