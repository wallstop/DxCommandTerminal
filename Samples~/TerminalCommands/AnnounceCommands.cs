namespace WallstopStudios.DxCommandTerminal.Samples
{
    using Backend;
    using UnityEngine;

    /*
        The unbounded trailing argument: every token after the command
        collects into one typed array, each element parsed and validated with
        the argument's own rules. Run `say hello world` or `say go north now`.
     */
    public sealed class AnnounceCommands : TerminalCommandSample
    {
        protected override void RegisterCommands()
        {
            Register(
                CommandBuilder
                    .Create("say", "Prints a message")
                    .Remaining<string>(
                        "message",
                        /*
                            Required() demands at least one token; without it a
                            bare `say` reads an empty array.
                         */
                        spec => spec.Required()
                    )
                    .Handler(
                        (context, arguments) =>
                            Terminal.Log(
                                "Message: {0}",
                                string.Join(" ", arguments.Get<string[]>("message"))
                            )
                    )
            );
        }
    }
}
