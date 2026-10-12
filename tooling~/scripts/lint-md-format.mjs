/*
    Markdown format gate (issue #215): every tracked markdown file is
    prettier-formatted at the repo's 180-character measure - the same
    decision the C# sweep landed - with code fences left alone
    (embeddedLanguageFormatting is off in .prettierrc.json, so shell and
    UnityYAML samples keep their exact bytes).

    The file list comes from `git ls-files`, so untracked scratch and
    git-ignored local files (PLAN.md, GOAL.md, progress/) never enter the
    gate, and the generated `.llm/skills/index.md` is excluded on purpose:
    its byte stability is owned by the skills-index generator, and a
    formatter rewrite would fight the regeneration the lint compares
    against.

    Usage: node tooling~/scripts/lint-md-format.mjs [--fix]
*/
import { execFileSync } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const PRETTIER_BIN = path.join(REPO_ROOT, "tooling~", "node_modules", "prettier", "bin", "prettier.cjs");
const GENERATED_FILES = new Set([".llm/skills/index.md"]);

const fix = process.argv.includes("--fix");

const tracked = execFileSync("git", ["ls-files", "-z", "*.md"], {
  cwd: REPO_ROOT,
  maxBuffer: 64 * 1024 * 1024,
  encoding: "utf8"
})
  .split("\0")
  .filter((file) => file.length > 0 && !GENERATED_FILES.has(file));

if (!existsSync(PRETTIER_BIN)) {
  console.error("[lint-md-format] prettier is not installed; run: npm --prefix tooling~ install");
  process.exit(1);
}
if (tracked.length === 0) {
  console.log("[lint-md-format] no tracked markdown files; nothing to check");
  process.exit(0);
}

try {
  /* Success output is discarded: the summary below is the report, and
     preflight captures failure output itself. */
  execFileSync(
    process.execPath,
    [PRETTIER_BIN, fix ? "--write" : "--check", ...tracked],
    { cwd: REPO_ROOT, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
  );
} catch (error) {
  const output = [error.stdout, error.stderr].filter((part) => typeof part === "string" && part.length > 0).join("\n");
  if (output.length > 0) {
    console.log(output.trimEnd());
  }
  if (!fix) {
    console.error(
      "\n[lint-md-format] markdown is not prettier-formatted; run: npm --prefix tooling~ run lint:md:fix"
    );
  }
  process.exit(1);
}
if (!fix) {
  console.log("[lint-md-format] all tracked markdown files are prettier-formatted");
}
