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
    expression-bodied factory, a label API other than a GUIContent
    construction (a bare `GUILayout.Label($"... {name}")`), and a hole whose
    own braces would need balancing. None of those exists in the package
    today; widening the gate to them is a separate piece of work.

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
    One left-to-right pass that classifies the file into the spans a
    GUIContent walk has to step over: string literals (whose text may hold
    brackets, braces, and the token itself) and comments (whose text is not
    code at all).
 */
function classify(text) {
  const literals = [];
  const comments = [];
  let index = 0;
  while (index < text.length) {
    const character = text[index];
    const next = text[index + 1];

    if (character === "/" && next === "/") {
      const start = index;
      index = text.indexOf("\n", index);
      if (index < 0) {
        index = text.length;
      }

      comments.push({ start, end: index });
      continue;
    }

    if (character === "/" && next === "*") {
      const start = index;
      index = text.indexOf("*/", index + 2);
      index = index < 0 ? text.length : index + 2;
      comments.push({ start, end: index });
      continue;
    }

    if (character === "'") {
      index += 1;
      while (index < text.length) {
        if (text[index] === "\\") {
          index += 2;
          continue;
        }

        if (text[index] === "'") {
          index += 1;
          break;
        }

        ++index;
      }

      continue;
    }

    if (character === '"' || (character === "@" && next === '"')) {
      const verbatim = character === "@";
      const start = index;
      index += verbatim ? 2 : 1;
      while (index < text.length) {
        if (text[index] === "\\" && !verbatim) {
          index += 2;
          continue;
        }

        if (text[index] === '"') {
          if (verbatim && text[index + 1] === '"') {
            index += 2;
            continue;
          }

          index += 1;
          break;
        }

        ++index;
      }

      /*
        `$@` is a verbatim interpolated string and `@` is a verbatim one that
        is not, and `start` indexes the `@` in both cases - so the character
        that decides is the one immediately before it. Reading a second
        character back instead matches nothing, and every `$@"...{x}"` hole
        then goes unreported.
       */
      literals.push({ start, end: index, interpolated: text[start - 1] === "$", verbatim });
      continue;
    }

    ++index;
  }

  return { literals, comments };
}

function startsInside(spans, index) {
  for (const span of spans) {
    if (span.start > index) {
      return false;
    }

    if (span.start <= index && index < span.end) {
      return true;
    }
  }

  return false;
}

/** The index of the paren that closes the call opened at openParen. */
function callCloseParen(text, openParen, literals) {
  let depth = 0;
  let index = openParen;
  let literal = 0;
  while (index < text.length) {
    while (literal < literals.length && literals[literal].start < index) {
      ++literal;
    }

    if (literal < literals.length && literals[literal].start === index) {
      index = literals[literal].end;
      continue;
    }

    if (text[index] === "(") {
      ++depth;
    } else if (text[index] === ")") {
      --depth;
      if (depth === 0) {
        return index;
      }
    }

    ++index;
  }

  return -1;
}

function lineOf(text, index) {
  return text.slice(0, index).split("\n").length;
}

/** Interpolated holes of every interpolated literal inside the given text. */
function interpolationHoles(source, literals) {
  const holes = [];
  for (const literal of literals) {
    if (!literal.interpolated) {
      continue;
    }

    const text = source.slice(literal.start, literal.end);
    /*
        A verbatim interpolated string doubles a literal brace, so "{{x}}" is
        the text {x} and not a hole. Blank the doubled pairs before looking
        for holes, or every one of them reads as an unescaped name.
     */
    const body = literal.verbatim ? text.replace(/\{\{|\}\}/g, "  ") : text;
    for (const match of body.matchAll(/\{([^{}]*)\}/g)) {
      holes.push({ hole: match[1].trim(), offset: literal.start + match.index });
    }
  }

  return holes;
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
  const findings = [];
  for (const match of text.matchAll(GUICONTENT_CONSTRUCTION)) {
    if (startsInside(comments, match.index) || startsInside(literals, match.index)) {
      continue;
    }

    const openParen = match.index + match[0].length - 1;
    const closeParen = callCloseParen(text, openParen, literals);
    if (closeParen < 0) {
      continue;
    }

    const argumentText = text.slice(openParen + 1, closeParen);
    const argumentLiterals = literals
      .filter((literal) => literal.start > openParen && literal.end <= closeParen)
      .map((literal) => ({ ...literal, start: literal.start - openParen, end: literal.end - openParen }));
    for (const { hole, offset } of interpolationHoles(argumentText, argumentLiterals)) {
      if (!hole.startsWith(SANITIZE_CALL)) {
        findings.push(`line ${lineOf(text, openParen + offset)}: ${hole}`);
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
    The file's own code, with every comment and string literal removed: a
    backstop satisfied by a TODO or a Debug.Log of the sanitizer's own name
    is not a backstop, and a mention in either is the cheapest way to write
    one by accident.
 */
function codeText(text, literals, comments) {
  let result = "";
  let index = 0;
  /*
    Ordered by position, and the order matters: the two lists are collected in
    separate passes, so concatenating them walks backwards the moment a
    literal precedes a comment, and every region between is emitted twice -
    comment text included, which is the one thing this function removes.
   */
  for (const span of [...comments, ...literals].sort((a, b) => a.start - b.start)) {
    result += text.slice(index, span.start);
    index = span.end;
  }

  return result + text.slice(index);
}

/*
    Rule 3's subject: the Debug.Log-family calls whose sink is Unity's
    Console window and the player log. `\b` matches the tail of
    `UnityEngine.Debug.` too, which is the shape the `log` command uses.
 */
const DEBUG_LOG_CALL = /\bDebug\.(?:Log|LogError|LogWarning)\s*\(/g;
/* The one message built without interpolation that still prints caller-typed text. */
const JOIN_ARGUMENTS_CALL = /^(?:UnityEngine\.)?JoinArguments\s*\(/;

/** A copy of text with every comment span blanked to spaces, length kept. */
function blankComments(text, comments) {
  const characters = text.split("");
  for (const span of comments) {
    for (let index = span.start; index < span.end; ++index) {
      if (characters[index] !== "\n") {
        characters[index] = " ";
      }
    }
  }

  return characters.join("");
}

/** The [start, end) of a call's first argument: to its top-level comma or closing paren. */
function firstArgumentSpan(text, openParen, closeParen, literals) {
  let depth = 0;
  let literal = 0;
  for (let index = openParen + 1; index < closeParen; ++index) {
    while (literal < literals.length && literals[literal].start < index) {
      ++literal;
    }

    if (literal < literals.length && literals[literal].start === index) {
      index = literals[literal].end - 1;
      continue;
    }

    const character = text[index];
    if (character === "(") {
      ++depth;
    } else if (character === ")") {
      --depth;
    } else if (character === "," && depth === 0) {
      return [openParen + 1, index];
    }
  }

  return [openParen + 1, closeParen];
}

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

test("display text: the hole the scanner cannot see is wrapped by hand", () => {
  /*
    A `string.Join(", ", ...)` hole holds a quote, which ends the literal
    scan early, so rule 3 never reports the call (issue #213). Pin the one
    site that uses the shape until the scanner can read it, and hold it to
    rule 3's own bar: the whole message argument wrapped, not just a
    sanitizer mention at the front.
  */
  const file = path.join(repoRoot, "Runtime/CommandTerminal/Input/TerminalKeyboardController.cs");
  const text = fs.readFileSync(file, "utf8");
  const message = text.indexOf("Control Order is missing");
  assert.ok(message >= 0, "the control-order warning moved; repoint this pin");
  const call = text.lastIndexOf("Debug.LogWarning(", message);
  assert.ok(call >= 0, "the control-order warning moved; repoint this pin");
  const openParen = call + "Debug.LogWarning(".length - 1;
  const { literals } = classify(text);
  const closeParen = callCloseParen(text, openParen, literals);
  assert.ok(closeParen >= 0, "the call's parens must balance for this pin to read it");
  const [argumentStart, argumentEnd] = firstArgumentSpan(text, openParen, closeParen, literals);
  const argumentText = text.slice(argumentStart, argumentEnd);
  const argumentLiterals = literals
    .filter((literal) => literal.start >= argumentStart && literal.end <= argumentEnd)
    .map((literal) => ({
      ...literal,
      start: literal.start - argumentStart,
      end: literal.end - argumentStart,
    }));
  assert.ok(
    isWrappedInSanitize(argumentText, argumentLiterals),
    "rule 3 cannot see this call, so the pin holds it to rule 3's own bar"
  );
});
