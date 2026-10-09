import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import http from "node:http";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { nextProjectPort, projectPort, startBridge } from "../unity-mcp.mjs";

const BEARER = "a".repeat(64);

function tempRepo() {
  const repoRoot = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-bridge-port-"));
  fs.writeFileSync(path.join(repoRoot, ".env.local"), `UNITY_MCP_BEARER_TOKEN=${BEARER}\n`);
  return repoRoot;
}

// The bridge options under test never spawn Unity: backend cli without an
// editor only prepares the HTTP server.
function bridgeOptions(repoRoot, overrides = {}) {
  return {
    repoRoot,
    bearerToken: BEARER,
    projectPath: repoRoot,
    bindHost: "127.0.0.1",
    host: "host.docker.internal",
    endpointPath: "/mcp",
    port: projectPort(repoRoot),
    portSource: "derived",
    logLevel: "none",
    ...overrides
  };
}

// A plain socket occupant: reachable, but no HTTP answer, so the bridge health
// probe must report "not a live bridge" after its timeout. Accepted sockets are
// tracked and destroyed by stop(): a raw net.Server has no closeAllConnections,
// and close() alone waits on a probe socket the client side already destroyed.
function occupy(port) {
  return new Promise((resolve, reject) => {
    const sockets = new Set();
    const server = net.createServer();
    server.on("connection", (socket) => sockets.add(socket));
    server.once("error", reject);
    server.stop = () => {
      for (const socket of sockets) socket.destroy();
      return new Promise((resolve) => server.close(resolve));
    };
    server.listen({ port, host: "127.0.0.1", exclusive: true }, () => resolve(server));
  });
}

// A stand-in bridge: /healthz answers 200 like the real one.
function fakeBridge(port) {
  return new Promise((resolve, reject) => {
    const server = http.createServer((request, response) => {
      response.writeHead(200, { "Content-Type": "text/plain" });
      response.end("ok");
    });
    server.once("error", reject);
    server.listen({ port, host: "127.0.0.1" }, () => resolve(server));
  });
}

async function stop(server) {
  if (!server) return;
  // Destroy tracked sockets first: server.close() waits for connections, and
  // the health probe leaves an accepted (idle) socket behind.
  if (server.stop) {
    await server.stop();
    return;
  }
  server.closeAllConnections?.();
  await new Promise((resolve) => server.close(resolve));
}

function persistedPort(repoRoot) {
  const match = /^UNITY_MCP_BRIDGE_PORT=(\d+)$/m.exec(
    fs.readFileSync(path.join(repoRoot, ".env.local"), "utf8")
  );
  return match === null ? undefined : Number(match[1]);
}

test("nextProjectPort skips taken ports and stays inside the range", async () => {
  const taken = new Set([27100, 27101, 27103]);
  assert.equal(await nextProjectPort(27100, (port) => taken.has(port)), 27102);
  assert.equal(await nextProjectPort(27103, (port) => taken.has(port)), 27104);
  assert.equal(await nextProjectPort(27999, (port) => port >= 27900), null, "exhausted range");
  assert.equal(await nextProjectPort(26000, () => false), 27100, "clamps to the range base");
});

test("a busy derived port moves to the next free port and persists the choice", async () => {
  const repoRoot = tempRepo();
  let occupier;
  let bridge;
  try {
    const busy = projectPort(repoRoot);
    occupier = await occupy(busy);
    const running = await startBridge(bridgeOptions(repoRoot));
    bridge = running.httpServer;
    assert.notEqual(running.alreadyRunning, true);
    assert.equal(running.options.port, busy + 1, "moves one port past the conflict");
    assert.equal(persistedPort(repoRoot), busy + 1, "the moved port is persisted in .env.local");
  } finally {
    await stop(bridge);
    await stop(occupier);
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

// Windows lets a 0.0.0.0 bind "succeed" over a 127.0.0.1-held port, shadowing
// the bridge on loopback; the availability check must treat that as a conflict.
test("a loopback-held port conflicts with a 0.0.0.0 bind and moves", async () => {
  const repoRoot = tempRepo();
  let occupier;
  let bridge;
  try {
    const busy = projectPort(repoRoot);
    occupier = await occupy(busy);
    const running = await startBridge(
      bridgeOptions(repoRoot, { bindHost: "0.0.0.0" })
    );
    bridge = running.httpServer;
    assert.notEqual(running.options.port, busy, "must not bind the shadowed port");
    assert.equal(persistedPort(repoRoot), running.options.port);
  } finally {
    await stop(bridge);
    await stop(occupier);
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("a free derived port binds exactly there and persists nothing", async () => {
  const repoRoot = tempRepo();
  let bridge;
  try {
    const expected = projectPort(repoRoot);
    const running = await startBridge(bridgeOptions(repoRoot));
    bridge = running.httpServer;
    assert.equal(running.options.port, expected);
    assert.equal(persistedPort(repoRoot), undefined, "derived ports are recomputable");
  } finally {
    await stop(bridge);
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("a live bridge on the port is reported instead of spawning a duplicate", async () => {
  const repoRoot = tempRepo();
  let bridge;
  let running;
  try {
    const busy = projectPort(repoRoot);
    bridge = await fakeBridge(busy);
    running = await startBridge(bridgeOptions(repoRoot));
    assert.equal(running.alreadyRunning, true);
    assert.match(running.url, new RegExp(`:${busy}/mcp$`));
    assert.equal(running.httpServer, undefined, "no second listener is created");
    assert.equal(persistedPort(repoRoot), undefined, "nothing is persisted for a live bridge");
  } finally {
    await stop(bridge);
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("an operator-supplied port fails fast instead of moving silently", async () => {
  const repoRoot = tempRepo();
  let occupier;
  try {
    const busy = projectPort(repoRoot);
    occupier = await occupy(busy);
    await assert.rejects(
      () => startBridge(bridgeOptions(repoRoot, { port: busy, portSource: "flag" })),
      (error) => {
        assert.match(error.message, new RegExp(`Port ${busy} is unavailable`));
        assert.match(error.message, /UNITY_MCP_BRIDGE_PORT/);
        return true;
      }
    );
    assert.equal(persistedPort(repoRoot), undefined, "explicit ports are never rewritten");
  } finally {
    await stop(occupier);
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});