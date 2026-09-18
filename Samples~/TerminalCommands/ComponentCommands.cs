namespace WallstopStudios.DxCommandTerminal.Samples
{
    using System;
    using System.Text;
    using Backend;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /*
        Component inspection commands: list the components on one object and
        scan the whole scene - inactive objects included, where missing
        scripts usually hide - for broken components (a destroyed or
        unresolvable component shows up as a null entry in
        GetComponents<Component>()). Both commands share one scene scope:
        the name adapter includes inactive objects so a name printed by
        find-missing-scripts resolves in list-components.
     */
    public sealed class ComponentCommands : TerminalCommandSample
    {
        private readonly SceneObjectArgumentAdapter<GameObject> _objects = new(
            includeInactive: true
        );

        private static bool HasMissingScript(GameObject target)
        {
            Component[] components = target.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static GameObject[] QuerySceneObjects()
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.InstanceID
            );
#else
            return Object.FindObjectsOfType<GameObject>(true);
#endif
        }

        protected override void RegisterCommands()
        {
            Register(
                CommandBuilder
                    .Create("list-components", "Lists every component on an object")
                    .Arg<GameObject>(
                        "target",
                        spec =>
                            spec.Required()
                                .RawParser(_objects.TryParse)
                                .Choices(_objects.GetChoices, _objects.FormatChoice)
                    )
                    .Handler(
                        (context, arguments) =>
                        {
                            GameObject target = arguments.Get<GameObject>("target");
                            Component[] components = target.GetComponents<Component>();
                            if (components.Length == 0)
                            {
                                Terminal.Log("{0}: no components.", target.name);
                                return;
                            }

                            StringBuilder report = new();
                            report.Append(target.name).Append(":\n");
                            for (int i = 0; i < components.Length; ++i)
                            {
                                report
                                    .Append("  [")
                                    .Append(i)
                                    .Append("] ")
                                    .Append(
                                        components[i] == null
                                            ? "<missing script>"
                                            : components[i].GetType().Name
                                    )
                                    .Append('\n');
                            }

                            Terminal.Log(report.ToString().TrimEnd('\n'));
                        }
                    )
            );

            Register(
                CommandBuilder
                    .Create("find-missing-scripts", "Logs every object with a missing script")
                    .Handler(
                        (context, arguments) =>
                        {
                            GameObject[] objects = QuerySceneObjects();
                            int broken = 0;
                            StringBuilder report = new();
                            foreach (GameObject candidate in objects)
                            {
                                if (!HasMissingScript(candidate))
                                {
                                    continue;
                                }

                                ++broken;
                                report.Append("\n  ").Append(candidate.name);
                            }

                            if (broken == 0)
                            {
                                Terminal.Log("No missing scripts in the scene.");
                                return;
                            }

                            Terminal.Log("{0} object(s) with missing scripts:{1}", broken, report);
                        }
                    )
            );
        }
    }
}
