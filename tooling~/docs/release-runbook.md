# Release Runbook

How to cut a DxCommandTerminal release with the automated pipeline (T14, issue #85).
Everything here is Unity-free: no step provisions a Unity editor or license, and every
distributable is produced from plain repository content.

## Pipeline overview

| Stage | Trigger | Workflow | What it does |
| --- | --- | --- | --- |
| Prepare | Manual dispatch (`Release Prepare`) | `.github/workflows/release-prepare.yml` | Rewrites `package.json`, rotates `CHANGELOG.md`'s `## Unreleased` content under a dated `## [X.Y.Z] - date` heading, opens the `release/vX.Y.Z` PR |
| Tag | Push to `master` touching `package.json` | `.github/workflows/release-tag.yml` | Pushes the annotated `vX.Y.Z` tag when the squash-merge subject is `release: vX.Y.Z` |
| Publish | Tag push (`v*`) | `.github/workflows/release.yml` | Verifies tag/package/changelog agreement, validates + attests, builds the `.unitypackage`, publishes npm, publishes the GitHub Release |

Versions are full semver including prerelease identifiers (`1.0.0-rc25.0`). Release tags
carry a `v` prefix (`v1.0.0-rc26.0`); the historical unprefixed tags (`1.0.0-rc25.0`)
predate the pipeline, and the tag gate treats both forms as already-released. Prerelease
versions publish to npm's `next` dist-tag; stable versions to `latest`.

## One-time setup

- **`RELEASE_PAT` secret (optional).** PRs created with the default `GITHUB_TOKEN` do not
  re-trigger CI on the release branch. The prepare job already runs the full Node tooling
  suite on the exact tree it pushes, so this only affects per-PR check displays. To get
  CI runs on release PRs, add a fine-grained PAT with `contents: write` +
  `pull-requests: write` as the `RELEASE_PAT` secret; the workflow uses it when present
  and falls back to `GITHUB_TOKEN` otherwise.
- **npm Trusted Publishing (one-time).** On npmjs.com: package settings -> Trusted
  Publisher, register this repository (`wallstop/DxCommandTerminal`) + workflow filename
  `release.yml`. Until that is configured, the `publish-npm` job fails at npm publish;
  every earlier stage runs normally. npm publish uses OIDC (`id-token: write` +
  `npm publish --provenance`); no npm token secret is stored in the repo.

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

## The publish flow (Release Publish)

Fires on every `v*` tag push. Five jobs, in order:

1. **verify** - `release.mjs verify-release` fails closed unless the tag, the
   `package.json` version, and the `## [X.Y.Z] - date` changelog heading all agree.
2. **validate** - package-content validator, `npm pack`, sha256, artifact upload,
   build-provenance attestation.
3. **unitypackage** - required, non-skippable exporter run + sha256 + attestation; an
   empty or failed export blocks publishing.
4. **publish-npm** - skipped when `npm view` shows the exact name@version already on the
   registry (safe re-runs); otherwise `npm publish --provenance` with the version-shape
   dist-tag (`next` for prereleases, `latest` for stable).
5. **github-release** - creates the draft Release from the changelog section (shared
   extractor, fail-closed on missing/empty notes), uploads `.tgz`, `.tgz.sha256`,
   `.unitypackage`, `.unitypackage.sha256`, verifies the four assets, then publishes.
   npm publish always completes first (job dependency), never the reverse.

### Rehearsing a release (no publish)

Actions -> **Release Publish** -> **Run workflow** on a candidate tag:

1. On a scratch branch, set `package.json` to a rehearsal version that will never be
   released (e.g. `1.0.0-rehearsal.0`), add a matching `## [1.0.0-rehearsal.0] - date`
   changelog heading, commit, and push the tag `v1.0.0-rehearsal.0`.
2. Dispatch the workflow with `tag: v1.0.0-rehearsal.0` and `dry_run: true`. Verify,
   validate, and unitypackage run for real; npm publish and the release publish step are
   skipped, and the Release stays a draft for review.
3. Delete the scratch branch, the rehearsal tag, and the draft release afterwards.

### Re-running after a partial failure

- **Prepare failed midway** (files rewritten, PR not opened): delete the local/ref
  leftovers, revert the push if any, fix the cause, and dispatch again; the script
  refuses to run while `release/vX.Y.Z` exists.
- **Tag failed after the merge** (`Release Tag` job red): the changelog/package state on
  `master` is already correct - use the manual fallback commands, or re-run the failed
  workflow run (the tag step is idempotent; an existing tag no-ops).
- **Publish failed after the tag**: re-run `Release Publish` from the same tag
  (workflow_dispatch with `tag` + `dry_run: false`, or re-run the failed jobs). Every
  stage is re-run safe: verify re-checks the same tree, npm skips a version already on
  the registry, the draft release is reused, and assets re-upload with `--clobber`.

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

## Local tooling

The same logic runs locally without CI:

```sh
npm run release:prepare -- --bump patch --dry-run   # or: -- --version 1.0.0-rc26.0 --dry-run
npm run release:prepare -- --version 1.0.0-rc26.0   # rewrites package.json + CHANGELOG.md
npm run release:gate -- --version X --subject S [--tag-exists]
```

The release CLI (`tooling~/scripts/release/release.mjs`) and the pure versioning module
(`tooling~/scripts/release/versioning.mjs`) are contract-tested in
`tooling~/scripts/tests/` and run in CI on every PR (node-tests lane). The publish-flow
subcommands (`verify-release`, `notes`, `publish-gate`) share that coverage; the PR-copy
linter (`npm --prefix tooling~ run lint:pr-copy`) enforces the STE PR structure.

## The .unitypackage import drill (local, maintainer-run)

T13's release gate: import the actual release artifact into a clean throwaway
Unity project and verify it compiles there. CI never imports Unity packages;
this runs on a machine with the local Unity license and network access (the
scratch project's manifest pulls the package's UPM dependencies -
`com.unity.inputsystem`, `com.unity.test-framework` - from the registry so
every shipped asmdef actually compiles).

**Never import the artifact into the live maintainer project via the editor or
the MCP bridge.** The first drill (session-025) did exactly that:
`AssetDatabase.ImportPackage` popped a modal dialog against the live project,
the main thread blocked, and the editor has been unreachable from the bridge
ever since. The drill tool avoids the whole failure mode: batch mode, a
scratch project, a non-interactive import, a hard timeout, and a separate
process.

```sh
npm run package:export --prefix tooling~ -- --out /tmp/drill.unitypackage
npm run package:import-drill --prefix tooling~ -- \
  --artifact /tmp/drill.unitypackage \
  --unity "<path to Unity editor binary>"   # e.g. .../6000.4.6f1/Editor/Unity.exe
```

The tool probes `<unity> -version`, scaffolds a scratch project under
`.artifacts/import-drill/`, imports the artifact with the non-interactive
`AssetDatabase.ImportPackage(artifact, false)` via
`-batchmode -nographics -executeMethod`, waits out the triggered compilation
(the driver exits only after `isCompiling`/`isUpdating` clear), then
validates the result on disk:

- every artifact entry exists at `<project>/<pathname>` with a `.meta` whose
  GUID matches the artifact's GUID directory;
- analyzer payload metas keep the `RoslynAnalyzer` label;
- every imported `.asmdef` compiled to `Library/ScriptAssemblies/<name>.dll`
  (dependency resolution and compilation in one check).

The drill proves the generator payload imports with its label and that the
package compiles with it present; it does not execute the generator.
Generator behavior stays gated by the Unity-free generator suite and the
payload byte-compare in CI.

A clean run deletes the scratch project and writes a manifest (environment,
SHA-256, per-check results) under `.artifacts/import-drill/`. Any failure
after Unity launches keeps the project, the Unity log, and the manifest for
diagnosis and exits non-zero; pre-flight failures (corrupt artifact, bad
editor path, non-empty `--project`) fail before anything is scaffolded.
`--keep` keeps the scratch project even on success; `--timeout-minutes`
overrides the 20-minute default.

If the editor from the first drill is still wedged (the bridge times out on
every call): dismiss the modal import dialog by hand, delete
`Assets/DxTerminalUnityPackageDrill`, and refresh assets (issue #85).

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
