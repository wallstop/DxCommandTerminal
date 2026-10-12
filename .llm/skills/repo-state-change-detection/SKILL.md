---
name: repo-state-change-detection
description: Key preflight cache checks and CI skip-gates on content and whole change ranges, not on change-state - git status names only uncommitted edits (a committed edit stays invisible, so a status-based snapshot ages cached passes; the index blob listing closes it), a status line can stay byte-identical across two rewrites (fold flagged files' bytes), a workflow self-gate must diff the event's whole range (push before-SHA fetched on demand, PR base SHA with the merge-ref parent as fallback), and a workflow paths: filter must cover every file the suite reads. Use when touching preflight-cache.mjs, cacheTrackedFiles, repositorySnapshot, cachePaths, workflow paths filters, or a conditional job's diff gate.
metadata:
  category: Core
---

# Repo-State Change Detection

## The one rule

A cache key or skip-gate answers "did the inputs change", so it must hash the inputs themselves, not a signal that merely correlates with change. git's change signals - status
lines, `HEAD~1`, curated path lists - each miss a whole class of real changes. Hash content; diff ranges.

## Three surfaces, one class

### The repo snapshot (`repositorySnapshot`, tooling~/scripts/preflight-cache.mjs)

Backs `cacheTrackedFiles: true` (node-tests, lint-md-format) - checks that read files outside their declared roots. It folds, in order:

1. The status output (`status --porcelain=v1 -z --untracked-files=all`).
2. The tracked index listing: `path\0blob\0eol\0` per file, from the same memoized `ls-files -s` the path identity uses.
3. Each status-flagged file's bytes (size-capped; stat signature above it).

Each fold exists because a signal alone lied once:

- Status never names a COMMITTED edit - the tree is clean against the new HEAD - so the blob fold carries committed content (Bugbot, #251).
- A staged rewrite repeats its `A `/`M ` status line byte-for-byte, so the flagged-byte fold carries index-only rewrites (Bugbot, #249; af2a71b).
- A worktree rewrite repeats its ` M <path>` line too - same fold.

The blob fold reads the index, which a commit rewrites, so it costs no extra spawn; never "fix" staleness by re-hashing worktree bytes per file - that is the 9p keying wall this
module removed.

### A conditional job's diff gate (`.github/workflows/tooling-tests.yml`, devtool-grammar)

The base must span the event's WHOLE change range:

- PR: `github.event.pull_request.base.sha`; when it is absent from the shallow clone, the merge ref's first parent (`HEAD~1` at depth 2) is the base tip, so the two-dot diff still
  covers every PR commit.
- Push: `github.event.before` (the pushed-over tip), fetched on demand - `HEAD~1` alone skips a gate when an EARLIER commit of a multi-commit push touched the inputs.
- Last resort stays `HEAD~1`: over-triggering wastes a runner minute, under-triggering skips a gate.

The gate's pathspec must equal the job's transitive read closure - for the grammar job that is the grammar project, the reporter `cs.txt` it compiles, `unity-mcp.mjs`, its
module-scope `t11` imports, the manifest, and the workflow file itself. Derive the closure by reading imports, not by naming what you remember.

### A workflow `paths:` filter

Filters gate which lanes run, so each filter must cover BOTH halves of the lane's read surface: every file its suite READS, and every file its GATES enforce. The node suite reads
`.github/workflows/*.yml` (docs-pages pins docs-build.yml; publish-release pins four workflows), so tooling-tests triggers on `.github/workflows/**`. The markdown gate enforces
prettier over every tracked `.md` plus `.prettierrc.json`, so the same workflow triggers on `**/*.md`, `.prettierrc.json`, and `.github/copilot-instructions.md` - Bugbot caught the
enforcement half after the read half landed, and a filter gap on an enforcement gate is a file class CI never checks. When you add a test that reads a file class or a gate that
enforces one, join that class to the filter in the same change - `publish-release.test.mjs` pins the filters on purpose.

## Pinned by

- `tooling~/scripts/tests/preflight.test.mjs`: the committed-edit, staged-rewrite, dirty-outside-roots, ignored-coin, and untracked tests, all on scratch repositories (below).
- `tooling~/scripts/tests/publish-release.test.mjs`: the path-filter pins.

## Tests that drive repo git state

Drive a SCRATCH repository, never the real checkout. The preflight-cache module derives REPO_ROOT from its own location, so a test copies `preflight-cache.mjs` into a three-file
repo under `os.tmpdir()` and runs every key observation in a child that imports the copy - the scratch tree becomes the keyed repository, git spawns cost milliseconds on the local
scratch filesystem instead of seconds on the 9p mount, and a probe can never touch real tracked state. Commit identity is repo-local `git config` inside the scratch repo.

The real-branch era is the cautionary record: the committed-probe test once committed on the real branch and `reset --hard` in `finally`, so it had to refuse dirty trees (computed
from `git status --porcelain --untracked-files=no` at registration) because the reset destroys uncommitted work - and it ate its own fix once in its birth session. If a future test
genuinely cannot use a scratch repo (it must pin the real checkout's git behavior), that refusal pattern and the inline identity (`git -c user.name=... -c user.email=...`) are the
recovery.

## Stated coins

- `assume-unchanged` / `skip-worktree` files key at their index blob and hide edits from both folds - a deliberate per-developer opt-in.
- Files above the size cap key at stat signatures: a same-size, same-mtime edit defeats that slice.
- An ignored file under a git-visible root does not move that root's path-state key; an ignored ROOT keeps the full filesystem walk.
