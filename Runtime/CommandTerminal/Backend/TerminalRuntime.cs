namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;

    internal sealed class TerminalRuntime : ITerminalRuntime
    {
        private readonly HashSet<TerminalLogType> _ignoredLogTypesScratch = new();
        private readonly HashSet<TerminalLogType> _allowedLogTypesScratch = new();
        private readonly HashSet<string> _ignoredCommandScratch = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly HashSet<string> _allowedCommandScratch = new(
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
            bool commandsRefreshed = EnsureShellConfiguration(settings, forceReset, shellRecreated);

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
                _log = new CommandLog(
                    desiredCapacity,
                    settings.BlockedLogTypes,
                    settings.AllowedLogTypes
                );
                ApplyLogFilters(settings.BlockedLogTypes, settings.AllowedLogTypes);
                return true;
            }

            if (_log.Capacity != desiredCapacity)
            {
                _log.Resize(desiredCapacity);
            }

            ApplyLogFilters(settings.BlockedLogTypes, settings.AllowedLogTypes);
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
                    bool ignoreFlagChanged =
                        _appliedIgnoreDefaultCommands != settings.IgnoreDefaultCommands;
                    if (ignoreFlagChanged)
                    {
                        shouldRefreshCommands = true;
                    }
                    else
                    {
                        _ignoredCommandScratch.Clear();
                        for (int i = 0; i < settings.BlockedCommands.Count; ++i)
                        {
                            string command = settings.BlockedCommands[i];
                            if (!string.IsNullOrWhiteSpace(command))
                            {
                                _ignoredCommandScratch.Add(command);
                            }
                        }

                        _allowedCommandScratch.Clear();
                        for (int i = 0; i < settings.AllowedCommands.Count; ++i)
                        {
                            string command = settings.AllowedCommands[i];
                            if (!string.IsNullOrWhiteSpace(command))
                            {
                                _allowedCommandScratch.Add(command);
                            }
                        }

                        bool blockedChanged = !_shell.IgnoredCommands.SetEquals(
                            _ignoredCommandScratch
                        );
                        bool allowedChanged = !_shell.AllowedCommands.SetEquals(
                            _allowedCommandScratch
                        );
                        if (blockedChanged || allowedChanged)
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
            for (int i = 0; i < settings.BlockedCommands.Count; ++i)
            {
                string command = settings.BlockedCommands[i];
                if (!string.IsNullOrWhiteSpace(command))
                {
                    _ignoredCommandScratch.Add(command);
                }
            }

            _allowedCommandScratch.Clear();
            for (int i = 0; i < settings.AllowedCommands.Count; ++i)
            {
                string command = settings.AllowedCommands[i];
                if (!string.IsNullOrWhiteSpace(command))
                {
                    _allowedCommandScratch.Add(command);
                }
            }

            _shell.InitializeAutoRegisteredCommands(
                _ignoredCommandScratch,
                ignoreDefaultCommands: settings.IgnoreDefaultCommands,
                allowedCommands: _allowedCommandScratch
            );

            _appliedIgnoreDefaultCommands = settings.IgnoreDefaultCommands;
            return true;
        }

        private void ApplyLogFilters(
            IReadOnlyList<TerminalLogType> blockedLogTypes,
            IReadOnlyList<TerminalLogType> allowedLogTypes
        )
        {
            if (_log == null)
            {
                return;
            }

            _ignoredLogTypesScratch.Clear();
            if (blockedLogTypes != null)
            {
                for (int i = 0; i < blockedLogTypes.Count; ++i)
                {
                    _ignoredLogTypesScratch.Add(blockedLogTypes[i]);
                }
            }

            _allowedLogTypesScratch.Clear();
            if (allowedLogTypes != null)
            {
                for (int i = 0; i < allowedLogTypes.Count; ++i)
                {
                    _allowedLogTypesScratch.Add(allowedLogTypes[i]);
                }
            }

            bool blockedChanged = !_log.ignoredLogTypes.SetEquals(_ignoredLogTypesScratch);
            bool allowedChanged = !_log.allowedLogTypes.SetEquals(_allowedLogTypesScratch);
            if (!blockedChanged && !allowedChanged)
            {
                return;
            }

            if (blockedChanged)
            {
                _log.ignoredLogTypes.Clear();
                _log.ignoredLogTypes.UnionWith(_ignoredLogTypesScratch);
            }

            if (allowedChanged)
            {
                _log.allowedLogTypes.Clear();
                _log.allowedLogTypes.UnionWith(_allowedLogTypesScratch);
            }
        }
    }
}
