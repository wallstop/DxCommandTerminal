#!/usr/bin/env node
/*
    Out-parameter discipline, enforced at 100% (context.md rule 13, issue #119).

    Rule 13: assign `out` parameters immediately before each `return`, per path. The
    review that spawned this linter (PR #118, `TryWriteManifestFile`) says why: a
    blanket assignment at method entry defeats the compiler's definite-assignment
    bugcheck, so a path that forgets its real assignment compiles clean and returns
    a stale value. Assigning in the tail of the path keeps that bugcheck armed.

    This tokenizes rather than greps because the violation is structural, not
    lexical. For every block-bodied function (method, local function, lambda,
    anonymous delegate) that declares an `out` parameter, a statement-level
    assignment to that parameter is a violation exactly when the parameter is
    ASSIGNED AGAIN on some path that can follow it: the first assignment is then
    a blanket fallback whose value may be overwritten, which is what silences
    the compiler's definite-assignment bugcheck (a path that forgets its real
    assignment compiles clean and returns the fallback). Every later path keeps
    one assignment of its own, immediately before its `return`. Per-path
    arm assignments that join into a shared `return` (`TryEatArgument`) are
    clean -- each path assigns exactly once. The "may be re-assigned" scan
    follows arms, skips past conditionals, walks outward through block ends,
    and stops at `return`/`throw`/`break`/`continue`. Loop bodies are exempt:
    a retry loop re-assigns per iteration by design, and post-loop paths stay
    compiler-checked. Expression-bodied members are exempt (no statement list).
    Discards (`out _`) are not parameters. Switch sections fall through exactly
    like the language says.

    Detection sees STATEMENT-level assignments whose target is the bare parameter
    name (`value = ...`, `value ??= ...`). Known blind spots, accepted for v1
    while everything unparseable is reported loudly rather than skipped: chained
    targets (`w = v = 2;`), tuple deconstruction (`(v, w) = Pair();`),
    re-assignment by passing the parameter on as an `out`/`ref` argument
    (`Other(out value);`), and `goto`-labelled statements. An entry seed
    re-assigned only inside a later loop (`best = -1; foreach (...) { best =
    item; }`) is flagged like any other blanket fallback; the rule-13 shape for
    accumulators is a local accumulator assigned to the parameter once, before
    the return.

    Known limitation, accepted for v1: preprocessor directives are skipped by the
    tokenizer, so `#if` branches read as one straight-line flow. No current file
    conditions an out-parameter's assignment paths on `#if`.

    There is no `--fix` on purpose: restoring per-path adjacency means choosing a
    return shape per call site, which is a code change, not a mechanical rewrite.

    Exit codes: 0 = clean, 1 = at least one violation (or nothing was scanned).

    Tokenizer adapted from the repository's lint-comparison-direction.mjs
    (issues #50/#51 lineage); linter conventions follow unity-helpers
    (MIT, Ambiguous-Interactive).
*/
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { tokenize } from "./lint-comparison-direction.mjs";

const REPO_ROOT = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../.."
);
// Overridable so the contract tests can point the scan at a fixture tree. Nothing in CI sets it.
const SCAN_ROOTS = process.env.OUT_PARAM_DISCIPLINE_ROOTS
  ? process.env.OUT_PARAM_DISCIPLINE_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor", "Tests", "Samples~", "Generator~"];

const ASSIGNMENT_OPS = new Set([
  "=",
  "+=",
  "-=",
  "*=",
  "/=",
  "%=",
  "&=",
  "|=",
  "^=",
  "??="
]);

const OPENERS = new Set(["(", "[", "{"]);
const CLOSERS = new Set([")", "]", "}"]);
const MATCHING = { "(": ")", "[": "]", "{": "}" };

const CONTROL_KEYWORDS = new Set([
  "if",
  "switch",
  "while",
  "for",
  "foreach",
  "do",
  "try",
  "using",
  "lock",
  "fixed"
]);
const LOOP_KEYWORDS = new Set(["while", "for", "foreach", "do"]);
const TERMINAL_KEYWORDS = new Set(["return", "throw", "break", "continue"]);

/** Index of the token closing the bracket opened at `open`, bounded by `end`, or -1. */
function matchingBracket(tokens, open, end) {
  const limit = end ?? tokens.length;
  const want = MATCHING[tokens[open].text];
  let depth = 0;
  for (let i = open; i < limit; i += 1) {
    if (OPENERS.has(tokens[i].text)) {
      depth += 1;
    } else if (CLOSERS.has(tokens[i].text)) {
      depth -= 1;
      if (depth === 0) {
        return tokens[i].text === want ? i : -1;
      }
    }
  }
  return -1;
}

/** Skips `(...)` starting at `cursor` and returns the index just past `)`, or -1. */
function skipThroughParens(tokens, cursor, end) {
  if (tokens[cursor]?.text !== "(") {
    return -1;
  }
  const close = matchingBracket(tokens, cursor, end);
  return close < 0 ? -1 : close + 1;
}

/**
 * Parses the parameter list opened at `openParen` and returns the names of its
 * `out` parameters: `out T name`, `out var name`, and the implicit-lambda
 * `out name` spelling. `out _` is a discard, not a parameter. Attributes are
 * skipped, so `[Out] out T value` still parses, and generic and tuple types
 * (`out Dictionary<string, int> map`, `out (int A, int B) pair`) split on
 * depth-zero commas only. Returns null when the list cannot be parsed; the
 * caller reports that instead of silently skipping the function.
 */
export function outParameters(tokens, openParen, end) {
  const closeParen = matchingBracket(tokens, openParen, end);
  if (closeParen < 0) {
    return null;
  }
  const names = [];
  let segment = [];
  const flush = () => {
    const name = segmentOutName(segment);
    if (name === null) {
      return false;
    }
    if (name !== "") {
      names.push(name);
    }
    segment = [];
    return true;
  };
  let parenDepth = 0;
  let angleDepth = 0;
  for (let i = openParen + 1; i < closeParen; i += 1) {
    const text = tokens[i].text;
    if (text === "," && parenDepth === 0 && angleDepth === 0) {
      if (!flush()) {
        return null;
      }
      continue;
    }
    if (text === "(" || text === "[" || text === "{") {
      parenDepth += 1;
    } else if (text === ")" || text === "]" || text === "}") {
      parenDepth -= 1;
    } else if (text === "<") {
      angleDepth += 1;
    } else if (text === ">" && 0 < angleDepth) {
      angleDepth -= 1;
    }
    segment.push(tokens[i]);
  }
  if (!flush()) {
    return null;
  }
  return names;
}

/** The `out` parameter name in one comma-separated parameter segment; "" when the segment is not an out parameter. */
function segmentOutName(segment) {
  const meaningful = [];
  for (let i = 0; i < segment.length; i += 1) {
    if (segment[i].text === "[") {
      let depth = 0;
      let j = i;
      while (j < segment.length) {
        if (segment[j].text === "[") {
          depth += 1;
        } else if (segment[j].text === "]") {
          depth -= 1;
          if (depth === 0) {
            break;
          }
        }
        j += 1;
      }
      if (j >= segment.length) {
        return null;
      }
      i = j;
      continue;
    }
    meaningful.push(segment[i]);
  }
  if (meaningful.length === 0 || meaningful[0].text !== "out") {
    return "";
  }
  if (meaningful.length === 1) {
    return null;
  }
  const last = meaningful[meaningful.length - 1];
  if (last.kind !== "identifier") {
    return null;
  }
  return last.text === "_" ? "" : last.text;
}

/**
 * Every statement in [start, end) as a flat list. Returns null when the span
 * cannot be parsed; an unparseable function body is reported, never silently
 * skipped.
 *
 * Statement shapes:
 *   { kind: "simple" }   - expression, declaration, or assignment; straight-line
 *   { kind: "terminal" } - return/throw/break/continue; ends the path or arm
 *   { kind: "control" }  - if/switch/loop/try/... or a bare block; branches.
 *                          `arms` lists every parsed body (main arm, else arms,
 *                          catch/finally arms).
 * `case X:` / `default:` label runs are transparent prefixes, so switch sections
 * share one list and fall through exactly like the language says.
 */
export function parseStatements(tokens, start, end) {
  const statements = [];
  let i = start;
  while (i < end) {
    const token = tokens[i];
    if (token.text === ";") {
      i += 1;
      continue;
    }
    if (token.text === "{") {
      const close = matchingBracket(tokens, i, end);
      if (close < 0) {
        return null;
      }
      const body = parseStatements(tokens, i + 1, close);
      if (body === null) {
        return null;
      }
      statements.push({ kind: "control", what: "block", arms: [body], start: i, end: close + 1 });
      i = close + 1;
      continue;
    }
    if (
      (token.text === "case" || token.text === "default") &&
      token.kind === "keyword"
    ) {
      // Transparent switch-section label: skip through its ':' at bracket depth zero.
      let depth = 0;
      let j = i;
      while (j < end) {
        if (OPENERS.has(tokens[j].text)) {
          depth += 1;
        } else if (CLOSERS.has(tokens[j].text)) {
          depth -= 1;
        } else if (depth === 0 && tokens[j].text === ":") {
          break;
        }
        j += 1;
      }
      if (j >= end) {
        return null;
      }
      i = j + 1;
      continue;
    }

    const controlHead =
      token.kind === "keyword" &&
      CONTROL_KEYWORDS.has(token.text) &&
      !(token.text === "using" && tokens[i + 1]?.text !== "(");
    if (controlHead) {
      const parsed = parseControl(tokens, i, end);
      if (parsed === null) {
        return null;
      }
      statements.push(parsed.statement);
      i = parsed.end;
      continue;
    }

    const simple = parseSimple(tokens, i, end);
    if (simple === null) {
      return null;
    }
    statements.push(simple.statement);
    i = simple.end;
  }
  return statements;
}

/** Parses one control statement starting at `start`. */
function parseControl(tokens, start, end) {
  const what = tokens[start].text;
  const arms = [];
  const parseArmBlock = (cursor) => {
    if (tokens[cursor]?.text === "{") {
      const close = matchingBracket(tokens, cursor, end);
      if (close < 0) {
        return null;
      }
      const body = parseStatements(tokens, cursor + 1, close);
      if (body === null) {
        return null;
      }
      arms.push(body);
      return close + 1;
    }
    const single = parseArmBlockStatement(tokens, cursor, end);    if (single === null) {
      return null;
    }
    arms.push(single.body);
    return single.end;
  };

  let cursor = start + 1;
  if (what === "do") {
    const afterArm = parseArmBlock(cursor);
    if (afterArm === null || tokens[afterArm]?.text !== "while") {
      return null;
    }
    cursor = skipThroughParens(tokens, afterArm + 1, end);
    if (cursor < 0 || tokens[cursor]?.text !== ";") {
      return null;
    }
    return { statement: { kind: "control", what, arms, start, end: cursor + 1 }, end: cursor + 1 };
  }

  if (what === "try") {
    if (tokens[cursor]?.text !== "{") {
      return null;
    }
    const afterArm = parseArmBlock(cursor);
    if (afterArm === null) {
      return null;
    }
    cursor = afterArm;
    while (cursor < end && (tokens[cursor].text === "catch" || tokens[cursor].text === "finally")) {
      cursor += 1;
      if (tokens[cursor - 1].text === "catch" && tokens[cursor]?.text === "(") {
        cursor = skipThroughParens(tokens, cursor, end);
        if (cursor < 0) {
          return null;
        }
      }
      const afterCatch = parseArmBlock(cursor);
      if (afterCatch === null) {
        return null;
      }
      cursor = afterCatch;
    }
    return { statement: { kind: "control", what, arms, start, end: cursor }, end: cursor };
  }

  if (tokens[cursor]?.text === "(") {
    cursor = skipThroughParens(tokens, cursor, end);
    if (cursor < 0) {
      return null;
    }
  }
  const afterArm = parseArmBlock(cursor);
  if (afterArm === null) {
    return null;
  }
  cursor = afterArm;
  if (what === "if") {
    while (tokens[cursor]?.text === "else") {
      cursor += 1;
      if (tokens[cursor]?.text === "if") {
        cursor = skipThroughParens(tokens, cursor + 1, end);
        if (cursor < 0) {
          return null;
        }
      }
      const afterElse = parseArmBlock(cursor);
      if (afterElse === null) {
        return null;
      }
      cursor = afterElse;
    }
  }
  return { statement: { kind: "control", what, arms, start, end: cursor }, end: cursor };
}

/**
 * One arm statement without a brace block (`if (a) Do();`, and the nested
 * `if (a) if (b) ...;` chain shape, which must recurse or its inner
 * assignments would be invisible to the walk).
 */
function parseArmBlockStatement(tokens, cursor, end) {
  const first = tokens[cursor];
  if (first?.kind === "keyword" && CONTROL_KEYWORDS.has(first.text)) {
    const parsed = parseControl(tokens, cursor, end);
    return parsed === null ? null : { body: [parsed.statement], end: parsed.end };
  }
  const single = parseSimple(tokens, cursor, end);
  return single === null ? null : { body: [single.statement], end: single.end };
}

/** Parses one simple or terminal statement ending at the `;` at bracket depth zero. */
function parseSimple(tokens, start, end) {
  const first = tokens[start];
  let depth = 0;
  for (let j = start; j < end; j += 1) {
    const current = tokens[j];
    if (depth === 0 && current.text === ";") {
      const kind =
        first.kind === "keyword" && TERMINAL_KEYWORDS.has(first.text)
          ? "terminal"
          : "simple";
      return { statement: { kind, what: first.text, start, end: j + 1 }, end: j + 1 };
    }
    if (depth === 0 && current.text === "{") {
      /*
          A block inside a simple statement: an initialized declaration
          (`int[] a = { 1, 2 };`), a lambda body (`Action a = () => { ... };`),
          or a local function declaration. The first two end at a `;` after the
          block; a local function ends at its closing brace. Either way the
          embedded function, when one exists, is analyzed by its own signature
          scan.
       */
      const close = matchingBracket(tokens, j, end);
      if (close < 0) {
        return null;
      }
      const endOfStatement =
        tokens[close + 1]?.text === ";" ? close + 2 : close + 1;
      return {
        statement: { kind: "simple", what: first.text, start, end: endOfStatement },
        end: endOfStatement
      };
    }
    if (OPENERS.has(current.text)) {
      depth += 1;
    } else if (CLOSERS.has(current.text)) {
      depth -= 1;
      if (depth < 0) {
        return null;
      }
    }
  }
  return null;
}

/** True when the statement at `start` is a statement-level assignment to the plain identifier `name`. */
function assignsOutParam(tokens, start, name) {
  const first = tokens[start];
  return first.kind === "identifier" && first.text === name && ASSIGNMENT_OPS.has(tokens[start + 1]?.text);
}

/**
 * May another assignment to `param` execute after the statement at `from` in
 * `list`? Arms are followed (a conditional's body may run), the conditional
 * itself is skipped past (it may not), terminals end the path, and a list that
 * runs out hands the question to the enclosing control statement -- outward
 * until the function body's implicit return.
 */
function makeMayFollow(tokens, param, parents) {
  const mayFollow = (list, from) => {
    for (let i = from; i < list.length; i += 1) {
      const statement = list[i];
      if (statement.kind === "terminal") {
        return false;
      }
      if (statement.kind === "control") {
        for (const arm of statement.arms) {
          if (mayFollow(arm, 0)) {
            return true;
          }
        }
        continue;
      }
      if (assignsOutParam(tokens, statement.start, param)) {
        return true;
      }
    }
    const parent = parents.get(list);
    return parent === undefined ? false : mayFollow(parent.list, parent.index + 1);
  };
  return mayFollow;
}

/** Best-effort declaring identifier before the parameter list, for messages only. */
function functionName(tokens, openParen) {
  let cursor = openParen - 1;
  while (0 <= cursor && tokens[cursor].text === ">") {
    let depth = 0;
    while (0 <= cursor) {
      if (tokens[cursor].text === ">") {
        depth += 1;
      } else if (tokens[cursor].text === "<") {
        depth -= 1;
        if (depth === 0) {
          break;
        }
      }
      cursor -= 1;
    }
    cursor -= 1;
  }
  while (0 <= cursor && tokens[cursor].kind !== "identifier" && tokens[cursor].kind !== "keyword") {
    cursor -= 1;
  }
  const name = tokens[cursor];
  return name === undefined || name.kind !== "identifier" ? null : name.text;
}

/** Every rule-13 violation in one file's tokens. */
export function analyzeSource(text, file = "file") {
  const tokens = tokenize(text);
  const violations = [];
  for (let i = 0; i < tokens.length; i += 1) {
    if (tokens[i].text !== "(") {
      continue;
    }
    const lead = tokens[i - 1];
    if (lead === undefined) {
      continue;
    }
    if (lead.kind === "keyword" && lead.text !== "delegate") {
      continue;
    }
    if (lead.kind !== "keyword" && lead.kind !== "identifier" && lead.text !== ">") {
      // A lambda's parameter list (`(string s, out int v) => { ... }`) follows
      // `=`, `(`, or `,`; every other non-identifier lead is a grouping paren.
      if (!["=", ",", "(", "=>"].includes(lead.text)) {
        continue;
      }
    }
    const closeParen = matchingBracket(tokens, i, tokens.length);
    if (closeParen < 0) {
      continue;
    }
    let after = closeParen + 1;
    if (tokens[after]?.text === "where") {
      while (after < tokens.length && tokens[after].text !== "{" && tokens[after].text !== "=>") {
        after += 1;
      }
    }
    /*
        A `=>` before the body only exempts an EXPRESSION-bodied member; a
        block-bodied lambda (`(string s, out int v) => { ... }`) still has a
        statement list one token later.
     */
    if (tokens[after]?.text === "=>") {
      after += 1;
    }
    if (tokens[after]?.text !== "{") {
      continue;
    }
    const params = outParameters(tokens, i, tokens.length);
    if (params === null) {
      violations.push({
        line: tokens[i].line,
        message: `${file}:${tokens[i].line}: the parameter list of '${functionName(tokens, i) ?? "function"}' could not be parsed; fix or extend the linter rather than letting this function pass on a silent skip (context.md rule 13).`
      });
      continue;
    }
    if (params.length === 0) {
      continue;
    }
    const name = functionName(tokens, i) ?? "function";
    const bodyClose = matchingBracket(tokens, after, tokens.length);
    let statements = null;
    let candidates;
    let parents;
    try {
      statements = bodyClose < 0 ? null : parseStatements(tokens, after + 1, bodyClose);
      if (statements === null) {
        violations.push({
          line: tokens[after].line,
          message: `${file}:${tokens[after].line}: the body of '${name}' could not be parsed into statements; fix or extend the linter rather than letting this file pass on a silent skip (context.md rule 13).`
        });
        continue;
      }
      candidates = [];
      parents = new Map();
      const walk = (list, inLoop) => {
        for (let index = 0; index < list.length; index += 1) {
          const statement = list[index];
          if (statement.kind === "control") {
            const loopHere = inLoop || LOOP_KEYWORDS.has(statement.what);
            for (const arm of statement.arms) {
              parents.set(arm, { list, index });
              walk(arm, loopHere);
            }
            continue;
          }
          if (statement.kind !== "simple") {
            continue;
          }
          if (inLoop) {
            continue;
          }
          for (const param of params) {
            if (assignsOutParam(tokens, statement.start, param)) {
              candidates.push({ statement, param, list, index });
            }
          }
        }
      };
      walk(statements, false);
    } catch (error) {
      violations.push({
        line: tokens[after].line,
        message: `${file}:${tokens[after].line}: the body of '${name}' defeated the statement walk (${error.message}); fix or extend the linter rather than letting this file pass on a silent skip (context.md rule 13).`
      });
      continue;
    }
    for (const candidate of candidates) {
      if (makeMayFollow(tokens, candidate.param, parents)(candidate.list, candidate.index + 1)) {
        violations.push({
          line: tokens[candidate.statement.start].line,
          message:
            `${file}:${tokens[candidate.statement.start].line}: out parameter '${candidate.param}' of '${name}' is ` +
            `assigned here and re-assigned on a path that can follow, so this first value is a blanket ` +
            `fallback that defeats the compiler's definite-assignment check; assign the parameter once, ` +
            `immediately before each return (context.md rule 13).`
        });
      }
    }
  }
  return violations;
}

function sourceFiles(directory, found) {
  if (!fs.existsSync(directory)) {
    return found;
  }
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === "bin" || entry.name === "obj" || entry.name === "node_modules") {
        continue;
      }
      sourceFiles(full, found);
    } else if (entry.name.endsWith(".cs")) {
      found.push(full);
    }
  }
  return found;
}

function main() {
  const files = [];
  for (const root of SCAN_ROOTS) {
    sourceFiles(path.isAbsolute(root) ? root : path.join(REPO_ROOT, root), files);
  }
  const byRelative = new Map();
  for (const file of files) {
    byRelative.set(path.relative(REPO_ROOT, file).split(path.sep).join("/"), file);
  }
  const scanned = [...byRelative.keys()].sort();
  if (scanned.length === 0) {
    console.error(
      `[out-param-discipline] ERROR: no C# files were found under ${SCAN_ROOTS.join(", ")}, so this run checked nothing. A scan that matched nothing is the absence of a measurement, not a pass.`
    );
    return 1;
  }

  let violations = 0;
  for (const relative of scanned) {
    const text = fs.readFileSync(byRelative.get(relative), "utf8");
    for (const violation of analyzeSource(text, relative)) {
      violations += 1;
      console.error(violation.message);
    }
  }

  console.log(`[out-param-discipline] ${scanned.length} file(s) scanned`);
  return 0 < violations ? 1 : 0;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  /*
      exitCode, not exit: on Windows, writes to a piped stderr are asynchronous, and a
      process.exit() would truncate the violation report this process is still flushing.
      Letting the loop drain keeps the report whole and the exit code identical.
   */
  process.exitCode = main();
}
