/*
    Compile-only stand-in for UnityEngine.TestRunner's LogAssert (issue
    #193). The UnityEngine.Modules package does not carry the test
    runner assembly, but two Tests/Editor files call LogAssert.Expect,
    and the point of tests-2021.3.csproj is that *every* Tests/Editor
    file compiles against the pinned 2021.3.33 assemblies.

    This file is never executed: the fixtures that call LogAssert
    assert against the editor's own log pump, which exists only inside
    a live editor, and the testhost excludes them by name.
 */
namespace UnityEngine.TestTools
{
    using System;
    using System.Text.RegularExpressions;

    public static class LogAssert
    {
        public static bool ignoreFailingMessages
        {
            get =>
                throw new InvalidOperationException(
                    "LogAssert is compile-only in the Unity-free lane; run this fixture in a live editor."
                );
            set =>
                throw new InvalidOperationException(
                    "LogAssert is compile-only in the Unity-free lane; run this fixture in a live editor."
                );
        }

        public static void Expect(LogType type, string message)
        {
            throw new InvalidOperationException(
                "LogAssert is compile-only in the Unity-free lane; run this fixture in a live editor."
            );
        }

        public static void Expect(LogType type, Regex message)
        {
            throw new InvalidOperationException(
                "LogAssert is compile-only in the Unity-free lane; run this fixture in a live editor."
            );
        }

        public static void NoUnexpectedReceived()
        {
            throw new InvalidOperationException(
                "LogAssert is compile-only in the Unity-free lane; run this fixture in a live editor."
            );
        }
    }
}
