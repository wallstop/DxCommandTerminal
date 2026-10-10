/*
    A Debug.Log-family call logs the exception itself, not its .Message.

    `{e.Message}` in a log line keeps the sentence and drops the throw site:
    the one thing a stack trace carries that nothing else does. Interpolating
    the exception (`{e}`) calls ToString() implicitly and carries the type,
    the message, and the stack, so a failure names where it came from. A log
    line is also where an exception's readability matters least - the trace
    is data there, not noise - while `ReportCommandFailure`-style split
    channels (a one-line in-game record plus Debug.LogException for the
    frames) keep .Message on purpose and are out of this rule's subject.

    Scope is the Unity Console sinks: Debug.Log, Debug.LogWarning, and
    Debug.LogError in shipped code (`Runtime/`, `Editor/`). The in-game
    funnel (Terminal.Log / CommandLog) renders the message by design and its
    callers may name .Message. Tests and `Generator~` tooling are exempt:
    they are not on any runtime path, and tests assert on .Message values.

    Detection walks the shipped sources with the shared brace-aware C#
    scanner (tooling~/scripts/lib/csharp-interpolation.mjs), so a `.Message`
    inside a comment or a plain string cannot trip the rule, and one inside
    an interpolation hole - the shape that carries it to the Console - is
    always seen. Stated limit: a plain string NESTED inside a hole (e.g.
    `{config["ex.Message"]}`) is hole text to the scanner and will read as a
    violation; write such a lookup without the word `.Message` in the key,
    because the gate cannot tell it from a member access without tracking
    nested literal spans.

    There is no `--fix` on purpose: choosing where the full exception reads
    well is a per-call decision, not a mechanical rewrite.

    Exit codes: 0 = clean, 1 = at least one violation (or nothing was scanned).

    Structure follows the repository's lint-linq-production.mjs conventions.
*/
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
// Overridable so the contract tests can point the scan at a fixture tree. Nothing in CI sets it.
const SCAN_ROOTS = process.env.EXCEPTION_TOSTRING_ROOTS
  ? process.env.EXCEPTION_TOSTRING_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor"];

const { blankComments, callCloseParen, classify, lineOf, startsInside } = await import(
  pathToFileURL(path.join(REPO_ROOT, "tooling~", "scripts", "lib", "csharp-interpolation.mjs")).href
);

/* `\b` matches the tail of `UnityEngine.Debug.` too. LogException already takes the exception. */
const DEBUG_LOG_CALL = /\bDebug\.(?:Log|LogWarning|LogError)\s*\(/g;
const MEMBER_MESSAGE = /(?<![\w.])\w+(?:\.\w+)*\.Message\b/g;

/**
 * One-based line numbers whose code text puts an `x.Message` inside a
 * Debug.Log-family argument span.
 */
export function exceptionMessageViolations(rawText) {
  const { literals, comments } = classify(rawText);
  const code = blankComments(rawText, comments);
  const violations = [];
  for (const match of code.matchAll(DEBUG_LOG_CALL)) {
    if (startsInside(literals, match.index)) {
      continue;
    }

    const openParen = match.index + match[0].length - 1;
    const closeParen = callCloseParen(code, openParen, literals);
    if (closeParen < 0) {
      continue;
    }

    /*
        Literal text is data, hole text is code: blank each literal span
        except its recorded hole spans, so a `.Message` inside a quoted
        sentence cannot trip the rule while a hole's expression always can.
     */
    const argumentText = blankLiteralText(code.slice(openParen, closeParen), literals, openParen);
    for (const candidate of argumentText.matchAll(MEMBER_MESSAGE)) {
      violations.push({
        line: lineOf(rawText, openParen + candidate.index),
        text: candidate[0],
      });
    }
  }

  return violations;
}

/** The span's text with literal contents blanked, hole spans kept. */
function blankLiteralText(span, literals, openParen) {
  const characters = span.split("");
  for (const literal of literals) {
    if (literal.start < openParen || literal.end > openParen + span.length) {
      continue;
    }

    const holeSpans = literal.interpolated
      ? literal.holes.filter((hole) => hole.start >= literal.start && hole.end <= literal.end)
      : [];
    for (let index = literal.start; index < literal.end; ++index) {
      if (holeSpans.some((hole) => hole.start <= index && index < hole.end)) {
        continue;
      }

      if (characters[index - openParen] !== "\n") {
        characters[index - openParen] = " ";
      }
    }
  }

  return characters.join("");
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
      `[exception-tostring] ERROR: no C# files were found under ${SCAN_ROOTS.join(", ")}, so this run checked nothing. A scan that matched nothing is the absence of a measurement, not a pass.`
    );
    return 1;
  }

  let violations = 0;
  for (const file of files) {
    const text = fs.readFileSync(file, "utf8");
    const fileViolations = exceptionMessageViolations(text);
    if (fileViolations.length === 0) {
      continue;
    }
    const relative = path.relative(REPO_ROOT, file);
    for (const violation of fileViolations) {
      violations++;
      console.error(
        `${relative}:${violation.line}: exception .Message in a Debug.Log call; log the exception itself (ToString): ${violation.text}`
      );
    }
  }

  console.log(`[exception-tostring] ${files.length} file(s) scanned`);
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
