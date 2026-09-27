/*
    A stand-in for the Unity types DxTerminalTestRunReporter uses, so the real
    file compiles and runs in CI without an editor (issue #167).

    Every mirrored type here carries only members the real API has, with the real
    signatures, and is a subset of what the real type declares. That is what makes
    the stand-in a gate rather than a mock: a member the reporter starts using is
    a compile error here until it is declared, and a member the real API lacks
    cannot be reached at all. The Test Runner half was checked against the
    metadata of the host editor's own UnityEditor.TestRunner.dll (6000.4.6f1) and
    against the package's source for both the 2021.3 floor and Unity 6:
    ITestResultAdaptor does not derive from ITestAdaptor there, it repeats Name
    and FullName, and RegisterCallbacks is a generic method that appends to a
    list - so none of those are approximated here either.

    What the gate cannot answer is whether the real editor accepts the file on a
    given version. That is #164's clean-project matrix, which compiles both dev
    tools in a real project.

    The seams the harness needs - a project root, the warnings raised, the
    callbacks registered - are on TestProject at the end of this file, which is
    not a Unity type. A mirrored type therefore has nothing on it that the editor
    does not also have, so a reporter reaching for one is reaching for something
    the editor does not have either.

    One file holds every type because this is a stand-in for a foreign assembly's
    surface, not package code. The production conventions - one top-level type per
    file, a member ordering per type - have no meaning for shims of foreign types.
    The reporter is held to all of them; these files are held to CSharpier, which
    is what the pre-commit hook runs on them.
 */
namespace UnityEngine
{
    using System;
    using DxTerminalDevTools.Grammar;

    public class Object
    {
        public HideFlags hideFlags { get; set; }
    }

    public enum HideFlags
    {
        None = 0,
        HideInHierarchy = 1,
        HideInInspector = 2,
        DontSaveInEditor = 4,
        NotEditable = 8,
        DontSaveInBuild = 16,
        DontUnloadUnusedAsset = 32,
        DontSave = 52,
        HideAndDontSave = 61,
    }

    public class ScriptableObject : Object
    {
        public static ScriptableObject CreateInstance(Type type)
        {
            return (ScriptableObject)Activator.CreateInstance(type);
        }

        public static T CreateInstance<T>()
            where T : ScriptableObject
        {
            return (T)Activator.CreateInstance(typeof(T));
        }
    }

    public static class Application
    {
        /// <summary>
        /// The editor's own project root, which a stand-in cannot know and the
        /// real API only reads. The seam is on TestProject, so nothing here is
        /// writable that is not writable in the editor.
        /// </summary>
        public static string dataPath => TestProject.DataPath;
    }

    public static class Debug
    {
        public static void LogWarning(object message)
        {
            TestProject.Warnings.Add(message?.ToString() ?? string.Empty);
        }
    }
}

namespace UnityEditor
{
    using System;

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class InitializeOnLoadMethodAttribute : Attribute { }
}

namespace UnityEditor.TestTools.TestRunner.Api
{
    using System;
    using System.Collections.Generic;
    using DxTerminalDevTools.Grammar;
    using UnityEngine;

    /// <summary>
    /// A flag with 1 &lt;&lt; n values in the real API, so a mode is a mask and
    /// not a count.
    /// </summary>
    [Flags]
    public enum TestMode
    {
        EditMode = 1 << 0,
        PlayMode = 1 << 1,
    }

    /*
        TestStatus and HideFlags keep the real ordinals, where zero is a real
        state (Inconclusive, None) rather than a sentinel. The repository rule
        that wants an Unknown = 0 is for this package's own state enums; adding a
        member the editor does not have would make this a mirror of something
        else.
     */
    public enum TestStatus
    {
        Inconclusive = 0,
        Skipped = 1,
        Passed = 2,
        Failed = 3,
    }

    public interface ITestAdaptor
    {
        string Id { get; }
        string Name { get; }
        string FullName { get; }
        bool IsSuite { get; }
        bool HasChildren { get; }
        IEnumerable<ITestAdaptor> Children { get; }
        TestMode TestMode { get; }
    }

    public interface ITestResultAdaptor
    {
        string Name { get; }
        string FullName { get; }
        ITestAdaptor Test { get; }
        TestStatus TestStatus { get; }
        double Duration { get; }
        int PassCount { get; }
        int FailCount { get; }
        int SkipCount { get; }
        int InconclusiveCount { get; }
        bool HasChildren { get; }
        IEnumerable<ITestResultAdaptor> Children { get; }
    }

    public interface ICallbacks
    {
        void RunStarted(ITestAdaptor testsToRun);
        void RunFinished(ITestResultAdaptor result);
        void TestStarted(ITestAdaptor test);
        void TestFinished(ITestResultAdaptor result);
    }

    public class TestRunnerApi : ScriptableObject
    {
        public void RegisterCallbacks<T>(T testCallbacks, int priority = 0)
            where T : ICallbacks
        {
            TestProject.Registered.Add(testCallbacks);
        }
    }
}

namespace DxTerminalDevTools.Grammar
{
    using System.Collections.Generic;
    using UnityEditor.TestTools.TestRunner.Api;

    /*
        The seams the harness drives, none of them Unity types and none of them
        reachable from the reporter, which names only the mirrored ones.
     */
    public static class TestProject
    {
        public static string DataPath { get; set; } = string.Empty;

        public static List<string> Warnings { get; } = new();

        /// <summary>
        /// Every callback the reporter registered, in order. The real API keeps a
        /// priority-ordered list; a domain reload empties it and the editor
        /// registers again, which is what the reload case models.
        /// </summary>
        public static List<ICallbacks> Registered { get; } = new();
    }
}
