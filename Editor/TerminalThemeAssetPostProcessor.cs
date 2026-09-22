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

        Filesystem discipline: every read and delete is attempted directly
        and failures are caught - no File.Exists guard before an act on the
        same path (the file can disappear between the check and the act,
        and the guard itself is an extra stat per import). Unreadable or
        missing files read as null/false; deletions no-op for missing files;
        genuine IO errors log and keep the import batch alive.

        Known limitation: Unity's undo system and VCS operations (reverts,
        branch switches) do not move files through this postprocessor, so a
        reverted/undone rename can leave its generated sheet behind under the
        old name; deleting that sheet (or renaming an asset over the stray
        name) cleans it up on the next import.
     */
    internal sealed class TerminalThemeAssetPostProcessor : AssetPostprocessor
    {
        /*
            Shared instance: File.WriteAllText writes the encoding's preamble
            (none here) and takes bytes from the encoding, so one stateless
            UTF8Encoding serves every write instead of allocating per write.
         */
        private static readonly UTF8Encoding Utf8NoBom = new(
            encoderShouldEmitUTF8Identifier: false
        );

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            foreach (string assetPath in importedAssets)
            {
                if (TryLoadThemeAsset(assetPath, out TerminalThemeAsset themeAsset))
                {
                    WriteSiblingSheet(themeAsset, assetPath);
                }
            }

            /*
                Parallel arrays (Unity's callback signature) need the index
                for both sides - rule 11's sanctioned counting-loop case.
             */
            for (int movedIndex = 0; movedIndex < movedAssets.Length; ++movedIndex)
            {
                string movedPath = movedAssets[movedIndex];
                string movedFromPath = movedFromAssetPaths[movedIndex];

                if (!TryLoadThemeAsset(movedPath, out TerminalThemeAsset themeAsset))
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
                string oldSheetPath = SheetPathFor(movedFromPath);
                string newSheetPath = SheetPathFor(movedPath);
                if (
                    !string.Equals(oldSheetPath, newSheetPath, StringComparison.Ordinal)
                    && TerminalThemeAsset.IsGeneratedSheet(oldSheetPath)
                    && !ResolvesToSameAsset(oldSheetPath, newSheetPath)
                )
                {
                    DeleteGeneratedSheet(oldSheetPath);
                }

                WriteSiblingSheet(themeAsset, movedPath);
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
                if (TerminalThemeAsset.IsGeneratedSheet(sheetPath))
                {
                    DeleteGeneratedSheet(sheetPath);
                }
            }
        }

        private static bool TryLoadThemeAsset(string assetPath, out TerminalThemeAsset themeAsset)
        {
            if (
                string.IsNullOrEmpty(assetPath)
                || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
            )
            {
                themeAsset = null;
                return false;
            }

            /*
                Metadata-only type lookup before load: bulk imports of
                unrelated .assets skip object deserialization entirely.
             */
            if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) != typeof(TerminalThemeAsset))
            {
                themeAsset = null;
                return false;
            }

            themeAsset = AssetDatabase.LoadAssetAtPath<TerminalThemeAsset>(assetPath);
            return themeAsset != null;
        }

        /*
            Deletes a generated sheet, falling back to direct file deletes
            for a never-imported leftover the asset database has no entry
            for, so no broken import or orphaned meta survives.
         */
        private static void DeleteGeneratedSheet(string sheetPath)
        {
            if (AssetDatabase.DeleteAsset(sheetPath))
            {
                return;
            }

            /*
                File.Delete no-ops for a missing file, so both deletes run
                unconditionally - an exists-check here would only add a
                check-then-act race.
             */
            TryDeleteFile(sheetPath);
            TryDeleteFile(sheetPath + ".meta");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning(
                    $"Could not delete generated theme sheet leftover '{path}': {e.Message}"
                );
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
#if UNITY_6000_4_OR_NEWER
            return oldSheet != null
                && newSheet != null
                && oldSheet.GetEntityId() == newSheet.GetEntityId();
#else
            return oldSheet != null
                && newSheet != null
                && oldSheet.GetInstanceID() == newSheet.GetInstanceID();
#endif
        }

        private static void WriteSiblingSheet(TerminalThemeAsset themeAsset, string assetPath)
        {
            if (themeAsset == null || string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            string sheetPath = SheetPathFor(assetPath);
            string contents = themeAsset.BuildUss();

            string existing = TerminalThemeAsset.TryReadText(sheetPath);
            if (existing != null)
            {
                if (MatchesGeneratedContent(existing, contents))
                {
                    return;
                }

                if (!TerminalThemeAsset.IsGeneratedSheetContent(existing))
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

            try
            {
                File.WriteAllText(sheetPath, contents, Utf8NoBom);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Debug.LogError(
                    $"Could not write generated theme sheet '{sheetPath}' for "
                        + $"'{themeAsset.name}': {e.Message}",
                    themeAsset
                );
                return;
            }

            AssetDatabase.ImportAsset(sheetPath);
            Debug.Log($"Generated theme sheet '{sheetPath}' for '{themeAsset.name}'.", themeAsset);
        }

        /*
            The generated output contains no '\r'; the normalize copy only
            runs for foreign files that use CRLF endings.
         */
        private static bool MatchesGeneratedContent(string existing, string contents)
        {
            if (0 <= existing.IndexOf('\r', StringComparison.Ordinal))
            {
                existing = NormalizeEndings(existing);
            }

            return string.Equals(existing, contents, StringComparison.Ordinal);
        }

        private static string NormalizeEndings(string contents)
        {
            return contents.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        private static string SheetPathFor(string assetPath)
        {
            int lastSlash = assetPath.LastIndexOf('/');
            string fileName = Path.GetFileNameWithoutExtension(assetPath) + ".uss";
            return 0 <= lastSlash ? assetPath[..lastSlash] + "/" + fileName : fileName;
        }
    }
}
