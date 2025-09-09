namespace WallstopStudios.DxCommandTerminal.Persistence
{
    using System;
    using System.Collections.Generic;
    using UI;

    [Serializable]
    public sealed class TerminalThemeConfigurations
    {
        public List<TerminalThemeConfiguration> configurations = new();

        public bool TryGetConfiguration(
            TerminalUI terminal,
            out TerminalThemeConfiguration configuration
        )
        {
            int existingIndex = -1;
            if (terminal != null)
            {
                for (int i = 0; i < configurations.Count; ++i)
                {
                    if (
                        string.Equals(
                            terminal.id,
                            configurations[i].terminalId,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        existingIndex = i;
                        break;
                    }
                }
            }

            bool exists = 0 <= existingIndex;
            configuration = exists ? configurations[existingIndex] : default;

            return exists;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="configuration"></param>
        /// <returns>True if a mutation happened, false if it was a no-op</returns>
        public bool AddOrUpdate(TerminalThemeConfiguration configuration)
        {
            int existingIndex = -1;
            for (int i = 0; i < configurations.Count; ++i)
            {
                if (
                    string.Equals(
                        configuration.terminalId,
                        configurations[i].terminalId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    existingIndex = i;
                    break;
                }
            }

            if (0 <= existingIndex)
            {
                if (configurations[existingIndex].Equals(configuration))
                {
                    return false;
                }
                configurations[existingIndex] = configuration;
            }
            else
            {
                configurations.Add(configuration);
            }

            return true;
        }
    }
}
