/*
    Production code compares strings explicitly (PR #73 review).

    `a == "b"` and `a != string.Empty` silently pick a comparison: the `==`
    operator is ordinal, so it is not a culture bug, but it never states the
    intended comparison and never states case sensitivity. Every string
    comparison in shipped code must name its rule with
    `string.Equals(a, b, StringComparison.Ordinal | OrdinalIgnoreCase)`
    (context.md rule 7), which forces the case-sensitivity decision at the
    site instead of leaving it implicit. Name-shaped comparisons (font,
    theme, command names) are `OrdinalIgnoreCase` by convention, matching
    the shell's own name lookups.

    Detection is token-based (the comparison-direction tokenizer) and
    deliberately precise: it flags `==` / `!=` when either operand is a
    string literal (plain, verbatim, interpolated, or raw) or the
    `string.Empty` / `String.Empty` member. That subset is type-safe to
    detect statically, so the scan has zero false positives. Identifier-vs-
    identifier string equality (`font.name == defaultFontName`) needs type
    information a tokenizer does not have; that class stays a review
    convention documented in context.md rule 7. Comments, string/char
    literal contents, and preprocessor lines are skipped, so documentation
    about the rule cannot trip it.

    There is no `--fix` on purpose: choosing `Ordinal` vs `OrdinalIgnoreCase`
    is a semantic decision per call site, not a mechanical rewrite.

    Exit codes: 0 = clean, 1 = at least one violation (or nothing was scanned).

    Adapted from the repository's lint-linq-production.mjs structure and the
    comparison-direction tokenizer (issues #50/#51 lineage).
*/
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const REPO_ROOT = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../.."
);
// Overridable so the contract tests can point the scan at a fixture tree. Nothing in CI sets it.
const SCAN_ROOTS = process.env.STRING_EQUALITY_ROOTS
  ? process.env.STRING_EQUALITY_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor"];

const { tokenize } = await import(
  pathToFileURL(
    path.join(REPO_ROOT, "tooling~", "scripts", "lint-comparison-direction.mjs")
  ).href
);

/** A string-literal token (a char literal token also has kind "string"; it is not a string operand). */
function isStringLiteral(token) {
  return token.kind === "string" && !token.text.startsWith("'");
}

/**
 * True for the `string` keyword or the `String` alias. The tokenizer classifies C# keywords
 * (including `string`) as kind "keyword" and the alias as an identifier, so both shapes pass.
 */
function isStringTypeToken(token) {
  return (
    token.kind === "identifier" &&
    (token.text === "string" || token.text === "String")
  ) || (token.kind === "keyword" && token.text === "string");
}

/** True when the tokens ending at `end` (exclusive) spell `string.Empty` or `String.Empty`. */
function isEmptyMemberAt(tokens, end) {
  if (end < 3) {
    return false;
  }
  const [type, dot, member] = [tokens[end - 3], tokens[end - 2], tokens[end - 1]];
  return (
    isStringTypeToken(type) &&
    dot.kind === "punctuation" &&
    dot.text === "." &&
    member.kind === "identifier" &&
    member.text === "Empty"
  );
}

/**
 * A lone backslash token can only come from an escaped character inside an interpolated
 * string's hole (`$"{name == \"on\"}"`): the hole slice is re-tokenized as code, where the
 * escape's backslash is not part of any literal. It is never real C# syntax, so the operand
 * walk steps over it to reach the escaped literal.
 */
function isEscapeArtifact(token) {
  return token.kind === "punctuation" && token.text === "\\";
}

/** True when the operand adjacent to the `==`/`!=` token at `operatorIndex` is a string operand. */
function operandIsString(tokens, operatorIndex, side) {
  if (side > 0) {
    let nextIndex = operatorIndex + 1;
    while (nextIndex < tokens.length && isEscapeArtifact(tokens[nextIndex])) {
      nextIndex++;
    }
    const next = tokens[nextIndex];
    if (next === undefined) {
      return false;
    }
    /*
        The tokenizer emits an interpolated literal's hole code BEFORE the literal token, so a
        top-level operator followed by hole tokens compares against that interpolated string.
        An in-hole operator's hole-tagged neighbors are its own expression, so the skip applies
        to top-level operators only (an in-hole operator followed by a nested interpolated
        literal is a false negative; that shape is documented as out of scope).
     */
    if (tokens[operatorIndex].hole === undefined && next.hole !== undefined) {
      return true;
    }
    if (isStringLiteral(next)) {
      return true;
    }
    return (
      isStringTypeToken(next) &&
      tokens[nextIndex + 1]?.text === "." &&
      tokens[nextIndex + 2]?.text === "Empty"
    );
  }

  let previousIndex = operatorIndex - 1;
  while (0 <= previousIndex && isEscapeArtifact(tokens[previousIndex])) {
    previousIndex--;
  }
  const previous = tokens[previousIndex];
  if (previous === undefined) {
    return false;
  }
  if (isStringLiteral(previous)) {
    return true;
  }
  return previous.kind === "identifier" &&
    previous.text === "Empty" &&
    isEmptyMemberAt(tokens, previousIndex + 1);
}

/** Returns one-based line/column violations for `==`/`!=` with a string operand. */
export function stringEqualityViolations(rawText) {
  const tokens = tokenize(rawText);
  const violations = [];
  for (let index = 0; index < tokens.length; index++) {
    const token = tokens[index];
    if (token.kind !== "punctuation" || (token.text !== "==" && token.text !== "!=")) {
      continue;
    }
    if (operandIsString(tokens, index, 1) || operandIsString(tokens, index, -1)) {
      violations.push({ line: token.line, column: token.column });
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
      `[string-equality] ERROR: no C# files were found under ${SCAN_ROOTS.join(", ")}, so this run checked nothing. A scan that matched nothing is the absence of a measurement, not a pass.`
    );
    return 1;
  }

  let violations = 0;
  for (const file of files) {
    const text = fs.readFileSync(file, "utf8");
    const fileViolations = stringEqualityViolations(text);
    if (fileViolations.length === 0) {
      continue;
    }
    const fileLines = text.split("\n");
    const relative = path.relative(REPO_ROOT, file);
    for (const violation of fileViolations) {
      violations++;
      console.error(
        `${relative}:${violation.line}:${violation.column}: string comparison without an explicit StringComparison: ${fileLines[violation.line - 1]?.trim() ?? ""}`
      );
    }
  }

  console.log(`[string-equality] ${files.length} file(s) scanned`);
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
