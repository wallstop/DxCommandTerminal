import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const workflowPath = path.join(repoRoot, ".github/workflows/docs-build.yml");
const workflow = fs.readFileSync(workflowPath, "utf8").replaceAll("\r\n", "\n");

function job(name) {
  const start = workflow.indexOf(`\n  ${name}:\n`);
  assert.notEqual(start, -1);
  return workflow.slice(start + 1).split(/\n(?=  [a-z][a-z-]*:\n)/)[0];
}

test("Pages deployment is manual and master-only", () => {
  assert.match(workflow, /^  workflow_dispatch:$/m);
  const guard = "github.event_name == 'workflow_dispatch' && github.repository == 'wallstop/DxCommandTerminal' && github.ref == 'refs/heads/master'";
  assert.equal((workflow.match(/github.event_name == 'workflow_dispatch'/g) ?? []).length, 2);
  const deploy = job("deploy");
  assert.match(deploy, new RegExp(`^    if: ${guard}$`, "m"));
  assert.match(deploy, /^    needs: docs-build$/m);
  assert.match(deploy, /^    environment: github-pages$/m);
});

test("Pages actions are SHA-pinned with least privilege", () => {
  const build = job("docs-build");
  const deploy = job("deploy");
  assert.match(build, /actions\/upload-pages-artifact@fc324d3547104276b827a68afc52ff2a11cc49c9/);
  assert.match(deploy, /actions\/deploy-pages@368f82528645a54fb793d4d04e342629a3f51346/);
  assert.match(deploy, /^      pages: write$/m);
  assert.match(deploy, /^      id-token: write$/m);
  assert.doesNotMatch(deploy, /^      contents: write$/m);
  assert.doesNotMatch(deploy, /^      attestations: write$/m);
});

test("ordinary documentation events cannot publish Pages", () => {
  const build = job("docs-build");
  const guard = "github.event_name == 'workflow_dispatch' && github.repository == 'wallstop/DxCommandTerminal' && github.ref == 'refs/heads/master'";
  assert.match(build, new RegExp(`^        if: ${guard}$`, "m"));
  assert.doesNotMatch(workflow.slice(0, workflow.indexOf("\n  docs-build:")), /deploy-pages|upload-pages-artifact/);
});
