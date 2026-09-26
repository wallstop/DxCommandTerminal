import test from "node:test";
import assert from "node:assert/strict";
import {
  resolveOptions,
  captureScriptSourcePath,
  captureInstallTarget,
  captureArtifactRoot,
  captureOutputDir,
  captureInvocationExpression,
  evalResultText,
  evalFailure,
  evalAnswerIsTrue,
  evalAnswerIsFalse,
  CAPTURE_PACKAGE_NAME
} from "../unity-mcp.mjs";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../..");

test("github token aliases resolve in documented order with env beating file", () => {
  const env = (token) => (token ? { GITHUB_TOKEN: token } : {});
  const local = { GH_TOKEN: "from-file" };
  assert.equal(resolveOptions({}, env("from-env"), local, "/tmp").githubToken, "from-env");
  assert.equal(resolveOptions({}, {}, local, "/tmp").githubToken, "from-file");

  const localFirst = resolveOptions(
    {},
    {},
    { GITHUB_PAT: "pat", GH_TOKEN: "gh" },
    "/tmp"
  );
  assert.equal(localFirst.githubToken, "gh", "alias order applies within .env.local too");
});

test("z.ai key aliases resolve with env beating file", () => {
  assert.equal(
    resolveOptions({}, { ZAI_API_KEY: "e1" }, { Z_AI_API_KEY: "f1" }, "/tmp").zaiToken,
    "e1"
  );
  assert.equal(
    resolveOptions({}, {}, { Z_AI_API_KEY: "f2" }, "/tmp").zaiToken,
    "f2"
  );
});

test("host and container project paths remain separate", () => {
  const options = resolveOptions(
    {},
    {
      UNITY_PROJECT_PATH: "/Users/dev/UnityProject",
      UNITY_PROJECT_CONTAINER_PATH: "/unity-project"
    },
    {},
    "/tmp"
  );
  assert.equal(options.projectPath, path.resolve("/Users/dev/UnityProject"));
  assert.equal(options.projectContainerPath, path.resolve("/unity-project"));
});

test("capture script source lives outside Unity compilation", () => {
  const source = captureScriptSourcePath(REPO_ROOT);
  assert.equal(path.basename(source), "DxTerminalStateCapture.cs.txt");
  assert.ok(fs.existsSync(source), `${source} must exist`);
});

test("capture install target follows the DxMessaging Assets/Editor convention", () => {
  const project = path.resolve("/host/UnityProject");
  assert.equal(
    captureInstallTarget(project),
    path.join(project, "Assets", "Editor", "DxTerminalStateCapture.cs")
  );
});

test("capture artifacts prefer the package tree and fall back to Library", () => {
  const project = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-cap-"));
  try {
    // No package directory: falls back to Library (never imported by Unity).
    assert.equal(
      captureArtifactRoot(project),
      path.join(project, "Library", "DxTerminalStateCapture")
    );

    const packageRoot = path.join(project, "Packages", CAPTURE_PACKAGE_NAME);
    fs.mkdirSync(packageRoot, { recursive: true });
    assert.equal(
      captureArtifactRoot(project),
      path.join(packageRoot, ".artifacts", "unity-state")
    );

    const stamp = "2026-09-07T00-00-00-000Z";
    assert.equal(
      captureOutputDir(project, stamp),
      path.join(packageRoot, ".artifacts", "unity-state", stamp)
    );

    // A host path no local filesystem can see must still pick the layout from
    // the container-visible project, then write through the resolved host path.
    const hostProject = path.resolve(path.sep, "host", "UnityProject");
    assert.equal(
      captureOutputDir(hostProject, stamp, project),
      path.join(hostProject, "Packages", CAPTURE_PACKAGE_NAME, ".artifacts", "unity-state", stamp)
    );
    assert.equal(
      captureOutputDir(hostProject, stamp, path.join(path.sep, "absent")),
      path.join(hostProject, "Library", "DxTerminalStateCapture", stamp)
    );
  } finally {
    fs.rmSync(project, { recursive: true, force: true });
  }
});

test("capture invocation resolves the editor type through qualified reflection", () => {
  const expression = captureInvocationExpression("CaptureAll", "C:\\host\\out dir");
  // Eval compiles statements: every line must end in ; and the type must be
  // resolved via assembly-qualified reflection, never by direct name (the
  // eval compiler does not reference Assembly-CSharp-Editor, issue #127).
  assert.match(expression, /System\.Type\.GetType\("DxTerminalDevTools\.DxTerminalStateCapture, Assembly-CSharp-Editor"\)/);
  assert.match(expression, /GetMethod\("CaptureAll"/);
  assert.match(expression, /BindingFlags\.Public \| System\.Reflection\.BindingFlags\.Static/);
  assert.match(expression, /@"C:\/host\/out dir"/);
  // C# verbatim strings escape quotes by doubling; backslashes never survive.
  assert.match(
    captureInvocationExpression("CaptureAll", '/tmp/say "hi"'),
    /@"\/tmp\/say ""hi"""/
  );
  for (const line of expression.split("\n")) {
    assert.match(line, /;$/, `statement must end in ';': ${line}`);
  }
  assert.match(expression, /captureType == null/);
  assert.match(expression, /captureMethod == null/);
});

test("eval result decoding never mistakes the envelope for the answer", () => {
  const cases = [
    // [envelope/text, expected decoded text]
    ['{"output":null,"diagnostics":[],"success":true,"result":"{\\"complete\\":true}"}', '{"complete":true}'],
    ['{"success":true,"result":true}', "true"],
    // A false result must never match /true/ via the envelope's success flag.
    ['{"success":true,"result":false}', "false"],
    ['{"success":true,"result":null}', ""],
    // Non-string results decode as JSON.
    ['{"success":true,"result":3}', "3"],
    ['{"success":true,"result":{"a":1}}', '{"a":1}'],
    // Envelopes without a result field (run_tests answers) stay untouched.
    ['{"Summary":{"total":5,"passed":5}}', '{"Summary":{"total":5,"passed":5}}'],
    // Raw-text backends stay untouched.
    ["Assets/Refresh executed", "Assets/Refresh executed"]
  ];
  for (const [text, expected] of cases) {
    assert.equal(evalResultText(text), expected, `input: ${text}`);
  }
});

test("eval failure envelopes surface the backend error instead of timing out", () => {
  // Observed live (issue #127 follow-up): a runtime exception inside the
  // invoked capture method answers success:false with no result field.
  const runtimeError = '{"output":null,"diagnostics":[],"success":false,"error":"Runtime Error","errorDetails":"Exception has been thrown by the target of an invocation."}';
  assert.equal(evalFailure(runtimeError), "Exception has been thrown by the target of an invocation.");
  assert.equal(evalFailure('{"success":false,"error":"Runtime Error"}'), "Runtime Error");
  // Empty-string details must not shadow the error field; non-strings are skipped.
  assert.equal(evalFailure('{"success":false,"error":"Runtime Error","errorDetails":""}'), "Runtime Error");
  assert.equal(evalFailure('{"success":false,"errorDetails":{"code":-1}}'), "eval failed");
  assert.equal(
    evalFailure('{"success":false,"diagnostics":[{"message":"CS0246: type not found"}]}'),
    "CS0246: type not found"
  );
  // Success envelopes, run_tests answers, and raw text are not failures.
  assert.equal(evalFailure('{"success":true,"result":null}'), null);
  assert.equal(evalFailure('{"Summary":{"total":5}}'), null);
  assert.equal(evalFailure("Assets/Refresh executed"), null);
});

test("decoded-answer predicates read the result, never the envelope flags", () => {
  // The #127 false positive: /true/i over the whole envelope matched
  // "success": true. The predicates must read only the decoded result.
  assert.equal(evalAnswerIsTrue('{"success":true,"result":false}'), false);
  assert.equal(evalAnswerIsTrue('{"success":true,"result":true}'), true);
  assert.equal(evalAnswerIsTrue('{"success":false,"result":false}'), false);
  assert.equal(evalAnswerIsTrue("True"), true);
  assert.equal(evalAnswerIsFalse('{"success":true,"result":true}'), false);
  assert.equal(evalAnswerIsFalse('{"success":false,"error":"Runtime Error"}'), false);
  assert.equal(evalAnswerIsFalse("false"), true);
});
