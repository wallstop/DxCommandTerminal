# Release Runbook

How to cut a DxCommandTerminal release with the automated pipeline (T14, issue #85).
Everything here is Unity-free: no step provisions a Unity editor or license, and every
distributable is produced from plain repository content.

## Pipeline overview

| Stage | Trigger | Workflow | What it does |
| --- | --- | --- | --- |
| Prepare | Manual dispatch (`Release Prepare`) | `.github/workflows/release-prepare.yml` | Rewrites `package.json`, rotates `CHANGELOG.md`'s `## Unreleased` content under a dated `## [X.Y.Z] - date` heading, opens the `release/vX.Y.Z` PR |
| Tag | Push to `master` touching `package.json` | `.github/workflows/release-tag.yml` | Pushes the annotated `vX.Y.Z` tag when the squash-merge subject is `release: vX.Y.Z` |
| Publish | Tag push | `release.yml` (next milestone, issue #85) | Verifies tag/package/changelog agreement, validates + attests, builds the `.unitypackage`, publishes npm + GitHub Release |

Versions are full semver including prerelease identifiers (`1.0.0-rc25.0`). Release tags
carry a `v` prefix (`v1.0.0-rc26.0`); the historical unprefixed tags (`1.0.0-rc25.0`)
predate the pipeline, and the tag gate treats both forms as already-released. Once the
publish workflow lands (next milestone), prerelease versions publish to npm's `next`
dist-tag and stable versions to `latest`.

## One-time setup

- **`RELEASE_PAT` secret (optional).** PRs created with the default `GITHUB_TOKEN` do not
  re-trigger CI on the release branch. The prepare job already runs the full Node tooling
  suite on the exact tree it pushes, so this only affects per-PR check displays. To get
  CI runs on release PRs, add a fine-grained PAT with `contents: write` +
  `pull-requests: write` as the `RELEASE_PAT` secret; the workflow uses it when present
  and falls back to `GITHUB_TOKEN` otherwise.
- **npm Trusted Publishing (pending).** Configured once npmjs.com-side when `release.yml`
  (publish flow) lands: register this repository + workflow as a trusted publisher for
  `com.wallstop-studios.dxcommandterminal`.

## Preparing a release

1. Confirm `master` is green and `CHANGELOG.md`'s `## Unreleased` section carries the
   user-facing entries for this release (internal/tooling work stays out per the
   changelog policy at the top of the file).
2. Actions → **Release Prepare** → **Run workflow** (default branch):
   - `bump` — `patch`/`minor`/`major`; ignored when a version is given. A prerelease
     version bumps to stable (`1.0.0-rc25.0` + patch → `1.0.1`).
   - `version` — explicit version, e.g. `1.0.0-rc26.0`; use this to iterate the rc
     series.
   - `dry_run` — **run this first.** It prints the exact prepared diff (package.json
     version field + changelog rotation) and writes nothing, and still fails fast when
     the `vX.Y.Z` tag or `release/vX.Y.Z` branch already exists.
3. Review the dry-run diff, then dispatch again with `dry_run` unchecked. The job runs
   the Node tooling suite on the prepared tree, pushes `release/vX.Y.Z`, and opens a PR
   titled `release: vX.Y.Z` containing a checklist and the changelog excerpt.

Failures at this stage are all fail-closed:

- `## Unreleased` missing or empty → nothing to release; fix the changelog first.
- `refs/tags/vX.Y.Z`, `refs/heads/release/vX.Y.Z`, or the remote-tracking
  `refs/remotes/origin/release/vX.Y.Z` (what a CI checkout actually sees) exists →
  delete the stale ref or pick another version.
- Invalid version / bump choice → fix the input.

## Reviewing and merging the release PR

1. Read the changelog excerpt in the PR body (user-facing entries only).
2. Confirm the version.
3. **Squash-merge with the default subject** `release: vX.Y.Z`. GitHub appends ` (#N)`;
   the tag gate accepts that suffix. Any other subject will not auto-tag.

## Auto-tagging behavior

`Release Tag` fires on the `master` push that the squash-merge creates:

| Push | Behavior |
| --- | --- |
| Subject `release: vX.Y.Z` (+ ` (#N)`) and `## [X.Y.Z] - date` heading present | Annotated tag `vX.Y.Z` pushed |
| Tag `vX.Y.Z` (or the historical unprefixed `X.Y.Z`) already exists | Silent no-op |
| Release subject but no matching changelog heading | **Fails closed** (workflow fails; fix the changelog or re-tag manually) |
| Subject names a *different* release (`release: vOTHER`) | `::warning::` with manual fallback commands when the current version's heading exists; silent no-op otherwise |
| Changelog documents `X.Y.Z` but the subject is not a release subject | `::warning::` with manual fallback commands; no tag pushed |
| Ordinary `package.json` push | Silent no-op |

Manual fallback (also printed by the warning):

```sh
git tag -a vX.Y.Z -m "DxCommandTerminal X.Y.Z"
git push origin vX.Y.Z
```

## Re-running after a partial failure

- **Prepare failed midway** (files rewritten, PR not opened): delete the local/ref
  leftovers, revert the push if any, fix the cause, and dispatch again; the script
  refuses to run while `release/vX.Y.Z` exists.
- **Tag failed after the merge** (`Release Tag` job red): the changelog/package state on
  `master` is already correct — use the manual fallback commands, or re-run the failed
  workflow run (the tag step is idempotent; an existing tag no-ops).
- **Publish fails after the tag** (once `release.yml` lands): re-run the publish
  workflow from the same tag; npm skips versions already on the registry.

## Local tooling

The same logic runs locally without CI:

```sh
npm run release:prepare -- --bump patch --dry-run   # or: -- --version 1.0.0-rc26.0 --dry-run
npm run release:prepare -- --version 1.0.0-rc26.0   # rewrites package.json + CHANGELOG.md
npm run release:gate -- --version X --subject S [--tag-exists]
```

The release CLI (`tooling~/scripts/release/release.mjs`) and the pure versioning module
(`tooling~/scripts/release/versioning.mjs`) are contract-tested in
`tooling~/scripts/tests/` and run in CI on every PR (node-tests lane).

## Security notes

- Every workflow pins action SHAs, requests minimal per-job permissions, and sets
  `persist-credentials: false`; pushes go through `gh auth setup-git`'s credential
  helper, so no token ever lands in a push URL that a failed push could echo into the
  public job log.
- No workflow provisions a Unity editor, consumes a license seat, or references
  third-party Unity packaging tooling; the `.unitypackage` comes from this repository's
  own exporter (`tooling~/scripts/release/export-unitypackage.mjs`).
- The release CLI validates the tag/branch/PR name components from the semver grammar,
  so no field can smuggle shell or ref syntax.
