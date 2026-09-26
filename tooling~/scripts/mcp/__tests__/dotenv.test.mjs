import test from "node:test";
import assert from "node:assert/strict";
import { parseDotEnv, readLocalEnv } from "../unity-mcp.mjs";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

test("parseDotEnv reads unquoted values with inline comments", () => {
  assert.deepEqual(parseDotEnv("A=1 # trailing comment\nB=2"), { A: "1", B: "2" });
});

test("parseDotEnv keeps hash characters inside quoted values", () => {
  assert.deepEqual(parseDotEnv('A="x # y"\nB=\'z # w\''), { A: "x # y", B: "z # w" });
});

test("parseDotEnv unescapes double quotes only", () => {
  assert.deepEqual(parseDotEnv('A="a\\"b\\\\c"'), { A: 'a"b\\c' });
  assert.deepEqual(parseDotEnv("B='a\\\"b'"), { B: "a\\\"b" });
});

test("parseDotEnv keeps trailing backslashes in double-quoted Windows paths", () => {
  assert.deepEqual(parseDotEnv('UNITY_PROJECT_PATH="D:\\Path\\To\\Proj\\"'), {
    UNITY_PROJECT_PATH: "D:\\Path\\To\\Proj\\"
  });
});

test("parseDotEnv supports export prefix and whitespace around equals", () => {
  assert.deepEqual(parseDotEnv("export  A = 1"), { A: "1" });
});

test("parseDotEnv allows comments after the closing quote", () => {
  assert.deepEqual(parseDotEnv('A="1" # note'), { A: "1" });
  assert.deepEqual(parseDotEnv("A='1'# note"), { A: "1" });
});

test("parseDotEnv rejects malformed lines", () => {
  assert.throws(() => parseDotEnv("A\nB=1"), /line 1/);
  assert.throws(() => parseDotEnv("=1"), /line 1/);
  assert.throws(() => parseDotEnv('A="unterminated'), /line 1/);
});

test("parseDotEnv accepts a UTF-8 BOM at the start of the file", () => {
  assert.deepEqual(parseDotEnv("\uFEFFFIRST=1\nSECOND=2"), { FIRST: "1", SECOND: "2" });
});

test("readLocalEnv skips malformed lines without aborting", () => {
  const repoRoot = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-env-"));
  try {
    fs.writeFileSync(path.join(repoRoot, ".env.local"), "GOOD=1\n!!!broken!!!\n# comment\nALSO=2\n");
    const warnings = [];
    const originalWarn = console.warn;
    console.warn = (...args) => warnings.push(args.join(" "));
    try {
      assert.deepEqual(readLocalEnv(repoRoot), { GOOD: "1", ALSO: "2" });
    } finally {
      console.warn = originalWarn;
    }
    assert.equal(warnings.length, 1);
    assert.match(warnings[0], /line 2/);
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("readLocalEnv returns empty for a missing file", () => {
  const repoRoot = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-env-"));
  try {
    assert.deepEqual(readLocalEnv(repoRoot), {});
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});
