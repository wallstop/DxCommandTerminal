---
name: simple-writing
description: Write user-facing copy in Simplified Technical English (STE), extremely short and to the point - a few sentences max for PRs, commits, issues, and comments, covering how (plus why/what for PRs). Use when writing or editing any text a human will read in this repo.
metadata:
  category: Core
---

# Simple Writing (STE)

All human-readable text uses Simplified Technical English. Extremely short. Direct.
No fluff, no verbosity. Default to fewer sentences than feel comfortable; cut
before adding. A reader should get the point in seconds.

## Rules

1. One idea per sentence. Max ~20 words. Active voice. Present tense.
2. Common words. No idioms, hype ("powerful", "blazing"), or filler ("very", "in order
   to", "it should be noted").
3. Lists over prose. Links over restatement. Never restate a diff in prose.
4. Write for a reader who knows Unity/C# but not this repo's history.
5. ASCII only. No em-dashes, smart quotes, or emoji.
6. Length budgets are hard limits. Cut before adding. When in doubt, delete the line.

## Structure by artifact

| Artifact | Structure | Budget |
| --- | --- | --- |
| PR title | Imperative, <= 72 chars. "Add X", "Fix Y". | 1 line |
| PR description | `**Why:**` 1-2 sentences. `**What:**` one-line bullets (3-6). Optional `**How we know:**` 1-3 plain evidence lines. Omit a section that adds nothing. | <= ~12 lines |
| Commit subject | Imperative, <= 72 chars, no trailing period. | 1 line |
| Commit body | `Why` 1-2 sentences, then `What changed` one-line bullets (2-5). Optional one-line `How we know`. Wrap at ~72 chars. | <= ~8 lines |
| Review comments | 1-3 sentences. Say what to change and why. | 3 lines |
| Code comments | Minimal. State only what the code cannot say (constraints, invariants, non-obvious why). No narration of the next line. Max ~4 lines per comment block. | 4 lines |
| Issues | `Problem`, `Evidence`, `Fix` - one short paragraph each. | short |

A few sentences is the ceiling, not the target. Two sentences that cover the
point beat six that cover it plus context. No nested sub-bullets, no per-file
tours, no process narration (review rounds, sub-agents, commits list), no
restated context already in linked issues or PLAN. Numbers only as evidence
lines. LLM-posted GitHub text keeps the `DISCLOSURE: LLM-GENERATED TEXT` first
line (see llm-attribution); it does not count against the budget.

## Enforcement (PRs)

PR copy is mechanically enforced, not just documented:

```sh
node tooling~/scripts/lint-pr-copy.mjs --title "Add X" --body-file body.md
```

Checks: disclosure first line, `**Why:**` 1-2 lines, `**What:**` 3-6 one-line
bullets, optional `**How we know:**` 1-3 lines, no other sections or preamble,
title <= 72 chars, body <= 16 content lines. The Cursor Bugbot summary block
(`<!-- CURSOR_SUMMARY -->` ... `<!-- /CURSOR_SUMMARY -->`) is stripped before
checking - the bot appends it and it is not authored copy. A PR titled
`release: vX.Y.Z` skips the body checks: release-prepare generates that
body as the machine-generated changelog excerpt. CI runs the same
check on every PR open/edit (`.github/workflows/pr-copy-lint.yml`). Run the
command before opening or editing any PR.

## Example

Bad:

```
This PR implements a comprehensive overhaul of the discovery subsystem, leveraging
cutting-edge source-generation technology to dramatically improve performance. The
generator emits one deterministic catalog per affected assembly, binds delegates for
accessible methods, falls back to cached exact-identity reflection for private
methods, and carries name inference, bounds, help, hints, and filters into the
generated descriptors, while the shell collects catalogs first and uses the
reflection walk as fallback through one shared registration loop.
```

Good:

```
Discovery walked every type in every assembly. A generated catalog binds commands
directly. Cold discovery drops from 2316 ms to 5.6 ms.

**What:**

- Ship a netstandard2.0 source generator emitting one catalog per assembly.
- Bind catalogs first; keep the reflection walk as fallback.
```
