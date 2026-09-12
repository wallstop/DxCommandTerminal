namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using UnityEngine;

    internal sealed class BuilderLifecycleComponent : MonoBehaviour
    {
        public int Invocations { get; private set; }

        private CommandRegistrationHandle Handle { get; set; }

        private void OnEnable()
        {
            /*
               The documented lifecycle pattern: register on enable, dispose on
               disable, so scene transitions never leak commands.
             */
            Terminal.Shell.AddCommand(
                CommandBuilder
                    .Create("lifecycle-hit")
                    .Arg<int>("value", spec => spec.Required())
                    .Handler((context, arguments) => ++Invocations),
                out CommandRegistrationHandle handle
            );
            Handle = handle;
        }

        private void OnDisable()
        {
            Handle?.Dispose();
            Handle = null;
        }
    }
}
