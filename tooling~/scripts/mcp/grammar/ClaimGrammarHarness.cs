/*
    Drives the real DxTerminalTestRunReporter and records what it wrote
    (issue #167). The file under test is linked into this project, so this is
    the reporter's own path: the callbacks the editor invokes, the claim files
    in the reporter's own layout, and the encoder and result walk reached only
    through RunFinished - the way a real run reaches them.

    What is checked here, and what is left to the node side:

    - C#: what the raw line looks like, because no reader can see it. That a
      claim stays ASCII, that a name list is a list of non-empty fields, that a
      run nobody asked for is refused rather than acknowledged, that the start is
      acknowledged with the token it was started under, and that a walk which
      throws costs the names and not the claim.
    - node (grammar.test.mjs): that the claims decode into exactly the results
      stated here. The decoder is JavaScript, so no C# can check it, and the two
      halves agreeing is the whole contract. Each record says what the run was,
      and the node side composes the object the reader must return for the line
      and compares it, so the comparison is an equality rather than a search for
      a fragment - which is also where a suite reported as a failed test, or a
      name mangled on the way, is caught.

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
    using DxTerminalDevTools.Grammar;
    using UnityEditor.TestTools.TestRunner.Api;

    internal static class ClaimGrammarHarness
    {
        private const string Fixture = "Wallstop.Fixture";
        private const int ReportedCap = 10;

        /// <summary>
        /// The duration every result reports, so the claim's own duration is an
        /// expectation rather than a string repeated in a test.
        /// </summary>
        private const double ResultDuration = 0.5;

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
            TestProject.DataPath = Path.Combine(project, "Assets");

            List<ClaimExpectation> expectations = new();
            try
            {
                DxTerminalTestRunReporter.RegisterCallbacks();
                ICallbacks callbacks = Last();
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
                /*
                   A failed delete is not the failure being reported, and letting
                   it escape would print a stack trace instead of the list.
                */
                try
                {
                    Directory.Delete(project, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
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
            Nobody asked for this run. It is refused at the start, and reported
            as nobody's at the end: a late callback must never adopt the token of
            the next request, and the reader is looking for a token of its own.
         */
        private static void RunNobodyAskedFor(
            ICallbacks callbacks,
            List<ClaimExpectation> expectations
        )
        {
            File.Delete(DxTerminalTestRunReporter.RequestPath());
            string[] names = { $"{Fixture}.Unattributed" };
            callbacks.RunStarted(Leaf(Fixture, isSuite: true));
            Require(
                ReadClaim().Contains("did-not-run token=none", StringComparison.Ordinal),
                "a run nobody asked for is refused at the start, not acknowledged"
            );
            callbacks.RunFinished(
                Root(children: new List<FakeTest> { Failed(names[0]) }, failed: 1)
            );
            expectations.Add(Finished("a run nobody asked for", "none", names, failed: 1));
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
            callbacks.RunFinished(Root());
            expectations.Add(
                Refused(
                    "a run that executed no tests",
                    "grammar-empty",
                    "the run executed no tests"
                )
            );
        }

        /*
            A green run writes the line it always wrote: no names field, so a
            reader keying on the field learns nothing new about a green run.
         */
        private static void RunGreen(ICallbacks callbacks, List<ClaimExpectation> expectations)
        {
            const string token = "grammar-green";
            Request(token);
            StartRun(callbacks, token, TestMode.PlayMode);
            callbacks.RunFinished(
                Root(
                    passed: 2,
                    skipped: 1,
                    children: new List<FakeTest>
                    {
                        Leaf($"{Fixture}.One.TestAlpha"),
                        Leaf($"{Fixture}.One.TestBeta"),
                        Leaf($"{Fixture}.One.TestGamma", TestStatus.Skipped),
                    }
                )
            );
            expectations.Add(
                Finished(
                    "a green run",
                    token,
                    new string[0],
                    passed: 2,
                    skipped: 1,
                    mode: "PlayMode"
                )
            );
        }

        /*
            The red run, shaped so every rule the reporter states is observable:
            a failed fixture is walked and must not be named, a childless failed
            node is still a suite and must not be named either, a result whose
            test never arrived has no name to give, and more failures than the cap
            become the cap plus a count of the rest.
         */
        private static void RunRedOverTheCap(
            ICallbacks callbacks,
            List<ClaimExpectation> expectations
        )
        {
            List<string> names = new() { $"{Fixture}.Alpha.One", $"{Fixture}.Beta.One" };
            List<FakeTest> children = new()
            {
                FailedFixture(
                    $"{Fixture}.Alpha",
                    new List<FakeTest>
                    {
                        Leaf($"{Fixture}.Alpha.Skipped", TestStatus.Skipped),
                        Failed($"{Fixture}.Alpha.One"),
                    }
                ),
                FailedFixture(
                    $"{Fixture}.Beta",
                    new List<FakeTest> { Failed($"{Fixture}.Beta.One") }
                ),
                /*
                   A childless failed node is still a suite, and reporting it
                   would point the reader at the fixture instead of the test.
                */
                Leaf($"{Fixture}.Childless", TestStatus.Failed, isSuite: true),
                // A result whose test never arrived: no name to report.
                Leaf(null, TestStatus.Failed, hasTest: false),
            };
            for (int index = 0; index < 12; ++index)
            {
                string name = $"{Fixture}.Bulk{index:00}";
                children.Add(Failed(name));
                names.Add(name);
            }

            Request("grammar-red");
            StartRun(callbacks, "grammar-red");
            callbacks.RunFinished(Root(failed: names.Count, children: children));
            /*
               Tree order, which is depth first: the two names inside the failed
               fixtures come before the bulk leaves, and the suites themselves are
               never among them.
            */
            expectations.Add(
                Finished(
                    "a red run over the cap",
                    "grammar-red",
                    names.GetRange(0, ReportedCap).ToArray(),
                    failed: names.Count,
                    more: names.Count - ReportedCap
                )
            );
        }

        /*
            A Play Mode run reloads the domain between the start and the finish,
            so the instance that sees the finish is a new one with nothing in
            memory. Registering again and finishing from the new instance is what
            that looks like, and the attribution has to survive it: a run that
            reported itself as somebody else's, or as nobody's, would be discarded
            by the caller waiting for it.
         */
        private static void RunAcrossADomainReload(
            ICallbacks callbacks,
            List<ClaimExpectation> expectations
        )
        {
            string[] names = { $"{Fixture}.Reload.One" };
            Request("grammar-reload");
            StartRun(callbacks, "grammar-reload");
            DxTerminalTestRunReporter.RegisterCallbacks();
            ICallbacks reloaded = Last();
            Require(
                !ReferenceEquals(callbacks, reloaded),
                "registering again must hand back a new instance, or this case proves nothing"
            );
            reloaded.RunFinished(
                Root(failed: 1, children: new List<FakeTest> { Failed(names[0]) })
            );
            expectations.Add(
                Finished("a run across a domain reload", "grammar-reload", names, failed: 1)
            );
        }

        /*
            A result tree the walk cannot survive. The walk reads a tree this code
            does not own, so an exception there must cost the names and not the
            claim: the counters are the part a caller has no other way to get.
            Without the guard the exception leaves RunFinished and the caller is
            left with no line at all.
         */
        private static void RunWithAThrowingResultTree(
            ICallbacks callbacks,
            List<ClaimExpectation> expectations
        )
        {
            Request("grammar-throwing");
            int warningsBefore = TestProject.Warnings.Count;
            StartRun(callbacks, "grammar-throwing");
            callbacks.RunFinished(
                Root(
                    passed: 4,
                    failed: 2,
                    children: new List<FakeTest>
                    {
                        Failed($"{Fixture}.Alpha.One"),
                        Leaf($"{Fixture}.Broken", TestStatus.Failed, throwsOnChildren: true),
                    }
                )
            );

            expectations.Add(
                Finished(
                    "a result tree the walk cannot survive",
                    "grammar-throwing",
                    new string[0],
                    passed: 4,
                    failed: 2
                )
            );
            int raised = TestProject.Warnings.Count - warningsBefore;
            Require(raised == 1, $"the degrade path must be reported once, not {raised} times");
            if (raised == 1)
            {
                string warning = TestProject.Warnings[warningsBefore];
                Require(
                    warning.Contains("walk failed", StringComparison.Ordinal)
                        && warning.Contains("broken tree", StringComparison.Ordinal),
                    $"the warning must name the walk and the failure, got: {warning}"
                );
            }
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
                RunNames(
                    callbacks,
                    "grammar-char",
                    "character U+" + code.ToString("X4", CultureInfo.InvariantCulture),
                    new[] { $"{Fixture}.Char{code:X2}({character})" },
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

            /*
                One run per cap-sized group, so every name is in a claim the
                reader can see rather than in the count the cap left out.
             */
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
            One claim per group of names, every one of them failed, so the names
            the reader gets back are exactly the group.
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
                children.Add(Failed(name));
            }

            Request(token);
            StartRun(callbacks, token);
            callbacks.RunFinished(Root(failed: names.Length, children: children));
            expectations.Add(Finished(label, token, names, failed: names.Length));
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
            callbacks.RunStarted(Leaf(Fixture, isSuite: true, mode: mode));
            string claim = ReadClaim();
            Require(
                claim.Contains($"running token={token} mode={mode}", StringComparison.Ordinal),
                $"the start must be acknowledged with its own token, got: {claim}"
            );
            Require(
                claim.Contains("started=", StringComparison.Ordinal),
                $"the acknowledgement must carry its timestamp, got: {claim}"
            );
        }

        private static void Request(string token)
        {
            File.WriteAllText(
                DxTerminalTestRunReporter.RequestPath(),
                token + Environment.NewLine,
                new UTF8Encoding(false)
            );
        }

        private static ICallbacks Last()
        {
            return TestProject.Registered[TestProject.Registered.Count - 1];
        }

        private static string ReadClaim()
        {
            return File.ReadAllText(DxTerminalTestRunReporter.ClaimPath()).Trim();
        }

        private static ClaimExpectation Finished(
            string label,
            string token,
            string[] names,
            int passed = 0,
            int failed = 0,
            int skipped = 0,
            int more = 0,
            string mode = "EditMode"
        )
        {
            return new ClaimExpectation
            {
                Label = label,
                Claim = ReadClaim(),
                State = "finished",
                Token = token,
                Mode = mode,
                Passed = passed,
                Failed = failed,
                Skipped = skipped,
                FailedMore = more,
                Duration = ResultDuration,
                Names = names,
            };
        }

        private static ClaimExpectation Refused(string label, string token, string reason)
        {
            return new ClaimExpectation
            {
                Label = label,
                Claim = ReadClaim(),
                State = "refused",
                Token = token,
                Mode = "EditMode",
                Reason = reason,
                Names = new string[0],
            };
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
            /*
                The layout the reporter really used, so the reader can be asked to
                use the same two names rather than both sides hard-coding them.
             */
            writer.WriteStartObject("layout");
            writer.WriteString("claim", Path.GetFileName(DxTerminalTestRunReporter.ClaimPath()));
            writer.WriteString(
                "request",
                Path.GetFileName(DxTerminalTestRunReporter.RequestPath())
            );
            writer.WriteEndObject();
            writer.WritePropertyName("claims");
            /*
                camelCase, so a hand-written key and a record's own field read the
                same in the document the node side consumes.
             */
            JsonSerializer.Serialize(
                writer,
                expectations,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            );
            writer.WriteEndObject();
        }

        /*
            The three shapes a result tree is built from. A leaf, a failed
            fixture, and the suite the editor hands back at the end: the
            reporter walks all of them and reports only the first.
         */
        private static FakeTest Leaf(
            string name,
            TestStatus status = TestStatus.Passed,
            bool isSuite = false,
            bool hasTest = true,
            bool throwsOnChildren = false,
            TestMode mode = TestMode.EditMode
        )
        {
            return new FakeTest(name, status, isSuite, hasTest, throwsOnChildren, mode);
        }

        /*
            A failed test case, and a failed fixture. Two shapes, because the
            reporter treats them differently: the fixture is walked and never
            named, the case inside it is.
         */
        private static FakeTest Failed(string name)
        {
            return new FakeTest(name, TestStatus.Failed);
        }

        private static FakeTest FailedFixture(string name, List<FakeTest> children)
        {
            return new FakeTest(name, TestStatus.Failed, isSuite: true, children: children);
        }

        private static FakeTest Root(
            List<FakeTest> children = null,
            int passed = 0,
            int failed = 0,
            int skipped = 0
        )
        {
            return new FakeTest(
                Fixture,
                TestStatus.Passed,
                isSuite: true,
                children: children,
                passed: passed,
                failed: failed,
                skipped: skipped
            );
        }

        /*
            One node of a result tree. `hasTest: false` is a result the editor
            could not attribute to a test, and `throwsOnChildren` a tree the walk
            cannot read; both are cases the reporter guards, and neither can be
            built from a real editor.
         */
        private sealed class FakeTest : ITestResultAdaptor, ITestAdaptor
        {
            public string Id => FullName ?? string.Empty;

            public string Name => FullName ?? string.Empty;

            public string FullName { get; }

            public bool IsSuite { get; }

            public TestMode TestMode { get; }

            public ITestAdaptor Test => _hasTest ? this : null;

            public TestStatus TestStatus { get; }

            public double Duration => ResultDuration;

            public int PassCount { get; }

            public int FailCount { get; }

            public int SkipCount { get; }

            public int InconclusiveCount { get; }

            public bool HasChildren => _throwsOnChildren || 0 < _children.Count;

            /*
                Explicit, because the two interfaces declare Children with
                different element types and the fake is both.
             */
            IEnumerable<ITestResultAdaptor> ITestResultAdaptor.Children =>
                Children<ITestResultAdaptor>();

            IEnumerable<ITestAdaptor> ITestAdaptor.Children => Children<ITestAdaptor>();

            private readonly List<FakeTest> _children;
            private readonly bool _hasTest;
            private readonly bool _throwsOnChildren;

            public FakeTest(
                string fullName,
                TestStatus status = TestStatus.Passed,
                bool isSuite = false,
                bool hasTest = true,
                bool throwsOnChildren = false,
                TestMode mode = TestMode.EditMode,
                List<FakeTest> children = null,
                int passed = 0,
                int failed = 0,
                int skipped = 0,
                int inconclusive = 0
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
                _hasTest = hasTest;
                _throwsOnChildren = throwsOnChildren;
                _children = children ?? new List<FakeTest>();
            }

            private IEnumerable<TTarget> Children<TTarget>()
            {
                if (_throwsOnChildren)
                {
                    throw new InvalidOperationException("broken tree");
                }

                foreach (FakeTest child in _children)
                {
                    yield return (TTarget)(object)child;
                }
            }
        }

        /*
            One claim, and the result the reader must return for it. Flat, because
            the shape of a decoded claim is the reader's business: the node side
            composes the object it compares against, so this file only states
            what the run was.
         */
        private sealed class ClaimExpectation
        {
            public string Label { get; init; }

            public string Claim { get; init; }

            public string State { get; init; }

            public string Token { get; init; }

            public string Mode { get; init; }

            public string Reason { get; init; }

            public int Passed { get; init; }

            public int Failed { get; init; }

            public int Skipped { get; init; }

            public int Inconclusive { get; init; }

            public int FailedMore { get; init; }

            public double Duration { get; init; }

            public string[] Names { get; init; }
        }
    }
}
