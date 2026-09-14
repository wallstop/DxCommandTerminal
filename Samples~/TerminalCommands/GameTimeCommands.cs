namespace WallstopStudios.DxCommandTerminal.Samples
{
    using Backend;
    using UnityEngine;

    /*
        Subcommands nested to any depth: `game` routes `time`, which routes
        `set`/`scale`, while `pause` stays a direct leaf. Run
        `game time set 9`, `game time scale 0.5`, or `game pause on`.
     */
    public sealed class GameTimeCommands : TerminalCommandSample
    {
        protected override void RegisterCommands()
        {
            Register(
                CommandBuilder
                    .Create("game", "Game-world debug controls")
                    .Handler(
                        (context, arguments) =>
                            Terminal.Log(
                                "Try: game time set <hour>, game time scale <multiplier>, game pause <on>"
                            )
                    )
                    .Subcommand(
                        "time",
                        time =>
                            time.Subcommand(
                                    "set",
                                    set =>
                                        set.Arg<int>("hour", spec => spec.Required().Range(0, 23))
                                            .Handler(
                                                (context, arguments) =>
                                                    Terminal.Log(
                                                        "In-game hour set to {0}:00.",
                                                        arguments.Get<int>("hour")
                                                    )
                                            )
                                )
                                .Subcommand(
                                    "scale",
                                    scale =>
                                        scale
                                            .Arg<float>(
                                                "multiplier",
                                                spec => spec.Required().Range(0f, 10f)
                                            )
                                            .Handler(
                                                (context, arguments) =>
                                                    Terminal.Log(
                                                        "Time scale set to {0}.",
                                                        arguments.Get<float>("multiplier")
                                                    )
                                            )
                                )
                    )
                    .Subcommand(
                        "pause",
                        pause =>
                            pause
                                .Arg<bool>("on", spec => spec.BoolChoices())
                                .Handler(
                                    (context, arguments) =>
                                        Terminal.Log("Game paused: {0}.", arguments.Get<bool>("on"))
                                )
                    )
            );
        }
    }
}
