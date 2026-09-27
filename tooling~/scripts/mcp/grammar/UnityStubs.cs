/*
    A stand-in for the Unity types DxTerminalTestRunReporter uses, so the real
    file compiles and runs in CI without an editor (issue #167).

    Only the surface the reporter touches is declared, with the real
    signatures. That is deliberate in both directions: a member the reporter
    starts using is a compile error here, which is the safe direction; and a
    member declared here that the real API does not have cannot hide a defect,
    because the reporter never calls one. What this cannot answer is whether
    the real editor accepts the file on a given version - that is #164's
    clean-project matrix, which compiles both dev tools in a real project.

    One file holds every type because this is a stand-in for a foreign
    assembly's surface, not package code. The production conventions
    (`Runtime/`, `Editor/`, `Generator~/`) are one top-level type per file, an
    explicit accessibility on every member, and a member ordering per type; the
    reporter itself is held to all of them and the harness is held to the
    formatting, but the type-per-file rule has no meaning for eleven shims of
    eleven different foreign types.
 */
namespace UnityEngine
{
    using System;

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
        /// The editor's own project root, which the stand-in cannot know. The
        /// harness points it at a temporary directory, so a claim written by a
        /// test run never lands in a real project's artifacts.
        /// </summary>
        public static string dataPath { get; set; } = string.Empty;
    }

    public static class Debug
    {
        /// <summary>
        /// Every warning the reporter raises, so the harness can assert that the
        /// degrade paths ran and that nothing else did.
        /// </summary>
        public static System.Collections.Generic.List<string> Warnings { get; } = new();

        public static void LogWarning(object message)
        {
            Warnings.Add(message?.ToString() ?? string.Empty);
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
    using System.Collections.Generic;
    using UnityEngine;

    public enum TestMode
    {
        EditMode = 0,
        PlayMode = 1,
    }

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
        TestMode TestMode { get; }
    }

    public interface ITestResultAdaptor : ITestAdaptor
    {
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
        /// <summary>
        /// What the editor would have kept. The real API registers the
        /// callbacks with the test runner and holds them; nothing here can run a
        /// suite, so the instance is handed back instead.
        /// </summary>
        public static ICallbacks Registered { get; set; }

        public void RegisterCallbacks(ICallbacks testCallbacks, int priority = 0)
        {
            Registered = testCallbacks;
        }
    }
}
