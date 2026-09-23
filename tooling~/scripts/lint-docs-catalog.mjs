/*
    Docs catalog + guide link integrity gate (issue #137 T12 remainder).

    The screenshot scenario catalog (tooling~/scripts/t11/scenario-catalog.json)
    is the human-readable description layer over the T04 capture scenarios. Three
    contracts keep it truthful and the guides link-clean, all Unity-free:

    1. Catalog drift: the catalog names exactly the scenarios the capture harness
       knows (pinned canon + env variants + expected-incomplete), with a non-empty
       summary and a group that matches the scenario's registry. A renamed or added
       capture scenario fails here until the catalog describes it.
    2. Catalog staleness: every baselined scenario has a committed baseline in the
       T11 store, the pinned environment covers the full canon, and every store
       entry is cataloged. A scenario captured but never described (or baselined
       under a name the catalog dropped) fails.
    3. Guide links: every relative markdown link (inline or reference-style)
       resolves to a file next to the guide, every `[!code-csharp]` sample
       reference resolves to a shipped sample source, and every `xref:` uid
       resolves against the docfx API model when one has been generated (skipped
       with a notice otherwise, e.g. before the first docs build).

    Exit codes: 0 = clean, 1 = at least one violation (or nothing was scanned).
*/
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  SCREENSHOT_GROUPS,
  T4_DEFAULT_SCENARIOS,
  T4_EXPECTED_INCOMPLETE,
  T4_VARIANT_SCENARIOS
} from "./t11/scenarios.mjs";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const CATALOG_PATH =
  process.env.DOCS_CATALOG ??
  path.join(REPO_ROOT, "tooling~/scripts/t11/scenario-catalog.json");
const GUIDES_DIR =
  process.env.DOCS_GUIDES_DIR ?? path.join(REPO_ROOT, "Documentation~");
const SAMPLES_DIR =
  process.env.DOCS_SAMPLES_DIR ??
  path.join(REPO_ROOT, "Samples~/TerminalCommands");
const BASELINES_DIR =
  process.env.DOCS_BASELINES_DIR ??
  path.join(REPO_ROOT, "Tests/Runtime/Capture/Baselines~");
const API_DIR = process.env.DOCS_API_DIR ?? path.join(REPO_ROOT, "tooling~/docs/obj/api");

/** The exact scenario-name set the capture harness and T11 gate know. */
export const KNOWN_SCENARIOS = Object.freeze([
  ...T4_DEFAULT_SCENARIOS,
  ...T4_VARIANT_SCENARIOS,
  ...T4_EXPECTED_INCOMPLETE
]);

/** Loads and JSON-parses the catalog. Throws with a readable message on failure. */
export function loadCatalog(catalogPath = CATALOG_PATH) {
  return JSON.parse(fs.readFileSync(catalogPath, "utf8"));
}

/**
 * Schema + drift problems for one parsed catalog object. Checks shape (scenario
 * map, non-empty summaries, known group, negative entries not baselined) and
 * agreement with the capture harness registries (exact name-set match, group
 * matches the registry the name came from).
 */
export function catalogProblems(catalog) {
  if (catalog === null || typeof catalog !== "object" || Array.isArray(catalog)) {
    return ["catalog is not a JSON object"];
  }
  const scenarios = catalog.scenarios;
  if (scenarios === null || typeof scenarios !== "object" || Array.isArray(scenarios)) {
    return ["catalog carries no scenarios map"];
  }

  const problems = [];
  if (catalog.version !== 1) {
    problems.push(`unsupported catalog version ${JSON.stringify(catalog.version)}`);
  }
  const names = Object.keys(scenarios);
  if (names.length === 0) return ["catalog describes no scenarios"];

  const registryGroup = new Map();
  for (const name of T4_DEFAULT_SCENARIOS) registryGroup.set(name, "canon");
  for (const name of T4_VARIANT_SCENARIOS) registryGroup.set(name, "variant");
  for (const name of T4_EXPECTED_INCOMPLETE) registryGroup.set(name, "incomplete");
  const allowedGroups = new Map([
    ["canon", SCREENSHOT_GROUPS.filter((group) => group !== "environment-variant" && group !== "negative")],
    ["variant", ["environment-variant"]],
    ["incomplete", ["negative"]]
  ]);

  for (const name of KNOWN_SCENARIOS) {
    if (!Object.hasOwn(scenarios, name)) {
      problems.push(`scenario ${name} has no catalog entry`);
    }
  }
  for (const name of names) {
    const entry = scenarios[name];
    const origin = registryGroup.get(name);
    if (origin === undefined) {
      problems.push(`catalog names unknown scenario ${name}`);
      continue;
    }
    if (entry === null || typeof entry !== "object" || Array.isArray(entry)) {
      problems.push(`scenario ${name} entry is not an object`);
      continue;
    }
    if (typeof entry.summary !== "string" || entry.summary.trim().length === 0) {
      problems.push(`scenario ${name} needs a non-empty summary`);
    }
    if (!SCREENSHOT_GROUPS.includes(entry.group)) {
      problems.push(`scenario ${name} group ${JSON.stringify(entry.group)} is not one of ${SCREENSHOT_GROUPS.join(", ")}`);
    } else {
      const allowed = allowedGroups.get(origin);
      if (!allowed.includes(entry.group)) {
        problems.push(`scenario ${name} is a ${origin} scenario and cannot use group ${JSON.stringify(entry.group)}`);
      }
    }
    const baselined = entry.baselined !== false;
    if (origin === "incomplete" && baselined) {
      problems.push(`expected-incomplete scenario ${name} must declare baselined: false`);
    }
    if (origin !== "incomplete" && !baselined) {
      problems.push(`scenario ${name} must not declare baselined: false`);
    }
  }
  return problems;
}

/**
 * Staleness problems between one parsed catalog and the baseline store: every
 * store entry must be cataloged, every baselined catalog entry must have a
 * baseline somewhere, and the pinned environment must cover the full canon
 * (the docs screenshots page renders the pinned set). A missing store (no
 * environment directories) fails the scan-nothing guard.
 */
export function storeProblems(catalog, storeDir = BASELINES_DIR) {
  const scenarios = catalog?.scenarios ?? {};
  const environments = fs
    .readdirSync(storeDir, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name);
  if (environments.length === 0) {
    return [`no baseline environments under ${storeDir}; the store was not scanned`];
  }

  const problems = [];
  const baselined = new Set();
  const pinned = new Set();
  for (const env of environments) {
    const indexPath = path.join(storeDir, env, "index.json");
    let index;
    try {
      index = JSON.parse(fs.readFileSync(indexPath, "utf8"));
    } catch (error) {
      problems.push(`${env}/index.json is unreadable (${error.message})`);
      continue;
    }
    const isPinned = index?.environment?.pinned !== false;
    for (const name of Object.keys(index?.scenarios ?? {})) {
      baselined.add(name);
      if (isPinned) pinned.add(name);
      const entry = scenarios[name];
      if (entry === undefined) {
        problems.push(`baseline ${name} (${env}) is missing from the catalog`);
      } else if (entry.baselined === false) {
        problems.push(`baseline ${name} (${env}) is cataloged as never-baselined`);
      }
    }
  }

  for (const name of KNOWN_SCENARIOS) {
    const entry = scenarios[name];
    if (entry === undefined || entry.baselined === false) continue;
    if (!baselined.has(name)) problems.push(`cataloged scenario ${name} has no committed baseline`);
  }
  for (const name of T4_DEFAULT_SCENARIOS) {
    if (!pinned.has(name)) problems.push(`canon scenario ${name} has no baseline in a pinned environment`);
  }
  return problems;
}

/**
 * Collects the docfx API uids from the generated model directory (api/*.yml
 * `uid:` / `- uid:` lines). Returns null when no model exists yet so callers
 * can skip xref checking with a notice instead of failing.
 */
export function collectApiUids(apiDir = API_DIR) {
  if (!fs.existsSync(apiDir)) return null;
  const uids = new Set();
  for (const file of fs.readdirSync(apiDir)) {
    if (!file.endsWith(".yml")) continue;
    const text = fs.readFileSync(path.join(apiDir, file), "utf8");
    for (const match of text.matchAll(/^\s*-?\s*uid:\s*(\S+)/gm)) {
      uids.add(match[1]);
    }
  }
  return uids;
}

function lineOf(text, offset) {
  return text.slice(0, offset).split("\n").length;
}

/** Pattern for docfx snippet references: `[!code-csharp[Title](path)]`. */
const SNIPPET_PATTERN = /\[!code-\w+\[[^\]]*\]\(([^)]+)\)\]/g;
/** Pattern for ordinary markdown links (images included) after snippets are removed. */
const LINK_PATTERN = /!?\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g;
/** Pattern for reference-style link definitions: `[label]: target`. */
const REF_DEFINITION_PATTERN = /^\s*\[[^\]]+\]:\s*(\S+)\s*$/gm;

function xrefProblem(uid, at, apiUids) {
  if (apiUids === null || apiUids.has(uid)) return null;
  return `${at}: xref target '${uid}' is not in the generated API model`;
}

/**
 * Known limits, accepted because neither form appears in the guides: link text
 * with nested brackets (`[a [b] c](t.md)`) is not matched, and site-absolute
 * targets (`/api/Foo.yml`) are skipped unverified. Everything else - inline
 * links, images, reference definitions, snippets, xrefs - is checked.
 */
function resolveTarget(guideDir, target) {
  let decoded = target.split("#")[0];
  try {
    decoded = decodeURI(decoded);
  } catch {
    // A malformed escape cannot exist on disk either; resolve it verbatim.
  }
  return path.resolve(guideDir, decoded);
}

function checkTarget(target, at, guideDir, apiUids, problems) {
  if (/^(https?:|mailto:|#|\/)/i.test(target)) return;
  if (target.startsWith("xref:")) {
    const problem = xrefProblem(target.slice("xref:".length), at, apiUids);
    if (problem !== null) problems.push(problem);
    return;
  }
  const resolved = resolveTarget(guideDir, target);
  if (!fs.existsSync(resolved)) {
    problems.push(`${at}: broken link '${target}' (resolved ${path.relative(REPO_ROOT, resolved)})`);
  }
}

/**
 * Link problems for one guide's markdown text. `guideDir` resolves relative
 * links; `sampleDir` resolves `[!code-...]` snippet references (their `samples/`
 * prefix maps onto the shipped sample sources; a docfx line-anchor suffix like
 * `#L4-L10` is stripped before the existence check). Xref uids are checked only
 * when `apiUids` is a Set (null skips). Fenced code blocks are ignored.
 */
export function guideProblems(text, guideName, guideDir, sampleDir, apiUids) {
  const problems = [];
  const stripped = text.replace(/```[\s\S]*?```/g, (match) => match.replace(/[^\n]/g, " "));

  let withoutSnippets = stripped;
  for (const match of stripped.matchAll(SNIPPET_PATTERN)) {
    const sample = match[1].split("#")[0].replace(/^samples\//, "");
    const at = `Documentation~/${guideName}:${lineOf(text, match.index)}`;
    if (!fs.existsSync(path.join(sampleDir, sample))) {
      problems.push(`${at}: snippet reference '${match[1]}' does not match a shipped sample source`);
    }
    withoutSnippets = withoutSnippets.replace(match[0], (m) => m.replace(/[^\n]/g, " "));
  }

  for (const match of withoutSnippets.matchAll(LINK_PATTERN)) {
    checkTarget(
      match[1],
      `Documentation~/${guideName}:${lineOf(text, match.index)}`,
      guideDir,
      apiUids,
      problems
    );
  }

  for (const match of withoutSnippets.matchAll(REF_DEFINITION_PATTERN)) {
    checkTarget(
      match[1],
      `Documentation~/${guideName}:${lineOf(text, match.index)}`,
      guideDir,
      apiUids,
      problems
    );
  }

  for (const match of stripped.matchAll(/<xref:([^>]+)>/g)) {
    const problem = xrefProblem(
      match[1],
      `Documentation~/${guideName}:${lineOf(text, match.index)}`,
      apiUids
    );
    if (problem !== null) problems.push(problem);
  }
  return problems;
}

/** Convenience wrapper: runs guideProblems over every .md file in a directory tree. */
export function guidesProblems(guidesDir, sampleDir, apiUids) {
  const files = fs
    .readdirSync(guidesDir, { recursive: true })
    .filter((file) => file.endsWith(".md"));
  if (files.length === 0) {
    return [`no guide markdown found under ${guidesDir}; nothing was scanned`];
  }
  const problems = [];
  for (const file of files) {
    const text = fs.readFileSync(path.join(guidesDir, file), "utf8");
    problems.push(...guideProblems(text, file, guidesDir, sampleDir, apiUids));
  }
  return problems;
}

function main() {
  let catalog;
  try {
    catalog = loadCatalog();
  } catch (error) {
    console.error(`[docs-catalog] ERROR: catalog unreadable: ${error.message}`);
    return 1;
  }

  const problems = catalogProblems(catalog);

  let storeChecked = false;
  try {
    problems.push(...storeProblems(catalog));
    storeChecked = true;
  } catch (error) {
    problems.push(`baseline store scan failed: ${error.message}`);
  }

  let guidesChecked = false;
  try {
    const apiUids = collectApiUids();
    if (apiUids === null) {
      console.log("[docs-catalog] no docfx API model yet; skipping xref checks");
    }
    problems.push(...guidesProblems(GUIDES_DIR, SAMPLES_DIR, apiUids));
    guidesChecked = true;
  } catch (error) {
    problems.push(`guide link scan failed: ${error.message}`);
  }

  if (problems.length > 0) {
    for (const problem of problems) console.error(`[docs-catalog] ${problem}`);
    return 1;
  }
  console.log(
    `[docs-catalog] ${Object.keys(catalog.scenarios).length} scenario(s) cataloged, ` +
      `store${storeChecked ? "" : " NOT"} scanned, guides${guidesChecked ? "" : " NOT"} scanned`
  );
  return 0;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  process.exitCode = main();
}
