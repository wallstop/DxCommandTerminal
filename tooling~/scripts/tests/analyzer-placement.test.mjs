/*
    Analyzer placement contract: the wire that makes Unity load the analyzer
    payload. Unity scopes a folder-resident analyzer by its nearest enclosing
    .asmdef: under an all-platforms runtime asmdef it applies to that
    assembly AND every assembly referencing it (including predefined
    Assembly-CSharp); under an editor-only asmdef it reaches no consumer
    runtime code at all, and consumer commands would fail with missing-type
    errors. The lesson is DxMessaging issue #229, adapted here with
    attribution; the shape checks mirror unity-helpers'
    test-analyzer-placement.
*/
import test from 'node:test';
import assert from 'node:assert';
import path from 'path';
import fs from 'fs';
import url from 'url';

const scriptDirectory = path.dirname(url.fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDirectory, '../../..');

const PAYLOAD_NAME = 'WallstopStudios.DxCommandTerminal.SourceGenerators.dll';
const PAYLOAD_PATH = path.join(repoRoot, 'Runtime', 'Analyzers', PAYLOAD_NAME);

function readJson(filePath) {
    return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

/*
    Walks up from a file to the nearest enclosing .asmdef, or null when the
    file is governed by the predefined assemblies.
 */
function governingAsmdef(filePath) {
    let directory = path.dirname(filePath);
    while (directory.startsWith(repoRoot)) {
        const entry = fs
            .readdirSync(directory)
            .find((name) => name.toLowerCase().endsWith('.asmdef'));
        if (entry) {
            return path.join(directory, entry);
        }

        const parent = path.dirname(directory);
        if (parent === directory) {
            return null;
        }

        directory = parent;
    }

    return null;
}

function isEditorOnly(asmdefPath) {
    const definition = readJson(asmdefPath);
    const platforms = definition.includePlatforms || [];
    return platforms.length === 1 && platforms[0] === 'Editor';
}

test('analyzer placement: payload + meta + label', () => {
    const metaPath = PAYLOAD_PATH + '.meta';

    assert.ok(
        fs.existsSync(PAYLOAD_PATH),
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
        fs.statSync(PAYLOAD_PATH).size > 0,
        'analyzer payload must not be empty'
    );
});

test('analyzer placement: governed by the runtime asmdef, not an editor-only one', () => {
    const asmdef = governingAsmdef(PAYLOAD_PATH);
    assert.ok(asmdef, 'analyzer payload must live under an .asmdef-governed folder');
    assert.equal(
        path.basename(asmdef),
        'WallstopStudios.DxCommandTerminal.asmdef',
        'analyzer payload must be governed by the runtime asmdef so it reaches ' +
            'every assembly that references it'
    );
    assert.equal(
        isEditorOnly(asmdef),
        false,
        'an editor-only asmdef would scope the analyzer away from all consumer ' +
            'runtime code (DxMessaging issue #229)'
    );
});

test('analyzer placement: no second copy anywhere Unity imports', () => {
    // A stray copy under a folder without its labeled .meta would shadow the
    // shipped payload: Unity would load it with an auto-generated meta that
    // carries no RoslynAnalyzer label, and generation would silently stop.
    const strays = [];
    (function walk(directory) {
        for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
            if (
                entry.name === 'node_modules' ||
                entry.name.startsWith('.') ||
                entry.name.endsWith('~') ||
                entry.name === 'Library' ||
                entry.name === 'Temp' ||
                entry.name === 'obj' ||
                entry.name === 'bin'
            ) {
                continue;
            }

            const full = path.join(directory, entry.name);
            if (entry.isDirectory()) {
                walk(full);
            } else if (entry.name === PAYLOAD_NAME && full !== PAYLOAD_PATH) {
                strays.push(path.relative(repoRoot, full));
            }
        }
    })(repoRoot);

    assert.deepEqual(strays, [], 'found stray analyzer copies outside Runtime/Analyzers');
});
