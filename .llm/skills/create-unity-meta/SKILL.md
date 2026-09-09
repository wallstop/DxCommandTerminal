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

- `git status` after the change: every new visible file has exactly one new `.meta`, and no
  `.meta` exists for dot-prefixed paths.
- GUIDs must be unique across the package; `grep -r "guid:" --include="*.meta" -h | sort | uniq -d`
  must be empty.
