namespace CommandTerminal.Extensions
{
    using System;
    using System.Collections;
    using UnityEngine;

    public static class UnityExtensions
    {
        public static Coroutine ExecuteFunctionAfterDelay(
            this MonoBehaviour monoBehaviour,
            Action action,
            float delay
        )
        {
            return monoBehaviour.StartCoroutine(FunctionDelayAsCoroutine(action, delay));
        }

        private static IEnumerator FunctionDelayAsCoroutine(Action action, float delay)
        {
            float startTime = Time.time;
            while (!HasEnoughTimePassed(startTime, delay))
            {
                yield return null;
            }

            action();
        }

        public static bool HasEnoughTimePassed(float timestamp, float desiredDuration) =>
            timestamp + desiredDuration < Time.time;
    }
}
