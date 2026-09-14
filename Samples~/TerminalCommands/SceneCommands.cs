namespace WallstopStudios.DxCommandTerminal.Samples
{
    using Backend;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    /*
        Execution contexts are explicit on builder commands. The default is
        gameplay (Editor Play Mode + players); Edit Mode execution needs
        opt-in. `scene-reload` keeps the gameplay default, `scene-info` opts
        into every context including Edit Mode.
     */
    public sealed class SceneCommands : TerminalCommandSample
    {
        protected override void RegisterCommands()
        {
            Register(
                CommandBuilder
                    .Create("scene-reload", "Reloads the active scene")
                    /*
                        Default contexts: Editor Play Mode and players. This
                        command is deliberately unavailable from an Edit Mode
                        terminal session.
                     */
                    .Handler(
                        (context, arguments) =>
                            SceneManager.LoadScene(SceneManager.GetActiveScene().name)
                    )
            );

            Register(
                CommandBuilder
                    .Create("scene-info", "Logs the active scene name")
                    /*
                        All = Edit Mode + Editor Play Mode + players: usable
                        from an Edit Mode terminal session too.
                     */
                    .Contexts(CommandExecutionContextSets.All)
                    .Handler(
                        (context, arguments) =>
                            Terminal.Log("Active scene: {0}", SceneManager.GetActiveScene().name)
                    )
            );
        }
    }
}
