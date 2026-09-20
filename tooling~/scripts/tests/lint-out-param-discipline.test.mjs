/*
    Contract tests for tooling~/scripts/lint-out-param-discipline.mjs.

    The rule (context.md rule 13, issue #119): an `out` parameter assigned a
    second time on some path that can follow the first assignment means the
    first value is a blanket fallback -- the shape that silences the compiler's
    definite-assignment bugcheck (a path that forgets its real assignment
    compiles clean and returns the fallback). The negative cases carry the
    weight: per-path arm assignments joining a shared return, retries inside
    loops, try/catch per-path assignment, switch fall-through, expression-bodied
    members, discards, and call-site `out var` must all stay silent, or a sweep
    drowns real blanket fallbacks in noise. The positive cases pin every
    redundancy shape the PR #118 review banned.
*/
import test from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath, pathToFileURL } from "node:url";

const repoRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../.."
);
const linterPath = path.join(repoRoot, "scripts", "lint-out-param-discipline.mjs");
const { analyzeSource, outParameters } = await import(
  pathToFileURL(linterPath).href
);
const { tokenize } = await import(
  pathToFileURL(path.join(repoRoot, "scripts", "lint-comparison-direction.mjs")).href
);

function violationsIn(source) {
  return analyzeSource(source, "test.cs");
}

/** Shapes that obey rule 13 and must stay silent. */
const EXEMPT = [
  [
    "per-path arm assignments joining a shared return (TryEatArgument shape)",
    `bool TryEat(string s, out int arg) {
        if (s.Length == 0) {
            arg = 0;
            return false;
        }
        if (s[0] == '"') {
            arg = Parse(s);
        } else {
            arg = Parse(s);
        }
        return true;
    }`
  ],

  [
    "a straight-line assignment before the return",
    "bool TryOne(out int v) { v = Compute(); return 0 < v; }"
  ],

  [
    "a tail run of out-param assignments before the return (TrySerializeValue shape)",
    `bool TryTail(out string insertion, out bool quoted) {
        insertion = Wrap(value);
        quoted = false;
        return !wrapped;
    }`
  ],

  [
    "an assignment used by plain statements before the return (TryWriteManifestFile tail)",
    `bool TryWrite(out string path) {
        string name = Unique();
        path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return true;
    }`
  ],

  [
    "a retry loop whose out parameter is only assigned at the call site",
    `bool TryRetry(out Lease lease) {
        for (int attempt = 0; attempt < 3; attempt++) {
            if (pool.TryClaim(out lease)) {
                return true;
            }
        }
        lease = null;
        return false;
    }`
  ],

  [
    "assignments inside a loop body",
    `int FirstEven(List<int> items, out int found) {
        foreach (int item in items) {
            found = item;
            if (item % 2 == 0) {
                return item;
            }
        }
        found = 0;
        return -1;
    }`
  ],

  [
    "per-path try/catch assignment",
    `bool TryLoad(out string text) {
        try {
            text = File.ReadAllText(p);
            return true;
        } catch (Exception e) {
            Log(e.Message);
            text = null;
            return false;
        }
    }`
  ],

  [
    "switch sections: assignment then break, and fall-through into the next section",
    `bool TryMode(int mode, out int value) {
        switch (mode) {
            case Mode.A:
                value = 1;
                break;
            case Mode.B:
                value = 2;
                if (!ready) {
                    return false;
                }
                break;
            default:
                value = 0;
                break;
        }
        return true;
    }`
  ],

  [
    "an expression-bodied member",
    "bool TryParse(string s, out int v) => int.TryParse(s, out v);"
  ],

  [
    "discarded out arguments",
    "int Count(object o) { return o is Foo { Bar: int _ } ? 1 : 0; }"
  ],

  [
    "call-site `out var` is an argument, not a parameter",
    `void Caller() {
        if (map.TryGetValue(key, out var value)) {
            Use(value);
        }
    }`
  ],

  [
    "a delegate declaration has no body",
    "delegate bool TryParser(string input, out int value);"
  ],

  [
    "attributes on the out parameter",
    "bool TryRead([Out] out int value) { value = Read(); return 0 < value; }"
  ],

  [
    "a local function with its own out params is independent of the enclosing one",
    `bool Outer(out int v) {
        v = Compute();
        return Inner(out int inner, v);
        bool Inner(out int inner, int seed) {
            inner = seed + 1;
            return true;
        }
    }`
  ],

  [
    "an else-if chain where every arm assigns before the shared return",
    `bool TryMode(bool a, bool b, out int v) {
        if (a) {
            v = 1;
        } else if (b) {
            v = 2;
        } else {
            v = 3;
        }
        return 0 < v;
    }`
  ],

  [
    "a dangling if arm whose call assigns the out parameter",
    `bool TryHit(bool ready, out int v) {
        if (ready) {
            return TryHitInner(out v);
        }
        v = 0;
        return false;
    }`
  ]
];

/** Shapes whose first assignments are blanket fallbacks; every listed line is flagged. */
const VIOLATIONS = [
  [
    "blanket entry assignment with per-path re-assignment (the PR #118 TryWriteManifestFile shape)",
    `bool TryWriteManifest(out string path) {
        path = null;
        if (string.IsNullOrWhiteSpace(manifest)) {
            path = null;
            return false;
        }
        path = Build();
        return true;
    }`,
    [2]
  ],

  [
    "entry null-out with a re-assignment on the success path (AddCommand shape)",
    `bool Add(CommandBuilder builder, out Handle handle) {
        handle = null;
        if (!Register(builder)) {
            return false;
        }
        handle = new Handle(builder.Name);
        return true;
    }`,
    [2]
  ],

  [
    "early defaults re-assigned inside a conditional (tokenizer shape; both params of the pair)",
    `bool TryPrepare(out int start, out int length) {
        start = 0;
        length = 0;
        if (quoted) {
            start = 1;
            length = 2;
        }
        return done;
    }`,
    [2, 3]
  ],

  [
    "an entry default re-assigned inside a try arm (TryComplete shape)",
    `bool TryComplete(out Context context) {
        context = default;
        try {
            if (empty) {
                context = default;
                return false;
            }
            context = Build();
            return true;
        } finally {
            depth--;
        }
    }`,
    [2]
  ],

  [
    "an entry null re-assigned inside a loop (GetEnclosingObject shape)",
    `object Walk(out FieldInfo fieldInfo) {
        fieldInfo = null;
        for (int i = 0; i < parts.Length - 1; i++) {
            fieldInfo = GetField(parts[i]);
            if (fieldInfo == null) {
                return null;
            }
        }
        return obj;
    }`,
    [2]
  ],

  [
    "compound assignment operators count: `??=` then a later `=`",
    `bool TryValue(out string value) {
        value ??= fallback;
        if (ready) {
            value = Load();
        }
        return ready;
    }`,
    [2]
  ],

  [
    "a block that ends walking outward to a later re-assignment",
    `bool TryJoin(bool flag, out int v) {
        if (flag) {
            v = 1;
        }
        v = 2;
        return true;
    }`,
    [3]
  ],

  [
    "an implicit lambda out parameter re-assigned",
    `var handler = (string s, out int v) => {
        v = 0;
        if (s.Length == 0) {
            return false;
        }
        v = Parse(s);
        return true;
    };`,
    [2]
  ],

  [
    "a generic-typed out parameter (depth-zero comma split)",
    `bool TryMap(out Dictionary<string, int> map) {
        map = new Dictionary<string, int>();
        if (ready) {
            map = Build();
            return true;
        }
        return false;
    }`,
    [2]
  ],

  [
    "a tuple-typed out parameter",
    `bool TryPair(out (int A, int B) pair) {
        pair = (0, 0);
        if (flag) {
            pair = (1, 1);
            return true;
        }
        return false;
    }`,
    [2]
  ],

  [
    "a re-assignment behind nested unbraced ifs",
    `bool TryDangling(bool a, bool b, out int v) {
        v = 0;
        if (a) if (b) v = 2;
        return true;
    }`,
    [2]
  ]
];

test("rule-obeying shapes stay silent", () => {
  for (const [name, source] of EXEMPT) {
    assert.deepStrictEqual(violationsIn(source), [], name);
  }
});

test("blanket fallback shapes are caught on the fallback lines", () => {
  for (const [name, source, expectedLines] of VIOLATIONS) {
    const violations = violationsIn(source);
    assert.deepStrictEqual(
      violations.map((violation) => violation.line),
      expectedLines,
      name
    );
    for (const violation of violations) {
      assert.ok(violation.message.includes("re-assigned"), name);
    }
  }
});

test("the reported line is the first (fallback) assignment", () => {
  const source = [
    "bool TrySample(out int value)",
    "{",
    "    value = 0;",
    "    if (ready)",
    "    {",
    "        value = 1;",
    "        return true;",
    "    }",
    "    return false;",
    "}"
  ].join("\n");
  const violations = violationsIn(source);
  assert.strictEqual(violations.length, 1);
  assert.strictEqual(violations[0].line, 3);
});

test("a chain flags every fallback link", () => {
  const source = [
    "bool TryChain(out int v)",
    "{",
    "    v = 0;",
    "    if (a)",
    "    {",
    "        v = 1;",
    "    }",
    "    v = 2;",
    "    return true;",
    "}"
  ].join("\n");
  assert.deepStrictEqual(
    violationsIn(source).map((violation) => violation.line),
    [3, 6]
  );
});

test("an unparseable parameter list is a violation, not a silent skip", () => {
  const violations = violationsIn("bool T(out ) { v = 0; v = 1; return true; }");
  assert.strictEqual(violations.length, 1);
  assert.ok(violations[0].message.includes("could not be parsed"));
});

test("a body too deeply nested to walk is reported, never a crash", () => {
  let source = "class D { bool T(out int v) { v = 0; ";
  for (let i = 0; i < 20000; i += 1) {
    source += "if (a) { ";
  }
  source += "v = 1; ";
  for (let i = 0; i < 20000; i += 1) {
    source += "} ";
  }
  source += "return true; } }";
  const violations = violationsIn(source);
  assert.strictEqual(violations.length, 1);
  assert.ok(violations[0].message.includes("statement walk"));
});

test("outParameters reads typed, var, implicit, and attribute forms; skips discards", () => {
  const read = (signature) => {
    const tokens = tokenize(signature);
    const open = tokens.findIndex((token) => token.text === "(");
    return outParameters(tokens, open, tokens.length);
  };
  assert.deepStrictEqual(read("bool T(out int v)"), ["v"]);
  assert.deepStrictEqual(read("bool T(out var v)"), ["v"]);
  assert.deepStrictEqual(read("bool T(int a, out string text, bool flag)"), ["text"]);
  assert.deepStrictEqual(read("(string s, out int v) =>"), ["v"]);
  assert.deepStrictEqual(read("(string s, out v) =>"), ["v"]);
  assert.deepStrictEqual(read("bool T([Out] out int value)"), ["value"]);
  assert.deepStrictEqual(read("bool T(out int _)"), []);
  assert.deepStrictEqual(read("bool T(int a)"), []);
});

function runLinter(fixtureRoot) {
  return spawnSync(process.execPath, [linterPath], {
    cwd: repoRoot,
    env: { ...process.env, OUT_PARAM_DISCIPLINE_ROOTS: fixtureRoot },
    encoding: "utf8"
  });
}

test("a fixture tree with a blanket fallback fails with a report", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "out-param-"));
  fs.mkdirSync(path.join(fixtureRoot, "Code"), { recursive: true });
  fs.writeFileSync(
    path.join(fixtureRoot, "Code", "Bad.cs"),
    "sealed class C\n{\n    bool Try(out string path)\n    {\n        path = null;\n        if (blank)\n        {\n            path = null;\n            return false;\n        }\n        path = Build();\n        return true;\n    }\n}\n"
  );
  try {
    const result = runLinter(fixtureRoot);
    assert.strictEqual(result.status, 1, result.stderr);
    assert.ok(result.stderr.includes("Bad.cs:5"), result.stderr);
    assert.ok(result.stdout.includes("1 file(s) scanned"), result.stdout);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});

test("a fixture tree of rule-obeying code passes", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "out-param-"));
  fs.mkdirSync(path.join(fixtureRoot, "Code"), { recursive: true });
  fs.writeFileSync(
    path.join(fixtureRoot, "Code", "Clean.cs"),
    "sealed class C\n{\n    bool Try(out string path)\n    {\n        if (blank)\n        {\n            path = null;\n            return false;\n        }\n        path = Build();\n        return true;\n    }\n}\n"
  );
  try {
    const result = runLinter(fixtureRoot);
    assert.strictEqual(result.status, 0, result.stderr);
    assert.ok(result.stdout.includes("1 file(s) scanned"), result.stdout);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});

test("a missing root fails loudly instead of scanning nothing", () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "out-param-"));
  try {
    const result = runLinter(fixtureRoot);
    assert.strictEqual(result.status, 1, result.stderr);
    assert.ok(result.stderr.includes("checked nothing"), result.stderr);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});
