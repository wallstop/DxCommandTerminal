namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using Object = UnityEngine.Object;

    public sealed class SceneObjectArgumentAdapter<T>
        where T : Object
    {
        private readonly SceneObjectAmbiguityPolicy _ambiguityPolicy;
        private readonly bool _includeInactive;

        public SceneObjectArgumentAdapter(
            SceneObjectAmbiguityPolicy ambiguityPolicy = SceneObjectAmbiguityPolicy.FirstMatch,
            bool includeInactive = false
        )
        {
            if (typeof(T) != typeof(GameObject) && !typeof(Component).IsAssignableFrom(typeof(T)))
            {
                throw new ArgumentException("Use GameObject or a Component type.", nameof(T));
            }

            if (
                ambiguityPolicy
                is not SceneObjectAmbiguityPolicy.FirstMatch
                    and not SceneObjectAmbiguityPolicy.RequireUnique
            )
            {
                throw new ArgumentOutOfRangeException(nameof(ambiguityPolicy));
            }

            _ambiguityPolicy = ambiguityPolicy;
            _includeInactive = includeInactive;
        }

        public bool TryParse(string input, out T value)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                value = null;
                return false;
            }

            T match = null;
            T[] candidates = Query();
            foreach (T candidate in candidates)
            {
                if (!string.Equals(candidate.name, input, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (_ambiguityPolicy == SceneObjectAmbiguityPolicy.FirstMatch)
                {
                    value = candidate;
                    return true;
                }

                if (match != null)
                {
                    value = null;
                    return false;
                }

                match = candidate;
            }

            value = match;
            return match != null;
        }

        public string FormatChoice(T value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            string name = value.name;
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        }

        public IReadOnlyList<T> GetChoices(CommandCompletionContext context)
        {
            T[] candidates = Query();
            return candidates;
        }

        private T[] Query()
        {
#if UNITY_6000_4_OR_NEWER
            T[] candidates = Object.FindObjectsByType<T>(
                _includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude
            );
            Array.Sort(
                candidates,
                (left, right) => left.GetEntityId().CompareTo(right.GetEntityId())
            );
            return candidates;
#elif UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<T>(
                _includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.InstanceID
            );
#else
            return Object.FindObjectsOfType<T>(_includeInactive);
#endif
        }
    }
}
