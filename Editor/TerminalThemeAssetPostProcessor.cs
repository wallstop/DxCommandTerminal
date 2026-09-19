namespace WallstopStudios.DxCommandTerminal.Editor
{
    using System;
    using System.IO;
    using System.Text;
    using Themes;
    using UnityEditor;
    using UnityEngine;

    /*
        Auto-wires TerminalThemeAsset authoring (issue #72 option A, owner
        decision "auto wire"): every time a theme asset is created or edited,
        the sibling .uss file named after the asset is (re)written with the
        sheet the asset's colors describe. Writes are content-compared first
        (line-ending normalized), so unchanged imports never dirty the
        project or re-trigger imports. Renames clean up the old generated
        sheet and write the new-name one; note Unity 6000.4 derives asset
        GUIDs from paths, so a rename mints a fresh stylesheet identity and
        packs referencing the previous file need reassignment - the same as
        renaming any hand-authored stylesheet. Only files carrying the
        generated marker are overwritten or deleted on rename/asset deletion
        - a hand-written sheet sharing the asset's name is preserved with a
        warning instead. The generated sheet is a plain StyleSheet: drop it
        into a TerminalThemePack's Themes list and it behaves like any
        hand-written theme sheet.

        Known limitation: Unity's undo system and VCS operations (reverts,
        branch switches) do not move files through this postprocessor, so a
        reverted/undone rename can leave its generated sheet behind under the
        old name; deleting that sheet (or renaming an asset over the stray
        name) cleans it up on the next import.
     */
    internal sealed class TerminalThemeAssetPostProcessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            foreach (string assetPath in importedAssets)
            {
                if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TerminalThemeAsset themeAsset = AssetDatabase.LoadAssetAtPath<TerminalThemeAsset>(
                    assetPath
                );
                if (themeAsset != null)
                {
                    WriteSiblingSheet(themeAsset, assetPath);
                }
            }

            for (int i = 0; i < movedAssets.Length; ++i)
            {
                if (!movedAssets[i].EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TerminalThemeAsset themeAsset = AssetDatabase.LoadAssetAtPath<TerminalThemeAsset>(
                    movedAssets[i]
                );
                if (themeAsset == null)
                {
                    continue;
                }

                /*
                    Clean up the old generated sheet, then write the new-name
                    one. (Unity 6000.4 derives asset GUIDs from paths, so a
                    rename necessarily mints a new stylesheet identity; packs
                    referencing the previous file need reassignment, exactly
                    like renaming any hand-authored stylesheet.) A hand-written
                    old sheet is left alone, and a case-only rename on a
                    case-insensitive filesystem rewrites the same file in
                    place.
                 */
                string oldSheetPath = SheetPathFor(movedFromAssetPaths[i]);
                string newSheetPath = SheetPathFor(movedAssets[i]);
                if (
                    !string.Equals(oldSheetPath, newSheetPath, StringComparison.Ordinal)
                    && IsGeneratedSheet(oldSheetPath)
                    && !ResolvesToSameAsset(oldSheetPath, newSheetPath)
                )
                {
                    DeleteGeneratedSheet(oldSheetPath);
                }

                WriteSiblingSheet(themeAsset, movedAssets[i]);
            }

            /*
                Deleting the asset deletes its generated sheet with it; a
                hand-written sheet sharing the name survives.
             */
            foreach (string deletedPath in deletedAssets)
            {
                if (!deletedPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string sheetPath = SheetPathFor(deletedPath);
                if (IsGeneratedSheet(sheetPath))
                {
                    DeleteGeneratedSheet(sheetPath);
                }
            }
        }

        /*
            Deletes a generated sheet, falling back to a direct file delete
            (plus its meta, if one was written) for a never-imported leftover
            the asset database has no entry for, so no broken import or
            orphaned meta survives.
         */
        private static void DeleteGeneratedSheet(string sheetPath)
        {
            if (AssetDatabase.DeleteAsset(sheetPath))
            {
                return;
            }

            File.Delete(sheetPath);
            if (File.Exists(sheetPath + ".meta"))
            {
                File.Delete(sheetPath + ".meta");
            }
        }

        /*
            True when both paths resolve to the same imported asset (a
            case-only rename on a case-insensitive filesystem), so the
            "stale" path is the sheet just written and must be kept.
         */
        private static bool ResolvesToSameAsset(string oldSheetPath, string newSheetPath)
        {
            if (string.Equals(oldSheetPath, newSheetPath, StringComparison.Ordinal))
            {
                return true;
            }

            UnityEngine.Object oldSheet = AssetDatabase.LoadMainAssetAtPath(oldSheetPath);
            UnityEngine.Object newSheet = AssetDatabase.LoadMainAssetAtPath(newSheetPath);
            return oldSheet != null
                && newSheet != null
                && oldSheet.GetEntityId() == newSheet.GetEntityId();
        }

        private static void WriteSiblingSheet(TerminalThemeAsset themeAsset, string assetPath)
        {
            if (themeAsset == null || string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            string sheetPath = SheetPathFor(assetPath);
            string contents = themeAsset.BuildUss();

            if (File.Exists(sheetPath))
            {
                string existing = File.ReadAllText(sheetPath);
                if (
                    string.Equals(
                        NormalizeEndings(existing),
                        NormalizeEndings(contents),
                        StringComparison.Ordinal
                    )
                )
                {
                    return;
                }

                if (!IsGeneratedSheetContent(existing))
                {
                    Debug.LogWarning(
                        $"Skipping generated theme sheet for '{assetPath}': '{sheetPath}' exists "
                            + "and was not generated by TerminalThemeAsset. Rename the asset or "
                            + "move/delete that file to let the theme asset manage its sheet.",
                        themeAsset
                    );
                    return;
                }
            }

            File.WriteAllText(sheetPath, contents, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(sheetPath);
            Debug.Log($"Generated theme sheet '{sheetPath}' for '{themeAsset.name}'.", themeAsset);
        }

        private static string NormalizeEndings(string contents)
        {
            return contents.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        private static bool IsGeneratedSheet(string path)
        {
            try
            {
                return File.Exists(path) && IsGeneratedSheetContent(File.ReadAllText(path));
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static bool IsGeneratedSheetContent(string contents)
        {
            return contents.StartsWith(
                TerminalThemeAsset.GeneratedMarkerPrefix,
                StringComparison.Ordinal
            );
        }

        private static string SheetPathFor(string assetPath)
        {
            int lastSlash = assetPath.LastIndexOf('/');
            string fileName = Path.GetFileNameWithoutExtension(assetPath) + ".uss";
            return 0 <= lastSlash ? assetPath[..lastSlash] + "/" + fileName : fileName;
        }
    }
}
