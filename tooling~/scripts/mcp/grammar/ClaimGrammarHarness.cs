/*
    Drives the real DxTerminalTestRunReporter and records what it wrote
    (issue #167). The file under test is linked into this project, so this is
    the reporter's own path: the callbacks the editor invokes, the claim files
    in the reporter's own layout, and the encoder and result walk reached only
    through RunFinished - the way a real run reaches them.

    What is checked here, and what is left to the node side:

    - C#: the claim line itself. The counters, token and mode are the ones the
      result tree describes; a red run names no more than the cap and reports
      how many it did not name; a run that named nothing writes no names field;
      a run nobody asked for is attributed to nobody; a run that ran no tests is
      a refusal. The line must stay ASCII, so no name can break it from inside.
    - node (grammar.test.mjs): that each claim decodes back to the exact names
      that went in. The decoder is JavaScript, so no C# can check it, and the
      two halves agreeing is the whole contract. That is also where a suite
      reported as a failed test, or a name mangled on the way, is caught.

    Every assertion reads the claim the reporter wrote, never a string this file
    assembled from the reporter's constants: a pin on the source cannot catch a
    behavior change, and catching that is what these replaced.
 */
namespace DxTerminalDevTools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using UnityEditor.TestTools.TestRunner.Api;
    using UnityEngine;

    internal static class ClaimGrammarHarness
    {
        private const string Fixture = "Wallstop.Fixture";
        private const int ReportedCap = 10;
        private const char Separator = ',';

        private static readonly List<string> Failures = new();

        public static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("usage: DxTerminalClaimGrammar <claims.json>");
                return 2;
            }

            string project = Path.Combine(
                Path.GetTempPath(),
                "dxt-grammar-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(Path.Combine(project, "Assets"));
            Application.dataPath = Path.Combine(project, "Assets");

            List<ClaimExpectation> expectations = new();
            try
            {
                DxTerminalTestRunReporter.RegisterCallbacks();
                ICallbacks callbacks = TestRunnerApi.Registered;
                if (callbacks is null)
                {
                    Console.Error.WriteLine("the reporter registered no callbacks");
                    return 1;
                }

                RunNobodyAskedFor(callbacks, expectations);
                RunThatExecutedNoTests(callbacks, expectations);
                RunGreen(callbacks, expectations);
                RunRedOverTheCap(callbacks, expectations);
                RunAcrossADomainReload(callbacks, expectations);
                RunWithAThrowingResultTree(callbacks, expectations);
                RunEveryPrintableAsciiCharacter(callbacks, expectations);
                RunCharactersOutsideAscii(callbacks, expectations);
                WriteJson(args[0], expectations);
            }
            catch (Exception exception)
            {
                Failures.Add($"the harness threw: {exception}");
            }
            finally
            {
                Directory.Delete(project, true);
            }

            foreach (string failure in Failures)
            {
                Console.Error.WriteLine(failure);
            }

            if (0 < Failures.Count)
            {
                Console.Error.WriteLine($"{Failures.Count} grammar check(s) failed.");
                return 1;
            }

            return 0;
        }

        /*
            Nobody asked for this run. It is still reported, but as nobody's: a
            late callback must never adopt the token of the next request, and
            the reader is looking for a token of its own.
         */
        private static void RunNobodyAskedFor(
            ICallbacks callbacks,
            List<ClaimExpectation> expectations
        )
        {
            File.Delete(DxTerminalTestRunReporter.RequestPath());
            string name = $"{Fixture}.Unattributed";
            callbacks.RunStarted(new FakeTest(Fixture, isSuite: true));
            Require(
                ReadClaim().Contains("did-not-run token=none", StringComparison.Ordinal),
                "a run nobody asked for is refused at the start, not acknowledged"
            );
            callbacks.RunFinished(
                new FakeTest(
                    Fixture,
                    isSuite: true,
                    failed: 1,
                    children: new List<FakeTest> { new FakeTest(name, TestStatus.Failed) }
                )
            );
            expectations.Add(
                Expect("a run nobody asked for", new[] { "token=none", "fail=1" }, new[] { name })
            );
        }

        /*
            A run that matched nothing. The counters are all zero, so there is no
            result to report, and a claim that looked finished with no tests in
            it would read as a green run.
         */
        private static void RunThatExecutedNoTests(
            ICallbacks callbacks,
            List<ClaimExpectation> expectations
        )
        {
            Request("grammar-empty");
            StartRun(callbacks, "grammar-empty");
            callbacks.RunFinished(new FakeTest(Fixture, isSuite: true));
            expectations.Add(
                Expect(
                    "a run that executed no tests",
                    new[]
                    {
                        "did-not-run token=grammar-empty mode=EditMode",
                        "reason=the run executed no tests",
                    }
                )
            );
        }

        /*
            A green run writes the line it always wrote: no names field, so a
            reader keying on the field learns nothing new about a green run.
         */
        private static void RunGreen(ICallbacks callbacks, List<ClaimExpectation> expectations)
        {
            Request("grammar-green");
            List<FakeTest> children = new()
            {
                new FakeTest($"{Fixture}.One.TestAlpha", TestStatus.Passed),
                new FakeTest($"{Fixture}.One.TestBeta", TestStatus.Passed),
                new FakeTest($"{Fixture}.One.TestGamma", TestStatus.Skipped),
            };
            StartRun(callbacks, "grammar-green", TestMode.PlayMode);
            callbacks.RunFinished(
                new FakeTest(Fixture, isSuite: true, passed: 2, skipped: 1, children: children)
            );

            ClaimExpectation expectation = Expect(
                "a green run",
                new[]
                {
                    "pass=2 fail=0 skipped=1 inconclusive=0",
                    // The duration and the two timestamps are for whoever reads
                    // the file after a failed run; the first is a number a
                    // reader can parse, so the separator is fixed too.
                    "duration=0.5",
                    "token=grammar-green",
                    "mode=PlayMode",
                    "finished=",
                }
            );
            Require(
                !expectation.Claim.Contains("failed-names", StringComparison.Ordinal),
                $"a green run must write no names field, got: {expectation.Claim}"
            );
            expectations.Add(expectation);
        }

        /*
            The red run, shaped so every rule the reporter states is observable:
            a failed fixture is walked and must not be named, a childless failed
            node is still a suite and must not be named either, a result whose
            test never arrived has no name to give, and more failures than the
            cap become the cap plus a count of the rest.
         */
        private static void RunRedOverTheCap(
            ICallbacks callbacks,
            List<ClaimExpectation> expectations
        )
        {
            List<string> named = new() { $"{Fixture}.Alpha.One", $"{Fixture}.Beta.One" };
            List<FakeTest> children = new()
            {
                new FakeTest(
                    $"{Fixture}.Alpha",
                    TestStatus.Failed,
                    children: new List<FakeTest>
                    {
                        new FakeTest($"{Fixture}.Alpha.Skipped", TestStatus.Skipped),
                        new FakeTest($"{Fixture}.Alpha.One", TestStatus.Failed),
                    }
                ),
                new FakeTest(
                    $"{Fixture}.Beta",
                    TestStatus.Failed,
                    children: new List<FakeTest>
                    {
                        new FakeTest($"{Fixture}.Beta.One", TestStatus.Failed),
                    }
                ),
                // A childless failed node is still a suite, and reporting it
                // would point the reader at the fixture instead of the test.
                new FakeTest($"{Fixture}.Childless", TestStatus.Failed, isSuite: true),
                // A result whose test never arrived: no name to report.
                new FakeTest(null, TestStatus.Failed, hasTest: false),
            };
            for (int index = 0; index < 12; ++index)
            {
                string name = $"{Fixture}.Bulk{index:00}";
                children.Add(new FakeTest(name, TestStatus.Failed));
                named.Add(name);
            }

            Request("grammar-red");
            StartRun(callbacks, "grammar-red");
            callbacks.RunFinished(
                new FakeTest(Fixture, isSuite: true, failed: named.Count, children: children)
            );

            expectations.Add(
                Expect(
                    "a red run over the cap",
                    new[]
                    {
                        $"pass=0 fail={named.Count} skipped=0 inconclusive=0",
                        "token=grammar-red",
                        "mode=EditMode",
                        $"failed-more={named.Count - ReportedCap}",
                    },
                    // Tree order, which is depth first: the two names inside the
                    // failed fixtures come before the bulk leaves, and the
                    // suites themselves are never among them.
                    named.GetRange(0, ReportedCap).ToArray()
                )
            );
        }

        /*
            Every printable ASCII character in a name, one run per name because
            the cap is ten. The claim line is space separated, its fields are
            key=value and the names are split on a comma, so each of those three
            characters is a delimiter a name may hold.
         */
        private static void RunEveryPrintableAsciiCharacter(
            ICallbacks callbacks,
            List<ClaimExpectation> expectations
        )
        {
            for (int code = 0x20; code <= 0x7F; ++code)
            {
                char character = (char)code;
                string name = $"{Fixture}.Char{code:X2}({character})";
                RunNames(
                    callbacks,
                    "grammar-char",
                    "character U+" + code.ToString("X4", CultureInfo.InvariantCulture),
                    new[] { name },
                    expectations
                );
            }
        }

        /*
            The characters a byte-wise UTF-8 encoder gets wrong: a no-break space
            and an em space read as a space, a line separator ends the line, an
            emoji is a surrogate pair, and a bare percent is the escape itself.
         */
        private static void RunCharactersOutsideAscii(
            ICallbacks callbacks,
            List<ClaimExpectation> expectations
        )
        {
            string[] names =
            {
                $"{Fixture}.NoBreakSpace({'\u00A0'})",
                $"{Fixture}.EmSpace({'\u2003'})",
                $"{Fixture}.LineSeparator({'\u2028'})",
                $"{Fixture}.ParagraphSeparator({'\u2029'})",
                $"{Fixture}.Percent(100%)",
                $"{Fixture}.Quote(\"a b\")",
                $"{Fixture}.Equals(a=b)",
                $"{Fixture}.Comma(a,b)",
                $"{Fixture}.Newline(a\nb)",
                $"{Fixture}.Tab(a\tb)",
                $"{Fixture}.Emoji(\U0001F600)",
                $"{Fixture}.Mixed(  ,=%\"\u2028\u00A0)",
            };

            // One run per cap-sized group, so every name is in a claim the
            // reader can see rather than in the count the cap left out.
            for (int start = 0; start < names.Length; start += ReportedCap)
            {
                List<string> group = new();
                for (
                    int index = start;
                    index < names.Length && index < start + ReportedCap;
                    ++index
                )
                {
                    group.Add(names[index]);
                }

                RunNames(
                    callbacks,
                    "grammar-unicode",
                    "characters outside ASCII",
                    group.ToArray(),
                    expectations
                );
            }
        }

        /*
            A Play Mode run reloads the domain between the start and the finish,
            so the instance that sees the finish is a new one with nothing in
            memory. Registering again and finishing from the new instance is
            what that looks like, and the attribution has to survive it: a run
            that reported itself as somebody else's, or as nobody's, would be
            discarded by the caller waiting for it.
         */
        private static void RunAcrossADomainReload(
            ICallbacks callbacks,
            List<ClaimExpectation> expectations
        )
        {
            string name = $"{Fixture}.Reload.One";
            Request("grammar-reload");
            StartRun(callbacks, "grammar-reload");
            DxTerminalTestRunReporter.RegisterCallbacks();
            ICallbacks reloaded = TestRunnerApi.Registered;
            Require(
                !ReferenceEquals(callbacks, reloaded),
                "registering again must hand back a new instance, or this case proves nothing"
            );
            reloaded.RunFinished(
                new FakeTest(
                    Fixture,
                    isSuite: true,
                    failed: 1,
                    children: new List<FakeTest> { new FakeTest(name, TestStatus.Failed) }
                )
            );
            expectations.Add(
                Expect(
                    "a run across a domain reload",
                    new[] { "token=grammar-reload", "mode=EditMode", "fail=1" },
                    new[] { name }
                )
            );
        }

        /*
            A result tree the walk cannot survive. The walk reads a tree this
            code does not own, so an exception there must cost the names and not
            the claim: the counters are the part a caller has no other way to
            get. Without the guard the exception leaves RunFinished and the
            caller is left with no line at all.
         */
        private static void RunWithAThrowingResultTree(
            ICallbacks callbacks,
            List<ClaimExpectation> expectations
        )
        {
            Request("grammar-throwing");
            int warningsBefore = Debug.Warnings.Count;
            StartRun(callbacks, "grammar-throwing");
            callbacks.RunFinished(
                new FakeTest(
                    Fixture,
                    isSuite: true,
                    passed: 4,
                    failed: 2,
                    children: new List<FakeTest>
                    {
                        new FakeTest($"{Fixture}.Alpha.One", TestStatus.Failed),
                        new FakeTest(
                            $"{Fixture}.Broken",
                            TestStatus.Failed,
                            throwsOnChildren: true
                        ),
                    }
                )
            );

            ClaimExpectation expectation = Expect(
                "a result tree the walk cannot survive",
                new[] { "pass=4 fail=2 skipped=0 inconclusive=0", "token=grammar-throwing" }
            );
            Require(
                !expectation.Claim.Contains("failed-names", StringComparison.Ordinal),
                $"a degraded walk must name nothing rather than half a tree, got: {expectation.Claim}"
            );
            int raised = Debug.Warnings.Count - warningsBefore;
            Require(raised == 1, $"the degrade path must be reported once, not {raised} times");
            if (raised == 1)
            {
                string warning = Debug.Warnings[warningsBefore];
                Require(
                    warning.Contains("walk failed", StringComparison.Ordinal)
                        && warning.Contains("broken tree", StringComparison.Ordinal),
                    $"the warning must name the walk and the failure, got: {warning}"
                );
            }

            expectations.Add(expectation);
        }

        /*
            One claim per group of names, every one of them failed, so the
            names the reader gets back are exactly the group.
         */
        private static void RunNames(
            ICallbacks callbacks,
            string token,
            string label,
            string[] names,
            List<ClaimExpectation> expectations
        )
        {
            List<FakeTest> children = new();
            foreach (string name in names)
            {
                children.Add(new FakeTest(name, TestStatus.Failed));
            }

            Request(token);
            StartRun(callbacks, token);
            callbacks.RunFinished(
                new FakeTest(Fixture, isSuite: true, failed: names.Length, children: children)
            );
            expectations.Add(
                Expect(label, new[] { $"token={token}", $"fail={names.Length}" }, names)
            );
        }

        /*
            Start a run and check the acknowledgement. The caller waits for the
            running line and gives up on a grace period when it does not arrive,
            so a claim it cannot see is a false diagnosis rather than a slow
            run: the running line is part of the grammar, not a courtesy.
         */
        private static void StartRun(
            ICallbacks callbacks,
            string token,
            TestMode mode = TestMode.EditMode
        )
        {
            callbacks.RunStarted(new FakeTest(Fixture, isSuite: true, mode: mode));
            string claim = ReadClaim().Trim();
            Require(
                claim.Contains($"running token={token} mode={mode}", StringComparison.Ordinal),
                $"the start must be acknowledged with its own token, got: {claim}"
            );
            Require(
                claim.Contains("started=", StringComparison.Ordinal),
                $"the acknowledgement must carry its timestamp, got: {claim}"
            );
        }

        private static string ReadClaim()
        {
            return File.ReadAllText(DxTerminalTestRunReporter.ClaimPath()).Trim();
        }

        private static void Request(string token)
        {
            File.WriteAllText(
                DxTerminalTestRunReporter.RequestPath(),
                token + Environment.NewLine,
                new UTF8Encoding(false)
            );
        }

        /*
            One claim, read back from the file the reporter wrote, checked
            against the fragments the grammar owes it and the names it must
            carry. Whether those names decode back is the node side's business.
         */
        private static ClaimExpectation Expect(
            string label,
            string[] fragments,
            string[] names = null
        )
        {
            string claim = ReadClaim();
            foreach (string fragment in fragments)
            {
                Require(
                    claim.Contains(fragment, StringComparison.Ordinal),
                    $"{label}: the claim must carry '{fragment}', got: {claim}"
                );
            }

            Require(IsAscii(claim), $"{label}: the claim must stay ASCII, got: {claim}");

            string field = Field(claim, "failed-names");
            if (names is null)
            {
                Require(
                    field.Length == 0,
                    $"{label}: a claim that names nothing must write no field, got: {claim}"
                );
            }
            else
            {
                string[] parts = field.Split(Separator);
                Require(
                    parts.Length == names.Length,
                    $"{label}: {names.Length} names must be {parts.Length} fields, got: {claim}"
                );
                foreach (string part in parts)
                {
                    Require(
                        part.Length > 0,
                        $"{label}: a name must not encode to nothing, got: {claim}"
                    );
                }
            }

            return new ClaimExpectation(label, claim, names ?? new string[0]);
        }

        private static string Field(string claim, string field)
        {
            string marker = field + "=";
            int start = claim.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            start += marker.Length;
            int end = claim.IndexOf(' ', start);
            return end < 0 ? claim.Substring(start) : claim.Substring(start, end - start);
        }

        private static bool IsAscii(string value)
        {
            foreach (char character in value)
            {
                if (0x7F < character)
                {
                    return false;
                }
            }

            return true;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                Failures.Add(message);
            }
        }

        private static void WriteJson(string path, List<ClaimExpectation> expectations)
        {
            using FileStream stream = File.Create(path);
            using Utf8JsonWriter writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            // The layout the reporter really used, so the reader can be asked to
            // use the same two names rather than both sides hard-coding them.
            writer.WriteStartObject("layout");
            writer.WriteString("claim", Path.GetFileName(DxTerminalTestRunReporter.ClaimPath()));
            writer.WriteString(
                "request",
                Path.GetFileName(DxTerminalTestRunReporter.RequestPath())
            );
            writer.WriteEndObject();
            writer.WriteStartArray("claims");
            foreach (ClaimExpectation expectation in expectations)
            {
                writer.WriteStartObject();
                writer.WriteString("label", expectation.Label);
                writer.WriteString("claim", expectation.Claim);
                writer.WriteStartArray("names");
                foreach (string name in expectation.Names)
                {
                    writer.WriteStringValue(name);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private sealed class ClaimExpectation
        {
            public ClaimExpectation(string label, string claim, string[] names)
            {
                Label = label;
                Claim = claim;
                Names = names;
            }

            public string Label { get; }

            public string Claim { get; }

            public string[] Names { get; }
        }

        /*
            One node of a result tree. `hasTest: false` is a result the editor
            could not attribute to a test, which is the case the reporter guards
            before it reads a name.
         */
        private sealed class FakeTest : ITestResultAdaptor
        {
            private readonly List<FakeTest> _children;

            public FakeTest(
                string fullName,
                TestStatus status = TestStatus.Passed,
                bool isSuite = false,
                bool hasTest = true,
                bool throwsOnChildren = false,
                TestMode mode = TestMode.EditMode,
                int passed = 0,
                int failed = 0,
                int skipped = 0,
                int inconclusive = 0,
                List<FakeTest> children = null
            )
            {
                FullName = fullName;
                TestStatus = status;
                IsSuite = isSuite;
                TestMode = mode;
                PassCount = passed;
                FailCount = failed;
                SkipCount = skipped;
                InconclusiveCount = inconclusive;
                HasTest = hasTest;
                ThrowsOnChildren = throwsOnChildren;
                _children = children ?? new List<FakeTest>();
            }

            public bool HasTest { get; }

            public bool ThrowsOnChildren { get; }

            public string Id => FullName ?? string.Empty;

            public string Name => FullName ?? string.Empty;

            public string FullName { get; }

            public bool IsSuite { get; }

            public TestMode TestMode { get; }

            public ITestAdaptor Test => HasTest ? this : null;

            public TestStatus TestStatus { get; }

            public double Duration => 0.5;

            public int PassCount { get; }

            public int FailCount { get; }

            public int SkipCount { get; }

            public int InconclusiveCount { get; }

            public bool HasChildren => ThrowsOnChildren || 0 < _children.Count;

            public IEnumerable<ITestResultAdaptor> Children
            {
                get
                {
                    if (ThrowsOnChildren)
                    {
                        throw new InvalidOperationException("broken tree");
                    }

                    foreach (FakeTest child in _children)
                    {
                        yield return child;
                    }
                }
            }
        }
    }
}
