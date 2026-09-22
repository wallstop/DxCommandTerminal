/*
    Builds the documentation site (Unity-free):

    1. dotnet tool restore  - docfx is pinned in .config/dotnet-tools.json.
    2. dotnet restore refs.csproj - downloads the pinned UnityEngine
       reference assemblies into the NuGet global cache.
    3. Copies those DLLs into obj/unity-refs/ - docfx reference globs are
       relative to docfx.json and cannot use `../`, so the files must live
       inside the docfx project directory.
    4. docfx metadata - extracts the API model from Runtime sources against
       those references (no Unity editor involved).
    5. docfx build - renders guides (Documentation~) + API YAML into
       obj/_site, a searchable static site.

    Extraction limits, by design:

    - No defines are set (Unity projects define ENABLE_INPUT_SYSTEM etc.),
      so input-system-gated public APIs (TerminalPlayerInputController,
      InputHelpers members) are absent from the API reference. There is no
      Unity.InputSystem reference assembly on nuget to bind them against;
      enabling the define without the reference would only add errors.
    - allowCompilationErrors: true in docfx.json absorbs the resulting
      unresolved types, and docfx prints "0 errors" even when extraction
      degrades - so `REQUIRED_API_PAGES` below asserts the core public
      surface actually produced pages before the build may pass.

    Output: tooling~/docs/obj/_site. Preview with any static server.
    Stdlib only, cross-OS (GitHub CI runs this on ubuntu).
*/
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const docsDir = path.dirname(fileURLToPath(import.meta.url));
const objDir = path.join(docsDir, "obj");
const refsDir = path.join(objDir, "unity-refs");
const guidesDir = path.join(objDir, "guides");
const samplesDir = path.join(guidesDir, "samples");
const repoRoot = path.resolve(docsDir, "..", "..");
const GUIDES_SOURCE = path.join(repoRoot, "Documentation~");
const SAMPLES_SOURCE = path.join(repoRoot, "Samples~", "TerminalCommands");
const NUGET_PACKAGE = "unityengine.modules";
const NUGET_VERSION = "2021.3.33";
const NUGET_LIB = "netstandard2.0";

/*
    ManagedReference pages are one file per type, named exactly by uid. A
    missing page here means API extraction degraded silently (the failure
    mode allowCompilationErrors permits), so the build must fail.
*/
const REQUIRED_API_PAGES = [
  "WallstopStudios.DxCommandTerminal.Backend.Terminal",
  "WallstopStudios.DxCommandTerminal.Backend.CommandShell",
  "WallstopStudios.DxCommandTerminal.Backend.CommandArg",
  "WallstopStudios.DxCommandTerminal.Backend.CommandBuilder",
  "WallstopStudios.DxCommandTerminal.Backend.CommandInfo",
  "WallstopStudios.DxCommandTerminal.Backend.CommandAutoComplete",
  "WallstopStudios.DxCommandTerminal.Attributes.RegisterCommandAttribute",
  "WallstopStudios.DxCommandTerminal.UI.TerminalUI",
  "WallstopStudios.DxCommandTerminal.UI.CommandPaletteUI",
  "WallstopStudios.DxCommandTerminal.Themes.TerminalThemePack",
  "WallstopStudios.DxCommandTerminal.Themes.TerminalFontPack",
];

function run(command, args, options) {
  execFileSync(command, args, {
    stdio: options?.quiet ? "pipe" : "inherit",
    cwd: options?.cwd ?? docsDir,
    shell: false,
  });
}

function dotnetStdout(args) {
  return execFileSync("dotnet", args, { cwd: docsDir, encoding: "utf8", shell: false });
}

function globalPackagesDir() {
  const output = dotnetStdout(["nuget", "locals", "global-packages", "--list"]);
  const marker = "global-packages:";
  const line = output.split(/\r?\n/).find((entry) => entry.trim().startsWith(marker));
  if (line === undefined) {
    throw new Error(`could not parse the NuGet global-packages folder from:\n${output}`);
  }
  return line.slice(line.indexOf(marker) + marker.length).trim();
}

function copyReferenceDlls() {
  const sourceDir = path.join(
    globalPackagesDir(),
    NUGET_PACKAGE,
    NUGET_VERSION,
    "lib",
    NUGET_LIB
  );
  if (!fs.existsSync(sourceDir)) {
    throw new Error(`reference assemblies missing: ${sourceDir} (restore failed?)`);
  }
  fs.rmSync(refsDir, { recursive: true, force: true });
  fs.mkdirSync(refsDir, { recursive: true });
  const dlls = fs.readdirSync(sourceDir).filter((file) => file.endsWith(".dll"));
  for (const file of dlls) {
    fs.copyFileSync(path.join(sourceDir, file), path.join(refsDir, file));
  }
  return dlls.length;
}

/*
    The guides live in Documentation~ and docfx cannot read anything outside
    its project directory (no `../` in content, snippet, or reference
    paths), so the build stages every input inside obj/: guide markdown,
    the real sample sources ([!code-csharp] excerpts stay byte-identical to
    the shipped samples, so docs cannot drift), and the Unity reference
    assemblies the API extraction binds against.
*/
function copyGuides() {
  fs.rmSync(guidesDir, { recursive: true, force: true });
  fs.mkdirSync(guidesDir, { recursive: true });
  const files = fs.readdirSync(GUIDES_SOURCE).filter((file) => file.endsWith(".md"));
  for (const file of files) {
    fs.copyFileSync(path.join(GUIDES_SOURCE, file), path.join(guidesDir, file));
  }
  fs.copyFileSync(path.join(docsDir, "toc.yml"), path.join(guidesDir, "toc.yml"));
  return files.length;
}

function copySampleSources() {
  fs.mkdirSync(samplesDir, { recursive: true });
  const files = fs
    .readdirSync(SAMPLES_SOURCE)
    .filter((file) => file.endsWith(".cs"));
  for (const file of files) {
    fs.copyFileSync(path.join(SAMPLES_SOURCE, file), path.join(samplesDir, file));
  }
  return files.length;
}

run("dotnet", ["tool", "restore"], { cwd: repoRoot, quiet: false });
run("dotnet", ["restore", "refs.csproj"]);
const dllCount = copyReferenceDlls();
const guideCount = copyGuides();
const sampleCount = copySampleSources();
fs.rmSync(path.join(objDir, "api"), { recursive: true, force: true });
fs.rmSync(path.join(objDir, "_site"), { recursive: true, force: true });
run("dotnet", ["docfx", "metadata", "docfx.json"]);
const missingPages = REQUIRED_API_PAGES.filter(
  (uid) => !fs.existsSync(path.join(objDir, "api", `${uid}.yml`))
);
if (missingPages.length > 0) {
  throw new Error(
    `API extraction dropped ${missingPages.length} required page(s) ` +
      `(docfx source mode fails silently; see REQUIRED_API_PAGES):\n` +
      missingPages.join("\n")
  );
}
run("dotnet", ["docfx", "build", "docfx.json"]);

const siteDir = path.join(objDir, "_site");
const pages = fs.readdirSync(siteDir).filter((file) => file.endsWith(".html")).length;
console.log(
  `[docs] ok: ${dllCount} reference DLLs, ${guideCount} guides, ` +
    `${sampleCount} sample sources, site at ${path.relative(process.cwd(), siteDir)}` +
    ` (${pages} root pages)`
);
