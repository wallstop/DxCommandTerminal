/*
    Production code bans LINQ (PR #60 review).

    Every LINQ operator allocates: each call chain materializes at least one
    enumerator per stage plus one closure per lambda, and several operators
    (`ToArray`, `ToDictionary`, `ToList`, `OfType`) copy the whole sequence. In a
    performance-first runtime library that must hit zero-allocation hot paths, LINQ
    can only ever be a silent allocation regression, so it is banned outright in
    shipped code (`Runtime/`, `Editor/`). Tests and `Generator~` tooling are exempt:
    they are not on any runtime path.

    Detection is line-based and intentionally broad: a `using System.Linq`
    directive fails even when the file no longer calls any operator, because a
    dead directive invites the next call. Fully-qualified `System.Linq.` uses and
    static `Enumerable.` calls are caught too, so the rule cannot be dodged by
    skipping the directive. Comment-only lines are skipped so documentation about
    the ban cannot trip it.

    There is no `--fix` on purpose: removing LINQ means choosing loop shapes and
    buffer strategies per call site, which is a code change, not a mechanical
    rewrite.

    Exit codes: 0 = clean, 1 = at least one violation (or nothing was scanned).

    Adapted from the repository's lint-multiline-comments.mjs structure
    (issues #50/#51 lineage) and unity-helpers' linter conventions
    (MIT, Ambiguous-Interactive).
*/
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const REPO_ROOT = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../.."
);
// Overridable so the contract tests can point the scan at a fixture tree. Nothing in CI sets it.
const SCAN_ROOTS = process.env.LINQ_PRODUCTION_ROOTS
  ? process.env.LINQ_PRODUCTION_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor"];

const { consumeLiteral } = await import(
  pathToFileURL(
    path.join(REPO_ROOT, "tooling~", "scripts", "lint-comparison-direction.mjs")
  ).href
);

/**
 * Replaces comment contents with spaces, keeping newlines, so banned
 * vocabulary inside comments can never trip the scan. String/char literals
 * are consumed with the comparison-direction scanner so `//` or `/*` inside a
 * literal stays data.
 */
export function stripComments(text) {
  let out = "";
  let i = 0;
  while (i < text.length) {
    const ch = text[i];
    if (ch === '"' || ch === "'" || ch === "@" || ch === "$") {
      const literal = consumeLiteral(text, i);
      if (literal === null) {
        /*
            A quote-like character that does not open a string (verbatim
            identifiers such as `@event`, a dangling quote): copy the
            character verbatim and keep scanning.
         */
        out += ch;
        i++;
        continue;
      }

      /*
          Mask literal contents too: banned vocabulary inside a string is
          data, not code, and must not read as a violation.
       */
      for (let j = i; j < literal.end; j++) {
        out += text[j] === "\n" ? "\n" : " ";
      }
      i = literal.end;
      continue;
    }

    if (ch === "/" && text[i + 1] === "/") {
      while (i < text.length && text[i] !== "\n") {
        out += " ";
        i++;
      }
      continue;
    }

    if (ch === "/" && text[i + 1] === "*") {
      while (i < text.length && !(text[i] === "*" && text[i + 1] === "/")) {
        out += text[i] === "\n" ? "\n" : " ";
        i++;
      }
      out += "  ";
      i += 2;
      continue;
    }

    out += ch;
    i++;
  }

  return out;
}

const USING_PATTERN = /\busing\s+System\s*\.\s*Linq\b/;
const QUALIFIED_PATTERN = /\bSystem\s*\.\s*Linq\s*\./;
const ENUMERABLE_PATTERN = /\bEnumerable\s*\.\s*\w+/;

/** Returns one-based line numbers whose code (not comment) text violates the ban. */
export function linqViolations(rawText) {
  const text = stripComments(rawText);
  const lines = text.split("\n");
  const violations = [];
  for (let index = 0; index < lines.length; index++) {
    const line = lines[index];
    if (
      USING_PATTERN.test(line) ||
      QUALIFIED_PATTERN.test(line) ||
      ENUMERABLE_PATTERN.test(line)
    ) {
      violations.push({ line: index + 1, text: line.trim() });
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
      `[linq-production] ERROR: no C# files were found under ${SCAN_ROOTS.join(", ")}, so this run checked nothing. A scan that matched nothing is the absence of a measurement, not a pass.`
    );
    return 1;
  }

  let violations = 0;
  for (const file of files) {
    const text = fs.readFileSync(file, "utf8");
    const fileViolations = linqViolations(text);
    if (fileViolations.length === 0) {
      continue;
    }
    const relative = path.relative(REPO_ROOT, file);
    for (const violation of fileViolations) {
      violations++;
      console.error(
        `${relative}:${violation.line}: LINQ in production code: ${violation.text}`
      );
    }
  }

  console.log(`[linq-production] ${files.length} file(s) scanned`);
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
