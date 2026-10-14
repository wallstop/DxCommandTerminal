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
        Set-Variable -Name LASTEXITCODE -Scope 2 -Value $runners[$Name]
    }
    else {
        Write-Output 'payload output'
        Set-Variable -Name LASTEXITCODE -Scope 2 -Value ${payloadExit}
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
