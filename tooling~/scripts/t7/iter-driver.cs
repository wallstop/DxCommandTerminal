/*
    T07 edit->ready measurement harness (PLAN.md), driven via Unity MCP:

      run_script  file=Packages/com.wallstop-studios.dxcommandterminal/tooling~/scripts/t7/iter-driver.cs
                  entry=T7IterDriver.Main  args=["AB0"]   // install probe + run the queue
                  entry=T7IterDriver.Cleanup              // remove probe + state, then recompile

    Main(config) installs a temporary [InitializeOnLoad] probe into host
    Assets/Editor/ that survives domain reloads and self-drives a scenario queue
    (one no-op content change per cycle). Config selects the queue and the
    pre-queue compile configuration:

      AB0    stock package (both RoslynAnalyzer DLLs labeled) - S2 x30 (gate run)
      AB1    both analyzer DLLs unlabeled (no generator, no analyzers) - S2 x12
      AB2    generator labeled, analyzers unlabeled - S2 x12
      AB3    analyzers labeled, generator unlabeled - S2 x12
      FLOOR  stock package + a trivial Assets/T7Floor assembly - S5 x24
             (per-assembly compile floor + ready-growth control)

    Scenarios: S2 runtime edit, S3 editor edit, S4 command-declaration edit,
    S5 floor-assembly edit. The probe self-expires 24h after install (deletes
    itself and its state), so an interrupted session cannot keep editing tracked
    sources on later editor opens.

    Log rows (.artifacts/session-053/iter-log.tsv, TSV event/scenario/time/
    delta[/extra]): config (install-time analyzer-label readback, extra=gen=/ana=),
    cycle_start, compile_start, asm_done (delta = compile_start -> asm_done, the true
    per-assembly compile window; extra = assembly name + error count), compile_finish,
    reload_end, ready, cycle_end (extra carries lead=/compile=/reload=/ready=/e2e=
    seconds), sample (S1 no-op refresh), warm_compile (a compilation with no queue
    scenario), stale, expired, restore_failed, skip, done. The install compile (after
    config changes) is the config's warm-up cycle and carries no queue scenario. The
    install DELETES the log, so copy it out before re-running. Scenario files are
    restored with `git checkout -- Editor/ Runtime/` in the package repo after
    a run; Cleanup restores stock analyzer labels and removes the floor assets.
 */
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

public static class T7IterDriver
{
    private const string PackageAsset = "Packages/com.wallstop-studios.dxcommandterminal";

    private const string GeneratorDll =
        PackageAsset + "/Runtime/Analyzers/WallstopStudios.DxCommandTerminal.SourceGenerators.dll";

    private const string AnalyzerDll =
        PackageAsset + "/Runtime/Analyzers/WallstopStudios.DxCommandTerminal.Analyzers.dll";

    public static string Main(string config)
    {
        int samples = SampleCount(config);
        if (samples < 0)
        {
            return "Unknown T7 config: " + config + " (expected AB0/AB1/AB2/AB3/FLOOR)";
        }

        string scenario = config == "FLOOR" ? "S5" : "S2";
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string artDir = Path.Combine(
            projectRoot,
            "Packages",
            "com.wallstop-studios.dxcommandterminal",
            ".artifacts",
            "session-053"
        );
        Directory.CreateDirectory(artDir);
        string logPath = Path.Combine(artDir, "iter-log.tsv");
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }

        if (config == "FLOOR")
        {
            ProvisionFloor();
        }
        else
        {
            ApplyAnalyzerConfig(config);
        }

        File.WriteAllText(logPath, ConfigRow(config));

        StringBuilder queue = new StringBuilder();
        for (int i = 0; i < samples; i++)
        {
            queue.Append(scenario).Append('\n');
        }

        File.WriteAllText(Path.Combine(artDir, "iter-queue.tsv"), queue.ToString());
        File.WriteAllText(
            Path.Combine(artDir, "iter-install.tsv"),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        );

        string editorDir = Path.Combine(projectRoot, "Assets", "Editor");
        Directory.CreateDirectory(editorDir);
        File.WriteAllText(Path.Combine(editorDir, "T7IterProbe.cs"), ProbeSource);

        AssetDatabase.Refresh();
        double t0 = EditorApplication.timeSinceStartup;
        File.WriteAllText(
            Path.Combine(artDir, "iter-cycle.tsv"),
            "install\t"
                + t0.ToString(CultureInfo.InvariantCulture)
                + "\nwall\t"
                + DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                + "\n"
        );

        CompilationPipeline.RequestScriptCompilation();
        return "T7 probe installed, config="
            + config
            + " queue="
            + scenario
            + "x"
            + samples
            + ", compilation requested at "
            + t0.ToString(CultureInfo.InvariantCulture);
    }

    public static string Cleanup()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string editorDir = Path.Combine(projectRoot, "Assets", "Editor");
        string probe = Path.Combine(editorDir, "T7IterProbe.cs");
        string meta = probe + ".meta";
        if (File.Exists(probe))
        {
            File.Delete(probe);
        }

        if (File.Exists(meta))
        {
            File.Delete(meta);
        }

        if (
            Directory.Exists(editorDir)
            && Directory.GetFiles(editorDir).Length == 0
            && Directory.GetDirectories(editorDir).Length == 0
        )
        {
            Directory.Delete(editorDir, false);
            string editorMeta = editorDir + ".meta";
            if (File.Exists(editorMeta))
            {
                File.Delete(editorMeta);
            }
        }

        RemoveFloor();
        ApplyAnalyzerConfig("AB0");

        string artDir = Path.Combine(
            projectRoot,
            "Packages",
            "com.wallstop-studios.dxcommandterminal",
            ".artifacts",
            "session-053"
        );
        foreach (string name in new[] { "iter-queue.tsv", "iter-cycle.tsv", "iter-install.tsv" })
        {
            string path = Path.Combine(artDir, name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        AssetDatabase.Refresh();
        CompilationPipeline.RequestScriptCompilation();
        return "T7 probe removed, cleanup compilation requested";
    }

    private static int SampleCount(string config)
    {
        if (config == "AB0")
        {
            return 30;
        }

        if (config == "AB1" || config == "AB2" || config == "AB3")
        {
            return 12;
        }

        if (config == "FLOOR")
        {
            return 24;
        }

        return -1;
    }

    private static string ConfigRow(string config)
    {
        return "config\t"
            + config
            + "\t0.000\t-1.000\tgen="
            + HasAnalyzerLabel(GeneratorDll)
            + " ana="
            + HasAnalyzerLabel(AnalyzerDll)
            + "\n";
    }

    private static bool HasAnalyzerLabel(string assetPath)
    {
        PluginImporter importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
        return importer != null
            && Array.IndexOf(AssetDatabase.GetLabels(importer), "RoslynAnalyzer") >= 0;
    }

    private static void ApplyAnalyzerConfig(string config)
    {
        bool generator = config == "AB0" || config == "AB2";
        bool analyzers = config == "AB0" || config == "AB3";
        SetAnalyzerLabel(GeneratorDll, generator);
        SetAnalyzerLabel(AnalyzerDll, analyzers);
    }

    private static void SetAnalyzerLabel(string assetPath, bool labeled)
    {
        PluginImporter importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
        if (importer == null)
        {
            return;
        }

        string[] current = AssetDatabase.GetLabels(importer);
        bool has = Array.IndexOf(current, "RoslynAnalyzer") >= 0;
        if (has == labeled)
        {
            return;
        }

        if (labeled)
        {
            string[] grown = new string[current.Length + 1];
            Array.Copy(current, grown, current.Length);
            grown[current.Length] = "RoslynAnalyzer";
            AssetDatabase.SetLabels(importer, grown);
        }
        else
        {
            string[] trimmed = new string[current.Length - 1];
            int at = 0;
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] != "RoslynAnalyzer")
                {
                    trimmed[at] = current[i];
                    at++;
                }
            }

            AssetDatabase.SetLabels(importer, trimmed);
        }

        importer.SaveAndReimport();
    }

    private static void ProvisionFloor()
    {
        string dir = Path.Combine(Application.dataPath, "T7Floor");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "T7Floor.asmdef"), FloorAsmdef);
        File.WriteAllText(Path.Combine(dir, "T7Floor.cs"), FloorSource);
    }

    private static void RemoveFloor()
    {
        string dir = Path.Combine(Application.dataPath, "T7Floor");
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
            string dirMeta = Path.Combine(Application.dataPath, "T7Floor.meta");
            if (File.Exists(dirMeta))
            {
                File.Delete(dirMeta);
            }
        }
    }

    private const string FloorAsmdef = "{\n    \"name\": \"T7Floor\"\n}\n";

    private const string FloorSource = "internal static class FloorProbe { }\n";

    private const string ProbeSource =
        @"using System.Collections.Generic;
using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace T7
{
    [InitializeOnLoad]
    internal static class IterProbe
    {
        private const string PackageRoot = ""Packages/com.wallstop-studios.dxcommandterminal"";
        private const string ArtDir = PackageRoot + ""/.artifacts/session-053"";
        private const string QueuePath = ArtDir + ""/iter-queue.tsv"";
        private const string LogPath = ArtDir + ""/iter-log.tsv"";
        private const string CyclePath = ArtDir + ""/iter-cycle.tsv"";
        private const string InstallPath = ArtDir + ""/iter-install.tsv"";
        private const double MaxInstallAgeSeconds = 86400.0;
        private const double StaleCycleSeconds = 120.0;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string RuntimeFile =
            Resolve(""Runtime/CommandTerminal/Backend/Terminal.cs"");
        private static readonly string EditorFile =
            Resolve(""Editor/CustomEditors/TerminalUIEditor.cs"");
        private static readonly string DeclarationFile =
            Resolve(""Runtime/CommandTerminal/Backend/BuiltinCommands.cs"");
        private static readonly string FloorFile =
            Path.GetFullPath(Path.Combine(Application.dataPath, ""T7Floor"", ""T7Floor.cs""));
        private static readonly Queue<string> Queue = new Queue<string>();

        private static double _reloadEnd;
        private static double _compileStart;
        private static bool _readyLogged;
        private static bool _doneLogged;

        static IterProbe()
        {
            _reloadEnd = EditorApplication.timeSinceStartup;
            if (!File.Exists(InstallPath))
            {
                return;
            }

            if (!File.Exists(QueuePath))
            {
                return;
            }

            string[] installLines = File.ReadAllLines(InstallPath);
            double installed;
            if (
                installLines.Length == 0
                || !double.TryParse(
                    installLines[0],
                    NumberStyles.Float,
                    Inv,
                    out installed)
            )
            {
                return;
            }

            double age = (double)(DateTimeOffset.UtcNow.ToUnixTimeSeconds()) - installed;
            if (age > MaxInstallAgeSeconds)
            {
                Log(""expired"", ""-"", _reloadEnd, age);
                try
                {
                    RestoreHostOnExpiry();
                }
                catch (Exception ex)
                {
                    Log(""restore_failed"", ""-"", _reloadEnd, -1.0, ex.Message);
                }

                ForgetState();
                DeleteSelf();
                AssetDatabase.Refresh();
                return;
            }

            CycleState cycle = ReadCycle();
            double wallAge = WallAgeSeconds();
            if (wallAge >= 0.0 && wallAge > StaleCycleSeconds)
            {
                Log(""stale"", cycle.Scenario, _reloadEnd, wallAge);
                ClearCycle();
                cycle = default;
            }

            if (cycle.T0 > 0.0 && cycle.CompileFinish > 0.0)
            {
                Log(""reload_end"", cycle.Scenario, _reloadEnd, _reloadEnd - cycle.CompileFinish);
            }

            RestoreQueue();
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            EditorApplication.update += Pump;
        }

        private static void OnCompilationStarted(object _)
        {
            _compileStart = EditorApplication.timeSinceStartup;
            CycleState cycle = ReadCycle();
            if (cycle.T0 <= 0.0)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            Log(""compile_start"", cycle.Scenario, now, now - cycle.T0);
            File.AppendAllText(
                CyclePath,
                ""compile_start\t"" + now.ToString(""F3"", Inv) + ""\n"");
        }

        private static void OnCompilationFinished(object _)
        {
            double now = EditorApplication.timeSinceStartup;
            double started = _compileStart;
            _compileStart = 0.0;
            CycleState cycle = ReadCycle();
            if (cycle.T0 <= 0.0)
            {
                if (started > 0.0)
                {
                    Log(""warm_compile"", ""-"", now, now - started);
                }

                return;
            }

            Log(""compile_finish"", cycle.Scenario, now, now - cycle.T0);
            File.AppendAllText(
                CyclePath,
                ""compile_finish\t"" + now.ToString(""F3"", Inv) + ""\n"");
        }

        private static void OnAssemblyCompilationFinished(
            string assembly, CompilerMessage[] messages)
        {
            double now = EditorApplication.timeSinceStartup;
            string name = assembly;
            int slash = name.LastIndexOf('/');
            if (0 <= slash)
            {
                name = name.Substring(slash + 1);
            }

            int errors = 0;
            for (int i = 0; i < messages.Length; i++)
            {
                if (messages[i].type == CompilerMessageType.Error)
                {
                    errors++;
                }
            }

            double window = _compileStart > 0.0 ? now - _compileStart : -1.0;
            CycleState cycle = ReadCycle();
            string scenario = cycle.T0 > 0.0 ? cycle.Scenario : ""-"";
            Log(
                ""asm_done"",
                scenario,
                now,
                window,
                name + "" errors="" + errors.ToString(Inv));
        }

        private static void Pump()
        {
            double now = EditorApplication.timeSinceStartup;
            if (!_readyLogged)
            {
                _readyLogged = true;
                CycleState pending = ReadCycle();
                if (pending.T0 > 0.0)
                {
                    Log(""ready"", pending.Scenario, now, now - _reloadEnd);
                }
            }

            CycleState cycle = ReadCycle();
            if (cycle.T0 > 0.0 && cycle.CompileFinish > 0.0 && cycle.CompileFinish < _reloadEnd)
            {
                double lead = cycle.CompileStart > 0.0
                    ? cycle.CompileStart - cycle.T0
                    : -1.0;
                double compile = cycle.CompileStart > 0.0
                    ? cycle.CompileFinish - cycle.CompileStart
                    : -1.0;
                double reload = _reloadEnd - cycle.CompileFinish;
                double e2e = now - cycle.T0;
                Log(
                    ""cycle_end"",
                    cycle.Scenario,
                    now,
                    -1.0,
                    ""lead="" + lead.ToString(""F3"", Inv)
                        + "" compile="" + compile.ToString(""F3"", Inv)
                        + "" reload="" + reload.ToString(""F3"", Inv)
                        + "" ready="" + (now - _reloadEnd).ToString(""F3"", Inv)
                        + "" e2e="" + e2e.ToString(""F3"", Inv));
                ClearCycle();
            }
            else if (cycle.T0 > 0.0 && cycle.CompileFinish <= 0.0 && cycle.T0 < _reloadEnd)
            {
                ClearCycle();
            }
            else if (cycle.T0 > 0.0 && now - cycle.T0 > StaleCycleSeconds)
            {
                Log(""stale"", cycle.Scenario, now, now - cycle.T0);
                ClearCycle();
            }
            else if (cycle.T0 > 0.0)
            {
                return;
            }

            if (_compileStart > 0.0)
            {
                return;
            }

            if (Queue.Count == 0)
            {
                if (!_doneLogged)
                {
                    _doneLogged = true;
                    Log(""done"", ""-"", now, 0.0);
                    ForgetState();
                    CompilationPipeline.compilationStarted -= OnCompilationStarted;
                    CompilationPipeline.compilationFinished -= OnCompilationFinished;
                    CompilationPipeline.assemblyCompilationFinished -=
                        OnAssemblyCompilationFinished;
                    EditorApplication.update -= Pump;
                }

                return;
            }

            string scenario = Queue.Dequeue();
            SaveQueue();
            if (scenario == ""S1"")
            {
                double a = EditorApplication.timeSinceStartup;
                AssetDatabase.Refresh();
                double b = EditorApplication.timeSinceStartup;
                Log(""sample"", scenario, b, b - a);
                return;
            }

            string target;
            if (scenario == ""S2"") { target = RuntimeFile; }
            else if (scenario == ""S3"") { target = EditorFile; }
            else if (scenario == ""S4"") { target = DeclarationFile; }
            else if (scenario == ""S5"") { target = FloorFile; }
            else
            {
                Log(""skip"", scenario, now, 0.0);
                return;
            }

            File.AppendAllText(target, ""\n"");
            double t0 = EditorApplication.timeSinceStartup;
            WriteCycle(scenario, t0);
            Log(""cycle_start"", scenario, t0, 0.0);
            AssetDatabase.Refresh();
        }

        private static void Log(
            string eventName, string scenario, double time, double delta)
        {
            Log(eventName, scenario, time, delta, null);
        }

        private static void Log(
            string eventName,
            string scenario,
            double time,
            double delta,
            string extra)
        {
            string line = eventName
                + ""\t"" + scenario
                + ""\t"" + time.ToString(""F3"", Inv)
                + ""\t"" + delta.ToString(""F3"", Inv);
            if (!string.IsNullOrEmpty(extra))
            {
                line += ""\t"" + extra;
            }

            File.AppendAllText(LogPath, line + ""\n"");
        }

        private static CycleState ReadCycle()
        {
            if (!File.Exists(CyclePath))
            {
                return default;
            }

            string[] lines = File.ReadAllLines(CyclePath);
            CycleState state = default;
            bool headParsed = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i]))
                {
                    continue;
                }

                string[] parts = lines[i].Split('\t');
                double value;
                if (
                    parts.Length != 2
                    || !double.TryParse(parts[1], NumberStyles.Float, Inv, out value)
                )
                {
                    continue;
                }

                if (!headParsed)
                {
                    headParsed = true;
                    state.Scenario = parts[0];
                    state.T0 = value;
                }
                else if (parts[0] == ""compile_start"")
                {
                    state.CompileStart = value;
                }
                else if (parts[0] == ""compile_finish"")
                {
                    state.CompileFinish = value;
                }
            }

            return state;
        }

        private static void WriteCycle(string scenario, double t0)
        {
            File.WriteAllText(
                CyclePath,
                scenario
                    + ""\t"" + t0.ToString(""F3"", Inv)
                    + ""\nwall\t"" + DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    + ""\n"");
        }

        private static double WallAgeSeconds()
        {
            if (!File.Exists(CyclePath))
            {
                return -1.0;
            }

            string[] lines = File.ReadAllLines(CyclePath);
            for (int i = 0; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('\t');
                double epoch;
                if (
                    parts.Length == 2
                    && parts[0] == ""wall""
                    && double.TryParse(parts[1], NumberStyles.Float, Inv, out epoch)
                )
                {
                    return (double)(DateTimeOffset.UtcNow.ToUnixTimeSeconds()) - epoch;
                }
            }

            return -1.0;
        }

        private static void RestoreHostOnExpiry()
        {
            string floorDir = Path.Combine(Application.dataPath, ""T7Floor"");
            if (Directory.Exists(floorDir))
            {
                Directory.Delete(floorDir, true);
            }

            string floorMeta = Path.Combine(Application.dataPath, ""T7Floor.meta"");
            if (File.Exists(floorMeta))
            {
                File.Delete(floorMeta);
            }

            SetLabel(
                AssetImporter.GetAtPath(
                    PackageRoot + ""/Runtime/Analyzers/WallstopStudios.DxCommandTerminal.SourceGenerators.dll""),
                true);
            SetLabel(
                AssetImporter.GetAtPath(
                    PackageRoot + ""/Runtime/Analyzers/WallstopStudios.DxCommandTerminal.Analyzers.dll""),
                true);
        }

        private static void SetLabel(AssetImporter importer, bool labeled)
        {
            if (importer == null)
            {
                return;
            }

            string[] current = AssetDatabase.GetLabels(importer);
            bool has = Array.IndexOf(current, ""RoslynAnalyzer"") >= 0;
            if (has == labeled)
            {
                return;
            }

            if (labeled)
            {
                string[] grown = new string[current.Length + 1];
                Array.Copy(current, grown, current.Length);
                grown[current.Length] = ""RoslynAnalyzer"";
                AssetDatabase.SetLabels(importer, grown);
            }
            else
            {
                string[] trimmed = new string[current.Length - 1];
                int at = 0;
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i] != ""RoslynAnalyzer"")
                    {
                        trimmed[at] = current[i];
                        at++;
                    }
                }

                AssetDatabase.SetLabels(importer, trimmed);
            }

            ((PluginImporter)importer).SaveAndReimport();
        }

        private static void ClearCycle()
        {
            if (File.Exists(CyclePath))
            {
                File.Delete(CyclePath);
            }
        }

        private static void ForgetState()
        {
            foreach (string path in new[] { QueuePath, CyclePath, InstallPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static void DeleteSelf()
        {
            string editorDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, ""Editor""));
            string probe = Path.Combine(editorDir, ""T7IterProbe.cs"");
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }

            if (File.Exists(probe + "".meta""))
            {
                File.Delete(probe + "".meta"");
            }

            if (
                Directory.Exists(editorDir)
                && Directory.GetFiles(editorDir).Length == 0
                && Directory.GetDirectories(editorDir).Length == 0
            )
            {
                Directory.Delete(editorDir, false);
                if (File.Exists(editorDir + "".meta""))
                {
                    File.Delete(editorDir + "".meta"");
                }
            }
        }

        private static void RestoreQueue()
        {
            if (!File.Exists(QueuePath))
            {
                return;
            }

            string[] lines = File.ReadAllLines(QueuePath);
            foreach (string line in lines)
            {
                if (!string.IsNullOrEmpty(line))
                {
                    Queue.Enqueue(line);
                }
            }
        }

        private static void SaveQueue()
        {
            File.WriteAllLines(QueuePath, new List<string>(Queue).ToArray());
        }

        private static string Resolve(string relative)
        {
            return Path.GetFullPath(
                Path.Combine(Application.dataPath, "".."", PackageRoot, relative));
        }

        private struct CycleState
        {
            public string Scenario;
            public double T0;
            public double CompileStart;
            public double CompileFinish;
        }
    }
}
";
}
