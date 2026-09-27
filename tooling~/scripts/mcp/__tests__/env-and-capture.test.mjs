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
  parseRunClaim,
  evalResultText,
  evalFailure,
  evalAnswerIsTrue,
  evalAnswerIsFalse,
  CAPTURE_PACKAGE_NAME,
  RUN_CLAIM_FILE,
  RUN_REQUEST_FILE,
  RUN_UNATTRIBUTED_TOKEN
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

test("the reporter and the claim decoder agree on names, heads, and fields", () => {
  // The writer is C# that only compiles inside a host project, so a renamed
  // field on either side is invisible to every other test: the reader would wait
  // out the start grace and then fail with a false diagnosis. Pin both sides
  // here instead.
  const reporter = fs.readFileSync(captureScriptFiles(REPO_ROOT)[1].source, "utf8");
  for (const name of [RUN_CLAIM_FILE, RUN_REQUEST_FILE, "test-run-active.txt"]) {
    assert.match(reporter, new RegExp(`"${name}"`, "u"), `${name} must be named in the reporter`);
  }
  assert.match(reporter, /RUN_UNATTRIBUTED_TOKEN|"none"/u, "the unattributed token must be in the reporter");

  for (const head of ["running", "did-not-run", "pass="]) {
    assert.ok(reporter.includes(head), `the reporter must write a ${head} claim`);
  }
  // Every field the decoder reads, and the ones it only forwards to a human.
  for (const field of [
    "token=",
    "mode=",
    "reason=",
    "pass=",
    "fail=",
    "failed-names=",
    "skipped=",
    "inconclusive=",
    "duration=",
    "started=",
    "finished="
  ]) {
    assert.ok(reporter.includes(field), `the reporter must write ${field}`);
  }
  // The field name, where the reporter actually writes it. The loop above is
  // satisfied by the header comment, so a renamed field would slip through it
  // and the reader's `fields.get("failed-names")` would quietly read no names.
  assert.match(reporter, /return " failed-names=" \+ builder;/u);
  // A round trip through the decoder: the shape the reporter writes must decode
  // as a finished run with the editor's own counters, and a red run must decode
  // into the names it carries.
  const claim = parseRunClaim(
    "pass=581 fail=2 skipped=1 inconclusive=0 duration=12.595 token=t-1 mode=PlayMode finished=x"
  );
  assert.deepEqual(claim.summary, {
    total: 584,
    passed: 581,
    failed: 2,
    skipped: 1,
    inconclusive: 0,
    failedNames: [],
    failedMore: 0
  });

  const red = parseRunClaim(
    "pass=361 fail=7 skipped=0 inconclusive=0 duration=9.1 token=t-2 mode=EditMode "
      + "failed-names=Wallstop.A.One,Wallstop.B.Two"
      // %20 is a space, %E2%80%83 is U+2003: the name survives the line intact.
      + ',Wallstop.C.Three(%22a%20b%22),Wallstop.C.Four(%E2%80%83) failed-more=3'
  );
  assert.deepEqual(red.summary.failedNames, [
    "Wallstop.A.One",
    "Wallstop.B.Two",
    'Wallstop.C.Three("a b")',
    "Wallstop.C.Four(\u2003)"
  ]);
  assert.equal(red.summary.failedMore, 3);

  /*
      The reporter's contract, at its declarations and at its use sites. Nothing
      in this repository can run the reporter's own C# - issue #164 is the
      follow-up that would compile it in CI - so a renamed separator, a moved cap,
      a byte-wise encoder or a dead gate would reach a developer machine with
      nothing red. These pins cost a behavior-preserving refactor its test suite;
      that tradeoff belongs next to them, not only in a review.

      Each of these is a value the reader also hard-codes, so disagreement between
      the two files is a bug on either side.
   */
  assert.match(reporter, /private const char FailureSeparator = ',';/u);
  assert.match(reporter, /builder\.Append\(FailureSeparator\)/u);
  assert.match(reporter, /private const int MaxReportedFailures = 10;/u);
  assert.match(reporter, /MaxReportedFailures <= names\.Count/u);
  assert.match(reporter, /if \(AllowedNameCharacter\(character\)\)/u);
  // The byte-wise cast is the whole reason U+2003 encodes to %E2%80%83 and not
  // to %2003, which would decode as a space and "03".
  assert.match(reporter, /char character = \(char\)value;/u);
  /*
      The escape, and the cap count, are the only two values the reporter puts
      onto the line without percent-encoding them. Both carry the invariant
      culture, so neither depends on the host's locale.
   */
  assert.match(
    reporter,
    /\.Append\('%'\)\.Append\(value\.ToString\("X2", CultureInfo\.InvariantCulture\)\)/u
  );
  assert.match(reporter, /\.Append\(" failed-more="\)/u);
  assert.match(reporter, /\.Append\(unnamed\.ToString\(CultureInfo\.InvariantCulture\)\)/u);
  const readme = fs.readFileSync(path.join(REPO_ROOT, "tooling~", "scripts", "mcp", "README.md"), "utf8");
  assert.match(readme, /capped at ten\b/u, "the README must state the reporter's cap");

  /*
      The reporter's name allowlist is the only thing standing between a test
      name and a broken claim line: the line is space separated, the fields are
      key=value, and the reader splits the names on a comma. A newline, a tab, or
      a no-break space would silently truncate the field, so the allowlist is
      pinned by its exact contents - not by a list of characters believed to be
      dangerous, which is a list that has to be kept up to date by hand.

      Nothing in this repository can run the reporter's own C#, so the invariant
      is pinned on its source (issue #164 is the follow-up that would compile it
      in CI instead).
   */
  const start = reporter.indexOf("private static bool AllowedNameCharacter");
  assert.notEqual(start, -1, "the reporter must hold an AllowedNameCharacter method");
  // The method, not the rest of the file: the literals after it belong to other
  // code, and a doc comment with an apostrophe in it would be read as a literal.
  const end = reporter.indexOf("\n        }\n", start);
  assert.notEqual(end, -1, "AllowedNameCharacter must be a method at eight-space indent");
  const allowlist = reporter.slice(start, end);
  // Every character literal in it, escape sequences included, so a `'\n'`, a
  // `'\t'` or a `'\u00a0'` cannot slip past. What is left is the ASCII
  // punctuation a test name is built from plus the six range endpoints. This
  // pins the allowlist, not the encoder: a numeric widening expressed as a hex
  // literal would pass here, which is the gap #164 closes.
  assert.deepEqual(
    [...new Set(allowlist.match(/'(?:\\.|[^'\\])*'/gu) ?? [])].sort(),
    ["'('", "')'", "'.'", "'0'", "'9'", "'A'", "'Z'", "'['", "']'", "'_'", "'a'", "'z'"],
    "only ASCII letters, digits, and the punctuation a test name is built from may pass through"
  );
});

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
