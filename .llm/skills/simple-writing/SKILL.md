---
name: simple-writing
description: Write user-facing copy in Simplified Technical English (STE) - PR titles and descriptions, commit messages, review comments, code comments, issue text. Use when writing or editing any text a human will read in this repo.
metadata:
  category: Core
---

# Simple Writing (STE)

All human-readable text uses Simplified Technical English. Short. Direct. No fluff.

## Rules

1. One idea per sentence. Max ~20 words. Active voice. Present tense.
2. Common words. No idioms, hype ("powerful", "blazing"), or filler ("very", "in order
   to", "it should be noted").
3. Lists over prose. Links over restatement.
4. Write for a reader who knows Unity/C# but not this repo's history.
5. ASCII only. No em-dashes, smart quotes, or emoji.

## Structure by artifact

| Artifact | Structure |
| --- | --- |
| PR title | Imperative, <= 72 chars. "Add X", "Fix Y". |
| PR description | "Why" (1-3 lines), "How" (bullets), "What changed" (bullets). Omit a section that adds nothing. |
| Commit subject | Imperative, <= 72 chars, no trailing period. |
| Commit body | "Why" then "What changed". Wrap at ~72 chars. |
| Review comments | 1-3 sentences. Say what to change and why. |
| Code comments | Minimal. State only what the code cannot say (constraints, invariants, non-obvious why). No narration of the next line. |
| Issues | "Problem" then "Evidence" then "Fix". |

## Example

Bad:

```
This PR implements a comprehensive overhaul of the discovery subsystem, leveraging
cutting-edge source-generation technology to dramatically improve performance.
```

Good:

```
Discovery walked every type in every assembly. A source-generated catalog lets the
shell bind commands directly. Cold discovery drops from 2316 ms to 5.6 ms.
```
