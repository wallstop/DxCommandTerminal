# Release Runbook

How to cut a DxCommandTerminal release with the automated pipeline (T14, issue #85).
Everything here is Unity-free: no step provisions a Unity editor or license, and every
distributable is produced from plain repository content.

## Pipeline overview

| Stage | Trigger | Workflow | What it does |
| --- | --- | --- | --- |
| Prepare | Manual dispatch (`Release Prepare`) | `.github/workflows/release-prepare.yml` | Rewrites `package.json`, rotates `CHANGELOG.md`'s `## Unreleased` content under a dated `## [X.Y.Z] - date` heading, opens the `release/vX.Y.Z` PR |
| Tag | Push to `master` touching `package.json` | `.github/workflows/release-tag.yml` | Pushes the annotated `vX.Y.Z` tag when the squash-merge subject is `release: vX.Y.Z` |
| Publish | Explicit call after auto-tagging, or manual dispatch from `master` | `.github/workflows/release.yml` | Verifies tag/package/changelog agreement, builds artifacts, then attests and publishes only with `dry_run: false` |

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
- **Release approval.** Create the `release` environment with required maintainer
  reviewers, prevent self-review, and allow only `master` deployments. Do this before
  merging a release PR. YAML references do not configure protection rules.
  **Blocked:** review found this environment absent; no remote settings were changed.
- **Publish opt-in.** Leave repository variable `RELEASE_PUBLISH_ENABLED` unset until
  a maintainer verifies the environment, npm trust, tag protections, and hosted acceptance.
  Only exact `true` enables publishing. This is a manual prerequisite confirmation,
  not an API audit; remove it before changing or removing those protections.
- **Tag protection.** Protect version tags against updates and deletion. Remote SHA
  checks run before npm and each Release mutation, but cannot make separate API calls atomic.
- **npm Trusted Publishing.** Register `wallstop/DxCommandTerminal` with environment
  `release` and workflow `release.yml` for manual publishing. Add a separate publisher
  for `release-tag.yml` for automatic tags: npm validates the calling workflow, not
  the reusable callee. Both need direct `npm publish` permission. Keep OIDC enabled;
  do not add an npm token fallback. These settings need maintainer verification.

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

Tag pushes alone do not start this workflow. A successful auto-tag job calls it
explicitly with the tag and expected merge SHA. Manual runs use workflow ref `master`
and default to `dry_run: true`. Publishing requires an existing version tag; rehearsal
can instead select a branch or commit. Other repositories and workflow refs are rejected.
All downstream checkouts use the verified commit SHA.

1. **verify** - requires non-empty dated notes for the checked-out package version.
   Tag runs also require tag/package agreement. Publishing requires opt-in and tag SHA
   equal to the workflow event SHA, including manual runs. Automatic handoffs check the merge SHA.
2. **validate** and **unitypackage** - read-only package validation, packing/export,
   checksums, and Actions artifact uploads. Missing or empty artifacts fail the run.
3. **attest** - publish-only build provenance, behind the `release` environment.
4. **publish-npm** - publish-only OIDC, behind the same environment. An existing npm
   version is skipped only when its SHA-512 `dist.integrity` matches the tarball.
   Missing integrity, different bytes, and registry failures block publication.
   New versions use `next` for prereleases and `latest` for stable.
5. **github-release** - publish-only, behind the same environment and npm success.
   Creates or reuses a Release, uploads four assets, then publishes the draft.
   Explicit publishing still replaces existing assets with `--clobber`; review reruns.

### Rehearsing a release (no publish)

The implemented rehearsal path runs only `verify`, `validate`, and `unitypackage`.
It writes Actions artifacts, not npm packages, attestations, or GitHub Releases.
Existing draft and published Releases are not queried or changed.

**Blocked pending hosted acceptance (#93). No operational rehearsal recipe is approved.**
Acceptance evidence must include candidate and existing-published-version runs, all
write jobs skipped, both artifact hashes, and unchanged public asset IDs/hashes.
Local structural and mocked-boundary tests do not prove hosted behavior. Restore the
recipe only after a maintainer records that evidence and run URLs.

#### Candidate contract (awaiting approval; do not dispatch yet)

The tag-only path could not rehearse current tooling: all existing tags were historical,
unprefixed releases. Creating a new release tag is not needed for artifact acceptance.

- Select workflow ref `master`, keep `dry_run: true`, and leave `tag` blank.
- `candidate_ref` accepts a branch name, `refs/heads/...`, or a full lowercase commit SHA.
  Blank selects `master`. Other values are branch names, not abbreviated SHAs or revision expressions.
- Prefer a reviewed full SHA for repeatable evidence. A branch resolves once at checkout;
  every build uses that resolved SHA. Workflow event SHA can differ only for rehearsal.
- The candidate must contain current tooling, a valid package version, and its non-empty
  dated changelog section. Existing versions are allowed; Unreleased is not substituted.
- Candidate mode is manual-only. Publishing with a candidate, no tag, or both inputs fails
  before checkout. Reusable publishing still requires `tag` and `expected_sha`.
- No version, changelog, tag, registry, or Release mutation occurs in candidate verification.
  Artifacts are rehearsal bytes, not evidence that an existing release has identical bytes.

Local verification without any tag or publication:

```sh
node tooling~/scripts/release/release.mjs verify-candidate --dry-run \
  --version "$(node -p "require('./package.json').version")" --changelog CHANGELOG.md
```

After approval and merge, record the selected ref, resolved build SHA, workflow event SHA,
run URL, skipped write jobs, both artifact hashes, and unchanged public asset IDs/hashes.
Do not enable publishing or create tags to gather this evidence. Tag-mode verification
still requires exact `v<package-version>` agreement; `v*-candidate` labels do not bypass it.

### Re-running after a partial failure

- **Prepare failed midway** (files rewritten, PR not opened): delete the local/ref
  leftovers, revert the push if any, fix the cause, and dispatch again; the script
  refuses to run while `release/vX.Y.Z` exists.
- **Tag failed after the merge** (`Release Tag` job red): the changelog/package state on
  `master` is already correct - use the manual fallback commands, or re-run the failed
  workflow run (the tag step is idempotent; an existing tag no-ops).
- **Publish failed after the tag**: rerun failed jobs on the original workflow SHA.
  A new manual publish is allowed only while the selected tag matches `master`'s event
  SHA. Never move a tag to satisfy this check. Approval and opt-in remain required.
  Rerunning all tag jobs no-ops on existing tags and does not call publishing.
  npm skips identical bytes only; Release assets are replaced only in publish mode.

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

Manual tag fallback (also printed by the warning; tagging alone does not publish):

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
  (dependency resolution and compilation in one check);
- the Unity log carries no compiler errors or analyzer-failure diagnostics
  (`CS8032`, `CS8784`, `CS8785`, `CS9057`, `AD0001`), even when Unity exits 0.

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
