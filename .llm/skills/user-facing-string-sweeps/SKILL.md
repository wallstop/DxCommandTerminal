---
name: user-facing-string-sweeps
description: Sweep and rename user-facing product strings across every surface that emits them - production diagnostics and the test pins that assert them, workflow YAML, tooling scripts that print commands or artifacts, test fixtures that mirror real config, docs-site chrome, release identities. Use when renaming a product or display string, adding user-visible text, reviewing a rename PR, or triaging a reviewer finding about a stale name.
metadata:
  category: Core
---

# User-facing string sweeps

A rename that only walks the source tree ships stale. Identity strings hide in surfaces that emit them later: a printed command a maintainer pastes, a fixture that mirrors a real
config file, a site header built by CI. On this repository the first pass of the DxTerminal rename (#232) missed the docfx site chrome, the release tag fallback command, and a
fixture `displayName`; two independent reviewers caught them. The class, not the instance, is the unit of work.

## Classify every hit before renaming

Grep the old name repo-wide, all file types, excluding `.git`, `.artifacts`, `node_modules`, build output. Every hit gets exactly one verdict:

- **Identifier** (namespace, type name, asmdef, package id, file name): keep.
- **URL or repo slug**: keep.
- **Persisted key or path** (settings file, generated manifest, marker a reader compares): keep - renaming orphans user data. Trace readers first; a marker nothing parses back is
  safe to rename, one something reads is not.
- **Changelog history, migration page**: exempt by contract.
- **User-facing string**: rename.
- **Pin** (a test or fixture asserting a renamed string): rename in the same commit, in matched pairs.

## Where emitting surfaces hide

Check each of these on every rename; each was missed at least once here:

1. Test regexes and `LogAssert` expectations pinning production strings - including regex-escaped forms (`\[OldName\]`), which a plain grep of the bare name still finds.
2. Workflow YAML: annotated tag messages, GitHub release titles, artifact display names.
3. Tooling scripts that print commands or artifacts a human pastes or ships (a fallback command printed when automation refuses, for example), plus the tests that pin their output
   verbatim.
4. Test fixtures that mirror real config files - a fixture `displayName` must match the real `package.json`, or the fixture certifies a package that no longer exists.
5. Docs-site chrome (site title, name, footer, toc root) and the build scripts that render it.
6. Menu-path strings: every doc or README that names an `Assets > Create` path must match the attribute string character for character.

## Verify the classification, not the intention

- Run the suites that assert renamed strings and the artifact linter that gates the surface (the docs build for site chrome), not just the unit tests.
- Re-grep at the end: every remaining hit must carry its exemption verdict. An unclassified hit is an unfixed finding.
- Size is not signal: the misses above were one-line literals inside scripts and fixtures, invisible to a diff review of the "real" rename.
