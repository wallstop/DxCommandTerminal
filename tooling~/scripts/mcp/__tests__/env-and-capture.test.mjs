import test from "node:test";
import assert from "node:assert/strict";
import {
  resolveOptions,
  captureScriptSourcePath,
  captureScriptFiles,
  captureInstallTarget,
  captureArtifactRoot,
  captureOutputDir,
  captureInvocationExpression,
  ensureCaptureScripts,
  evalResultText,
  evalFailure,
  evalAnswerIsTrue,
  evalAnswerIsFalse,
  CAPTURE_PACKAGE_NAME,
  SCRIPT_REFRESH_EXPRESSION,
  refreshScripts
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

test("GITHUB_MCP_PAT is an accepted github token alias", () => {
  assert.equal(
    resolveOptions({}, { GITHUB_MCP_PAT: "from-env" }, {}, "/tmp").githubToken,
    "from-env"
  );
  assert.equal(
    resolveOptions({}, {}, { GITHUB_MCP_PAT: "from-file" }, "/tmp").githubToken,
    "from-file"
  );
  // Canonical-first within a source: an explicit canonical key beats the alias.
  assert.equal(
    resolveOptions(
      {},
      {},
      { GITHUB_TOKEN: "canonical", GITHUB_MCP_PAT: "alias" },
      "/tmp"
    ).githubToken,
    "canonical"
  );
});

test("githubTokenSource names the variable that supplied the token", () => {
  assert.equal(
    resolveOptions({}, { GITHUB_MCP_PAT: "from-env" }, {}, "/tmp").githubTokenSource,
    "GITHUB_MCP_PAT"
  );
  assert.equal(
    resolveOptions({}, {}, { GH_TOKEN: "from-file" }, "/tmp").githubTokenSource,
    "GH_TOKEN"
  );
  assert.equal(
    resolveOptions({}, { GITHUB_TOKEN: "from-env" }, { GITHUB_PAT: "from-file" }, "/tmp")
      .githubTokenSource,
    "GITHUB_TOKEN",
    "process environment beats .env.local for the source name too"
  );
  assert.equal(resolveOptions({}, {}, {}, "/tmp").githubTokenSource, undefined);
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

test("zaiTokenSource names the variable that supplied the key", () => {
  assert.equal(
    resolveOptions({}, { ZAI_API_KEY: "e1" }, { Z_AI_API_KEY: "f1" }, "/tmp").zaiTokenSource,
    "ZAI_API_KEY",
    "process environment beats .env.local for the source name too"
  );
  assert.equal(
    resolveOptions({}, {}, { Z_AI_API_KEY: "f2" }, "/tmp").zaiTokenSource,
    "Z_AI_API_KEY"
  );
  assert.equal(resolveOptions({}, {}, {}, "/tmp").zaiTokenSource, undefined);
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

/*
    The reporter's claim grammar is not pinned here. It is driven through the
    callbacks a real run uses and its claims are decoded with the real reader:
    `npm run mcp:grammar`, and the `devtool-grammar` CI job. The reader's own
    states (a torn line, a counter that is not a count, a refusal) are tabulated
    in tests-command.test.mjs.
 */
/*
    The same class in the other dev tool, which the reporter's pins do not
    cover. `DxTerminalStateCapture` has no node-side reader for its output, so
    source text is all CI can check - the same stopgap, and the same follow-up
    (#167).

    Both of these replaced a hand-written range. `character < ' '` covered the
    C0 controls only, so a DEL, a C1 character, or a line separator wrote raw
    into every manifest a capture produces; a raw console message with a newline
    appended lines to a one-entry-per-line file that a red run is read from.
 */
test("the state capture asks a category question, not a hand-written range", () => {
  const capture = fs.readFileSync(captureScriptSourcePath(REPO_ROOT), "utf8");

  // The JSON writer, and the console-message writer: both must go through the
  // category predicate. A bare range comparison here is the regression.
  assert.doesNotMatch(capture, /character < ' '/u, "a hand-written C0 range must not return");
  const predicates = capture.match(/char\.IsControl\(character\)/gu) ?? [];
  assert.equal(predicates.length, 2, "both writers must ask char.IsControl");
  assert.match(capture, /IsLineSeparator\(char character\)/u);
  assert.match(capture, /return character is '\\u2028' or '\\u2029';/u);
  // The console file is one entry per line, so the message goes through the
  // same filter rather than being appended raw.
  assert.match(capture, /\.Append\(OneLine\(message\)\)/u);
});

test("a script refresh is asked for in script, and asks for the compile too", () => {
  /*
      Measured on a host editor with auto refresh switched off (issue #168):
      `menu: Assets/Refresh` answered success, the editor went idle, and
      Library/ScriptAssemblies kept the assembly it already had. A test leg
      without a refresh then named a test that no longer existed in the source.
      Both statements are load-bearing: the import finds the change, and the
      compile request is what the wait after it is for.
   */
  assert.match(SCRIPT_REFRESH_EXPRESSION, /UnityEditor\.AssetDatabase\.Refresh\(\);/u);
  assert.match(
    SCRIPT_REFRESH_EXPRESSION,
    /UnityEditor\.Compilation\.CompilationPipeline\.RequestScriptCompilation\(\);/u
  );
  // The eval compiler takes a statement list, so every statement ends here.
  const statements = SCRIPT_REFRESH_EXPRESSION.split(";").filter((part) => part.trim().length > 0);
  assert.equal(statements.length, 2, `two statements, got: ${SCRIPT_REFRESH_EXPRESSION}`);
});

test("the refresh is requested before the idle wait, and a busy editor is waited out", async () => {
  /*
      The order is the fix. The import and the compile request have to reach the
      editor before anything asks whether it is idle, and the wait after it has to
      survive an editor that is busy - which a compile that was only just
      scheduled looks like. Neither fact is visible to a reader of the source, and
      a command that dropped either step is the regression this gate exists for.
   */
  const answered = (text) => ({ call: { content: [{ text }] } });
  let clock = 0;
  const tick = () => {
    clock += 1_000;
    return clock;
  };

  const asked = [];
  let probes = 0;
  await refreshScripts(
    (expression) => {
      asked.push(expression);
      if (SCRIPT_REFRESH_EXPRESSION === expression) return answered("null");
      // Busy once, then idle: a compile that has only just been scheduled.
      probes += 1;
      return answered(probes === 1 ? "true" : "false");
    },
    60_000,
    tick,
    () => Promise.resolve()
  );

  assert.equal(asked[0], SCRIPT_REFRESH_EXPRESSION, "the refresh must be asked for first");
  assert.equal(asked.length, 3, `refresh, then two probes, got: ${asked.length}`);

  // A refused call is the editor holding its main thread, which is what a cold
  // compile looks like. It must be asked again rather than ending the command.
  const retried = [];
  await refreshScripts(
    (expression) => {
      retried.push(expression);
      if (retried.length === 1) throw new Error("Main thread operation timed out after 5000ms");
      return answered(SCRIPT_REFRESH_EXPRESSION === expression ? "null" : "false");
    },
    60_000,
    tick,
    () => Promise.resolve()
  );
  assert.equal(
    retried.filter((expression) => SCRIPT_REFRESH_EXPRESSION === expression).length,
    2,
    "a refused refresh must be asked again"
  );

  /*
      A refresh that is never accepted is the failure the whole command exists to
      avoid: the editor is still on the assembly it had, and the caller cannot
      see that. So the command ends, and it says why.
   */
  let refusals = 0;
  await assert.rejects(
    refreshScripts(
      () => {
        refusals += 1;
        throw new Error("Main thread operation timed out after 5000ms");
      },
      60_000,
      tick,
      () => Promise.resolve()
    ),
    /refused the script refresh/u
  );
  assert.ok(1 < refusals, "a refused refresh must be asked again before giving up");

  // A reported failure is the editor's own answer, and asking again would only
  // repeat it, so it is not retried.
  let reported = 0;
  await assert.rejects(
    refreshScripts(
      () => {
        reported += 1;
        return {
          call: {
            content: [
              {
                text: '{"success":false,"errorDetails":{"code":-1},"error":"Runtime Error"}'
              }
            ]
          }
        };
      },
      60_000,
      tick,
      () => Promise.resolve()
    ),
    /script refresh failed/u
  );
  assert.equal(reported, 1, "a reported failure must not be retried");
});

test("both editor dev tools install together, and a re-install is a no-op", () => {
  const files = captureScriptFiles(REPO_ROOT);
  // State capture and the test run reporter: one command installs both, or the
  // reporter is missing on a fresh host and every run polls the bridge.
  assert.deepEqual(
    files.map((file) => file.target),
    ["DxTerminalStateCapture.cs", "DxTerminalTestRunReporter.cs"]
  );
  for (const file of files) {
    assert.ok(fs.existsSync(file.source), `${file.source} must exist`);
  }

  const project = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-install-"));
  try {
    // The reporter has no package tree here, so backups land under Library.
    const assets = path.join(project, "Assets", "Editor");
    fs.mkdirSync(assets, { recursive: true });
    const reporter = captureInstallTarget(project, "DxTerminalTestRunReporter.cs");
    fs.writeFileSync(reporter, "// a different local copy\n");

    const installed = ensureCaptureScripts(project, REPO_ROOT);
    assert.equal(installed.length, files.length);
    for (const result of installed) {
      assert.equal(result.changed, true, `${result.target} must install`);
      assert.equal(
        fs.readFileSync(result.target, "utf8"),
        fs.readFileSync(captureScriptFiles(REPO_ROOT).find((file) => file.target === path.basename(result.target)).source, "utf8")
      );
    }
    // A clobbered file is backed up, never silently replaced.
    const backup = installed.find((result) => result.target === reporter).backup;
    assert.match(backup, /DxTerminalTestRunReporter\.cs\..*\.bak$/u);
    assert.equal(fs.readFileSync(backup, "utf8"), "// a different local copy\n");

    for (const result of ensureCaptureScripts(project, REPO_ROOT)) {
      assert.equal(result.changed, false, `${result.target} must be current`);
      assert.equal(result.backup, undefined);
    }
  } finally {
    fs.rmSync(project, { recursive: true, force: true });
  }
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
