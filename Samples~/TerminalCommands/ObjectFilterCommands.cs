namespace WallstopStudios.DxCommandTerminal.Samples
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Backend;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /*
        Object filtering by layer and tag: both arguments complete the live
        scene's valid values (defined layers, tags in use) and the handlers
        re-validate at run time, because completion is only a suggestion.
        Demonstrates dynamic choices built from engine state instead of the
        object-name adapter.
     */
    public sealed class ObjectFilterCommands : TerminalCommandSample
    {
        private static GameObject[] QuerySceneObjects()
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude);
#elif UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.InstanceID
            );
#else
            return Object.FindObjectsOfType<GameObject>(false);
#endif
        }

        protected override void RegisterCommands()
        {
            Register(
                CommandBuilder
                    .Create("find-objects", "Logs every active object on a layer")
                    .Arg<string>(
                        "layer",
                        spec =>
                            spec.Required()
                                .Choices(context =>
                                {
                                    List<string> names = new();
                                    for (int layer = 0; layer < 32; ++layer)
                                    {
                                        string name = LayerMask.LayerToName(layer);
                                        if (!string.IsNullOrEmpty(name))
                                        {
                                            names.Add(name);
                                        }
                                    }

                                    return names;
                                })
                    )
                    .Handler(
                        (context, arguments) =>
                        {
                            string layerName = arguments.Get<string>("layer");
                            int layer = LayerMask.NameToLayer(layerName);
                            if (layer < 0)
                            {
                                Terminal.Log("Unknown layer: {0}", layerName);
                                return;
                            }

                            GameObject[] objects = QuerySceneObjects();
                            StringBuilder report = new();
                            int count = 0;
                            foreach (GameObject candidate in objects)
                            {
                                if (candidate.layer != layer)
                                {
                                    continue;
                                }

                                ++count;
                                report.Append("\n  ").Append(candidate.name);
                            }

                            Terminal.Log(
                                "{0} active object(s) on layer '{1}' ({2}):{3}",
                                count,
                                LayerMask.LayerToName(layer),
                                layer,
                                report
                            );
                        }
                    )
            );

            Register(
                CommandBuilder
                    .Create("find-tagged", "Logs every active object carrying a tag")
                    .Arg<string>(
                        "tag",
                        spec =>
                            spec.Required()
                                .Choices(context =>
                                {
                                    HashSet<string> tags = new(OrdinalComparer.Instance);
                                    GameObject[] objects = QuerySceneObjects();
                                    foreach (GameObject candidate in objects)
                                    {
                                        tags.Add(candidate.tag);
                                    }

                                    return new List<string>(tags);
                                })
                    )
                    .Handler(
                        (context, arguments) =>
                        {
                            string tag = arguments.Get<string>("tag");
                            GameObject[] objects = QuerySceneObjects();
                            StringBuilder report = new();
                            int count = 0;
                            foreach (GameObject candidate in objects)
                            {
                                if (!candidate.CompareTag(tag))
                                {
                                    continue;
                                }

                                ++count;
                                report.Append("\n  ").Append(candidate.name);
                            }

                            Terminal.Log(
                                "{0} active object(s) tagged '{1}':{2}",
                                count,
                                tag,
                                report
                            );
                        }
                    )
            );
        }

        /*
            Tag matching must agree with the engine's case sensitivity, so
            the completion set dedupes with an ordinal comparison instead of
            the default culture-aware one.
         */
        private sealed class OrdinalComparer : IEqualityComparer<string>
        {
            internal static readonly OrdinalComparer Instance = new();

            public bool Equals(string left, string right)
            {
                return string.Equals(left, right, StringComparison.Ordinal);
            }

            public int GetHashCode(string value)
            {
                return value?.GetHashCode(StringComparison.Ordinal) ?? 0;
            }
        }
    }
}
