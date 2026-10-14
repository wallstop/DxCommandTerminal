/*
    Comments state why, never what (issue #255).

    A comment whose every content word already appears in the code it annotates
    restates that code: the reader reads the sentence, then the line, and gains
    nothing but a second place to drift out of sync. The same holds for a comment
    that carries no words at all - a bare `//`, a `// TODO` with nothing after it,
    a line of dashes. Both are deleted on sight; a comment earns its bytes by
    carrying the why the code cannot say. Two shapes survive an empty remainder:
    a doc run is XML input the compiler consumes, not authoring prose, and a
    complexity note (`O(1)`) tokenizes to filtered fragments while stating a
    contract the code does not.

    The rule is deliberately narrow so a sweep cannot corrupt good comments:
    - Words are compared against the ONE line the comment annotates (a trailing
      comment's own line; otherwise the next code line within three lines of the
      comment's end). A comment spread over a section, or one whose words reach
      past that line, stays silent.
    - A stopword set absorbs the grammar glue and the XML doc markup, so
      "Gets the count." over `public int Count` flags but a sentence that adds
      one real word does not.
    - A `#pragma` line's trailing comment is compiler-mandated text, not
      authoring, and `// minimal-comments:allow: <reason>` exempts one site the
      way the blocking-waits marker does.

    The scan is a real state machine, not a grep: string, char, verbatim,
    interpolated (with holes), and raw-string literals are consumed with the
    same literal scanner the comparison-direction linter uses, so a `//` inside
    any literal stays silent. Consecutive `///` lines group into one comment so
    a tail fragment ("...of the query.") is judged with its run, never alone.

    No --fix by design: whether a flagged comment dies or gains a why is a
    judgement call, and deleting the wrong sentence loses information.

    Exit codes: 0 = clean, 1 = at least one violation.

    Adapted from the repository's lint-multiline-comments.mjs structure and
    unity-helpers' linter conventions (MIT, Ambiguous-Interactive).
*/
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const REPO_ROOT = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../.."
);
// Overridable so the contract tests can point the scan at a fixture tree. Nothing in CI sets it.
// `Generator~` is C# this repository authors, so the rule applies there too.
const SCAN_ROOTS = process.env.MINIMAL_COMMENT_ROOTS
  ? process.env.MINIMAL_COMMENT_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor", "Tests", "Generator~"];

const { consumeLiteral } = await import(
  pathToFileURL(
    path.join(REPO_ROOT, "tooling~", "scripts", "lint-comparison-direction.mjs")
  ).href
);

/*
    Grammar glue and XML doc markup. A word on this list never makes a comment
    "content", so a sentence must carry at least one word the code does not say
    to stay legal.
*/
const STOPWORDS = new Set(
  ("the a an of to for and or is are be been was were in on at by with from as that this these those it its if when while not no new " +
    "get gets got set sets set's setting returning return returns current value values flag flags use uses using used uses' we our out into per only all any each every " +
    "before after during under over via than then so because but also more less most least own has have had do does did done can cannot may might must " +
    "should would will shall need needs up down back again once twice one two first last both either neither none here there where which who whose what why how " +
    "whether about above below between through against without within along across behind beyond plus vs eg ie etc itself they them their " +
    "let make makes made take takes taken give gives given keep keeps kept put puts call calls called add adds added run runs ran " +
    "line lines item items entry entries element elements field fields member members method methods property properties type types class classes " +
    "case cases true false null empty default defaults other others another same different instead rather just even still yet already always never " +
    "todo fixme hack xxx note " +
    "summary param paramref see cref returns remarks inheritdoc typeparam list description term example code c")
    .split(" ")
);

const ANNOTATION_WINDOW_LINES = 3;

/** One comment's identity: kind, source span, end line, and the words after `//` or `///`. */
function scan(text) {
  const lineStarts = [0];
  for (let i = 0; i < text.length; i++) {
    if (text[i] === "\n") {
      lineStarts.push(i + 1);
    }
  }
  const lineOf = (offset) => {
    let low = 0;
    let high = lineStarts.length - 1;
    while (low < high) {
      const middle = (low + high + 1) >> 1;
      if (lineStarts[middle] <= offset) {
        low = middle;
      } else {
        high = middle - 1;
      }
    }
    return low;
  };

  const comments = [];
  const length = text.length;
  let i = 0;
  while (i < length) {
    const c = text[i];
    if (c === "/" && text[i + 1] === "*") {
      const end = text.indexOf("*/", i + 2);
      const stop = end < 0 ? length : end + 2;
      comments.push({
        kind: "block",
        start: i,
        end: stop,
        line: lineOf(i),
        endLine: lineOf(stop),
        trailing: text.slice(lineStarts[lineOf(i)], i).trim() !== "",
        text: text.slice(i + 2, end < 0 ? length : end)
      });
      i = stop;
      continue;
    }
    if (c === "/" && text[i + 1] === "/") {
      if (text.startsWith("///", i)) {
        /* Consecutive `///` lines are one comment; only the line's own indent
           may sit between them, so a blank line ends the run. */
        const parts = [];
        let stop = i;
        let cursor = i;
        while (cursor < length && text.startsWith("///", cursor)) {
          const lineEnd = text.indexOf("\n", cursor);
          const lineStop = lineEnd < 0 ? length : lineEnd;
          parts.push(text.slice(cursor + 3, lineStop).replace(/\r$/, ""));
          stop = lineStop;
          cursor = lineStop;
          if (text[cursor] === "\r") {
            cursor++;
          }
          if (text[cursor] === "\n") {
            cursor++;
          }
          while (cursor < length && (text[cursor] === " " || text[cursor] === "\t")) {
            cursor++;
          }
        }
        comments.push({
          kind: "doc",
          start: i,
          end: stop,
          line: lineOf(i),
          endLine: lineOf(stop),
          trailing: text.slice(lineStarts[lineOf(i)], i).trim() !== "",
          text: parts.join(" ")
        });
        i = stop;
        continue;
      }
      const lineEnd = text.indexOf("\n", i);
      const stop = lineEnd < 0 ? length : lineEnd;
      comments.push({
        kind: "line",
        start: i,
        end: stop,
        line: lineOf(i),
        endLine: lineOf(stop),
        trailing: text.slice(lineStarts[lineOf(i)], i).trim() !== "",
        text: text.slice(i + 2, stop).replace(/\r$/, "")
      });
      i = stop;
      continue;
    }
    if (c === '"' || c === "'" || c === "@" || c === "$") {
      const literal = consumeLiteral(text, i);
      if (literal) {
        i = literal.end;
        continue;
      }
    }
    i++;
  }

  return { comments, lineStarts, text };
}

function splitIdentifierParts(word) {
  return word
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .toLowerCase()
    .split(/[^a-z0-9]+/)
    .filter(Boolean);
}

/** Every token the code line offers: identifiers, keywords, and string content, camel-split. */
function codeWords(line) {
  const words = new Set();
  for (const raw of line.match(/[A-Za-z_][A-Za-z0-9_]*|[a-z]+/g) ?? []) {
    words.add(raw.toLowerCase());
    for (const part of splitIdentifierParts(raw)) {
      words.add(part);
    }
  }
  return words;
}

/**
 * The comment's content words: markup-free, grammar-free, camel-split. An
 * empty result means the comment says nothing at all.
 */
function contentWords(commentText) {
  const words = [];
  for (const raw of commentText.toLowerCase().match(/[a-z0-9]+/g) ?? []) {
    for (const part of splitIdentifierParts(raw)) {
      if (part.length > 1 && !STOPWORDS.has(part)) {
        words.push(part);
      }
    }
  }
  return words;
}

/**
 * The line a full comment annotates: the next code line at or after `from`,
 * skipping comment and blank lines, or undefined when none sits inside the
 * window. `from` is the comment's end line; the window keeps block-scoped
 * prose (headers over several blank lines) out of the rule.
 */
function annotatedLine(text, lineStarts, endLine) {
  for (let line = endLine; line <= endLine + ANNOTATION_WINDOW_LINES && line < lineStarts.length; line++) {
    const lineEnd = lineStarts[line + 1] ?? text.length;
    const content = text.slice(lineStarts[line], lineEnd).trim();
    if (content.length > 0 && !content.startsWith("//") && !content.startsWith("/*") && !content.startsWith("*") && content !== "*/") {
      return { line, code: content };
    }
  }
  return undefined;
}

/**
 * Violations in one source file: comments that restate the code they
 * annotate, and comments that carry no content at all.
 */
export function minimalComments(text) {
  const { comments, lineStarts, text: source } = scan(text);
  const violations = [];
  for (const comment of comments) {
    if (comment.text.includes("minimal-comments:allow")) {
      continue;
    }
    let target;
    if (comment.trailing) {
      const code = source.slice(lineStarts[comment.line], comment.start).trim();
      /* A `#pragma` line's trailing text is the compiler's own message. */
      if (code.includes("#pragma")) {
        continue;
      }
      target = { line: comment.line, code };
    } else {
      target = annotatedLine(source, lineStarts, comment.endLine);
    }
    if (target === undefined) {
      continue;
    }
    const words = contentWords(comment.text);
    if (words.length === 0) {
      /*
          Two shapes survive an empty remainder: a doc run is XML input the
          compiler consumes, not authoring prose (a restatement still
          fails it), and a complexity note - `O(1)` tokenizes to filtered
          fragments - states a contract the code does not.
      */
      const exempt = comment.kind === "doc" || /\bO\([^)]*\)/.test(comment.text);
      if (!exempt) {
        violations.push({
          line: comment.line + 1,
          kind: comment.kind,
          text: comment.text.trim(),
          reason: "carries no content; delete it or say what only the code cannot",
          targetLine: target.line + 1
        });
      }
      continue;
    }
    const misses = words.filter((word) => !codeWords(target.code).has(word));
    if (misses.length === 0) {
      violations.push({
        line: comment.line + 1,
        kind: comment.kind,
        text: comment.text.trim(),
        reason: "restates the code it annotates; delete it or write the why the code cannot say",
        targetLine: target.line + 1
      });
    }
  }
  return violations;
}

function listFiles(root) {
  const absolute = path.resolve(REPO_ROOT, root);
  if (!fs.existsSync(absolute)) {
    // A missing root contributes nothing; the caller reports an empty walk as an error
    // rather than reading the absence of a scan as green.
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
  walk(absolute);
  return found;
}

function main() {
  const files = SCAN_ROOTS.flatMap((root) => listFiles(root));
  if (files.length === 0) {
    console.error(
      `[minimal-comments] ERROR: no C# files were found under ${SCAN_ROOTS.join(", ")}, so this run checked nothing. A scan that matched nothing is the absence of a measurement, not a pass.`
    );
    return 1;
  }

  let violations = 0;
  for (const file of files) {
    const text = fs.readFileSync(file, "utf8");
    const found = minimalComments(text);
    if (found.length === 0) {
      continue;
    }
    const relative = path.relative(REPO_ROOT, file);
    for (const violation of found) {
      violations++;
      const preview = violation.text.length > 80 ? `${violation.text.slice(0, 77)}...` : violation.text;
      console.error(
        `${relative}:${violation.line}: ${violation.reason} (${violation.kind} comment over line ${violation.targetLine}): ${preview}`
      );
    }
  }

  console.log(`[minimal-comments] ${files.length} file(s) scanned`);
  return 0 < violations ? 1 : 0;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  /*
      exitCode, not exit: on Windows, writes to a piped stderr are asynchronous, and a
      process.exit() would truncate the violation report this process is still flushing.
      Letting the loop drain keeps the report whole and the exit code identical.
   */
  process.exitCode = main(process.argv.slice(2));
}
