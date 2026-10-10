/*
    Contract tests for the display-text rule: text on its way to a display
    sink is escaped, and the raw text is what the code acts on.

    Why a source-level contract and not a Unity test: the sinks this covers
    are the package's own IMGUI inspectors, and no CI lane compiles the
    Editor assembly, so a test that needs an editor is a test that never
    runs. The runtime sinks are pinned by LogTextSanitizerTests in the
    editor's own test assembly; what was missing is the inspector side, and
    a rule checkable without Unity is the only gate available.

    Two rules, deliberately different in strength:

    1. Precise, and the one that carries weight. Every hole of an
       interpolated string inside a GUIContent construction must be a
       LogTextSanitizer.Sanitize call: a tooltip that names a theme, a
       font, a command, or an asset path has to escape it, and a tooltip
       made of the package's own words is not a finding. Read from the
       source, not run, so it also reads the shapes Unity would never
       execute - an editor-only branch, a platform-guarded line. What it
       does not cover is stated below rather than assumed.
    2. A backstop for the popups, whose labels are escaped in the caching
       builder that fills them and are therefore not visible at the call.

    Rule 1 sees exactly one shape: a `GUIContent <name> = new(` or
    `new GUIContent(` whose argument list holds an interpolated string. Not
    seen, so a future reader is not misled: a tooltip assembled by
    concatenation, a tooltip held in a local and passed as a variable, a
    `GUIContent` bound by an object initializer or on a later line, an
    expression-bodied factory, and a label API other than a GUIContent
    construction (a bare `GUILayout.Label($"... {name}")`). None of those
    exists in the package today; widening the gate to them is a separate
    piece of work.

    Rule 2 watches `EditorGUILayout.Popup` only - the call the three
    inspectors use. `EditorGUI.Popup` and `EditorGUILayout.IntPopup` are
    the same hazard and are not watched.

    Rule 3 covers the package's own Debug.Log-family calls. Their sink is
    Unity's Console window and the player log, which show the message the
    package handed them before the in-game funnel ever sees it, so the call
    site is where the escape has to happen. A message built by
    interpolation is display text end to end - there is no raw value the
    reader acts on - so the rule wraps the whole message, the same shape the
    funnel sanitizes in CommandLog.HandleLog. A Debug.Log whose message
    argument holds an interpolated string must be wrapped in
    LogTextSanitizer.Sanitize, and so must the one call that prints
    developer-typed text without interpolating it: the `log` command's
    JoinArguments. Not seen, so not covered: a message assembled into a
    local before the call, a Debug.LogFormat (none exists in the package,
    and the rule's regex does not match it), and Debug.LogException, whose
    message belongs to the exception object rather than to this package.

    The scanner has its own tests below: a gate that cannot fail is worse
    than no gate, and an earlier version of it walked only the top level of
    each root and passed on the very sinks it was written for.
*/
import test from "node:test";
import assert from "node:assert";
import path from "node:path";
import fs from "node:fs";
import { fileURLToPath } from "node:url";
import {
  blankComments,
  callCloseParen,
  classify,
  codeText,
  firstArgumentSpan,
  interpolationHoles,
  lineOf,
  startsInside
} from "../lib/csharp-interpolation.mjs";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
/* The call, with its paren: a prefix test would accept a same-named sibling. */
const SANITIZE_CALL = "LogTextSanitizer.Sanitize(";
/* The backstop only asks whether the file reaches the sanitizer at all. */
const SANITIZER = "LogTextSanitizer.";

/** Every shipped C# file that can print a name. Tests and tooling are exempt. */
function shippedCSharpFiles() {
  const files = [];
  for (const root of ["Runtime", "Editor", "Packs", "Styles", "Samples~"]) {
    collectCSharpFiles(path.join(repoRoot, root), files);
  }

  return files;
}

function collectCSharpFiles(directory, files) {
  if (!fs.existsSync(directory)) {
    return;
  }

  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      collectCSharpFiles(full, files);
    } else if (entry.name.endsWith(".cs")) {
      files.push(full);
    }
  }
}

/*
    A GUIContent construction, not a GUIContent-typed parameter: the
    constructor's argument list is where a name reaches a label. The regex
    requires the `new`, so a method that takes a GUIContent is not read as
    one and the walk cannot land on an unrelated call.
 */
const GUICONTENT_CONSTRUCTION =
  /\bGUIContent\s+\w+\s*=\s*new\s*\(|\bnew\s+GUIContent\s*\(/g;

/** Every unescaped name interpolated into a GUIContent, as "line: hole". */
function unescapedTooltipNames(text) {
  const { literals, comments } = classify(text);
  // The paren walk reads comment-blanked text (same offsets): a comment with
  // an unbalanced paren must not desync it (rule 3 and the lints do this too).
  const code = blankComments(text, comments);
  const findings = [];
  for (const match of text.matchAll(GUICONTENT_CONSTRUCTION)) {
    if (startsInside(comments, match.index) || startsInside(literals, match.index)) {
      continue;
    }

    const openParen = match.index + match[0].length - 1;
    const closeParen = callCloseParen(code, openParen, literals);
    if (closeParen < 0) {
      continue;
    }

    const argumentText = code.slice(openParen + 1, closeParen);
    const argumentLiterals = literals
      .filter((literal) => literal.start > openParen && literal.end <= closeParen)
      .map((literal) => ({
        ...literal,
        // Both spans rebase by the argument's start, so literal and hole
        // offsets read from the same origin.
        start: literal.start - openParen - 1,
        end: literal.end - openParen - 1,
        holes: literal.holes.map((span) => ({
          start: span.start - openParen - 1,
          end: span.end - openParen - 1
        }))
      }));
    for (const { hole, offset } of interpolationHoles(argumentText, argumentLiterals)) {
      if (!hole.startsWith(SANITIZE_CALL)) {
        findings.push(`line ${lineOf(text, openParen + 1 + offset)}: ${hole}`);
      }
    }
  }

  return findings;
}

/*
    A popup returns an index, so its labels are display-only and the value
    behind the index is the raw name. Escaping those labels means a call the
    gate cannot see: the escape lives in the caching builder that fills the
    array, and a label array stamped on a source and count is rebuilt only
    when that source changes. Following the call would take a call graph, so
    rule 2 is a backstop instead: a file that pops up project names has to
    reach the sanitizer somewhere. It cannot tell an escaped label from a
    raw one, and it does not claim to.
 */
function popupsWithoutASanitizer(text) {
  const { literals, comments } = classify(text);
  const code = codeText(text, literals, comments);
  return code.includes("EditorGUILayout.Popup(") && !code.includes(SANITIZER);
}

/*
    Rule 3's subject: the Debug.Log-family calls whose sink is Unity's
    Console window and the player log. `\b` matches the tail of
    `UnityEngine.Debug.` too, which is the shape the `log` command uses.
 */
const DEBUG_LOG_CALL = /\bDebug\.(?:Log|LogError|LogWarning)\s*\(/g;
/* The one message built without interpolation that still prints caller-typed text. */
const JOIN_ARGUMENTS_CALL = /^(?:UnityEngine\.)?JoinArguments\s*\(/;

/** True when the argument is exactly a Sanitize call around one expression. */
function isWrappedInSanitize(argumentText, argumentLiterals) {
  const leading = argumentText.length - argumentText.trimStart().length;
  if (!argumentText.startsWith(SANITIZE_CALL, leading)) {
    return false;
  }

  const trailing = argumentText.length - argumentText.trimEnd().length;
  if (argumentText[argumentText.length - trailing - 1] !== ")") {
    return false;
  }

  /*
      The walk reads the untrimmed text, so literal offsets stay valid; only
      the open paren's offset accounts for the leading whitespace.
   */
  const openParen = leading + SANITIZE_CALL.length - 1;
  const closeParen = callCloseParen(argumentText, openParen, argumentLiterals);
  return closeParen === argumentText.length - trailing - 1;
}

/** Every Debug.Log-family message the rule requires wrapped, as "line: what". */
function unwrappedDebugLogMessages(text) {
  const { literals, comments } = classify(text);
  const code = blankComments(text, comments);
  const findings = [];
  for (const match of code.matchAll(DEBUG_LOG_CALL)) {
    if (startsInside(literals, match.index)) {
      continue;
    }

    const openParen = match.index + match[0].length - 1;
    const closeParen = callCloseParen(code, openParen, literals);
    if (closeParen < 0) {
      continue;
    }

    const [argumentStart, argumentEnd] = firstArgumentSpan(code, openParen, closeParen, literals);
    const argumentText = code.slice(argumentStart, argumentEnd);
    const argumentLiterals = literals
      .filter((literal) => literal.start >= argumentStart && literal.end <= argumentEnd)
      .map((literal) => ({
        ...literal,
        start: literal.start - argumentStart,
        end: literal.end - argumentStart,
        holes: literal.holes.map((span) => ({
          start: span.start - argumentStart,
          end: span.end - argumentStart
        }))
      }));
    const holes = interpolationHoles(argumentText, argumentLiterals);
    const printsCallerText = JOIN_ARGUMENTS_CALL.test(argumentText.trim());
    if (holes.length === 0 && !printsCallerText) {
      continue;
    }

    if (!isWrappedInSanitize(argumentText, argumentLiterals)) {
      const what = printsCallerText ? "JoinArguments output" : holes.map((hole) => hole.hole).join(", ");
      findings.push(`line ${lineOf(text, openParen)}: ${what}`);
    }
  }

  return findings;
}

test("display text: a tooltip escapes every name it interpolates", () => {
  const findings = [];
  for (const file of shippedCSharpFiles()) {
    const text = fs.readFileSync(file, "utf8");
    for (const finding of unescapedTooltipNames(text)) {
      findings.push(`${path.relative(repoRoot, file)} ${finding}`);
    }
  }

  assert.deepEqual(
    findings,
    [],
    "a name that reads one way in a tooltip is not the name the project holds:\n" +
      findings.join("\n")
  );
});

test("display text: an inspector that pops up names reaches the sanitizer", () => {
  const findings = [];
  for (const file of shippedCSharpFiles()) {
    const text = fs.readFileSync(file, "utf8");
    if (popupsWithoutASanitizer(text)) {
      findings.push(
        `${path.relative(repoRoot, file)} pops up project names and never reaches ${SANITIZER}`
      );
    }
  }

  assert.deepEqual(findings, [], findings.join("\n"));
});

test("display text: a Debug.Log message built by interpolation is wrapped in the sanitizer", () => {
  const findings = [];
  for (const file of shippedCSharpFiles()) {
    const text = fs.readFileSync(file, "utf8");
    for (const finding of unwrappedDebugLogMessages(text)) {
      findings.push(`${path.relative(repoRoot, file)} ${finding}`);
    }
  }

  assert.deepEqual(
    findings,
    [],
    "Unity's Console shows the message the package handed it, before the in-game funnel runs:\n" +
      findings.join("\n")
  );
});

test("display text: the scanner names a raw tooltip hole", () => {
  const shapes = [
    { lines: 4, line: 3 },
    { lines: 1, line: 1 }
  ];
  const sources = [
    [
      "GUIContent tip = new(",
      '    "Set Theme",',
      '    $"Will set the current theme to {theme}"',
      ");"
    ],
    ['GUIContent tip = new("Set Theme", $"Will set the current theme to {theme}");']
  ];
  for (const [index, { line }] of shapes.entries()) {
    assert.deepEqual(
      unescapedTooltipNames(sources[index].join("\n")),
      [`line ${line}: theme`],
      "a raw name in a tooltip must be reported, wrapped or on one line"
    );
  }
});

test("display text: the scanner accepts an escaped tooltip hole", () => {
  const shapes = [
    [`GUIContent tip = new("Set Theme", $"Will set {${SANITIZE_CALL}theme)}");`],
    ["GUIContent tip = new(", '    "Set Theme",', `    $"Will set {${SANITIZE_CALL}theme)}"`, ");"]
  ];
  for (const lines of shapes) {
    assert.deepEqual(unescapedTooltipNames(lines.join("\n")), []);
  }
});

test("display text: the scanner is not fooled by the shapes around it", () => {
  const sources = [
    [
      "GUIContent tip = new(",
      '    "Label",',
      '    @"a""b (c) {d}",',
      '    $"Will set {name}"',
      ");"
    ],
    ['// GUIContent tip = new("x", $"raw {name} in a line comment");'],
    ['/* GUIContent tip = new("x", $"raw {name} in a block comment"); */'],
    ['var text = @"GUIContent = new(""x"", $""raw {name} in a string"")";'],
    ['public override float GetPropertyHeight(SerializedProperty property, GUIContent label)']
  ];
  assert.deepEqual(
    unescapedTooltipNames(sources[0].join("\n")),
    ["line 4: name"],
    "a bracket or brace inside an earlier argument must not move the hole"
  );
  for (const source of sources.slice(1)) {
    assert.deepEqual(unescapedTooltipNames(source.join("\n")), [], source.join("\n"));
  }

});

test("display text: a verbatim interpolated tooltip is read, and its doubled brace is not a hole", () => {
  assert.deepEqual(
    unescapedTooltipNames([`GUIContent tip = new("a", $@"Will set {name} now");`].join("\n")),
    ["line 1: name"],
    "a $@ hole is a hole: not seeing it is a false negative, not a clean file"
  );
  assert.deepEqual(
    unescapedTooltipNames([`GUIContent tip = new("a", $@"literal {{name}} text");`].join("\n")),
    [],
    "a doubled brace in a verbatim string is literal text, not a hole: {name}"
  );
  assert.deepEqual(
    unescapedTooltipNames([`GUIContent tip = new("a", $@"Will set {${SANITIZE_CALL}name)}");`].join("\n")),
    [],
    "an escaped hole in a verbatim string is still escaped"
  );
});

test("display text: the popup backstop is not satisfied by a mention", () => {
  /*
    The mentions sit between literals, which is the shape that breaks a
    removal walk whose spans are not ordered: a comment before every literal
    passes whether or not the walk is correct, so interleave them.
   */
  assert.ok(
    popupsWithoutASanitizer(
      [
        'string a = "first";',
        "// TODO: the labels should call LogTextSanitizer.Sanitize",
        'string b = "second";',
        "EditorGUILayout.Popup(0, names);",
        "// end of file TODO"
      ].join("\n")
    ),
    "a mention in a comment is not a call, even between literals"
  );
  assert.ok(
    popupsWithoutASanitizer(['Debug.Log("LogTextSanitizer.Sanitize");', "EditorGUILayout.Popup(0, names);"].join("\n")),
    "a mention in a string literal is not a call"
  );
  assert.ok(
    !popupsWithoutASanitizer(
      [
        "private static void Draw(string[] names, ref string[] labels)",
        "{",
        "    LogTextSanitizer.SanitizeInto(names, ref labels);",
        "    EditorGUILayout.Popup(0, labels);",
        "}"
      ].join("\n")
    ),
    "a real call in code satisfies the backstop"
  );
});

test("display text: the Debug.Log rule names a raw interpolated message", () => {
  const sources = [
    ['Debug.Log($"Will set the theme to {theme}.", this);', "theme"],
    ['UnityEngine.Debug.Log($"count {count} ms");', "count"],
    ['Debug.LogWarning($"assembly {assemblyName} failed");', "assemblyName"]
  ];
  for (const [source, hole] of sources) {
    assert.deepEqual(unwrappedDebugLogMessages(source), [`line 1: ${hole}`], source);
  }

  const multiline = ["Debug.LogWarning(", '    $"assembly {assemblyName} failed"', ");"].join("\n");
  assert.deepEqual(unwrappedDebugLogMessages(multiline), ["line 1: assemblyName"], multiline);
});

test("display text: the Debug.Log rule accepts a wrapped or constant message", () => {
  const passing = [
    'Debug.Log(LogTextSanitizer.Sanitize($"Will set {theme}."), this);',
    [
      "Debug.LogWarning(",
      '    LogTextSanitizer.Sanitize($"[Dx] assembly {name} failed"\n        + $" ({reason})"),',
      "    this",
      ");"
    ].join("\n"),
    'Debug.Log(LogTextSanitizer.Sanitize(currentFont == null ? $"a {font.name}" : $"b {font.name}"), this);',
    'Debug.LogError("Cannot set null font.", this);',
    "Debug.Log(message);",
    'Debug.LogFormat("legacy {0}", name);',
    "Debug.DrawLine(a, b);",
    'Logger.Debug.Log($"not Unity\'s Debug");'
  ];
  for (const source of passing) {
    assert.deepEqual(unwrappedDebugLogMessages(source), [], source);
  }
});

test("display text: the Debug.Log rule covers the log command's JoinArguments output", () => {
  assert.deepEqual(
    unwrappedDebugLogMessages("UnityEngine.Debug.Log(JoinArguments(args));"),
    ["line 1: JoinArguments output"],
    "developer-typed text reaches Unity's Console through the log command unwrapped"
  );
  assert.deepEqual(
    unwrappedDebugLogMessages("UnityEngine.Debug.Log(LogTextSanitizer.Sanitize(JoinArguments(args)));"),
    []
  );
  assert.deepEqual(
    unwrappedDebugLogMessages("UnityEngine.Debug.Log(SomeOther(JoinArguments(args)));"),
    [],
    "only a message argument that starts with JoinArguments is the log command's shape"
  );
});

test("display text: the Debug.Log rule ignores the shapes around it", () => {
  const immune = [
    '// Debug.Log($"raw {name} in a line comment");',
    '/* Debug.Log($"raw {name} in a block comment"); */',
    'var s = "Debug.Log($\\"raw {name} in a string\\");";'
  ];
  for (const source of immune) {
    assert.deepEqual(unwrappedDebugLogMessages(source), [], source);
  }

  const followedByComment = [
    "Debug.Log(",
    '    LogTextSanitizer.Sanitize($"theme {theme} set"),',
    "    this);"
  ].join("\n");
  assert.deepEqual(unwrappedDebugLogMessages(followedByComment), [], followedByComment);
});

test("display text: the Debug.Log rule reads a hole that holds a string literal", () => {
  /*
    A `string.Join(", ", ...)` hole holds a quote. The literal scan used to
    end at that quote, the hole went unseen, and rule 3 passed the call
    (issue #213). The scanner is brace-depth aware now, so the shape is
    reported raw and accepted wrapped - and the shipped tree's one such site
    is under the ordinary rule-3 sweep, not a special pin.
  */
  const raw = [
    "Debug.LogWarning(",
    '    $"Control Order is missing the following controls: [{string.Join(", ", missingControls)}]. "',
    '        + "Input for these will not be handled. Is this intentional?",',
    "    this",
    ");"
  ].join("\n");
  assert.deepEqual(
    unwrappedDebugLogMessages(raw),
    ['line 1: string.Join(", ", missingControls)'],
    "a hole holding a quoted argument is still a hole"
  );

  const wrapped = [
    "Debug.LogWarning(",
    "    LogTextSanitizer.Sanitize(",
    '        $"Control Order is missing the following controls: [{string.Join(", ", missingControls)}]. "',
    '            + "Input for these will not be handled. Is this intentional?"',
    "    ),",
    "    this",
    ");"
  ].join("\n");
  assert.deepEqual(unwrappedDebugLogMessages(wrapped), [], wrapped);
});

test("display text: the shipped control-order warning is under rule 3's own sweep", () => {
  /*
    The scanner reads the shape now, so the real site needs no pin of its
    own - but this assertion fails with a named pointer if the site is
    deleted or rewritten past recognition, which is the one silent drift a
    shape-only test cannot name.
  */
  const file = path.join(repoRoot, "Runtime/CommandTerminal/Input/TerminalKeyboardController.cs");
  const text = fs.readFileSync(file, "utf8");
  assert.match(
    text,
    /Debug\.LogWarning\(\s*LogTextSanitizer\.Sanitize\(/u,
    "the control-order warning left rule 3's shape; check its replacement is wrapped"
  );
});

test("display text: nested strings and braces inside a hole do not end it", () => {
  const sources = [
    'Debug.Log($"values [{string.Join("; ", items.Select(i => i["key"]))}]");',
    'Debug.Log($"flag {value is \'\\\'\' ? "quote" : "plain"} end");',
    'Debug.Log($"outer {string.Concat($"inner {nested}")} tail");'
  ];
  for (const source of sources) {
    assert.deepEqual(
      unwrappedDebugLogMessages(source),
      ["line 1: " + source.slice(source.indexOf("{") + 1, source.lastIndexOf("}")).trim()],
      source
    );
  }

  assert.deepEqual(
    unwrappedDebugLogMessages(
      'Debug.Log(LogTextSanitizer.Sanitize($"outer {string.Concat($"inner {nested}")} tail"));'
    ),
    [],
    "a nested-interpolated hole is accepted wrapped"
  );
});

test("display text: a verbatim-interpolated string reads in both prefix orders", () => {
  /*
    `$@"..."` and `@$"..."` are the same string to C#. `@$` is the canonical
    path shape - backslashes are literal - and its doubled quotes stay
    literal text even when a hole follows them.
   */
  const paths = [
    [
      'Debug.Log($@"scanning {directory} \\\\server\\{shared} now");',
      ["line 1: directory, shared"]
    ],
    [
      'Debug.Log(@$"found {count} in ""{directory}""");',
      ["line 1: count, directory"]
    ],
    [
      'Debug.Log(LogTextSanitizer.Sanitize($@"scanning {directory} \\\\server\\{shared} now"));',
      []
    ],
    [
      'Debug.Log(LogTextSanitizer.Sanitize(@"scanning $directory ""x"""));',
      []
    ]
  ];
  for (const [source, expected] of paths) {
    assert.deepEqual(unwrappedDebugLogMessages(source), expected, source);
  }
});
