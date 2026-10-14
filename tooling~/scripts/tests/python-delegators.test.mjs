import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import test from "node:test";

const scriptsRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const quote = (value) => `'${value.replaceAll("'", "''")}'`;
const delegates = [
  ["lint-llm-instructions.ps1", "lint_llm_instructions.py", ["-Fix", "-VerboseOutput", "-RepoRoot", "root with spaces"], ["--fix", "--verbose-output", "--repo-root", "root with spaces"]],
  ["generate-skills-index.ps1", "generate_skills_index.py", ["-VerboseOutput", "-OutputPath", "out with spaces", "-RepoRoot", "root with spaces"], ["--output-path", "out with spaces", "--verbose-output", "--repo-root", "root with spaces"]],
  ["lint-skill-sizes.ps1", "lint_skill_sizes.py", ["-VerboseOutput", "-FailOnCritical", "-RepoRoot", "root with spaces"], ["--verbose-output", "--fail-on-critical", "--repo-root", "root with spaces"]],
  ["lint-unity-meta.ps1", "lint_unity_meta.py", ["-VerboseOutput", "-RepoRoot", "root with spaces"], ["--verbose-output", "--repo-root", "root with spaces"]],
  ["tests/test-lint-skill-sizes.ps1", "test_lint_skill_sizes.py", ["-VerboseOutput"], ["-v"]],
  ["tests/test-lint-unity-meta.ps1", "test_lint_unity_meta.py", ["-VerboseOutput"], ["-v"]]
];
const scenarios = [
  ["uv takes precedence and preserves payload failure", { uv: 0, python: 0, python3: 0 }, "uv", 23],
  ["working python fallback", { python: 0, python3: 0 }, "python", 0],
  ["Store alias exits 9009 before python3 fallback", { python: 9009, python3: 0 }, "python3", 23],
  ["old Python fails the version floor before python3 fallback", { python: 1, python3: 0 }, "python3", 0],
  ["unlaunchable python falls back to python3", { python: "throw", python3: 0 }, "python3", 0],
  ["only broken interpreters give actionable error", { python: 9009, python3: 1 }, null, 1],
  ["missing interpreters give actionable error", {}, null, 1]
];

for (const [delegate, payload, cliArgs, payloadArgs] of delegates) {
  for (const [scenario, runners, selected, payloadExit] of scenarios) {
    test(`${delegate}: ${scenario}`, () => {
      const temp = fs.mkdtempSync(path.join(os.tmpdir(), "python-delegators-"));
      try {
        const callsPath = path.join(temp, "calls.jsonl");
        const harnessPath = path.join(temp, "harness.ps1");
        fs.writeFileSync(harnessPath, `
$ErrorActionPreference = 'Stop'
$runners = ConvertFrom-Json ${quote(JSON.stringify(runners))} -AsHashtable
function Get-Command {
    param($Name, $ErrorAction)
    if ($runners.ContainsKey($Name)) { return $Name }
}
function Invoke-MockRunner {
    param($Name, $Arguments)
    ConvertTo-Json -Compress -InputObject @{ name = $Name; arguments = @($Arguments) } | Add-Content -LiteralPath ${quote(callsPath)}
    if ($Arguments[0] -eq '-c') {
        if ($runners[$Name] -eq 'throw') { throw 'Cannot launch interpreter' }
        Write-Output 'probe output must be suppressed'
        Set-Variable -Name LASTEXITCODE -Scope Global -Value $runners[$Name]
    }
    else {
        Write-Output 'payload output'
        Set-Variable -Name LASTEXITCODE -Scope Global -Value ${payloadExit}
    }
}
function uv { Invoke-MockRunner 'uv' $args }
function python { Invoke-MockRunner 'python' $args }
function python3 { Invoke-MockRunner 'python3' $args }
& ${quote(path.join(scriptsRoot, delegate))} ${cliArgs.map((value) => value.startsWith("-") ? value : quote(value)).join(" ")}
exit $LASTEXITCODE
`);
        const result = spawnSync("pwsh", ["-NoProfile", "-File", harnessPath], { encoding: "utf8", timeout: 20_000 });
        assert.ifError(result.error);
        assert.equal(result.status, payloadExit, result.stdout + result.stderr);
        assert.ok(!result.stdout.includes("probe output"), result.stdout);
        const calls = fs.existsSync(callsPath) ? fs.readFileSync(callsPath, "utf8").trim().split(/\r?\n/).map(JSON.parse) : [];
        const runs = calls.filter((call) => call.arguments[0] !== "-c");
        if (selected) {
          assert.equal(runs.length, 1, JSON.stringify(calls));
          assert.equal(runs[0].name, selected);
          const prefix = selected === "uv" ? ["run", "--project", path.dirname(scriptsRoot), "python"] : [];
          assert.deepEqual(runs[0].arguments, [...prefix, path.join(scriptsRoot, delegate.startsWith("tests/") ? "tests" : "", payload), ...payloadArgs]);
          assert.match(result.stdout, /payload output/);
          assert.ok(!calls.some((call) => call.name === "python3") || selected === "python3");
        }
        else {
          assert.equal(runs.length, 0);
          assert.match(result.stdout, /no working runner found.*Python 3\.11\+/);
        }
        for (const probe of calls.filter((call) => call.arguments[0] === "-c")) {
          assert.deepEqual(probe.arguments, ["-c", "import sys; sys.exit(0 if sys.version_info >= (3, 11) else 1)"]);
        }
      }
      finally {
        fs.rmSync(temp, { recursive: true, force: true });
      }
    });
  }
}

test("instruction linter decodes UTF-8 with an inherited ASCII console", () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "llm-native-encoding-"));
  try {
    fs.mkdirSync(path.join(temp, ".llm", "skills", "valid-skill"), { recursive: true });
    fs.mkdirSync(path.join(temp, ".github"));
    fs.writeFileSync(path.join(temp, ".llm", "skills", "valid-skill", "SKILL.md"), "---\nname: valid-skill\ndescription: Contains — punctuation.\n---\nBody.\n");
    fs.writeFileSync(path.join(temp, ".llm", "context.md"), "# Context\n[index](./skills/index.md)\n");
    for (const name of ["AGENTS.md", "CLAUDE.md", ".cursorrules", ".github/copilot-instructions.md"]) {
      fs.writeFileSync(path.join(temp, name), `[context](${name.startsWith(".github/") ? "../" : "./"}.llm/context.md)\n`);
    }
    const harness = path.join(temp, "encoding.ps1");
    fs.writeFileSync(harness, `
[Console]::OutputEncoding = [System.Text.Encoding]::ASCII
. ${quote(path.join(scriptsRoot, "lint-llm-instructions.ps1"))}
$output = & { $script:result = Invoke-LlmInstructionsLint -RepoRoot ${quote(temp)} -Fix } 6>&1
if ($script:result -ne 1) { throw 'Expected frontmatter failure' }
if ([Console]::OutputEncoding.CodePage -ne 20127) { throw 'Console encoding was not restored' }
[Console]::WriteLine([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($output -join "\n"))))
`);
    const result = spawnSync("pwsh", ["-NoProfile", "-File", harness], { encoding: "utf8", timeout: 20_000 });
    assert.ifError(result.error);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    const output = Buffer.from(result.stdout.trim(), "base64").toString("utf8");
    assert.match(output, /character\(s\) \[—\]/u);
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});

for (const [delegate, payload] of delegates) {
  for (const [runner, exitCode] of [["python", 0], ["python3", 23]]) {
    test(`${delegate}: real ${runner} fallback preserves native exit ${exitCode}`, () => {
      const temp = fs.mkdtempSync(path.join(os.tmpdir(), "native-python-fallback-"));
      try {
        const copiedDelegate = path.join(temp, "tooling~", "scripts", delegate);
        fs.mkdirSync(path.dirname(copiedDelegate), { recursive: true });
        fs.copyFileSync(path.join(scriptsRoot, delegate), copiedDelegate);
        const copiedPayload = path.join(path.dirname(copiedDelegate), payload);
        fs.writeFileSync(copiedPayload, `import sys\nprint('real native payload')\nsys.exit(${exitCode})\n`);
        const harness = path.join(temp, "native.ps1");
        fs.writeFileSync(harness, `
$ErrorActionPreference = 'Stop'
$nativePython = foreach ($name in @('python', 'python3')) {
    $command = Microsoft.PowerShell.Core\\Get-Command $name -CommandType Application -ErrorAction SilentlyContinue
    if ($command) { $command[0].Source; break }
}
if (-not $nativePython) { throw 'Python 3.11+ test prerequisite missing' }
Set-Alias -Name ${runner} -Value $nativePython
function Get-Command {
    param($Name, $ErrorAction, $CommandType)
    if ($Name -ne '${runner}') { return $null }
    Microsoft.PowerShell.Core\\Get-Command @PSBoundParameters
}
& ${quote(copiedDelegate)}
exit $global:LASTEXITCODE
`);
        const result = spawnSync("pwsh", ["-NoProfile", "-File", harness], { encoding: "utf8", timeout: 20_000 });
        assert.ifError(result.error);
        assert.equal(result.status, exitCode, result.stdout + result.stderr);
        assert.match(result.stdout, /real native payload/u);
        assert.doesNotMatch(result.stdout, /no working runner/u);
      } finally {
        fs.rmSync(temp, { recursive: true, force: true });
      }
    });
  }
}
