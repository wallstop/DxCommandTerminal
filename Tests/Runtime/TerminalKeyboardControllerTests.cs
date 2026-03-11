namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Input;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class TerminalKeyboardControllerTests
    {
        private readonly List<GameObject> _gameObjects = new();

        private static TerminalControlTypes[] GetControlTypes()
        {
            FieldInfo controlTypesField = typeof(TerminalKeyboardController).GetField(
                "ControlTypes",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy
            );
            return (TerminalControlTypes[])controlTypesField.GetValue(null);
        }

        private static FieldInfo GetControlOrderField()
        {
            return typeof(TerminalKeyboardController).GetField(
                "_controlOrder",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
        }

        private static MethodInfo GetVerifyMethod()
        {
            return typeof(TerminalKeyboardController).GetMethod(
                "VerifyControlOrderIntegrity",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _gameObjects)
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
            _gameObjects.Clear();
        }

        [Test]
        public void ControlTypesContainsAllNonNoneEnumValues()
        {
            TerminalControlTypes[] expected = Enum.GetValues(typeof(TerminalControlTypes))
                .OfType<TerminalControlTypes>()
#pragma warning disable CS0612 // Type or member is obsolete
                .Except(new[] { TerminalControlTypes.None })
#pragma warning restore CS0612 // Type or member is obsolete
                .ToArray();

            TerminalControlTypes[] actual = GetControlTypes();
            Assert.IsNotNull(actual, "ControlTypes should not be null");
            Assert.AreEqual(
                expected.Length,
                actual.Length,
                $"ControlTypes length mismatch. Expected: [{string.Join(", ", expected)}], Actual: [{string.Join(", ", actual)}]"
            );

            foreach (TerminalControlTypes controlType in expected)
            {
                Assert.IsTrue(
                    actual.Contains(controlType),
                    $"ControlTypes is missing {controlType}. Contents: [{string.Join(", ", actual)}]"
                );
            }
        }

        [UnityTest]
        public IEnumerator DefaultControlOrderProducesNoWarning()
        {
            // Default _controlOrder contains all TerminalControlTypes, so no warning should fire.
            // Awake will log an error about missing TerminalUI -- expect that.
            LogAssert.Expect(LogType.Error, "Failed to find TerminalUI, Input will not work.");
            GameObject go = new("TerminalKeyboardControllerTest");
            _gameObjects.Add(go);
            go.AddComponent<TerminalKeyboardController>();
            yield return null;

            // LogAssert will fail the test if any unexpected warnings were emitted.
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ControlOrderWithDuplicatesButAllTypesPresent_ProducesNoWarning()
        {
            // Regression test: if _controlOrder has duplicates but still covers all control types,
            // VerifyControlOrderIntegrity should NOT produce a warning.
            LogAssert.Expect(LogType.Error, "Failed to find TerminalUI, Input will not work.");
            GameObject go = new("TerminalKeyboardControllerTest");
            _gameObjects.Add(go);
            TerminalKeyboardController controller = go.AddComponent<TerminalKeyboardController>();
            yield return null;

            // Now modify _controlOrder to have duplicates but still include all types, then invoke VerifyControlOrderIntegrity.
            FieldInfo controlOrderField = GetControlOrderField();
            Assert.IsNotNull(controlOrderField, "_controlOrder field should exist on TerminalKeyboardController");

            List<TerminalControlTypes> orderWithDuplicates = GetControlTypes().ToList();
            // Add duplicates
            orderWithDuplicates.Add(orderWithDuplicates[0]);
            orderWithDuplicates.Add(orderWithDuplicates[1]);
            controlOrderField.SetValue(controller, orderWithDuplicates);

            MethodInfo verifyMethod = GetVerifyMethod();
            Assert.IsNotNull(verifyMethod, "VerifyControlOrderIntegrity method should exist on TerminalKeyboardController");

            verifyMethod.Invoke(controller, null);

            // LogAssert will fail the test if any unexpected warnings were emitted.
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ControlOrderMissingType_ProducesWarning()
        {
            // When _controlOrder is missing a control type, a warning should be emitted.
            LogAssert.Expect(LogType.Error, "Failed to find TerminalUI, Input will not work.");
            GameObject go = new("TerminalKeyboardControllerTest");
            _gameObjects.Add(go);
            TerminalKeyboardController controller = go.AddComponent<TerminalKeyboardController>();
            yield return null;

            FieldInfo controlOrderField = GetControlOrderField();
            Assert.IsNotNull(controlOrderField, "_controlOrder field should exist on TerminalKeyboardController");

            // Remove the last type from the list
            TerminalControlTypes[] allTypes = GetControlTypes();
            TerminalControlTypes removedType = allTypes[^1];
            List<TerminalControlTypes> incompleteOrder = allTypes.Take(allTypes.Length - 1).ToList();
            controlOrderField.SetValue(controller, incompleteOrder);

            MethodInfo verifyMethod = GetVerifyMethod();
            Assert.IsNotNull(verifyMethod, "VerifyControlOrderIntegrity method should exist on TerminalKeyboardController");

            LogAssert.Expect(
                LogType.Warning,
                $"Control Order is missing the following controls: [{removedType}]. "
                    + "Input for these will not be handled. Is this intentional?"
            );
            verifyMethod.Invoke(controller, null);

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator EmptyControlOrder_ProducesWarningForAllTypes()
        {
            // When _controlOrder is empty, VerifyControlOrderIntegrity should warn about all missing types.
            // Note: Awake fires during AddComponent with the default (full) control order, so
            // we only modify _controlOrder afterward and invoke VerifyControlOrderIntegrity directly.
            LogAssert.Expect(LogType.Error, "Failed to find TerminalUI, Input will not work.");
            GameObject go = new("TerminalKeyboardControllerTest");
            _gameObjects.Add(go);
            TerminalKeyboardController controller = go.AddComponent<TerminalKeyboardController>();
            yield return null;

            FieldInfo controlOrderField = GetControlOrderField();
            Assert.IsNotNull(controlOrderField, "_controlOrder field should exist on TerminalKeyboardController");
            controlOrderField.SetValue(controller, new List<TerminalControlTypes>());

            MethodInfo verifyMethod = GetVerifyMethod();
            Assert.IsNotNull(verifyMethod, "VerifyControlOrderIntegrity method should exist on TerminalKeyboardController");

            TerminalControlTypes[] allTypes = GetControlTypes();
            string expectedMissing = string.Join(", ", allTypes);

            LogAssert.Expect(
                LogType.Warning,
                $"Control Order is missing the following controls: [{expectedMissing}]. "
                    + "Input for these will not be handled. Is this intentional?"
            );
            verifyMethod.Invoke(controller, null);

            LogAssert.NoUnexpectedReceived();
        }
    }
}
