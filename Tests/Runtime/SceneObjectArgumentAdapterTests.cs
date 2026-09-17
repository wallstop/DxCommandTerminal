namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;
    using UnityEngine;
    using Object = UnityEngine.Object;

    [TestFixture(typeof(GameObject))]
    [TestFixture(typeof(Transform))]
    [TestFixture(typeof(BoxCollider))]
    public sealed class SceneObjectArgumentAdapterTests<T>
        where T : Object
    {
        private const string TargetName = "DxAdapter Target";
        private readonly List<GameObject> _objects = new();

        private static IEnumerable<TestCaseData> UnsupportedTypes()
        {
            yield return new TestCaseData(
                new TestDelegate(() => new SceneObjectArgumentAdapter<Texture2D>())
            ).SetName("RejectsTexture2D");
            yield return new TestCaseData(
                new TestDelegate(() => new SceneObjectArgumentAdapter<Object>())
            ).SetName("RejectsUnityObject");
            yield return new TestCaseData(
                new TestDelegate(() => new SceneObjectArgumentAdapter<ScriptableObject>())
            ).SetName("RejectsScriptableObject");
            yield return new TestCaseData(
                new TestDelegate(() => new SceneObjectArgumentAdapter<Material>())
            ).SetName("RejectsNonComponentAsset");
        }

        private static CommandCompletionContext CompletionContext()
        {
            return new CommandCompletionContext(
                default,
                string.Empty,
                0,
                0,
                new List<CommandArg>(),
                string.Empty,
                0,
                0,
                false,
                null
            );
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in _objects)
            {
                if (target != null)
                {
                    Object.DestroyImmediate(target);
                }
            }

            _objects.Clear();
        }

        [TestCase("DxAdapter Target", true)]
        [TestCase("dxadapter target", true)]
        [TestCase("DxAdapter", false)]
        [TestCase("DxAdapter Missing", false)]
        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase(" ", false)]
        [TestCase("\t\r\n", false)]
        [TestCase(" DxAdapter Target", false)]
        [TestCase("DxAdapter Target ", false)]
        public void NamesMatchExactlyIgnoringCase(string input, bool expected)
        {
            T target = CreateTarget();
            SceneObjectArgumentAdapter<T> adapter = new();
            Assert.AreEqual(expected, adapter.TryParse(input, out T result));
            Assert.AreEqual(expected ? target : null, result);
        }

        [TestCase(SceneObjectAmbiguityPolicy.FirstMatch, true)]
        [TestCase(SceneObjectAmbiguityPolicy.RequireUnique, false)]
        public void DuplicateNamesFollowPolicy(SceneObjectAmbiguityPolicy policy, bool expected)
        {
            T first = CreateTarget();
            T second = CreateTarget();
            second.name = TargetName.ToLowerInvariant();
            SceneObjectArgumentAdapter<T> adapter = new(policy);
            Assert.AreEqual(expected, adapter.TryParse(TargetName, out T result));
            T firstByID =
#if UNITY_6000_4_OR_NEWER
                first.GetEntityId().CompareTo(second.GetEntityId()) < 0 ? first : second;
#else
                first.GetInstanceID() < second.GetInstanceID() ? first : second;
#endif
            Assert.AreEqual(expected ? firstByID : null, result);
        }

        [Test]
        public void DefaultPolicyReturnsFirstQueryMatch()
        {
            CreateTarget();
            CreateTarget();
            SceneObjectArgumentAdapter<T> adapter = new();
            Assert.IsTrue(adapter.TryParse(TargetName, out T result));
            foreach (T candidate in adapter.GetChoices(default))
            {
                if (string.Equals(candidate.name, TargetName, StringComparison.Ordinal))
                {
                    Assert.AreSame(candidate, result);
                    return;
                }
            }

            Assert.Fail("The parsed object must occur in the fresh choices.");
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void InactiveObjectsRequireOptIn(bool includeInactive, bool expected)
        {
            T target = CreateTarget();
            _objects[0].SetActive(false);
            SceneObjectArgumentAdapter<T> adapter = new(includeInactive: includeInactive);
            Assert.AreEqual(expected, adapter.TryParse(TargetName, out T result));
            Assert.AreEqual(expected ? target : null, result);
            Assert.AreEqual(expected, new List<T>(adapter.GetChoices(default)).Contains(target));
        }

        [TestCase("rename")]
        [TestCase("destroy")]
        [TestCase("deactivate")]
        public void QueriesDoNotReusePreviousResults(string mutation)
        {
            T target = CreateTarget();
            SceneObjectArgumentAdapter<T> adapter = new();
            Assert.IsTrue(adapter.TryParse(TargetName, out _));
            Assert.Contains(target, new List<T>(adapter.GetChoices(default)));
            switch (mutation)
            {
                case "rename":
                    target.name = "DxAdapter Renamed";
                    break;
                case "destroy":
                    Object.DestroyImmediate(_objects[0]);
                    break;
                case "deactivate":
                    _objects[0].SetActive(false);
                    break;
            }

            Assert.IsFalse(adapter.TryParse(TargetName, out T missing));
            Assert.IsNull(missing);
            foreach (T candidate in adapter.GetChoices(default))
            {
                Assert.IsFalse(string.Equals(TargetName, candidate.name, StringComparison.Ordinal));
            }

            T replacement = CreateTarget();
            Assert.IsTrue(adapter.TryParse(TargetName, out T result));
            Assert.AreSame(replacement, result);
            Assert.Contains(replacement, new List<T>(adapter.GetChoices(default)));
        }

        [Test]
        public void ExplicitParserDoesNotRegisterGlobally()
        {
            T target = CreateTarget();
            SceneObjectArgumentAdapter<T> adapter = new();
            Assert.IsFalse(CommandArg.CanParse(typeof(T)));
            Assert.IsTrue(new CommandArg(TargetName).TryGetRaw(out T result, adapter.TryParse));
            Assert.AreSame(target, result);
            Assert.IsFalse(CommandArg.CanParse(typeof(T)));
        }

        [TestCase("\r")]
        [TestCase("\n")]
        [TestCase("\r\n")]
        public void ExplicitParserPreservesLineBreakNameIdentity(string lineBreak)
        {
            T target = CreateTarget();
            target.name = $"DxAdapter{lineBreak}Target";
            T decoy = CreateTarget();
            decoy.name = "DxAdapterTarget";
            SceneObjectArgumentAdapter<T> adapter = new();
            Assert.IsTrue(adapter.TryParse(target.name, out T direct));
            Assert.AreSame(target, direct);
            Assert.IsTrue(new CommandArg(target.name).TryGetRaw(out T parsed, adapter.TryParse));
            Assert.AreSame(target, parsed);
            Assert.AreNotSame(decoy, parsed);
        }

        [TestCase("\r", false)]
        [TestCase("\n", false)]
        [TestCase("\r\n", false)]
        [TestCase("\r", true)]
        [TestCase("\n", true)]
        [TestCase("\r\n", true)]
        public void BuilderPreservesLineBreakNameIdentity(string lineBreak, bool remaining)
        {
            T target = CreateTarget();
            target.name = $"DxAdapter{lineBreak}Target";
            T decoy = CreateTarget();
            decoy.name = "DxAdapterTarget";
            SceneObjectArgumentAdapter<T> adapter = new();
            CommandShell shell = new(new CommandHistory(16));
            T[] resolved = null;
            int calls = 0;
            CommandBuilder builder = CommandBuilder
                .Create("inspect-object")
                .Contexts(CommandExecutionContextSets.All);
            builder = remaining
                ? builder.Remaining<T>(
                    "target",
                    spec => spec.RawParser(adapter.TryParse).Required()
                )
                : builder.Arg<T>("target", spec => spec.RawParser(adapter.TryParse).Required());
            Assert.IsTrue(
                shell.AddCommand(
                    builder.Handler(
                        (context, arguments) =>
                        {
                            resolved = remaining
                                ? arguments.Get<T[]>("target")
                                : new[] { arguments.Get<T>("target") };
                            ++calls;
                        }
                    ),
                    out CommandRegistrationHandle handle
                )
            );
            using (handle)
            {
                string input = $"inspect-object \"{target.name}\"";
                if (remaining)
                {
                    input = $"{input} \"{decoy.name}\" \"{target.name}\"";
                }

                Assert.IsTrue(shell.RunCommand(input));
                Assert.IsFalse(shell.TryConsumeErrorMessage(out string error), error);
                Assert.AreEqual(1, calls);
                Assert.IsNotNull(resolved);
                Assert.AreEqual(remaining ? 3 : 1, resolved.Length);
                Assert.AreSame(target, resolved[0]);
                Assert.AreNotSame(decoy, resolved[0]);
                if (remaining)
                {
                    Assert.AreSame(decoy, resolved[1]);
                    Assert.AreSame(target, resolved[2]);
                }
            }
        }

        [TestCase(SceneObjectAmbiguityPolicy.FirstMatch, 1)]
        [TestCase(SceneObjectAmbiguityPolicy.RequireUnique, 0)]
        public void BuilderUsesParserAndFreshNameChoices(
            SceneObjectAmbiguityPolicy policy,
            int expectedCalls
        )
        {
            T target = CreateTarget();
            SceneObjectArgumentAdapter<T> adapter = new(policy);
            CommandShell shell = new(new CommandHistory(16));
            int calls = 0;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inspect-object")
                        .Contexts(CommandExecutionContextSets.All)
                        .Arg<T>(
                            "target",
                            spec =>
                                spec.Required()
                                    .RawParser(adapter.TryParse)
                                    .Choices(adapter.GetChoices, adapter.FormatChoice)
                        )
                        .Handler(
                            (context, arguments) =>
                            {
                                Assert.IsNotNull(arguments.Get<T>("target"));
                                ++calls;
                            }
                        ),
                    out CommandRegistrationHandle handle
                )
            );
            using (handle)
            {
                List<CommandCompletion> results = new();
                const string input = "inspect-object dxadapter";
                Assert.IsTrue(
                    shell.TryComplete(
                        CommandExecutionContext.Current,
                        input,
                        input.Length,
                        results,
                        out _
                    )
                );
                CollectionAssert.AreEqual(
                    new[] { TargetName },
                    results.ConvertAll(choice => choice.InsertionText)
                );
                CreateTarget();
                shell.RunCommand($"inspect-object \"{TargetName}\"");
                Assert.AreEqual(expectedCalls, calls);
                Assert.AreEqual(expectedCalls == 0, shell.TryConsumeErrorMessage(out _));
                target.name = "DxAdapter Renamed";
                Assert.IsTrue(
                    shell.TryComplete(
                        CommandExecutionContext.Current,
                        input,
                        input.Length,
                        results,
                        out _
                    )
                );
                CollectionAssert.AreEquivalent(
                    new[] { TargetName, target.name },
                    results.ConvertAll(choice => choice.InsertionText)
                );
            }
        }

        [TestCase("$target", "inspect-object $")]
        [TestCase("$target", "inspect-object \"$")]
        [TestCase("$target", "inspect-object '$")]
        [TestCase("$target\"quoted", "inspect-object \"$\"")]
        [TestCase("$target's", "inspect-object '$'")]
        [TestCase("DxAdapter\"'target", "inspect-object \"DxAdapter\"")]
        [TestCase("$target", "inspect-object \"$\"")]
        [TestCase("$target", "inspect-object '$'")]
        [TestCase("$target's", "inspect-object $")]
        [TestCase("$target\"quoted", "inspect-object $")]
        [TestCase("$target name", "inspect-object $")]
        [TestCase("DxAdapter Target", "inspect-object DxAdapter")]
        [TestCase("DxAdapter'target", "inspect-object DxAdapter")]
        [TestCase("DxAdapter\"target", "inspect-object DxAdapter")]
        [TestCase("DxAdapter\rTarget", "inspect-object \"DxAdapter\r", "DxAdapterTarget")]
        [TestCase("DxAdapter\nTarget", "inspect-object \"DxAdapter\n", "DxAdapterTarget")]
        [TestCase("DxAdapter\r\nTarget", "inspect-object \"DxAdapter\r\n", "DxAdapterTarget")]
        [TestCase("DxAdapter\rTarget", "inspect-object \"DxAdapter\r\"", "DxAdapterTarget")]
        [TestCase("DxAdapter\nTarget", "inspect-object \"DxAdapter\n\"", "DxAdapterTarget")]
        [TestCase("DxAdapter\r\nTarget", "inspect-object \"DxAdapter\r\n\"", "DxAdapterTarget")]
        public void AcceptedNameCompletionDispatchesSelectedObject(
            string name,
            string input,
            string decoyName = "OtherTarget"
        )
        {
            T target = CreateTarget();
            target.name = name;
            T decoy = CreateTarget();
            decoy.name = decoyName;
            SceneObjectArgumentAdapter<T> adapter = new();
            CommandShell shell = new(new CommandHistory(16));
            Assert.IsTrue(shell.SetVariable("target", decoy.name));
            T resolved = null;
            int calls = 0;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inspect-object")
                        .Contexts(CommandExecutionContextSets.All)
                        .Arg<T>(
                            "target",
                            spec =>
                                spec.Required()
                                    .RawParser(adapter.TryParse)
                                    .Choices(adapter.GetChoices, adapter.FormatChoice)
                        )
                        .Handler(
                            (context, arguments) =>
                            {
                                resolved = arguments.Get<T>("target");
                                ++calls;
                            }
                        ),
                    out CommandRegistrationHandle handle
                )
            );
            using (handle)
            {
                List<CommandCompletion> results = new();
                int caret = input.Length - (input.EndsWith('"') || input.EndsWith('\'') ? 1 : 0);
                Assert.IsTrue(
                    shell.TryComplete(
                        CommandExecutionContext.Current,
                        input,
                        caret,
                        results,
                        out CommandCompletionContext completionContext
                    )
                );
                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(name, results[0].InsertionText);
                Assert.IsTrue(
                    CommandTokenizer.TryPrepareInsertion(
                        input,
                        results[0].InsertionText,
                        completionContext.ReplacementStart,
                        completionContext.ReplacementLength,
                        completionContext.IsQuoted,
                        out string insertion,
                        out int replacementStart,
                        out int replacementLength
                    )
                );
                string completed = input
                    .Remove(replacementStart, replacementLength)
                    .Insert(replacementStart, insertion);
                Assert.IsTrue(shell.RunCommand(completed));
                Assert.IsFalse(shell.TryConsumeErrorMessage(out string error), error);
                Assert.AreEqual(1, calls);
                Assert.AreSame(target, resolved);
                Assert.AreNotSame(decoy, resolved);
            }
        }

        [TestCase("inspect-object $")]
        [TestCase("inspect-object \"$")]
        [TestCase("inspect-object '$'")]
        public void UnrepresentableNameCompletionDoesNotSelectDecoy(string input)
        {
            T target = CreateTarget();
            target.name = "$target\"'";
            T decoy = CreateTarget();
            decoy.name = "OtherTarget";
            SceneObjectArgumentAdapter<T> adapter = new();
            CommandShell shell = new(new CommandHistory(16));
            Assert.IsTrue(shell.SetVariable("target\"'", decoy.name));
            T resolved = null;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inspect-object")
                        .Contexts(CommandExecutionContextSets.All)
                        .Arg<T>(
                            "target",
                            spec =>
                                spec.Required()
                                    .RawParser(adapter.TryParse)
                                    .Choices(adapter.GetChoices, adapter.FormatChoice)
                        )
                        .Handler((context, arguments) => resolved = arguments.Get<T>("target")),
                    out CommandRegistrationHandle handle
                )
            );
            using (handle)
            {
                List<CommandCompletion> results = new();
                int caret = input.Length - (input.EndsWith('\'') ? 1 : 0);
                Assert.IsTrue(
                    shell.TryComplete(
                        CommandExecutionContext.Current,
                        input,
                        caret,
                        results,
                        out CommandCompletionContext context
                    )
                );
                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(target.name, results[0].InsertionText);
                Assert.IsFalse(
                    CommandTokenizer.TryPrepareInsertion(
                        input,
                        results[0].InsertionText,
                        context.ReplacementStart,
                        context.ReplacementLength,
                        context.IsQuoted,
                        out _,
                        out _,
                        out _
                    )
                );
                Assert.IsNull(resolved);
                Assert.IsTrue(shell.RunCommand($"inspect-object {target.name}"));
                Assert.AreSame(decoy, resolved);
                Assert.AreNotSame(target, resolved);
            }
        }

        [TestCase("$target", true)]
        [TestCase("\"$target\"", false)]
        [TestCase("'$target'", false)]
        [TestCase("\"$target", true)]
        [TestCase("'$target", true)]
        public void ManualVariableInputKeepsExistingResolution(string argument, bool expands)
        {
            T target = CreateTarget();
            target.name = "$target";
            T decoy = CreateTarget();
            decoy.name = "OtherTarget";
            SceneObjectArgumentAdapter<T> adapter = new();
            CommandShell shell = new(new CommandHistory(16));
            Assert.IsTrue(shell.SetVariable("target", decoy.name));
            T resolved = null;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inspect-object")
                        .Contexts(CommandExecutionContextSets.All)
                        .Arg<T>("target", spec => spec.Required().RawParser(adapter.TryParse))
                        .Handler((context, arguments) => resolved = arguments.Get<T>("target")),
                    out CommandRegistrationHandle handle
                )
            );
            using (handle)
            {
                Assert.IsTrue(shell.RunCommand($"inspect-object {argument}"));
                Assert.IsFalse(shell.TryConsumeErrorMessage(out string error), error);
                Assert.AreSame(expands ? decoy : target, resolved);
            }
        }

        [Test]
        public void MissingComponentAndDestroyedComponentAreNotResolved()
        {
            SceneObjectArgumentAdapter<BoxCollider> adapter = new();
            CreateTarget();
            BoxCollider existing = _objects[0].GetComponent<BoxCollider>();
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            Assert.IsFalse(adapter.TryParse(TargetName, out _));
            BoxCollider collider = _objects[0].AddComponent<BoxCollider>();
            Assert.IsTrue(adapter.TryParse(TargetName, out BoxCollider result));
            Assert.AreSame(collider, result);
            Object.DestroyImmediate(collider);
            Assert.IsFalse(adapter.TryParse(TargetName, out BoxCollider missing));
            Assert.IsNull(missing);
        }

        [TestCase(SceneObjectAmbiguityPolicy.FirstMatch, true)]
        [TestCase(SceneObjectAmbiguityPolicy.RequireUnique, false)]
        public void MultipleComponentsOnOneObjectFollowPolicy(
            SceneObjectAmbiguityPolicy policy,
            bool expected
        )
        {
            CreateTarget();
            _objects[0].AddComponent<BoxCollider>();
            _objects[0].AddComponent<BoxCollider>();
            SceneObjectArgumentAdapter<BoxCollider> adapter = new(policy);
            Assert.AreEqual(expected, adapter.TryParse(TargetName, out BoxCollider result));
            Assert.AreEqual(expected, result != null);
        }

        [TestCaseSource(nameof(UnsupportedTypes))]
        public void RejectsUnsupportedTypes(TestDelegate construct)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(construct);
            Assert.AreEqual("T", exception.ParamName);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void RejectsInvalidPolicies(int policy)
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SceneObjectArgumentAdapter<T>((SceneObjectAmbiguityPolicy)policy)
            );
            Assert.AreEqual("ambiguityPolicy", exception.ParamName);
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("\t\r\n")]
        public void BlankSceneNamesAreNotIdentifiers(string name)
        {
            T target = CreateTarget();
            target.name = name;
            SceneObjectArgumentAdapter<T> adapter = new();
            Assert.IsFalse(adapter.TryParse(name, out T result));
            Assert.IsNull(result);
            Assert.AreEqual(string.Empty, adapter.FormatChoice(target));
        }

        [Test]
        public void FormatChoicePreservesOriginalCRLF()
        {
            const string name = "DxAdapter\r\nTarget";
            T target = CreateTarget();
            target.name = name;
            SceneObjectArgumentAdapter<T> adapter = new();
            string formatted = adapter.FormatChoice(target);
            Assert.AreEqual(name, formatted);
            Assert.AreEqual('\r', formatted[9]);
            Assert.AreEqual('\n', formatted[10]);
        }

        [TestCase("\r")]
        [TestCase("\n")]
        [TestCase("\r\n")]
        public void LegacyOverridesKeepCleanedNameResolution(string lineBreak)
        {
            T target = CreateTarget();
            target.name = $"DxAdapter{lineBreak}Target";
            T decoy = CreateTarget();
            decoy.name = "DxAdapterTarget";
            SceneObjectArgumentAdapter<T> adapter = new();
            CommandArg input = new(target.name);
            Assert.IsTrue(input.TryGet(out T parsed, adapter.TryParse));
            Assert.AreSame(decoy, parsed);
            CommandArgumentSpec<T> spec = new("target");
            Assert.IsTrue(spec.Parser(adapter.TryParse).TryParse(input, out object value));
            Assert.AreSame(decoy, value);
            Assert.IsFalse(CommandArg.DoNotCleanTypes.Contains(typeof(T)));
        }

        [TestCase(" DxAdapter Target ")]
        [TestCase("DxAdapter\tTarget")]
        public void NonblankSceneNamesPreserveWhitespace(string name)
        {
            T target = CreateTarget();
            target.name = name;
            SceneObjectArgumentAdapter<T> adapter = new();
            Assert.IsTrue(adapter.TryParse(name, out T result));
            Assert.AreSame(target, result);
            Assert.AreEqual(name, adapter.FormatChoice(target));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DefaultChoiceFormattingKeepsToStringRoundTrip(bool dynamicChoices)
        {
            T target = CreateTarget();
            string identifier = target.ToString();
            CommandArgumentSpec<T> spec = new("target");
            spec = spec.Parser(
                (string input, out T parsed) =>
                {
                    bool matches = string.Equals(input, identifier, StringComparison.Ordinal);
                    parsed = matches ? target : null;
                    return matches;
                }
            );
            spec = dynamicChoices
                ? spec.Choices(context => new[] { target })
                : spec.Choices(target);
            List<CommandCompletion> results = new();
            spec.AppendCompletions(CompletionContext(), results);
            Assert.AreEqual(identifier, results[0].InsertionText);
            Assert.IsTrue(
                spec.TryParse(new CommandArg(results[0].InsertionText), out object value)
            );
            Assert.AreSame(target, value);
        }

        [Test]
        public void ExplicitFormatterSurvivesFluentCopiesWithoutMutatingOriginal()
        {
            T target = CreateTarget();
            SceneObjectArgumentAdapter<T> adapter = new();
            CommandArgumentSpec<T> original = new("target");
            original = original.Choices(adapter.GetChoices, adapter.FormatChoice);
            CommandArgumentSpec<T> copy = original
                .Default(target)
                .Required()
                .Describe("target")
                .Validate(value => null)
                .RawParser(adapter.TryParse);
            List<CommandCompletion> results = new();
            copy.AppendCompletions(CompletionContext(), results);
            Assert.IsTrue(
                results.Exists(choice =>
                    string.Equals(choice.InsertionText, TargetName, StringComparison.Ordinal)
                )
            );
            results.Clear();
            original
                .Choices(context => new[] { target })
                .AppendCompletions(CompletionContext(), results);
            Assert.AreEqual(target.ToString(), results[0].InsertionText);
            Assert.AreEqual(string.Empty, adapter.FormatChoice(null));
            Object.DestroyImmediate(_objects[0]);
            Assert.AreEqual(string.Empty, adapter.FormatChoice(target));
        }

        [Test]
        public void ChoiceFormatterRejectsNullConfiguration()
        {
            CommandArgumentSpec<T> spec = new("target");
            Assert.Throws<ArgumentNullException>(() => spec.Choices(null, value => "name"));
            Assert.Throws<ArgumentNullException>(() =>
                spec.Choices(context => Array.Empty<T>(), null)
            );
        }

        private T CreateTarget()
        {
            GameObject target = new(TargetName);
            _objects.Add(target);
            if (target is T gameObject)
            {
                return gameObject;
            }

            Component component = target.GetComponent(typeof(T));
            if (component == null)
            {
                component = target.AddComponent(typeof(T));
            }

            return component as T;
        }
    }
}
