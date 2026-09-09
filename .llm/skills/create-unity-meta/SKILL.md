---
name: create-unity-meta
description: Generate correct Unity .meta files for new files and folders in the DxCommandTerminal package, including which paths Unity ignores (dot-directories) and which importer blocks to expect. Use whenever creating, moving, renaming, or deleting any visible file or folder in the package.
metadata:
  category: Core
---

# Create Unity Meta

Unity tracks every visible asset via a sibling `.meta` file containing a stable GUID. Missing or
regenerated `.meta` files churn diffs and can break references.

## Rules

1. **Visible file/folder created** -> create a sibling `.meta` with a fresh lowercase GUID
   (32 hex chars). Copy the importer block from the nearest matching existing asset (text files
   use `TextScriptImporter`, folders use `folderAsset: yes`, both with empty
   `externalObjects`/`userData`/`assetBundleName`/`assetBundleVariant`).
2. **Dot-prefixed paths are ignored by Unity** - `.llm/`, `.github/`, `.config/`, `.git/`,
   `.editorconfig`, `.gitignore`, `.pre-commit-config.yaml`. NEVER create `.meta` files for them.
   Existing proof: `.github/`, `.config/` have no `.meta` siblings in this package.
3. **Never let Unity auto-generate and commit a stub** - author the `.meta` alongside the asset in
   the same change so the GUID is stable from the first commit.
4. **Moving/renaming** an asset moves its `.meta` unchanged (GUID travels with the file).
5. **Deleting** an asset deletes its `.meta` in the same change.

## Folder meta template

```yaml
fileFormatVersion: 2
guid: <32-hex-lowercase>
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

Note the trailing single space after `userData:` - match the existing files byte-for-byte.

## Text asset meta template

```yaml
fileFormatVersion: 2
guid: <32-hex-lowercase>
TextScriptImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

## Verification

Run `pwsh -NoProfile -File scripts/lint-unity-meta.ps1` (also enforced by pre-commit and
CI). It checks, over tracked files only:

1. Every tracked `.meta` has its target tracked (no orphan metas for gitignored or
   locally generated data); a folder meta is valid when its directory still contains
   tracked files.
2. Every Unity-visible tracked file/directory (no dot-prefixed or `~`-suffixed segment)
   has a tracked `.meta`, so Unity never auto-generates a stub.
3. Every `.meta` declares exactly one 32-hex `guid:`, and GUIDs are unique across the
   package.

Conventions: dot-prefixed paths (`.llm/`, `.github/`, `.config/`, `.git/`) are ignored by
Unity and never get `.meta` files; the folder meta template uses `folderAsset: yes` (some
historical folder metas omit it and remain valid).
