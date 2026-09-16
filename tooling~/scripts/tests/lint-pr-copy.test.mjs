/*
    Contract tests for tooling~/scripts/lint-pr-copy.mjs.

    Data-driven over the STE copy rules (.llm/skills/simple-writing/SKILL.md):
    the canonical valid body passes, each violation class fails with its own
    message, the Cursor Bugbot summary block is ignored, and the CLI pins the
    exit codes end to end.
*/
import test from "node:test";
import assert from "node:assert";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const toolingRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const linterPath = path.join(toolingRoot, "scripts", "lint-pr-copy.mjs");
const { DISCLOSURE_LINE, bodyLines, lintPullRequestCopy } = await import(
  pathToFileURL(linterPath).href
);

const TITLE = "Add feature X";
const VALID_BODY = [
  DISCLOSURE_LINE,
  "",
  "**Why:**",
  "Discovery walked every assembly type. A generated catalog binds commands directly.",
  "",
  "**What:**",
  "- Ship a netstandard2.0 source generator emitting one catalog per assembly.",
  "- Bind catalogs first; keep the reflection walk as fallback.",
  "- Pin payload reproducibility with a byte-compare test.",
  "",
  "**How we know:**",
  "Cold discovery: 2316 ms -> 5.6 ms in a 246-assembly domain.",
  "PlayMode 175/175; generator suite 23/23."
].join("\n");

function case_(name, overrides) {
  return { name, ...overrides };
}

const validCases = [
  case_("canonical body", { title: TITLE, body: VALID_BODY }),
  case_("minimum body: 3 bullets, no evidence", {
    title: TITLE,
    body: [DISCLOSURE_LINE, "", "**Why:**", "One reason.", "", "**What:**", "- One.", "- Two.", "- Three."].join("\n")
  }),
  case_("one-line Why", {
    title: TITLE,
    body: [DISCLOSURE_LINE, "", "**Why:**", "One sentence only.", "", "**What:**", "- One.", "- Two.", "- Three."].join("\n")
  }),
  case_("blank lines between bullets do not count against budgets", {
    title: TITLE,
    body: [
      DISCLOSURE_LINE,
      "",
      "**Why:**",
      "Reason one.",
      "",
      "**What:**",
      "- One.",
      "",
      "- Two.",
      "",
      "- Three.",
      "",
      "**How we know:**",
      "Evidence."
    ].join("\n")
  }),
  case_("crlf body is normalized", { title: TITLE, body: VALID_BODY.split("\n").join("\r\n") })
];

const violationCases = [
  case_("empty title", { title: "  ", body: VALID_BODY }, [/title is empty/]),
  case_("overlong title", { title: "x".repeat(73), body: VALID_BODY }, [/title is 73 chars \(max 72\)/]),
  case_("missing disclosure", { title: TITLE, body: VALID_BODY.slice(DISCLOSURE_LINE.length + 1) }, [
    /line 1 must be "DISCLOSURE: LLM-GENERATED TEXT"/
  ]),
  case_("overlong body", { title: TITLE, body: VALID_BODY + "\n- Extra bullet.\n- Another.\n- Third.\n- Fourth." }, [
    /body is 1[78] content lines \(max 16\)/
  ]),
  case_("preamble between disclosure and first section", {
    title: TITLE,
    body: [DISCLOSURE_LINE, "", "This PR is a comprehensive overhaul.", "", "**What:**", "- One.", "- Two.", "- Three."].join("\n")
  }, [/no content between the disclosure line/]),
  case_("missing Why", { title: TITLE, body: [DISCLOSURE_LINE, "", "**What:**", "- One.", "- Two.", "- Three."].join("\n") }, [
    /missing "\*\*Why:\*\*" section/
  ]),
  case_("missing What", { title: TITLE, body: [DISCLOSURE_LINE, "", "**Why:**", "Reason."].join("\n") }, [
    /missing "\*\*What:\*\*" section/
  ]),
  case_("Why over budget", {
    title: TITLE,
    body: [DISCLOSURE_LINE, "", "**Why:**", "One.", "Two.", "Three.", "", "**What:**", "- One.", "- Two.", "- Three."].join("\n")
  }, [/"\*\*Why:\*\*" has 3 lines \(max 2\)/]),
  case_("What under budget", {
    title: TITLE,
    body: [DISCLOSURE_LINE, "", "**Why:**", "Reason.", "", "**What:**", "- One.", "- Two."].join("\n")
  }, [/"\*\*What:\*\*" has 2 bullets \(need 3-6\)/]),
  case_("What over budget", {
    title: TITLE,
    body: [
      DISCLOSURE_LINE,
      "",
      "**Why:**",
      "Reason.",
      "",
      "**What:**",
      "- One.",
      "- Two.",
      "- Three.",
      "- Four.",
      "- Five.",
      "- Six.",
      "- Seven."
    ].join("\n")
  }, [/"\*\*What:\*\*" has 7 bullets \(need 3-6\)/]),
  case_("What with prose instead of bullets", {
    title: TITLE,
    body: [DISCLOSURE_LINE, "", "**Why:**", "Reason.", "", "**What:**", "- One.", "- Two.", "Prose line."].join("\n")
  }, [/"\*\*What:\*\*" entries must all be one-line bullets/]),
  case_("evidence over budget", {
    title: TITLE,
    body: [
      DISCLOSURE_LINE,
      "",
      "**Why:**",
      "Reason.",
      "",
      "**What:**",
      "- One.",
      "- Two.",
      "- Three.",
      "",
      "**How we know:**",
      "A.",
      "B.",
      "C.",
      "D."
    ].join("\n")
  }, [/"\*\*How we know:\*\*" has 4 lines \(max 3\)/]),
  case_("unknown section", {
    title: TITLE,
    body: [DISCLOSURE_LINE, "", "**Why:**", "Reason.", "", "**What:**", "- One.", "- Two.", "- Three.", "", "**Notes:**", "Extra."].join("\n")
  }, [/unknown section "\*\*Notes:\*\*"/]),
  case_("duplicate section", {
    title: TITLE,
    body: [DISCLOSURE_LINE, "", "**Why:**", "Reason.", "**Why:**", "Again.", "", "**What:**", "- One.", "- Two.", "- Three."].join("\n")
  }, [/duplicate "\*\*Why:\*\*" section/]),
  case_("empty evidence section", {
    title: TITLE,
    body: [DISCLOSURE_LINE, "", "**Why:**", "Reason.", "", "**What:**", "- One.", "- Two.", "- Three.", "", "**How we know:**", ""].join("\n")
  }, [/"\*\*How we know:\*\*" is empty/])
];

for (const testCase of validCases) {
  test(`valid: ${testCase.name}`, () => {
    assert.deepStrictEqual(lintPullRequestCopy(testCase), []);
  });
}

for (const testCase of violationCases) {
  test(`violation: ${testCase.name}`, () => {
    const violations = lintPullRequestCopy(testCase);
    assert.ok(0 < violations.length, "expected violations");
    for (const pattern of testCase.expected ?? []) {
      assert.ok(violations.some((violation) => pattern.test(violation)), `${pattern} not found in: ${violations.join("; ")}`);
    }
  });
}

test("bodyLines strips the Cursor Bugbot summary block", () => {
  const body = `${VALID_BODY}\n\n<!-- CURSOR_SUMMARY -->\n\n> [!NOTE]\n> Huge bot block\n> spanning many lines.\n\n<!-- /CURSOR_SUMMARY -->\n`;
  assert.deepStrictEqual(lintPullRequestCopy({ title: TITLE, body }), []);
  assert.deepStrictEqual(bodyLines(body).slice(-1), ["PlayMode 175/175; generator suite 23/23."]);
});

test("bodyLines strips an unterminated summary block", () => {
  const body = `${VALID_BODY}\n\n<!-- CURSOR_SUMMARY -->\n> Never closed.\n`;
  assert.deepStrictEqual(lintPullRequestCopy({ title: TITLE, body }), []);
});

test("empty body fails on the disclosure and missing sections", () => {
  const violations = lintPullRequestCopy({ title: TITLE, body: "" });
  assert.ok(violations.some((violation) => /line 1 must be/.test(violation)));
  assert.ok(violations.some((violation) => /missing "\*\*Why:\*\*"/.test(violation)));
});

test("CLI exits 0 on a valid copy and 1 with violations listed", () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "dxt-pr-copy-"));
  try {
    const bodyFile = path.join(dir, "body.md");
    fs.writeFileSync(bodyFile, VALID_BODY);
    const stdout = execFileSync(process.execPath, [linterPath, "--title", TITLE, "--body-file", bodyFile], {
      encoding: "utf8"
    });
    assert.match(stdout, /\[pr-copy\] ok:/);

    const badFile = path.join(dir, "bad.md");
    fs.writeFileSync(badFile, "Verbose wall of text without any sections.");
    let failed = false;
    try {
      execFileSync(process.execPath, [linterPath, "--title", TITLE, "--body-file", badFile], {
        encoding: "utf8",
        stdio: ["ignore", "pipe", "pipe"]
      });
    } catch (error) {
      failed = true;
      assert.strictEqual(error.status, 1);
      assert.match(error.stderr, /4 violation\(s\)/);
      assert.match(error.stderr, /line 1 must be/);
      assert.match(error.stderr, /simple-writing/);
    }
    assert.ok(failed, "expected the CLI to exit non-zero on violations");
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test("CLI reads the body from stdin with --body-file -", () => {
  const stdout = execFileSync(process.execPath, [linterPath, "--title", TITLE, "--body-file", "-"], {
    input: VALID_BODY,
    encoding: "utf8"
  });
  assert.match(stdout, /\[pr-copy\] ok:/);
});
