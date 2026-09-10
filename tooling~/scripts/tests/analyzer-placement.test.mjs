/*
    Analyzer placement contract: the wire that makes Unity load the analyzer
    payload. Asserts the exact file shape Unity requires:
    <AssemblyName>.dll + a sibling PluginImporter .meta declaring the
    RoslynAnalyzer label under Runtime/Analyzers (the assembly root folder in
    the unity-helpers' analyzer contract).

    Inspired by the unity-helpers' test-analyzer-placement test.
*/
import test from 'node:test';
import assert from 'node:assert';
import path from 'path';
import fs from 'fs';
import url from 'url';

const scriptDirectory = path.dirname(url.fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDirectory, '../../..');

test('analyzer placement: payload + meta + label', () => {
    const payloadPath = path.join(
        repoRoot,
        'Runtime',
        'Analyzers',
        'WallstopStudios.DxCommandTerminal.SourceGenerators.dll'
    );
    const metaPath = payloadPath + '.meta';

    assert.ok(
        fs.existsSync(payloadPath),
        'analyzer payload missing from Runtime/Analyzers'
    );
    assert.ok(
        fs.existsSync(metaPath),
        'analyzer payload .meta missing from Runtime/Analyzers'
    );

    const meta = fs.readFileSync(metaPath, 'utf8');
    assert.ok(
        /PluginImporter:/.test(meta),
        'analyzer payload .meta must use a PluginImporter block'
    );
    assert.ok(
        /- RoslynAnalyzer/.test(meta),
        'analyzer payload .meta must declare the RoslynAnalyzer label'
    );
    const guid = /guid:\s*([0-9a-f]{32})/.exec(meta);
    assert.ok(guid, 'analyzer payload .meta must declare a 32-hex guid');
    assert.ok(
        fs.statSync(payloadPath).size > 0,
        'analyzer payload must not be empty'
    );
});
