/*
    STE copy linter for PR titles and descriptions (issue: PR descriptions
    are too verbose).

    Enforces the structure locked in .llm/skills/simple-writing/SKILL.md:
    - Disclosure first line: "DISCLOSURE: LLM-GENERATED TEXT" (never counted
      against any budget).
    - Title: imperative, <= 72 chars.
    - "**Why:**" 1-2 content lines. Required.
    - "**What:**" 3-6 one-line bullets ("- "). Required.
    - "**How we know:**" 1-3 plain evidence lines. Optional.
    - Nothing else: no preamble after the disclosure line, no unknown
      "**X:**" sections, no duplicates, sections in that order.

    Carve-out: a PR titled "release: vX.Y.Z" (opened by release-prepare.yml)
    carries the machine-generated changelog excerpt as its body - that is
    shipped content, not authored copy - so its body is skipped; the title
    check still applies.

    The Cursor Bugbot summary block ("<!-- CURSOR_SUMMARY -->" through
    "<!-- /CURSOR_SUMMARY -->") is stripped before checking: the bot appends
    it to PR bodies after the fact, and it is not authored copy.

    Pure checks live here (lintPullRequestCopy); the CLI owns I/O. Stdlib only.
*/
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const DISCLOSURE_LINE = "DISCLOSURE: LLM-GENERATED TEXT";
const TITLE_MAX_CHARS = 72;
const BODY_MAX_LINES = 16;
const WHY_MAX_LINES = 2;
const WHAT_MIN_BULLETS = 3;
const WHAT_MAX_BULLETS = 6;
const EVIDENCE_MAX_LINES = 3;
const SECTION_MARKERS = ["Why", "What", "How we know"];
const CURSOR_SUMMARY_START = "<!-- CURSOR_SUMMARY -->";
const CURSOR_SUMMARY_END = "<!-- /CURSOR_SUMMARY -->";
const RELEASE_TITLE_PATTERN = /^release: v/;

/*
    Strips the Cursor Bugbot summary block (bot-appended, not authored copy)
    and trailing blank lines, normalizes CRLF. Returns the body as a line
    array.
*/
function bodyLines(body) {
  let text = body ?? "";
  const start = text.indexOf(CURSOR_SUMMARY_START);
  if (0 <= start) {
    const end = text.indexOf(CURSOR_SUMMARY_END, start);
    text = end < 0 ? text.slice(0, start) : text.slice(0, start) + text.slice(end + CURSOR_SUMMARY_END.length);
  }
  const lines = text.split("\n").map((line) => line.replace(/\r$/, ""));
  while (0 < lines.length && lines[lines.length - 1].trim().length === 0) {
    lines.pop();
  }
  return lines;
}

/*
    Splits the body into { preamble, sections: [{ name, lines }] }.
    Section markers are exact "**Name:**" lines (trimmed). Anything before
    the first marker (after the disclosure line) is preamble.
*/
function parseSections(lines) {
  const sections = [];
  let current = null;
  const preamble = [];
  for (const line of lines) {
    const marker = /^\*\*(.+?):\*\*$/.exec(line.trim());
    if (marker !== null) {
      current = { name: marker[1], lines: [] };
      sections.push(current);
      continue;
    }
    if (current === null) {
      preamble.push(line);
    } else {
      current.lines.push(line);
    }
  }
  return { preamble, sections };
}

function contentLines(lines) {
  return lines.filter((line) => line.trim().length !== 0);
}

/*
    Checks one PR { title, body }. Returns violation strings; empty means the
    copy passes.
*/
function lintPullRequestCopy(pullRequest) {
  const violations = [];
  const title = pullRequest.title ?? "";
  if (title.trim().length === 0) {
    violations.push("title is empty");
  } else if (TITLE_MAX_CHARS < title.length) {
    violations.push(`title is ${title.length} chars (max ${TITLE_MAX_CHARS}): keep it imperative and short`);
  }
  if (RELEASE_TITLE_PATTERN.test(title)) {
    // Automated release PR: body is the machine-generated changelog excerpt.
    return violations;
  }

  const lines = bodyLines(pullRequest.body);
  if (lines.length === 0 || lines[0] !== DISCLOSURE_LINE) {
    violations.push(`line 1 must be "${DISCLOSURE_LINE}"`);
  }
  const afterDisclosure = lines[0] === DISCLOSURE_LINE ? lines.slice(1) : lines;

  const counted = contentLines(afterDisclosure).length;
  if (BODY_MAX_LINES < counted) {
    violations.push(
      `body is ${counted} content lines (max ${BODY_MAX_LINES}); cut before adding - link instead of restating`
    );
  }

  const { preamble, sections } = parseSections(afterDisclosure);
  if (0 < contentLines(preamble).length) {
    violations.push("no content between the disclosure line and the first section");
  }

  const seen = new Map();
  for (const section of sections) {
    if (seen.has(section.name)) {
      violations.push(`duplicate "**${section.name}:**" section`);
      continue;
    }
    seen.set(section.name, section);
    if (!SECTION_MARKERS.includes(section.name)) {
      violations.push(
        `unknown section "**${section.name}:**" (allowed: ${SECTION_MARKERS.map((name) => `**${name}:**`).join(", ")})`
      );
    }
  }

  const why = seen.get("Why");
  if (why === undefined) {
    violations.push('missing "**Why:**" section (1-2 sentences)');
  } else {
    const count = contentLines(why.lines).length;
    if (count === 0) {
      violations.push('"**Why:**" is empty (1-2 sentences)');
    } else if (WHY_MAX_LINES < count) {
      violations.push(`"**Why:**" has ${count} lines (max ${WHY_MAX_LINES})`);
    }
  }

  const what = seen.get("What");
  if (what === undefined) {
    violations.push('missing "**What:**" section (3-6 one-line bullets)');
  } else {
    const entries = contentLines(what.lines);
    const bullets = entries.filter((line) => line.startsWith("- "));
    if (bullets.length !== entries.length) {
      violations.push('"**What:**" entries must all be one-line bullets starting with "- "');
    }
    if (entries.length < WHAT_MIN_BULLETS || WHAT_MAX_BULLETS < entries.length) {
      violations.push(`"**What:**" has ${entries.length} bullets (need ${WHAT_MIN_BULLETS}-${WHAT_MAX_BULLETS})`);
    }
  }

  const evidence = seen.get("How we know");
  if (evidence !== undefined) {
    const count = contentLines(evidence.lines).length;
    if (count === 0) {
      violations.push('"**How we know:**" is empty (1-3 evidence lines, or omit the section)');
    } else if (EVIDENCE_MAX_LINES < count) {
      violations.push(`"**How we know:**" has ${count} lines (max ${EVIDENCE_MAX_LINES})`);
    }
  }

  return violations;
}

const USAGE =
  "usage: node lint-pr-copy.mjs --title <text> (--body-file <path> | --body <text>)\n" +
  "       (reads the body from stdin when --body-file is \"-\")";

function parseArgs(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    const next = () => {
      index += 1;
      if (index >= argv.length) {
        throw new Error(`missing value for ${argument}`);
      }
      return argv[index];
    };
    if (argument === "--title") {
      options.title = next();
    } else if (argument === "--body") {
      options.body = next();
    } else if (argument === "--body-file") {
      const value = next();
      options.body = value === "-" ? fs.readFileSync(0, "utf8") : fs.readFileSync(value, "utf8");
    } else {
      throw new Error(`unknown argument: ${argument}`);
    }
  }
  return options;
}

function main() {
  let options;
  try {
    options = parseArgs(process.argv.slice(2));
    if (options.body === undefined) {
      throw new Error("missing body (pass --body-file <path>, --body <text>, or --body-file -)");
    }
  } catch (error) {
    console.error(`[pr-copy] ERROR: ${error.message}`);
    console.error(USAGE);
    process.exitCode = 1;
    return;
  }
  const violations = lintPullRequestCopy(options);
  if (violations.length === 0) {
    console.log("[pr-copy] ok: PR title and description follow the STE copy rules");
    return;
  }
  console.error(`[pr-copy] ${violations.length} violation(s):`);
  for (const violation of violations) {
    console.error(`[pr-copy] - ${violation}`);
  }
  console.error("[pr-copy] rules: .llm/skills/simple-writing/SKILL.md");
  process.exitCode = 1;
}

const isMain =
  process.argv[1] !== undefined && fileURLToPath(import.meta.url) === path.resolve(process.argv[1]);

if (isMain) {
  main();
}

export { DISCLOSURE_LINE, bodyLines, lintPullRequestCopy };
