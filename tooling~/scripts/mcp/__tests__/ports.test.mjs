import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { projectPort, resolveProjectPorts, resolveProjectPath, resolveOptions, parseArgs } from "../unity-mcp.mjs";

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

test("projectPort is independent of the working directory for Windows paths", () => {
  // The devcontainer holds the host identity (UNITY_PROJECT_PATH=C:/...); a POSIX
  // resolve() would read that as relative and prepend the cwd, so the container
  // derived a different port than the Windows host bridge binds.
  const cwd = process.cwd();
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "unity-mcp-port-cwd-"));
  try {
    const outside = projectPort("C:/Code/DxCommandTerminal");
    process.chdir(scratch);
    assert.equal(projectPort("C:/Code/DxCommandTerminal"), outside);
  } finally {
    process.chdir(cwd);
    fs.rmdirSync(scratch);
  }
});

test("projectPort treats Windows path spellings as one project", () => {
  const direct = projectPort("C:/Code/DxCommandTerminal");
  assert.equal(projectPort("C:\\Code\\DxCommandTerminal"), direct);
  assert.equal(projectPort("c:\\code\\dxcommandterminal"), direct);
  assert.equal(projectPort("C:/Code/DxCommandTerminal/"), direct);
  assert.equal(projectPort("C:/Code/./Dx/../DxCommandTerminal"), direct);
  assert.equal(projectPort("  C:/Code/DxCommandTerminal  "), direct);
});

test("projectPort treats UNC spellings as one project", () => {
  const direct = projectPort("//server/share/Proj");
  assert.equal(projectPort("\\\\server\\share\\Proj"), direct);
  assert.equal(projectPort("//server/share/Proj/"), direct);
});

test("resolveProjectPath keeps the UNC prefix a share", () => {
  // Collapsing the empty segments once turned "//server/share/Proj" into
  // "/server/share/Proj": a drive-rooted spelling on Windows and a nonexistent
  // local directory in the container, so existsSync, cwd, and --project-path
  // all missed. The double slash is what names the share.
  assert.equal(resolveProjectPath("//server/share/Proj"), "//server/share/Proj");
  assert.equal(resolveProjectPath("\\\\server\\share\\Proj"), "//server/share/Proj");
  assert.equal(resolveProjectPath("//server/share/Proj/"), "//server/share/Proj");
  assert.equal(resolveProjectPath("///server/share"), "//server/share");
  assert.equal(
    resolveProjectPath("//server/share/./Proj/../Nested"),
    "//server/share/Nested",
    "segment collapse still applies inside the share"
  );
  // POSIX absolute paths keep their single slash and resolve() semantics.
  assert.equal(resolveProjectPath("/home/dev/Proj"), path.resolve("/home/dev/Proj"));
});

test("projectPort keeps the live host bridge port for the checked-out project", () => {
  // Data-backed pin: the bridge for C:\Code\DxCommandTerminal answers on 27142,
  // which is FNV-1a over "c:/code/dxcommandterminal". Every platform and spelling
  // must keep deriving that same port.
  assert.equal(projectPort("C:/Code/DxCommandTerminal"), 27142);
  assert.equal(projectPort("C:\\Code\\DxCommandTerminal"), 27142);
});

test("resolveOptions derives the host bridge port from Windows project paths", () => {
  const options = resolveOptions(
    { "no-discover": true },
    {},
    { UNITY_PROJECT_PATH: "C:\\Code\\DxCommandTerminal" },
    "/tmp/does-not-matter-for-options"
  );
  assert.equal(options.port, 27142);
  assert.equal(options.projectPath, "C:/Code/DxCommandTerminal");
  assert.equal(projectPort(options.projectPath), 27142);
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
  assert.equal(flag.portSource, "flag");

  const env = resolveOptions({ ...base }, { [ENV_KEYS.port]: "27501" }, {}, repoRoot);
  assert.equal(env.port, 27501);
  assert.equal(env.portSource, "env");

  const local = resolveOptions({ ...base }, {}, { [ENV_KEYS.port]: "27502" }, repoRoot);
  assert.equal(local.port, 27502);
  assert.equal(local.portSource, "local");

  const derived = resolveOptions(
    { ...base },
    {},
    { [ENV_KEYS.projectPath]: projectPath },
    repoRoot
  );
  assert.equal(derived.port, projectPort(projectPath));
  assert.equal(derived.explicitPort, undefined, "derived port must not suppress discovery");
  assert.equal(derived.portSource, "derived");

  const stock = resolveOptions({ ...base }, {}, {}, repoRoot);
  assert.equal(stock.port, 9020);
  assert.equal(stock.portSource, "derived");
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
