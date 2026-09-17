namespace WallstopStudios.DxCommandTerminal.Samples
{
    using System;
    using Backend;
    using UnityEngine;

    /*
        Argument kinds on builder commands: bool choices, enum choices, and a
        numeric range. Run `god on`, `weather storm` (Tab cycles the enum
        names), or `timescale 2.5`.
     */
    public sealed class ArgumentKinds : TerminalCommandSample
    {
        private bool _godMode;
        private WeatherCondition _weather = WeatherCondition.Clear;
        private float _timeScale = 1f;

        protected override void RegisterCommands()
        {
            Register(
                CommandBuilder
                    .Create("god", "Toggles god mode")
                    .Arg<bool>("enabled", spec => spec.BoolChoices())
                    .Handler(
                        (context, arguments) =>
                        {
                            _godMode = arguments.Get<bool>("enabled");
                            Terminal.Log("God mode {0}.", _godMode ? "enabled" : "disabled");
                        }
                    )
            );

            Register(
                CommandBuilder
                    .Create("weather", "Sets the weather")
                    .Arg<WeatherCondition>(
                        "condition",
                        spec =>
                            spec.Default(WeatherCondition.Clear)
                                .Choices(
                                    WeatherCondition.Clear,
                                    WeatherCondition.Rain,
                                    WeatherCondition.Storm,
                                    WeatherCondition.Fog
                                )
                    )
                    .Handler(
                        (context, arguments) =>
                        {
                            _weather = arguments.Get<WeatherCondition>("condition");
                            Terminal.Log("Weather set to {0}.", _weather);
                        }
                    )
            );

            Register(
                CommandBuilder
                    .Create("timescale", "Sets the time scale")
                    .Arg<float>("multiplier", spec => spec.Range(0f, 10f).Default(1f))
                    .Handler(
                        (context, arguments) =>
                        {
                            _timeScale = arguments.Get<float>("multiplier");
                            Terminal.Log("Time scale set to {0}.", _timeScale);
                        }
                    )
            );
        }

        private enum WeatherCondition
        {
            [Obsolete("Use a valid value")]
            Unknown = 0,
            Clear = 4,
            Rain = 1,
            Storm = 2,
            Fog = 3,
        }
    }
}
