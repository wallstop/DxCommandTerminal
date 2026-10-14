/*
    Contract tests for tooling~/scripts/lint-minimal-comments.mjs.

    The rule (issue #255): a comment must carry the why the code cannot say. Two
    shapes fail: a comment whose every content word appears in the one line it
    annotates (a restatement), and a comment with no content words at all (a
    bare `//`, a marker-only `// TODO`, a dash line). The negative cases carry
    the weight: why-comments that add one real word, comments spread over a
    section, `///` doc runs judged whole (a tail fragment never stands alone),
    `//` inside any literal, pragma trailing text, and the allow marker must all
    stay silent, or a sweep corrupts good comments.
*/
import test from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath, pathToFileURL } from "node:url";

const repoRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../.."
);
const linterPath = path.join(repoRoot, "scripts", "lint-minimal-comments.mjs");
const { minimalComments } = await import(
  pathToFileURL(linterPath).href
);

function flagged(source) {
  return minimalComments(source);
}

function oneReason(source) {
  const violations = flagged(source);
  assert.equal(violations.length, 1, `expected exactly one violation in:\n${source}`);
  return violations[0].reason;
}

function silent(source, label) {
  const violations = flagged(source);
  assert.deepEqual(
    violations.map((violation) => violation.text),
    [],
    `${label} must stay silent`
  );
}

/** The positive cases: comments that restate, and comments that say nothing. */
const FLAGGED = [
  [
    "a full-line comment restating the line below",
    "// the buffer\n_buffer = new TerminalBuffer();",
    "restates the code it annotates"
  ],
  [
    "a doc summary that restates the member name",
    "/// <summary>Gets the count.</summary>\npublic int Count => _count;",
    "restates the code it annotates"
  ],
  [
    "a trailing comment restating its own line",
    'shell.RunCommand("clear-history"); // runs clear history',
    "restates the code it annotates"
  ],
  [
    "an empty comment",
    "//\nTick();",
    "carries no content"
  ],
  [
    "a marker-only TODO",
    "// TODO\n{",
    "carries no content"
  ],
  [
    "a dash separator",
    "// ---\nTick();",
    "carries no content"
  ],
  [
    "a comment restating a line two rows below",
    "// the buffer\n\n_buffer = new TerminalBuffer();",
    "restates the code it annotates"
  ]
];

for (const [name, source, expected] of FLAGGED) {
  test(`flags: ${name}`, () => {
    assert.ok(oneReason(source).includes(expected), `unexpected reason for:\n${source}`);
  });
}

/** The negative cases: every shape a sweep must not corrupt. */
const SILENT = [
  [
    "a why-comment whose words the code does not say",
    "// The probe races the OS write; answer in dispatch (measured on the pinned host).\nvoid Copy() { }"
  ],
  [
    "a comment that adds one real word to a restatement",
    "// the buffer, wrapped in place\n_buffer = new TerminalBuffer();"
  ],
  [
    "a comment spread over a section, past the annotation window",
    "// Bootstrap owns the session lifecycle.\n\n\n\n\nTerminal.Bootstrap();"
  ],
  [
    "a doc run judged whole, not by its tail fragment",
    '/// <summary>\n/// Re-runs the search for <paramref name="query"/>, rebuilding the rows\n/// and resetting the selection to the first match, reading the caret at\n/// the end of the query.\n/// </summary>\npublic void SetQuery(string query) { }'
  ],
  [
    "an act label the code does not name",
    "// Arrange\nvar shell = new CommandShell();"
  ],
  [
    "a pragma line's mandated trailing text",
    "#pragma warning disable CS0612 // Type or member is obsolete"
  ],
  [
    "the allow marker exempts one site",
    "// minimal-comments:allow: reviewer asked to keep the echo\nTick();"
  ],
  [
    "a single-line comment inside an interpolated string",
    'var message = $"// not a comment";'
  ],
  [
    "a single-line comment inside a verbatim string",
    'const string source = @"\n// not a comment either\n";'
  ],
  [
    "a single-line comment inside a raw string",
    'const string raw = """\n// still not a comment\n""";'
  ],
  [
    "a comment inside a block comment stays a block comment case",
    "/*\n// nested text\n*/\nTick();"
  ],
  [
    "a budget note with units and host facts",
    "// 450 ms budget, measured on the pinned host\nAwaitDispatch();"
  ],
  [
    "a markup-only doc run is XML input, not authoring prose",
    "/// <inheritdoc />\npublic void Tick() { }"
  ],
  [
    "a complexity note tokenizes to filtered fragments but carries its contract",
    "// O(1)\nvalue = _buffer[_buffer.Length - 1];"
  ],
  [
    "a compound complexity note",
    "// O(n log n)\nSort(values);"
  ]
];

for (const [name, source] of SILENT) {
  test(`stays silent: ${name}`, () => silent(source, name));
}

test("two doc runs separated by a blank line are judged separately", () => {
  const source =
    "/// <summary>Stores the primary index so the wrap is O(1).</summary>\npublic int First { get; }\n\n/// <summary>Gets the count.</summary>\npublic int Count => _count;";
  const violations = flagged(source);
  assert.equal(violations.length, 1, "only the restating second run may flag");
  assert.equal(violations[0].line, 4, "the flag must name the second run, not a merged run");
});

test("a CRLF source groups doc runs the same way", () => {
  const source = [
    "        /// <summary>",
    "        ///     Re-runs the search, rebuilding the rows and resetting",
    "        ///     the selection to the first match, at the end of the",
    "        ///     query.",
    "        /// </summary>",
    "        public void SetQuery(string query) { }"
  ].join("\r\n");
  silent(source, "CRLF doc run");
  const restating = [
    "/// <summary>Sets the query.</summary>",
    "public void SetQuery(string query) { }"
  ].join("\r\n");
  assert.equal(flagged(restating).length, 1, "CRLF restatement must still flag");
});

test("the CLI scans a fixture tree and reports file and line", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "minimal-comments-"));
  try {
    fs.mkdirSync(path.join(root, "Runtime"));
    fs.writeFileSync(
      path.join(root, "Runtime", "Sample.cs"),
      "// the buffer\n_buffer = new TerminalBuffer();\n"
    );
    const scanRoots = path.join(root, "Runtime");
    const result = spawnSync(
      process.execPath,
      [linterPath],
      {
        env: {
          ...process.env,
          MINIMAL_COMMENT_ROOTS: scanRoots,
          NODE_OPTIONS: ""
        },
        encoding: "utf8"
      }
    );
    assert.equal(result.status, 1, "a restating comment must fail the scan");
    assert.ok(
      result.stderr.includes(`Runtime${path.sep}Sample.cs:1`),
      "the report must name the file and line"
    );
    assert.ok(result.stderr.includes("restates the code it annotates"), "the report must carry the reason");
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test("the CLI refuses an empty scan instead of passing silently", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "minimal-comments-empty-"));
  try {
    fs.mkdirSync(path.join(root, "Runtime"));
    const result = spawnSync(process.execPath, [linterPath], {
      env: {
        ...process.env,
        MINIMAL_COMMENT_ROOTS: path.join(root, "Runtime"),
        NODE_OPTIONS: ""
      },
      encoding: "utf8"
    });
    assert.equal(result.status, 1, "a scan that matched nothing must fail");
    assert.ok(result.stderr.includes("no C# files"), "the empty-scan guard must report itself");
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test("the live tree is clean: the sweep left no flagged comment", () => {
  const files = ["Runtime", "Editor", "Tests", "Generator~"].flatMap((root) =>
    listCs(path.join(repoRoot, "..", root))
  );
  assert.ok(files.length > 100, `the live scan must cover the real trees (found ${files.length})`);
  const violations = files.flatMap((file) =>
    minimalComments(fs.readFileSync(file, "utf8")).map((violation) => `${file}:${violation.line}`)
  );
  assert.deepEqual(violations, []);
});

/** Mirrors the linter's own walk so the live-tree test scans the same set. */
function listCs(root) {
  if (!fs.existsSync(root)) {
    return [];
  }
  const found = [];
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const child = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (entry.name === "bin" || entry.name === "obj" || entry.name === "node_modules") {
          continue;
        }
        walk(child);
      } else if (entry.name.endsWith(".cs")) {
        found.push(child);
      }
    }
  };
  walk(root);
  return found;
}
