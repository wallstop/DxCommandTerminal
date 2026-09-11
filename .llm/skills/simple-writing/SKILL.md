---
name: simple-writing
description: Write user-facing copy in Simplified Technical English (STE) with the Why/What changed/How we know structure and hard length budgets for PRs, commits, issues, and comments. Use when writing or editing any text a human will read in this repo.
metadata:
  category: Core
---

# Simple Writing (STE)

All human-readable text uses Simplified Technical English. Short. Direct. No fluff.
Reviewers are volume-limited; every line must earn its place.

## Rules

1. One idea per sentence. Max ~20 words. Active voice. Present tense.
2. Common words. No idioms, hype ("powerful", "blazing"), or filler ("very", "in order
   to", "it should be noted").
3. Lists over prose. Links over restatement. Never restate a diff in prose.
4. Write for a reader who knows Unity/C# but not this repo's history.
5. ASCII only. No em-dashes, smart quotes, or emoji.
6. Length budgets are hard limits. Cut before adding.

## Structure by artifact

| Artifact | Structure | Budget |
| --- | --- | --- |
| PR title | Imperative, <= 72 chars. "Add X", "Fix Y". | 1 line |
| PR description | `**Why:**` (1-2 sentences), `**What:**` (one-line bullets), optional `**How we know:**` (counts, one line each). Omit a section that adds nothing. | <= ~20 lines |
| Commit subject | Imperative, <= 72 chars, no trailing period. | 1 line |
| Commit body | `Why` then `What changed`; one-line bullets; optional `How we know`. Wrap at ~72 chars. | <= ~12 lines |
| Review comments | 1-3 sentences. Say what to change and why. | 3 lines |
| Code comments | Minimal. State only what the code cannot say (constraints, invariants, non-obvious why). No narration of the next line. Max ~4 lines per comment block. | 4 lines |
| Issues | `Problem` then `Evidence` then `Fix`. | short |

The structure matches the reference repos (Ambiguous-Interactive/unity-helpers,
Ambiguous-Interactive/DxMessaging): a 1-2 sentence `Why`, a terse `What` bullet
list, and a `How we know` section of plain evidence lines ("All 1,300 script
tests pass."). No nested sub-bullets, no per-file tours, no restated context
already in the linked issues or PLAN.

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
