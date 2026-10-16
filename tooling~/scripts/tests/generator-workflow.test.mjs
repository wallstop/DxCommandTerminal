import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const workflow = fs.readFileSync(fileURLToPath(new URL("../../../.github/workflows/generator-tests.yml", import.meta.url)), "utf8").replace(/\r\n/g, "\n");
function step(name) {
    const start = workflow.indexOf(`      - name: ${name}\n`);
    assert.notEqual(start, -1, `missing step: ${name}`);
    const end = workflow.indexOf("\n      - name:", start + 1);
    return workflow.slice(start, end === -1 ? undefined : end);
}
function runBlock(name) {
    const body = step(name);
    const run = body.slice(body.indexOf("        run: |\n") + "        run: |\n".length);
    return run
        .split("\n")
        .map((line) => (line.startsWith("          ") ? line.slice(10) : line))
        .join("\n");
}

for (const [name, command, log, expectedArgs] of [
    [
        "Run generator tests",
        "dotnet",
        "generator-tests.log",
        ["test", "Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators.Tests/WallstopStudios.DxCommandTerminal.SourceGenerators.Tests.csproj", "-c", "Release"],
    ],
    [
        "Test payload verifier with real compilers",
        "uv",
        "payload-verifier-tests.log",
        ["run", "--project", "tooling~", "python", "-m", "unittest", "discover", "-s", "tooling~/scripts/tests", "-p", "test_verify_analyzer_payload.py", "-v"],
    ],
    [
        "Verify shipped analyzer payload and two-checkout reproducibility",
        "uv",
        "payload-reproducibility.log",
        ["run", "--project", "tooling~", "python", "tooling~/scripts/verify_analyzer_payload.py", "--two-checkout"],
    ],
]) {
    for (const code of [0, 42]) {
        test(`${name}: actual Bash pipeline retains output and exit ${code}`, { skip: process.platform === "win32" }, () => {
            const root = fs.mkdtempSync(path.join(os.tmpdir(), "generator-workflow-"));
            try {
                const calls = path.join(root, "arguments");
                const stub = `${command}() { printf '%s\\0' "$@" > "$CALLS_PATH"; printf 'stub stdout\\n'; printf 'stub stderr\\n' >&2; return "$STUB_EXIT"; }\n`;
                const result = spawnSync("bash", ["--noprofile", "--norc", "-e", "-o", "pipefail", "-c", stub + runBlock(name)], {
                    cwd: root,
                    env: { ...process.env, GENERATOR_DIAGNOSTICS: root, CALLS_PATH: calls, STUB_EXIT: String(code) },
                    encoding: "utf8",
                });
                assert.ifError(result.error);
                assert.equal(result.status, code, result.stderr);
                const actualArgs = fs.readFileSync(calls, "utf8").split("\0").slice(0, -1);
                assert.deepEqual(actualArgs.slice(0, expectedArgs.length), expectedArgs);
                assert.ok(!actualArgs.includes("--filter"), "the actual command must remain unfiltered");
                if (command === "dotnet") {
                    assert.ok(actualArgs.includes("trx;LogFileName=generator-tests.trx"));
                    assert.ok(actualArgs.includes("/clp:PerformanceSummary"));
                    assert.equal(actualArgs[actualArgs.indexOf("--results-directory") + 1], root);
                } else {
                    assert.deepEqual(actualArgs, expectedArgs);
                }
                const retained = fs.readFileSync(path.join(root, log), "utf8");
                assert.match(retained, /stub stdout/);
                assert.match(retained, /stub stderr/);
            } finally {
                fs.rmSync(root, { recursive: true, force: true });
            }
        });
    }
}

test("generator diagnostics upload runs after earlier failures", () => {
    const upload = step("Upload generator diagnostics");
    assert.match(upload, /if: always\(\)/);
    assert.match(upload, /actions\/upload-artifact@[a-f0-9]{40}/);
    assert.match(upload, /path: \$\{\{ runner\.temp \}\}\/generator-test-diagnostics/);
    assert.match(step("Record runner diagnostics"), /if: always\(\)/);
});

for (const available of [false, true]) {
    test(`runtime configuration is honestly retained after build (available=${available})`, { skip: process.platform === "win32" }, () => {
        const root = fs.mkdtempSync(path.join(os.tmpdir(), "generator-workflow-"));
        try {
            const diagnostics = path.join(root, "diagnostics");
            const bytes = '{"runtimeOptions":{"configProperties":{"System.GC.Server":false}}}\n';
            if (available) {
                const source = path.join(root, "Generator~", "WallstopStudios.DxCommandTerminal.SourceGenerators.Tests", "bin", "Release", "net9.0");
                fs.mkdirSync(source, { recursive: true });
                fs.writeFileSync(path.join(source, "WallstopStudios.DxCommandTerminal.SourceGenerators.Tests.runtimeconfig.json"), bytes);
            }
            assert.match(step("Retain test runtime configuration"), /if: always\(\)/);
            const result = spawnSync("bash", ["--noprofile", "--norc", "-e", "-o", "pipefail", "-c", runBlock("Retain test runtime configuration")], {
                cwd: root,
                env: { ...process.env, GENERATOR_DIAGNOSTICS: diagnostics },
                encoding: "utf8",
            });
            assert.ifError(result.error);
            assert.equal(result.status, 0, result.stderr);
            assert.equal(fs.readFileSync(path.join(diagnostics, "runtimeconfig-status.txt"), "utf8"), `test_runtimeconfig=${available ? "retained" : "unavailable"}\n`);
            assert.equal(fs.existsSync(path.join(diagnostics, "test.runtimeconfig.json")), available);
            if (available) assert.equal(fs.readFileSync(path.join(diagnostics, "test.runtimeconfig.json"), "utf8"), bytes);
        } finally {
            fs.rmSync(root, { recursive: true, force: true });
        }
    });
}
