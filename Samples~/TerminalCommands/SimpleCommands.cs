namespace WallstopStudios.DxCommandTerminal.Samples
{
    using Backend;
    using UnityEngine;

    /*
        The simplest typed command: one required int validated by a range,
        one optional string with a default and static choices. Run
        `heal 50`, `heal 25 ally`, or Tab-complete the target.
     */
    public sealed class SimpleCommands : TerminalCommandSample
    {
        protected override void RegisterCommands()
        {
            Register(
                CommandBuilder
                    .Create("heal", "Heals a target")
                    .Arg<int>("amount", spec => spec.Required().Range(1, 100))
                    .Arg<string>(
                        "target",
                        spec => spec.Default("self").Choices("self", "ally", "enemy")
                    )
                    .Handler(
                        (context, arguments) =>
                        {
                            int amount = arguments.Get<int>("amount");
                            string target = arguments.Get<string>("target");
                            Terminal.Log("Healed {0} for {1} HP.", target, amount);
                        }
                    )
            );
        }
    }
}
