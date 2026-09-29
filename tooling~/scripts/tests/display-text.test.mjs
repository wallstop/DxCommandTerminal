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

    Not covered, so a future reader is not misled: a label API other than
    a GUIContent construction (a bare `GUILayout.Label($"... {name}")`), a
    `tooltip` set through an object initializer, and a hole whose own
    braces would need balancing. None of those exists in the package today;
    widening the gate to them is a separate piece of work.

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

      const interpolated =
        !verbatim && text[start - 1] === "$"
          ? true
          : verbatim && text[start - 1] === "@" && text[start - 2] === "$";
      literals.push({ start, end: index, interpolated, verbatim });
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

/** The text a constructor call is given: from its open paren to its match. */
function callArgumentText(text, openParen, literals) {
  const close = callCloseParen(text, openParen, literals);
  return close < 0 ? "" : text.slice(openParen + 1, close);
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
  for (const span of [...comments, ...literals]) {
    result += text.slice(index, span.start);
    index = span.end;
  }

  return result + text.slice(index);
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

  assert.deepEqual(
    unescapedTooltipNames([`GUIContent tip = new("a", $@"literal {{theme}} text");`].join("\n")),
    [],
    'a doubled brace in a verbatim string is literal text, not a hole: {theme}'
  );
});

test("display text: the popup backstop is not satisfied by a mention", () => {
  assert.ok(
    popupsWithoutASanitizer(
      ["// TODO: the labels should call LogTextSanitizer.Sanitize", "EditorGUILayout.Popup(0, names);"]
        .join("\n")
    ),
    "a mention in a comment is not a call"
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
