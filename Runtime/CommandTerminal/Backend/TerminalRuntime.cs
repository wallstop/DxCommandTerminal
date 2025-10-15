namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;

    internal sealed class TerminalRuntime : ITerminalRuntime
    {
        private readonly HashSet<TerminalLogType> _ignoredLogTypesScratch = new();
        private readonly HashSet<string> _ignoredCommandScratch = new(
            StringComparer.OrdinalIgnoreCase
        );

        private CommandLog _log;
        private CommandHistory _history;
        private CommandShell _shell;
        private CommandAutoComplete _autoComplete;

        private bool _appliedIgnoreDefaultCommands;

        public CommandLog Log => _log;

        public CommandHistory History => _history;

        public CommandShell Shell => _shell;

        public CommandAutoComplete AutoComplete => _autoComplete;

        public TerminalRuntimeUpdateResult Configure(
            in TerminalRuntimeSettings settings,
            bool forceReset
        )
        {
            bool logRecreated = EnsureLog(settings, forceReset);
            bool historyRecreated = EnsureHistory(settings, forceReset);
            bool shellRecreated = EnsureShell(settings, forceReset, historyRecreated);
            bool autoCompleteRecreated = EnsureAutoComplete(
                forceReset || historyRecreated || shellRecreated
            );
            bool commandsRefreshed = EnsureShellConfiguration(
                settings,
                forceReset,
                shellRecreated
            );

            return new TerminalRuntimeUpdateResult(
                logRecreated,
                historyRecreated,
                shellRecreated,
                autoCompleteRecreated,
                commandsRefreshed
            );
        }

        public bool LogMessage(TerminalLogType type, string format, params object[] parameters)
        {
            CommandLog log = _log;
            if (log == null || string.IsNullOrEmpty(format))
            {
                return false;
            }

            string formattedMessage = parameters is { Length: > 0 }
                ? string.Format(format, parameters)
                : format;
            log.EnqueueMessage(formattedMessage, type, includeStackTrace: true);
            return true;
        }

        private bool EnsureLog(in TerminalRuntimeSettings settings, bool forceReset)
        {
            int desiredCapacity = Math.Max(0, settings.LogCapacity);
            if (forceReset || _log == null)
            {
                _log = new CommandLog(desiredCapacity, settings.IgnoredLogTypes);
                ApplyIgnoredLogTypes(settings.IgnoredLogTypes);
                return true;
            }

            if (_log.Capacity != desiredCapacity)
            {
                _log.Resize(desiredCapacity);
            }

            ApplyIgnoredLogTypes(settings.IgnoredLogTypes);
            return false;
        }

        private bool EnsureHistory(in TerminalRuntimeSettings settings, bool forceReset)
        {
            int desiredCapacity = Math.Max(0, settings.HistoryCapacity);
            if (forceReset || _history == null)
            {
                _history = new CommandHistory(desiredCapacity);
                return true;
            }

            if (_history.Capacity != desiredCapacity)
            {
                _history.Resize(desiredCapacity);
            }

            return false;
        }

        private bool EnsureShell(
            in TerminalRuntimeSettings settings,
            bool forceReset,
            bool historyRecreated
        )
        {
            if (forceReset || _shell == null || historyRecreated)
            {
                _shell = new CommandShell(_history);
                return true;
            }

            return false;
        }

        private bool EnsureAutoComplete(bool recreate)
        {
            if (recreate || _autoComplete == null)
            {
                _autoComplete = new CommandAutoComplete(_history, _shell, _shell.Commands.Keys);
                return true;
            }

            return false;
        }

        private bool EnsureShellConfiguration(
            in TerminalRuntimeSettings settings,
            bool forceReset,
            bool shellRecreated
        )
        {
            if (_shell == null)
            {
                return false;
            }

            bool shouldRefreshCommands = shellRecreated;
            if (!shouldRefreshCommands)
            {
                if (_shell.Commands.Count <= 0)
                {
                    shouldRefreshCommands = true;
                }
                else
                {
                    bool ignoreFlagChanged = _appliedIgnoreDefaultCommands
                        != settings.IgnoreDefaultCommands;
                    if (ignoreFlagChanged)
                    {
                        shouldRefreshCommands = true;
                    }
                    else
                    {
                        _ignoredCommandScratch.Clear();
                        for (int i = 0; i < settings.DisabledCommands.Count; ++i)
                        {
                            string command = settings.DisabledCommands[i];
                            if (!string.IsNullOrWhiteSpace(command))
                            {
                                _ignoredCommandScratch.Add(command);
                            }
                        }

                        if (!_shell.IgnoredCommands.SetEquals(_ignoredCommandScratch))
                        {
                            shouldRefreshCommands = true;
                        }
                    }
                }
            }

            if (forceReset)
            {
                shouldRefreshCommands = true;
            }

            if (!shouldRefreshCommands)
            {
                return false;
            }

            _shell.ClearAutoRegisteredCommands();
            _ignoredCommandScratch.Clear();
            for (int i = 0; i < settings.DisabledCommands.Count; ++i)
            {
                string command = settings.DisabledCommands[i];
                if (!string.IsNullOrWhiteSpace(command))
                {
                    _ignoredCommandScratch.Add(command);
                }
            }

            _shell.InitializeAutoRegisteredCommands(
                _ignoredCommandScratch,
                ignoreDefaultCommands: settings.IgnoreDefaultCommands
            );

            _appliedIgnoreDefaultCommands = settings.IgnoreDefaultCommands;
            return true;
        }

        private void ApplyIgnoredLogTypes(IReadOnlyList<TerminalLogType> ignoredLogTypes)
        {
            if (_log == null)
            {
                return;
            }

            _ignoredLogTypesScratch.Clear();
            if (ignoredLogTypes != null)
            {
                for (int i = 0; i < ignoredLogTypes.Count; ++i)
                {
                    _ignoredLogTypesScratch.Add(ignoredLogTypes[i]);
                }
            }

            if (_log.ignoredLogTypes.SetEquals(_ignoredLogTypesScratch))
            {
                return;
            }

            _log.ignoredLogTypes.Clear();
            _log.ignoredLogTypes.UnionWith(_ignoredLogTypesScratch);
        }
    }
}
