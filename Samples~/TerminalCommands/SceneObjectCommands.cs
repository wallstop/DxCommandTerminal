namespace WallstopStudios.DxCommandTerminal.Samples
{
    using Backend;
    using UnityEngine;

    public sealed class SceneObjectCommands : TerminalCommandSample
    {
        protected override void RegisterCommands()
        {
            SceneObjectArgumentAdapter<GameObject> objects = new();
            SceneObjectArgumentAdapter<Transform> transforms = new(
                SceneObjectAmbiguityPolicy.RequireUnique
            );
            Register(
                CommandBuilder
                    .Create("object-info", "Logs the first object's name and layer")
                    .Arg<GameObject>(
                        "target",
                        spec =>
                            spec.Required()
                                .Parser(objects.TryParse)
                                .Choices(objects.GetChoices, objects.FormatChoice)
                    )
                    .Handler(
                        (context, arguments) =>
                        {
                            GameObject target = arguments.Get<GameObject>("target");
                            Terminal.Log("{0}: layer {1}", target.name, target.layer);
                        }
                    )
            );
            Register(
                CommandBuilder
                    .Create("object-position", "Logs a uniquely named object's position")
                    .Arg<Transform>(
                        "target",
                        spec =>
                            spec.Required()
                                .Parser(transforms.TryParse)
                                .Choices(transforms.GetChoices, transforms.FormatChoice)
                    )
                    .Handler(
                        (context, arguments) =>
                        {
                            Transform target = arguments.Get<Transform>("target");
                            Terminal.Log("{0}: {1}", target.name, target.position);
                        }
                    )
            );
        }
    }
}
