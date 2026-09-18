/*
    UnityEngine.Object null checks use Unity's `==`/`!=` operators explicitly (rule 25,
    issue #98): `Assert.IsNull(x)`/`Assert.IsNotNull(x)` bypass Unity's fake-null operator,
    so a destroyed-but-referenced object slips through both. Every null assertion must
    read `Assert.That(x == null)` / `Assert.That(x != null)`, which evaluates the same
    operator a production null check would.

    The scan is a real token walk, not a grep, because the banned call text can appear
    inside string literals (fixture sources, error messages) and interpolated holes:
    `tokenize` from the comparison-direction linter consumes comments, preprocessor
    directives, and every literal shape, so only real call sites are reported.

    `--fix` rewrites each call mechanically: `Assert.IsNull(subject, rest...)` becomes
    `Assert.That(subject == null, rest...)` with every other byte of the call preserved
    (whitespace, message, line breaks). A subject whose top level contains `??` or a
    ternary would change precedence under the appended operator, so `--fix` refuses it
    and the violation stays for a hand edit.

    Exit codes: 0 = clean (or every fixable violation fixed), 1 = at least one violation
    remains.

    Adapted from the repository's lint-multiline-comments.mjs structure (issue #57) and
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
const SCAN_ROOTS = process.env.UNITY_NULL_ROOTS
  ? process.env.UNITY_NULL_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor", "Tests", "Generator~"];

const { tokenize } = await import(
  pathToFileURL(
    path.join(REPO_ROOT, "tooling~", "scripts", "lint-comparison-direction.mjs")
  ).href
);

const BANNED = new Set(["IsNull", "IsNotNull"]);

/**
 * Finds every `Assert.IsNull(`/`Assert.IsNotNull(` call span. Tokens tagged with an
 * interpolation `hole` are string data, not code, so they can never start or extend a call.
 */
export function violations(text) {
  const tokens = tokenize(text).filter((token) => token.hole === undefined);
  const found = [];
  for (let i = 0; i < tokens.length - 2; i++) {
    const assertToken = tokens[i];
    const dotToken = tokens[i + 1];
    const kindToken = tokens[i + 2];
    if (
      assertToken.kind !== "identifier" ||
      assertToken.text !== "Assert" ||
      dotToken.text !== "." ||
      dotToken.kind !== "punctuation" ||
      kindToken.kind !== "identifier" ||
      !BANNED.has(kindToken.text)
    ) {
      continue;
    }
    const openParen = tokens[i + 3];
    if (!openParen || openParen.text !== "(" || openParen.kind !== "punctuation") {
      continue;
    }
    let depth = 0;
    let closeParen;
    let firstComma;
    for (let j = i + 3; j < tokens.length; j++) {
      const token = tokens[j];
      if (token.kind !== "punctuation") {
        continue;
      }
      if (token.text === "(") {
        depth++;
        continue;
      }
      if (token.text === ")") {
        depth--;
        if (depth === 0) {
          closeParen = token;
          break;
        }
        continue;
      }
      if (token.text === "," && depth === 1 && firstComma === undefined) {
        firstComma = token;
      }
    }
    if (!closeParen) {
      // An unbalanced call cannot compile; report it so the tree is never read as green.
      found.push({
        line: assertToken.line,
        column: assertToken.column,
        kind: kindToken.text,
        start: assertToken.start,
        end: assertToken.start,
        subject: "",
      });
      continue;
    }
    const subjectEnd = firstComma ? firstComma.start : closeParen.start;
    found.push({
      line: assertToken.line,
      column: assertToken.column,
      kind: kindToken.text,
      start: assertToken.start,
      end: closeParen.end,
      subject: text.slice(openParen.end, subjectEnd),
    });
  }
  return found;
}

/**
 * Builds the `Assert.That(subject == null, rest...)` replacement for one violation, or
 * undefined when the subject's precedence would change under the appended operator.
 */
export function planFix(text, violation) {
  // An unbalanced call cannot compile; there is no mechanical fix for it.
  if (violation.end === violation.start) {
    return undefined;
  }
  if (/[?]/.test(violation.subject.replace(/"[^"]*"/g, ""))) {
    return undefined;
  }
  const subject = violation.subject;
  const trimmedSubject = subject.trimEnd();
  const insertAt = violation.start + "Assert".length + 1 + violation.kind.length + 1 + trimmedSubject.length;
  const operator = violation.kind === "IsNull" ? " == null" : " != null";
  return {
    start: violation.start,
    end: violation.end,
    replacement:
      "Assert.That(" +
      trimmedSubject +
      operator +
      text.slice(insertAt, violation.end),
  };
}

export function applyFixes(text, found) {
  const plans = found
    .map((violation) => planFix(text, violation))
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
      `[unity-null-patterns] ERROR: no C# files were found under ${SCAN_ROOTS.join(", ")}, so this run checked nothing. A scan that matched nothing is the absence of a measurement, not a pass.`
    );
    return 1;
  }

  let violationCount = 0;
  let converted = 0;
  for (const file of files) {
    const text = fs.readFileSync(file, "utf8");
    const found = violations(text);
    if (found.length === 0) {
      continue;
    }
    const relative = path.relative(REPO_ROOT, file);
    for (const violation of found) {
      if (!shouldFix || planFix(text, violation) === undefined) {
        violationCount++;
        console.error(
          `${relative}:${violation.line}:${violation.column}: Assert.${violation.kind} bypasses Unity's null operator; use Assert.That(x ${violation.kind === "IsNull" ? "==" : "!="} null)`
        );
      }
    }
    if (shouldFix) {
      const updated = applyFixes(text, found);
      if (updated !== text) {
        converted += found.filter(
          (violation) => planFix(text, violation) !== undefined
        ).length;
        fs.writeFileSync(file, updated);
      }
    }
  }

  if (shouldFix) {
    console.log(`[unity-null-patterns] converted ${converted} assert call(s) to Assert.That`);
  } else {
    console.log(`[unity-null-patterns] ${files.length} file(s) scanned`);
  }
  return 0 < violationCount ? 1 : 0;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  /*
      exitCode, not exit: on Windows, writes to a piped stderr are asynchronous, and a
      process.exit() would truncate the violation report this process is still flushing.
      Letting the loop drain keeps the report whole and the exit code identical.
   */
  process.exitCode = main(process.argv.slice(2));
}
