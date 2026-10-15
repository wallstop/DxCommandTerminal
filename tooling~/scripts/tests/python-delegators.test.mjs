import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { execFile, spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import test from "node:test";

const cases = [];
function testCase(name, options, action) {
  cases.push(typeof options === "function" ? { name, options: {}, action: options } : { name, options, action });
}

function runProcess(command, arguments_, options) {
  return new Promise((resolve) => {
    execFile(command, arguments_, options, (error, stdout, stderr) => {
      resolve({ error: error && typeof error.code !== "number" ? error : null, status: error ? error.code : 0, stdout, stderr });
    });
  });
}

const scriptsRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const quote = (value) => `'${value.replaceAll("'", "''")}'`;
const delegates = [
  ["verify-analyzer-payload.ps1", "verify_analyzer_payload.py", ["-RepoRoot", "root with spaces", "-TwoCheckout"], ["--repo-root", "root with spaces", "--two-checkout"]],
  ["lint-llm-instructions.ps1", "lint_llm_instructions.py", ["-Fix", "-VerboseOutput", "-RepoRoot", "root with spaces"], ["--fix", "--verbose-output", "--repo-root", "root with spaces"]],
  ["generate-skills-index.ps1", "generate_skills_index.py", ["-VerboseOutput", "-OutputPath", "out with spaces", "-RepoRoot", "root with spaces"], ["--output-path", "out with spaces", "--verbose-output", "--repo-root", "root with spaces"]],
  ["lint-skill-sizes.ps1", "lint_skill_sizes.py", ["-VerboseOutput", "-FailOnCritical", "-RepoRoot", "root with spaces"], ["--verbose-output", "--fail-on-critical", "--repo-root", "root with spaces"]],
  ["lint-unity-meta.ps1", "lint_unity_meta.py", ["-VerboseOutput", "-RepoRoot", "root with spaces"], ["--verbose-output", "--repo-root", "root with spaces"]],
  ["tests/test-llm-instructions-lint.ps1", "test_lint_llm_instructions.py", ["-VerboseOutput"], ["-v"]],
  ["tests/test-generate-skills-index.ps1", "test_generate_skills_index.py", ["-VerboseOutput"], ["-v"]],
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
    testCase(`${delegate}: ${scenario}`, async () => {
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
    ConvertTo-Json -Compress -InputObject @{ name = $Name; arguments = @($Arguments); environment = $env:UV_PROJECT_ENVIRONMENT } | Add-Content -LiteralPath ${quote(callsPath)}
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
        const result = await runProcess("pwsh", ["-NoProfile", "-File", harnessPath], { encoding: "utf8", timeout: 20_000 });
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

testCase("instruction linter decodes UTF-8 with an inherited ASCII console", async () => {
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
    const result = await runProcess("pwsh", ["-NoProfile", "-File", harness], { encoding: "utf8", timeout: 20_000 });
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
    testCase(`${delegate}: real ${runner} fallback preserves native exit ${exitCode}`, async () => {
      const temp = fs.mkdtempSync(path.join(os.tmpdir(), "native-python-fallback-"));
      try {
        const copiedDelegate = path.join(temp, "tooling~", "scripts", delegate);
        fs.mkdirSync(path.dirname(copiedDelegate), { recursive: true });
        fs.copyFileSync(path.join(scriptsRoot, delegate), copiedDelegate);
        fs.copyFileSync(path.join(scriptsRoot, "python-runner.ps1"), path.join(temp, "tooling~", "scripts", "python-runner.ps1"));
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
        const result = await runProcess("pwsh", ["-NoProfile", "-File", harness], { encoding: "utf8", timeout: 20_000 });
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

for (const scope of ["host", "container"]) {
  for (const override of [null, "custom environment with spaces"]) {
    testCase(`uv environment: ${scope}, ${override ? "explicit override" : "platform default"}`, async () => {
      const temp = fs.mkdtempSync(path.join(os.tmpdir(), "uv-environment-"));
      try {
        const harness = path.join(temp, "environment.ps1");
        fs.writeFileSync(harness, `
$ErrorActionPreference = 'Stop'
. ${quote(path.join(scriptsRoot, "python-runner.ps1"))}
$env:UV_PROJECT_ENVIRONMENT = ${override ? quote(override) : "$null"}
$env:VIRTUAL_ENV = 'inherited environment'
function Get-Command { param($Name, $ErrorAction) if ($Name -eq 'uv') { return 'uv' } }
function Test-Path { param($LiteralPath) return $${scope === "container" ? "true" : "false"} }
function uv {
    if ($null -ne $env:VIRTUAL_ENV) { throw 'uv inherited another active environment' }
    [Console]::WriteLine($env:UV_PROJECT_ENVIRONMENT)
    $global:LASTEXITCODE = 23
}
$result = Invoke-ToolingPython -ToolingRoot ${quote(temp)} -PythonArguments @('payload.py') -ErrorPrefix 'test'
if ($result -ne 23) { throw 'Payload exit code changed' }
if ($env:VIRTUAL_ENV -cne 'inherited environment') { throw 'Caller active environment changed' }
if ($env:UV_PROJECT_ENVIRONMENT -cne ${override ? quote(override) : "$null"}) { throw 'Caller environment changed' }
`);
        const result = await runProcess("pwsh", ["-NoProfile", "-File", harness], { encoding: "utf8", timeout: 20_000 });
        assert.ifError(result.error);
        assert.equal(result.status, 0, result.stdout + result.stderr);
        const platform = process.platform === "win32" ? "windows" : process.platform === "darwin" ? "macos" : "linux";
        assert.equal(result.stdout.trim(), override ?? path.join(temp, `.venv-${scope}-${platform}-${os.arch()}`));
      } finally { fs.rmSync(temp, { recursive: true, force: true }); }
    });
  }
}

testCase("alternating host and container uv runs preserve both environments and raw output", { skip: spawnSync("uv", ["--version"]).status !== 0 ? "uv is not installed" : false }, async () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "uv-alternating-"));
  try {
    fs.writeFileSync(path.join(temp, "pyproject.toml"), '[project]\nname = "environment-test"\nversion = "0.0.0"\nrequires-python = ">=3.11"\n');
    const payload = path.join(temp, "payload.py");
    fs.writeFileSync(payload, 'import sys\nfrom pathlib import Path\np = Path(sys.prefix) / "sentinel"\np.touch(exist_ok=True)\nprint(str(p))\n');
    const harness = path.join(temp, "alternating.ps1");
    fs.writeFileSync(harness, `
$ErrorActionPreference = 'Stop'
. ${quote(path.join(scriptsRoot, "python-runner.ps1"))}
$env:UV_PROJECT_ENVIRONMENT = $null
$script:container = $false
function Test-Path { param($LiteralPath) return $script:container }
foreach ($scope in @($false, $true, $false, $true)) {
    $script:container = $scope
    $code = Invoke-ToolingPython -ToolingRoot ${quote(temp)} -PythonArguments @(${quote(payload)}) -ErrorPrefix 'test' -RawOutput
    if ($code -ne 0) { throw 'uv run failed' }
    if ($null -ne $env:UV_PROJECT_ENVIRONMENT) { throw 'Caller environment changed' }
}
`);
    const result = await runProcess("pwsh", ["-NoProfile", "-File", harness], { encoding: "utf8", timeout: 60_000, env: { ...process.env, UV_PYTHON_DOWNLOADS: "never" } });
    assert.ifError(result.error);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    const sentinels = result.stdout.trim().split(/\r?\n/);
    assert.equal(sentinels.length, 4, result.stdout);
    assert.equal(sentinels[0], sentinels[2]);
    assert.equal(sentinels[1], sentinels[3]);
    assert.notEqual(sentinels[0], sentinels[1]);
    for (const sentinel of sentinels.slice(0, 2)) assert.ok(fs.existsSync(sentinel), sentinel);
    assert.ok(!fs.existsSync(path.join(temp, ".venv")));
  } finally { fs.rmSync(temp, { recursive: true, force: true }); }
});

/* Every case owns its files and child environment; cap simultaneous PowerShell starts. */
test("Python delegators", { concurrency: 4 }, async (context) => {
  await Promise.all(cases.map(({ name, options, action }) => context.test(name, options, action)));
});
