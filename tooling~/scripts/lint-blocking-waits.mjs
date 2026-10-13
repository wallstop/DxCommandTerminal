/*
    Production code never blocks the calling thread (wallstop, PR #254:
    "blocking the main thread for 450ms is a no-go for anything we do, ever").

    Every copy command and UI key handler runs on Unity's main thread, so a
    sleep, a spin, or a blocking await inside `Runtime/` or `Editor/` freezes
    the whole game or editor for the wait's duration - and the wait class that
    prompted this linter (awaiting a clipboard read-back settle) is a race
    that should be answered by the caller's contract or a deferred design,
    never by holding the thread hostage. Tests and `Generator~` tooling are
    exempt: they are not on any runtime path (the PlayMode capture harness
    states its own "never Thread.Sleep" rule separately).

    Detection is line-based over comment-stripped code, matching the shapes a
    blocking wait takes in C#:

    - `Thread.Sleep` and `SpinWait` (System.Threading) - the direct waits.
    - `SpinUntil` - Sleep in a loop wearing a callback.
    - `.Wait()`, `.Result`, `.GetAwaiter().GetResult()` - blocking on tasks
      and awaitables, which deadlock under a single-threaded context.
    - `WaitOne`, `Join`, `Monitor.Wait` - handle/thread/event waits.

    There is no `--fix` on purpose: replacing a wait is a design decision per
    call site (poll from the frame loop, defer, or answer synchronously), not
    a mechanical rewrite.

    Exit codes: 0 = clean, 1 = at least one violation (or nothing was scanned).

    Adapted from the repository's lint-linq-production.mjs structure (PR #60
    lineage) and unity-helpers' linter conventions (MIT,
    Ambiguous-Interactive).
*/
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const REPO_ROOT = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../.."
);
// Overridable so the contract tests can point the scan at a fixture tree. Nothing in CI sets it.
const SCAN_ROOTS = process.env.BLOCKING_WAIT_ROOTS
  ? process.env.BLOCKING_WAIT_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor"];

const { consumeLiteral } = await import(
  pathToFileURL(
    path.join(REPO_ROOT, "tooling~", "scripts", "lint-comparison-direction.mjs")
  ).href
);

/**
 * Replaces comment and string-literal contents with spaces, keeping newlines,
 * EXCEPT the holes of an interpolated string (`$"...{expr}..."`), which are
 * executable code and are copied through verbatim - a wait inside a formatting
 * hole is a real main-thread block, and consumeLiteral already reports where
 * the holes are. `//` or `/*` inside a literal stays data.
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
          Hole contents are code and stay; literal contents (including the
          quotes and every non-hole segment) are data and are masked.
       */
      let cursor = i;
      for (const hole of literal.holes) {
        for (let j = cursor; j < hole.start; j++) {
          out += text[j] === "\n" ? "\n" : " ";
        }
        out += text.slice(hole.start, hole.end);
        cursor = hole.end;
      }
      for (let j = cursor; j < literal.end; j++) {
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

const BLOCKING_PATTERNS = [
  /\bThread\s*\.\s*Sleep\b/,
  /*
      A bare `Sleep(` call (the using-directive import): narrowed to a
      numeric argument, the shape a duration-wait takes, so an unrelated
      member named Sleep called with an object stays legal and a wait is
      still caught.
   */
  /\bSleep\s*\(\s*\d/,
  /\bSpinWait\s*\.\s*(SpinUntil|Wait)\b/,
  /\bSpinWait\b/,
  /\bSpinUntil\s*[\.(]/,
  /*
      Member .Wait(...) and .Join(...) block for ANY argument list: Task.Wait
      takes a TimeSpan or cancellation token, Thread.Join a timeout - the
      timed overloads are the exact shape the ban exists for (Bugbot,
      PR #254: a no-argument-only pattern scanned `.Wait(450)` clean).
      string.Join/String.Join/Path.Join concatenate and stay legal, so a
      match whose receiver is one of those type names is exempt by the
      receiver check in blockingWaitViolations.
   */
  /\.\s*Wait\s*\(/,
  /\.\s*Result\b/,
  /\.\s*Join\s*\(/,
  /\.\s*GetAwaiter\s*\(\s*\)\s*\.\s*GetResult\s*\(\s*\)/,
  /\.\s*WaitOne\s*\(/,
  /\bMonitor\s*\.\s*Wait\b/
];

/*
    Member names whose `.Join(...)`/`.Wait(...)` calls concatenate or poll
    rather than block: the receiver's type name decides, because a timed
    Thread.Join/Task.Wait overload and a two-argument string.Join differ
    only in that receiver.
 */
const NON_BLOCKING_JOIN_RECEIVERS = new Set([
  "string",
  "String",
  "Path",
  "Environment",
  "Uri",
]);

function isNonBlockingJoinCall(text, matchIndex) {
  const receiver = /([A-Za-z_][A-Za-z0-9_]*)\s*$/.exec(text.slice(0, matchIndex));
  return receiver !== null && NON_BLOCKING_JOIN_RECEIVERS.has(receiver[1]);
}

/*
    An opted-out line must carry its reason: `// blocking-waits:allow: <reason>`,
    either trailing the violation or on the comment lines directly above it. A
    marker exempts its own line and the next one, so both shapes work; a bare
    marker with nothing after the colon fails to exempt, so a marker can never
    rot into a silent bypass. Markers are scanned in the RAW text because they
    live in comments, which the code mask erases.
 */
const ALLOW_MARKER = "blocking-waits:allow:";

/** Returns one-based line numbers whose code (not comment) text blocks the thread. */
export function blockingWaitViolations(rawText) {
  const rawLines = rawText.split("\n");
  const allowed = new Set();
  for (let index = 0; index < rawLines.length; index++) {
    const markerAt = rawLines[index].indexOf(ALLOW_MARKER);
    if (markerAt < 0 || 0 >= rawLines[index].slice(markerAt + ALLOW_MARKER.length).trim().length) {
      continue;
    }
    allowed.add(index + 1);
    allowed.add(index + 2);
  }

  const text = stripComments(rawText);
  const lines = text.split("\n");
  const violations = [];
  for (let index = 0; index < lines.length; index++) {
    if (allowed.has(index + 1)) {
      continue;
    }
    const line = lines[index];
    const column = BLOCKING_PATTERNS.findIndex((pattern) => {
      const match = pattern.exec(line);
      if (match === null) {
        return false;
      }
      if (pattern.source.endsWith("Join\\s*\\(") && isNonBlockingJoinCall(line, match.index)) {
        return false;
      }
      return true;
    });
    if (0 <= column) {
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
      `[blocking-waits] ERROR: no C# files were found under ${SCAN_ROOTS.join(", ")}, so this run checked nothing. A scan that matched nothing is the absence of a measurement, not a pass.`
    );
    return 1;
  }

  let violations = 0;
  for (const file of files) {
    const text = fs.readFileSync(file, "utf8");
    const fileViolations = blockingWaitViolations(text);
    if (fileViolations.length === 0) {
      continue;
    }
    const relative = path.relative(REPO_ROOT, file);
    for (const violation of fileViolations) {
      violations++;
      console.error(
        `${relative}:${violation.line}: blocking wait in production code - the main thread is a no-go to block, ever: ${violation.text}`
      );
    }
  }

  console.log(`[blocking-waits] ${files.length} file(s) scanned`);
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
