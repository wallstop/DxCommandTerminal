/*
    Multi-line comments are block comments, never stacked `//` lines (PR #57 review).

    Two or more consecutive comment-only `//` lines read as one comment with fake
    structure, so they must be written as one block comment instead. A single `//` line is
    fine, and `///` doc comments are exempt (they are XML doc input, not authoring prose).
    Lines that carry a `//` comment after code are left alone: the rule is about comment
    blocks, and an inline block comment would swallow the rest of its line.

    The scan is a real state machine, not a grep, because fixture sources embed C# inside
    verbatim strings: `//` lines in string content are data, not comments. String, char,
    verbatim, interpolated (with holes), and raw-string literals are consumed with the
    same literal scanner the comparison-direction linter uses, so a `//` inside any
    literal stays silent.

    `--fix` rewrites each run as one block comment: `/*` opens at the run's indent, every
    content line keeps its deeper indentation and gains a three-space base indent, and the
    comment closes with a slash-asterisk marker at the run's indent column. A run whose
    content contains that closing marker cannot be converted safely, so `--fix` refuses it
    and the violation stays.

    Exit codes: 0 = clean (or every fixable violation fixed), 1 = at least one violation
    remains.

    Adapted from the repository's lint-comparison-direction.mjs structure (issues #50/#51)
    and unity-helpers' linter conventions (MIT, Ambiguous-Interactive).
*/
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const REPO_ROOT = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../.."
);
// Overridable so the contract tests can point the scan at a fixture tree. Nothing in CI sets it.
// `Generator~` is C# this repository authors -- the analyzer payload and its tests -- so the rule
// applies there too, even though Unity ignores the tilde directory.
const SCAN_ROOTS = process.env.MULTILINE_COMMENT_ROOTS
  ? process.env.MULTILINE_COMMENT_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor", "Tests", "Generator~"];

const { consumeLiteral } = await import(
  pathToFileURL(
    path.join(REPO_ROOT, "tooling~", "scripts", "lint-comparison-direction.mjs")
  ).href
);

/** Groups comment-only, non-doc `//` lines into consecutive-line runs of length 2 or more. */
export function commentRuns(text) {
  const lineStarts = [];
  for (let i = 0; i < text.length; i++) {
    if (text[i] === "\n") {
      lineStarts.push(i + 1);
    }
  }
  lineStarts.unshift(0);

  const entries = [];
  let i = 0;
  let line = 0;
  const length = text.length;
  while (i < length) {
    const c = text[i];
    if (c === "\n") {
      line++;
      i++;
      continue;
    }
    if (/\s/.test(c)) {
      i++;
      continue;
    }
    if (c === "/" && text[i + 1] === "*") {
      const end = text.indexOf("*/", i + 2);
      const stop = end < 0 ? length : end + 2;
      while (i < stop) {
        if (text[i] === "\n") {
          line++;
        }
        i++;
      }
      continue;
    }
    if (c === "/" && text[i + 1] === "/") {
      const end = text.indexOf("\n", i);
      const stop = end < 0 ? length : end;
      if (!text.startsWith("///", i)) {
        entries.push({
          line,
          start: i,
          end: stop,
          text: text.slice(i, stop),
          lineStart: lineStarts[line],
        });
      }
      i = stop;
      continue;
    }
    if (c === '"' || c === "'" || c === "@" || c === "$") {
      const literal = consumeLiteral(text, i);
      if (literal) {
        while (i < literal.end) {
          if (text[i] === "\n") {
            line++;
          }
          i++;
        }
        continue;
      }
    }
    i++;
  }

  const runs = [];
  for (const entry of entries) {
    const previous = runs[runs.length - 1];
    if (
      previous &&
      previous.entries[previous.entries.length - 1].line + 1 === entry.line
    ) {
      previous.entries.push(entry);
    } else {
      runs.push({ entries: [entry] });
    }
  }
  return runs.filter((run) => 1 < run.entries.length);
}

/** Builds the block-comment replacement for one run, or undefined when it is unconvertible. */
export function planFix(text, run) {
  for (const entry of run.entries) {
    if (entry.text.includes("*/")) {
      return undefined;
    }
  }
  const first = run.entries[0];
  const last = run.entries[run.entries.length - 1];
  const eol = text.includes("\r\n") ? "\r\n" : "\n";
  const indent = /^[ \t]*/.exec(text.slice(first.lineStart, first.start))[0];
  const contents = run.entries.map((entry) => {
    let content = entry.text.replace(/\r$/, "").slice(2);
    if (content.startsWith(" ")) {
      content = content.slice(1);
    }
    return content.replace(/[ \t]+$/, "");
  });
  const body = contents
    .map((content) => (content === "" ? "" : `${indent}   ${content}`))
    .join(eol);
  /*
      The replaced region stops short of the final line terminator, so the file's own
      line ending survives the rewrite untouched.
   */
  const end = text[last.end - 1] === "\r" ? last.end - 1 : last.end;
  return {
    // Replace from the line start so the original indent is not doubled.
    start: first.lineStart,
    end,
    replacement: `${indent}/*${eol}${body}${eol}${indent}*/`,
  };
}

export function applyFixes(text, runs) {
  const plans = runs
    .map((run) => planFix(text, run))
    .filter((plan) => plan !== undefined)
    .sort((a, b) => b.start - a.start);
  let fixed = text;
  for (const plan of plans) {
    fixed = fixed.slice(0, plan.start) + plan.replacement + fixed.slice(plan.end);
  }
  return fixed;
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
  const shouldFix = process.argv.includes("--fix");
  const files = SCAN_ROOTS.flatMap((root) => listFiles(root));
  if (files.length === 0) {
    console.error(
      `[multiline-comments] ERROR: no C# files were found under ${SCAN_ROOTS.join(", ")}, so this run checked nothing. A scan that matched nothing is the absence of a measurement, not a pass.`
    );
    return 1;
  }

  let violations = 0;
  let converted = 0;
  for (const file of files) {
    const text = fs.readFileSync(file, "utf8");
    const runs = commentRuns(text);
    if (runs.length === 0) {
      continue;
    }
    const relative = path.relative(REPO_ROOT, file);
    for (const run of runs) {
      if (!shouldFix || planFix(text, run) === undefined) {
        violations++;
        console.error(
          `${relative}:${run.entries[0].line + 1}: ${run.entries.length} stacked // comment lines`
        );
      }
    }
    if (shouldFix) {
      const updated = applyFixes(text, runs);
      if (updated !== text) {
        converted += runs.filter((run) => planFix(text, run) !== undefined).length;
        fs.writeFileSync(file, updated);
      }
    }
  }

  if (shouldFix) {
    console.log(`[multiline-comments] converted ${converted} comment run(s) to block comments`);
  } else {
    console.log(`[multiline-comments] ${files.length} file(s) scanned`);
  }
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
