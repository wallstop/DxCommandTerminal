---
name: plan-hygiene
description: Keep PLAN.md a lean living plan (status, open work, gates, constraints only) and route history to progress/ session logs. Use when editing PLAN.md, ending a session, archiving plan content, or when PLAN.md grows with session summaries, evidence, or completed items.
metadata:
  category: Core
---

# Plan Hygiene

`PLAN.md` is a living plan, not a log. It answers one question: what is left to do and
what still binds that work. Everything else has a home (see the document map).

## Document map (what lives where)

| Content | Home |
| --- | --- |
| In-progress + future work, open performance gates, binding constraints | `PLAN.md` |
| Work style, session scoping, fix philosophy, PR targets | `GOAL.md` |
| Standing code/repo rules and skills | `.llm/context.md`, `.llm/skills/` |
| Session history, RCA, measurements, evidence tallies | `progress/session-NNN-slug.md` (git-ignored) |
| Raw benchmark/capture artifacts | `.artifacts/` |

## PLAN.md contract

The file contains ONLY:

1. A dated status snapshot (a few lines: what landed, what is open, by issue/task).
2. Order of attack for remaining work.
3. Open work by task, each bullet requirement-bearing (a future session could execute it).
4. The performance gate table with per-gate status (MET/OPEN).
5. Binding constraints agreed with the owner.
6. References needed by open work only.

Never place in `PLAN.md`:

- Session narration or summaries ("Session-NNN landed ...") - that belongs in the
  session's `progress/` log, written once, never mirrored.
- Measurement numbers, test tallies, evidence trails (live in `progress/` records).
- Completed items. Delete them; do not merely tick the box. Landed tasks collapse into
  one status line.
- Stale grounding ("starting evidence", dependency schedules for finished work, links
  consumed by landed work).

## Session-end checklist

1. Write the session log: `progress/session-NNN-brief-description.md`.
2. Update `PLAN.md`: refresh the status date/snapshot, delete items this session
   completed entirely, and add new open items with their acceptance criteria.
3. Re-read the file top to bottom: if a line no longer changes what a future session
   would do, delete it.

## Size budget

- Soft target: ~200 lines. Hard cap: 250.
- Before adding material, cut: landed items, closed references, superseded constraints.
- Growth past the cap is a signal to restructure, not to compress prose.

## Structural cleanup (rare)

When the plan needs a rewrite (scope shift, post-milestone reset):

1. Copy the current file to `progress/plan-archive-YYYY-MM-DD-slug.md` first (lossless).
2. Rewrite; keep only items 1-6 of the contract above.
3. Record the archive path in the new file's header.

## Red flags (fix on sight)

- Any paragraph starting with a session number.
- A checkbox that has been checked for more than one session without being deleted.
- Evidence parentheticals growing inside task bullets.
- Gate rows without a status.
- The file only ever growing between sessions.
