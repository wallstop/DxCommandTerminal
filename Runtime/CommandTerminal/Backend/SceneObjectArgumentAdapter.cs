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

#if UNITY_6000_4_OR_NEWER
        /*
            Reusable sort-key buffer for GetChoices, grown to the query size
            and reused across queries (completion queries run on one thread,
            one adapter instance per command). Extracting each entity id once
            replaces the two native id reads per comparison a direct sort
            pays; the key values and the resulting order are unchanged.
         */
        private EntityId[] _sortKeys = Array.Empty<EntityId>();
#endif

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
            int candidateCount = candidates.Length;
            for (int i = 0; i < candidateCount; ++i)
            {
                T candidate = candidates[i];
                if (
                    candidate == null
                    || !string.Equals(candidate.name, input, StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                if (_ambiguityPolicy == SceneObjectAmbiguityPolicy.RequireUnique)
                {
                    if (match != null)
                    {
                        value = null;
                        return false;
                    }

                    match = candidate;
                    continue;
                }

#if UNITY_6000_4_OR_NEWER
                /*
                    The engine order is arbitrary here, so the first match is
                    the lowest entity id among the name matches, matching the
                    sorted-query behavior this version's predecessors had. The
                    minimum scan replaces a full managed sort per invocation.
                 */
                if (match == null || candidate.GetEntityId().CompareTo(match.GetEntityId()) < 0)
                {
                    match = candidate;
                }
#else
                if (match == null)
                {
                    /*
                       The query order is the deterministic instance-id order,
                       so the first name match is the same object a sorted
                       query would have produced.
                    */
                    match = candidate;
                }
#endif
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

        /*
            Returns every query candidate in deterministic (entity id)
            order; the completion pipeline filters the formatted candidates
            by the active token prefix. Prefix-filtering here instead was
            measured slower on prefixes that match most objects (every
            filter candidate pays a second name read through the formatter)
            while the pipeline's post-format filter costs nothing extra, so
            the query stays unfiltered (see the completion scaling tests).
         */
        public IReadOnlyList<T> GetChoices(CommandCompletionContext context)
        {
            T[] candidates = Query();
#if UNITY_6000_4_OR_NEWER
            int candidateCount = candidates.Length;
            if (_sortKeys.Length < candidateCount)
            {
                _sortKeys = new EntityId[candidateCount];
            }

            for (int i = 0; i < candidateCount; ++i)
            {
                _sortKeys[i] = candidates[i].GetEntityId();
            }

            /*
               The reusable key buffer can be longer than this query's result
               (the scene shrank since the largest previous query), so the
               sort is range-limited to the live candidates.
            */
            Array.Sort(_sortKeys, candidates, 0, candidateCount);
#endif
            return candidates;
        }

        private T[] Query()
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>(
                _includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude
            );
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
