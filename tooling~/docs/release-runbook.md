# Release Runbook

Release PRs on `master` publish automatically after tagging. There is no repository opt-in switch or release environment approval. Merge the reviewed release PR to publish; use
`dry_run=true` for an artifact-only rehearsal.

## Pipeline

| Stage   | Trigger                                                    | Result                                                                                               |
| ------- | ---------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| Prepare | Manual `Release Prepare` dispatch                          | Updates the version and changelog and opens a release PR                                             |
| Tag     | Release merge touching `package.json`                      | Pushes annotated `v<version>` and dispatches publishing explicitly                                   |
| Publish | Automatic tag handoff or manual `Release Publish` dispatch | Packs npm, exports Unity, checksums and attests both, publishes npm, then publishes a GitHub Release |

The pipeline needs no Unity editor or license. The repository exporter builds the `.unitypackage` from the shipped files and their Unity metadata.

## One-time npm setup

On the npm package settings page, add GitHub trusted publishers with:

- Owner: `wallstop`
- Repository: `DxCommandTerminal`
- Workflow: `release.yml` for both automatic releases and manual recovery
- Environment: leave blank
- Allowed action: enable direct `npm publish`

Automatic tagging dispatches `release.yml` as a separate workflow run. Both paths use the same npm identity, so only one trusted publisher is needed. GitHub permits
`workflow_dispatch` from `GITHUB_TOKEN`; the dispatch job requests `actions: write`. Node 24 supplies a compatible npm CLI. Authentication uses OIDC with no npm token. See
[npm trusted publishing](https://docs.npmjs.com/trusted-publishers/).

`RELEASE_PAT` is optional. The prepare workflow uses it when present and otherwise uses `GITHUB_TOKEN`. The prepared tree runs the Node suite before its PR is opened. GitHub can
require approval for CI on a PR created by `GITHUB_TOKEN`.

## Prepare and publish

1. Check that `master` CI passes and `## Unreleased` contains user-facing changes.
2. Run **Release Prepare** from `master` with `dry_run=true`. Choose a bump or an explicit semver version. Empty notes and conflicting tags or branches fail.
3. Review the diff. Run again with `dry_run=false` to open the release PR.
4. Review the version, changelog, and checks. Squash-merge with subject `release: v<version>`. GitHub's ` (#N)` suffix is accepted.
5. **Release Tag** pushes the annotated tag and dispatches **Release Publish**. A tag push alone does not start publishing, so bot-created tags need no extra token.

Stable versions publish to npm `latest`; prereleases publish to `next` and remain GitHub prereleases. Historical unprefixed version tags count as already released.

## Recover an existing tag

From `master`, dispatch **Release Publish** with:

- `tag`: the existing tag, such as `v1.0.1`
- `candidate_ref`: blank
- `dry_run`: `false`

The current workflow can publish an older tag. It resolves that tag once and pins all source checkouts to its commit SHA. Automatic handoffs also require the tag to match the
release merge SHA. Remote tag checks before npm and each GitHub Release write reject a deleted or moved tag. Never move a tag to repair a workflow.

The run summary records both the package source commit and the workflow revision. Provenance identifies the workflow execution; a recovery workflow revision can be newer than the
package source tag.

The tag, `package.json` version, and dated non-empty changelog section must agree. Both artifacts must build before publishing starts. npm checks the downloaded checksum and skips
an existing version only when its SHA-512 integrity matches. Registry errors or different bytes fail. GitHub reuses an existing Release, uploads the four assets, checks their
count, and publishes the draft after npm succeeds. Explicit reruns replace Release assets with `--clobber`.

After a partial failure, prefer **Re-run failed jobs** to reuse the original build artifacts. A complete rerun rebuilds them; changes to the Node/npm toolchain can change
compressed bytes and cause the registry integrity check to reject a skip.

If a prepare run failed after pushing its branch, resolve the leftover `release/v<version>` branch before retrying. If tagging failed before the push, rerun the tag job. Rerunning
all tag jobs over an existing tag is a no-op; use the manual publish dispatch for recovery.

## Artifact-only rehearsal

Dispatch **Release Publish** from `master` with `dry_run=true` and either an existing `tag` or a `candidate_ref` branch/full lowercase commit SHA. Leave both blank to rehearse
`master`. The candidate needs a valid version and dated notes. Tag and candidate inputs are mutually exclusive.

Rehearsals build and upload both artifacts. They skip npm publishing, attestations, and GitHub Release writes. Existing tags and Releases are unchanged.

## Local checks

```sh
npm run release:prepare -- --bump patch --dry-run
npm run package:validate
npm run package:export -- --out /tmp/release.unitypackage
npm --prefix tooling~ run package:import-drill -- \
  --artifact /tmp/release.unitypackage --unity '<path to Unity editor>'
```

The import drill creates a clean scratch project, imports with the Unity CLI, waits for compilation, and checks all entries, importer settings, GUIDs, and shipped assemblies. It
never imports into the live project. Failures keep logs and a manifest under `.artifacts/import-drill/`; successful runs delete the scratch project.

## Retained-project player drill

On the Windows host, keep a passing T13 matrix's scratch projects and run:

```sh
python tooling~/scripts/t13/player_smoke.py \
  --matrix-report <verified-matrix-manifest.json> --out C:/dxt13/player-smoke
```

Use short Windows paths for both the editor and output roots. Unity support installers can silently skip files at long paths.

The output root must be fresh. The default editor is Unity 2021; use `--editor-id unity-2022` for that retained family. Each family needs legacy, Input System, and combined input
legs. The drill checks the artifact against its original clean source revision and imported files. It records the package and harness revisions separately. To update owned
fixtures, pass `--previous-player-report <prior-player-matrix.json>`. Existing bytes must match its recorded hashes.

Metadata must match the artifact. GUID-only script metas can gain Unity's default MonoImporter block. Other changes fail. Player manifests record hashes for these expansions.

Add `--backend IL2CPP --stripping High` to test native code and aggressive stripping. Defaults remain `Mono` and `Low`. Use a new output root per backend/stripping pair. Windows
IL2CPP support and its native toolchain must be installed for that editor.

Each release player must load its generated catalog, execute generated and manual commands, parse arguments, and reject excess arguments. All compiler, analyzer, and build warnings
fail. The batch build retains graphics for scene probes. The compiled player must confirm the requested backend. Recorded build settings must match the requested stripping level.
For browser players, install that editor's WebGL support and add:

```sh
--target WebGL --backend IL2CPP --stripping High \
  --browser 'C:/Program Files/Google/Chrome/Application/chrome.exe'
```

The drill serves the build on loopback and starts an isolated headless Chromium profile. Only that run's report token is accepted. The browser confirms the platform, backend, input
profile, catalog binders, and command results. Output includes the report, browser log, and build hashes. Software WebGL is allowed for these command checks. UI, rendered visual
acceptance, and performance gates remain separate.

## Maintenance

Run the release contract tests through `npm test` when changing these workflows. Keep immutable action SHA pins, minimal job permissions, and `persist-credentials=false`. Git
pushes use the GitHub credential helper.

### Player readiness measurements

Run
`uv run --project tooling~ python tooling~/scripts/t13/player_readiness.py --matrix-report <verified-import-matrix.json> --previous-player-report <owned-player-matrix.json> --out C:/dxt13/readiness`
on the Windows host. The default cohort is Unity 2021 Mono with all three input profiles.

The drill builds 1,000 generated consumer commands and launches 30 regular release players per profile. Each process measures its first shell readiness after Unity startup; the
first process also measures 1,000 warmed shells after eight warmups. Timings include history/shell construction, deferred configuration, and registration. Catalog validation
follows timing. Raw clock samples, timings, player identities, environment, and build hashes remain in the output. Cold phase timings split construction, configuration, and
registration. `--commands 0` collects an empty-workload control and cannot approve the readiness gate.

Both cold and warm p95 must stay below 5 ms. `executionPassed` records successful workload checks even when the timing gate fails; `passed` requires both. These measurements do not
approve UI, input routing, numeric allocations, or pinned-editor recovery. A normal smoke run can restore the owned fixture by using the readiness matrix as its previous ownership
report.
