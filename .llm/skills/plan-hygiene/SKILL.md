---
name: plan-hygiene
description: Audit and keep PLAN.md lean: executable open tasks, blockers, and acceptance criteria only. Move standing contracts to context or skills and completed work to progress/ session logs. Use when editing PLAN.md, ending a session, or removing plan bloat.
metadata:
  category: Core
---

# Plan Hygiene

`PLAN.md` answers: what remains, what blocks it, and how will we know it is done? Every line must change the next action or its acceptance. Link supporting knowledge once.

## Content ownership

| Content                                                             | Home                              |
| ------------------------------------------------------------------- | --------------------------------- |
| Ordered open tasks, blockers, dependencies, acceptance criteria     | `PLAN.md`                         |
| Session scope and work style                                        | `GOAL.md`                         |
| Standing repository rules                                           | `.llm/context.md`                 |
| Modernization constraints, target definitions, execution references | `.llm/modernization.md`           |
| Reusable procedures                                                 | Existing `.llm/skills/*/SKILL.md` |
| Completed work, gate results, measurements, diagnosis, audit trail  | `progress/session-NNN-slug.md`    |
| Raw captures and benchmarks                                         | `.artifacts/`                     |

`PLAN.md`, `GOAL.md`, `progress/`, and their Unity metas are local-only and gitignored. Never force-add, package, or publish them. Keep durable agent knowledge in tracked `.llm/`
files.

## Plan contract

- Keep one date, one ordered task list, and links needed to execute it.
- Each task states the remaining action and acceptance; include a blocker only when it changes execution.
- Preserve issue/task IDs and unresolved requirements. Merge duplicate actions; do not silently cancel scope.
- Keep open gates with their owning tasks. Put target definitions in context and measured outcomes in session logs.
- Delete completed tasks immediately. Do not keep checked boxes, MET rows, landed PRs, or session summaries.
- Omit standing constraints, measurement methods, command catalogs, archival headers, and repeated status/order sections.
- Do not copy an issue, runbook, or skill into the plan. Link the authoritative source.

## Audit and session close

1. Read the whole plan and relevant session evidence. Do not infer completion from a partial pass.
2. Before a structural rewrite, copy the plan byte-for-byte to `progress/plan-archive-YYYY-MM-DD-slug.md`.
3. Map each old requirement to an open task, durable context/skill, or completed-work record.
4. Move history into the session log. Route durable facts to their existing owner; add a focused context file only when needed.
5. Rewrite the plan. Check that each unresolved requirement, blocker, target, and authorization boundary survives.
6. Review adversarially: can the next session execute each task without reconstructing its requirements from history?
7. Record before/after line and word counts, destinations, and validation in the session log.
8. Run instruction/skill-size checks; regenerate the skill index when skills change.

## Size review

Target at most 60 lines; review before exceeding 80. This is a review budget, not permission to drop requirements. Count words too: joining paragraphs into long lines does not make
a plan lean. If it grows, remove duplication and relocate context before adding sections or creating more planning files. Do not add a plan-specific linter for this local-only
document unless recurring failures justify it.
