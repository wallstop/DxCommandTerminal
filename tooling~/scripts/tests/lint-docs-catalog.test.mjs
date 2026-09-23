/*
    Contract tests for tooling~/scripts/lint-docs-catalog.mjs (issue #137).

    The gate keeps the screenshot scenario catalog truthful and the guides
    link-clean. The negative cases carry the weight: a capture scenario missing
    from the catalog, a catalog name the harness does not know, an expected-
    incomplete entry that claims a baseline, a store entry the catalog dropped,
    canon without a pinned-environment baseline, a dead guide link, a snippet
    reference to a sample that does not ship, and an xref to a uid the API model
    never produced. Positive cases pin the clean shapes and the api-model-absent
    skip.
*/
import test from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const linterPath = path.join(repoRoot, "scripts", "lint-docs-catalog.mjs");
const {
  catalogProblems,
  storeProblems,
  guideProblems,
  guidesProblems,
  collectApiUids,
  loadCatalog
} = await import(pathToFileURL(linterPath).href);

const tempDirs = [];
test.after(() => {
  for (const dir of tempDirs) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

function tempRoot(label) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), `dxt-docs-${label}-`));
  tempDirs.push(dir);
  return dir;
}

function write(root, relative, content) {
  const target = path.join(root, relative);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, content);
}

/** The shipped catalog: the linter must accept it verbatim. */
function shippedCatalog() {
  return loadCatalog(path.join(repoRoot, "scripts/t11/scenario-catalog.json"));
}

function scenarioStore(root, envName, pinned, scenarioNames) {
  const scenarios = {};
  for (const name of scenarioNames) {
    scenarios[name] = { png: `${name}.png` };
  }
  write(
    root,
    path.join("Baselines~", envName, "index.json"),
    JSON.stringify({
      version: 1,
      environment: {
        unityVersion: "6000.4.6f1",
        graphicsApi: "Metal",
        colorSpace: "Linear",
        resolution: { width: 397, height: 489 },
        logicalScale: 1,
        ...(pinned ? {} : { pinned: false })
      },
      scenarios
    })
  );
}

const CANON = [
  "CapturesTerminalSmallSurface",
  "CapturesTerminalFullSurfaceWithErrors",
  "CapturesCompletionHintsSurface",
  "CapturesScrollingLogSurface",
  "CapturesLongNameCompletionSurface",
  "CapturesCommandPaletteSurface",
  "CapturesPaletteEmptyResultsSurface",
  "CapturesPaletteLongDescriptionSurface",
  "CapturesLightThemeSurface",
  "CapturesDarkThemeSurface",
  "CapturesTerminalUIInspectorSurface",
  "CapturesThemePackInspectorSurface",
  "CapturesFontPackInspectorSurface"
];

test("the shipped catalog passes schema, drift, and store checks", () => {
  const catalog = shippedCatalog();
  assert.deepStrictEqual(catalogProblems(catalog), []);

  const store = tempRoot("shipped-store");
  scenarioStore(store, "pinned-env", true, [
    ...CANON,
    "CapturesAlternateFontTerminalSmall"
  ]);
  scenarioStore(store, "variant-envs", false, [
    "CapturesNarrowScreenTerminalSmall",
    "CapturesWideScreenPaletteLongHelp",
    "CapturesScaleTwoTerminalSmall",
    "CapturesTallScreenTerminalFull",
    "CapturesResizedViewportTerminalSmall"
  ]);
  assert.deepStrictEqual(storeProblems(catalog, path.join(store, "Baselines~")), []);
});

test("a scenario missing from the catalog and an unknown name both fail", () => {
  const catalog = shippedCatalog();
  delete catalog.scenarios.CapturesTerminalSmallSurface;
  catalog.scenarios.NotARealScenario = { group: "terminal", summary: "ghost" };
  const problems = catalogProblems(catalog);
  assert.ok(
    problems.some((problem) => problem.includes("CapturesTerminalSmallSurface has no catalog entry")),
    problems.join("; ")
  );
  assert.ok(
    problems.some((problem) => problem.includes("unknown scenario NotARealScenario")),
    problems.join("; ")
  );
});

test("an empty summary, bad group, and misplaced baseline flags fail", () => {
  const catalog = shippedCatalog();
  catalog.scenarios.CapturesTerminalSmallSurface.summary = "   ";
  catalog.scenarios.CapturesCommandPaletteSurface.group = "mystery";
  catalog.scenarios.BlankRenderFailsBounds.baselined = true;
  catalog.scenarios.CapturesTerminalFullSurfaceWithErrors.baselined = false;
  const problems = catalogProblems(catalog);
  assert.ok(problems.some((problem) => problem.includes("CapturesTerminalSmallSurface needs a non-empty summary")));
  assert.ok(problems.some((problem) => problem.includes("group \"mystery\"")));
  assert.ok(problems.some((problem) => problem.includes("BlankRenderFailsBounds must declare baselined: false")));
  assert.ok(problems.some((problem) => problem.includes("CapturesTerminalFullSurfaceWithErrors must not declare baselined: false")));
});

test("store entries the catalog dropped, and canon without a pinned baseline, fail", () => {
  const catalog = shippedCatalog();
  const store = tempRoot("drifted-store");
  scenarioStore(store, "pinned-env", true, CANON.slice(1));
  const drifted = storeProblems(catalog, path.join(store, "Baselines~"));
  assert.ok(
    drifted.some((problem) => problem.includes("CapturesTerminalSmallSurface has no baseline in a pinned environment")),
    drifted.join("; ")
  );

  const staleStore = tempRoot("stale-store");
  scenarioStore(staleStore, "pinned-env", true, CANON);
  scenarioStore(staleStore, "extra", false, ["CapturesRemovedScenario"]);
  const stale = storeProblems(catalog, path.join(staleStore, "Baselines~"));
  assert.ok(
    stale.some((problem) => problem.includes("CapturesRemovedScenario") && problem.includes("missing from the catalog")),
    stale.join("; ")
  );
});

test("an empty store fails the scan-nothing guard", () => {
  const store = tempRoot("empty-store");
  fs.mkdirSync(path.join(store, "Baselines~"), { recursive: true });
  const problems = storeProblems(shippedCatalog(), path.join(store, "Baselines~"));
  assert.strictEqual(problems.length, 1, problems.join("; "));
  assert.ok(problems[0].includes("no baseline environments"), problems[0]);
});

test("guide links resolve, samples map to shipped sources, xrefs check against the model", () => {
  const guides = tempRoot("guides");
  const samples = tempRoot("samples");
  fs.writeFileSync(path.join(samples, "Widget.cs"), "// sample\n");
  write(guides, "a.md", [
    "See [commands](commands.md).",
    "",
    "[!code-csharp[Widget](samples/Widget.cs)]",
    "",
    "[API](xref:Some.Api.Type)",
    "",
    "Inline <xref:Other.Api.Type> works too.",
    "",
    "[Missing](nowhere.md)",
    "[!code-csharp[Ghost](samples/Ghost.cs)]",
    "[Bad](xref:Never.Generated)",
    "[External](https://example.com) and [anchor](#section) are skipped.",
    "",
    "```md",
    "[not scanned](also-skipped.md)",
    "```"
  ].join("\n"));
  write(guides, "commands.md", "# ok\n");
  const apiUids = new Set(["Some.Api.Type", "Other.Api.Type"]);

  const problems = guidesProblems(guides, samples, apiUids);
  assert.strictEqual(problems.length, 3, problems.join("; "));
  assert.ok(problems.some((problem) => problem.includes("broken link 'nowhere.md'") && problem.includes("a.md:9")));
  assert.ok(problems.some((problem) => problem.includes("samples/Ghost.cs") && problem.includes("a.md:10")));
  assert.ok(problems.some((problem) => problem.includes("xref target 'Never.Generated'") && problem.includes("a.md:11")));

  assert.deepStrictEqual(guidesProblems(guides, samples, apiUids), problems);
});

test("a missing API model skips xref checks but still scans links", () => {
  const guides = tempRoot("guides-no-api");
  const samples = tempRoot("samples-no-api");
  write(guides, "a.md", "[API](xref:Never.Generated)\n[dead](missing.md)\n");
  const problems = guidesProblems(guides, samples, null);
  assert.strictEqual(problems.length, 1, problems.join("; "));
  assert.ok(problems[0].includes("broken link 'missing.md'"));
});

test("reference-style link definitions are checked like inline links", () => {
  const guides = tempRoot("guides-ref");
  const samples = tempRoot("samples-ref");
  write(guides, "a.md", [
    "See [commands][cmd] and [ghost][gone].",
    "",
    "[cmd]: commands.md",
    "[gone]: nowhere.md"
  ].join("\n"));
  write(guides, "commands.md", "# ok\n");
  const problems = guidesProblems(guides, samples, null);
  assert.strictEqual(problems.length, 1, problems.join("; "));
  assert.ok(problems[0].includes("broken link 'nowhere.md'") && problems[0].includes("a.md:4"));
});

test("docfx snippet line anchors are stripped before the sample check", () => {
  const guides = tempRoot("guides-anchor");
  const samples = tempRoot("samples-anchor");
  fs.writeFileSync(path.join(samples, "Widget.cs"), "// sample\n");
  write(guides, "a.md", "[!code-csharp[Widget](samples/Widget.cs#L4-L10)]");
  assert.deepStrictEqual(guidesProblems(guides, samples, null), []);
});

test("a malformed percent-escape is flagged, not thrown", () => {
  const guides = tempRoot("guides-pct");
  const samples = tempRoot("samples-pct");
  write(guides, "a.md", "[bad](100%.md)");
  const problems = guidesProblems(guides, samples, null);
  assert.strictEqual(problems.length, 1, problems.join("; "));
  assert.ok(problems[0].includes("broken link '100%.md'"));
});

test("collectApiUids parses uid lines and reports absence as null", () => {
  const api = tempRoot("api");
  write(api, "Some.Api.Type.yml", "### YamlMime:ManagedReference\nuid: Some.Api.Type\nitems:\n- uid: Some.Api.Type.Method\n");
  assert.deepStrictEqual([...collectApiUids(api)].sort(), [
    "Some.Api.Type",
    "Some.Api.Type.Method"
  ]);
  assert.strictEqual(collectApiUids(path.join(api, "absent")), null);
});
