﻿namespace WallstopStudios.DxCommandTerminal.Persistence
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Attributes;
    using UI;
    using UnityEngine;

    [DisallowMultipleComponent]
    public class TerminalThemePersister : MonoBehaviour
    {
        protected virtual string ThemeFile =>
            Path.Join(Application.persistentDataPath, "DxCommandTerminal", "TerminalTheme.json");

        [Header("System")]
        public TerminalUI terminal;

        [Header("Config")]
        public bool savePeriodically = true;

        [DxShowIf(nameof(savePeriodically))]
        public float savePeriod = 1f;

        protected string _storagePath;

        private Font _lastSeenFont;
        private string _lastSeenTheme;
        private float? _nextUpdateTime;

        private bool _persisting;
        private Coroutine _persistence;

        private protected virtual void Awake()
        {
            _storagePath = Application.persistentDataPath;
            if (terminal != null)
            {
                return;
            }

            if (!TryGetComponent(out terminal))
            {
                Debug.LogError("Failed to find TerminalUI, Theme persistence will not work.", this);
            }
        }

        protected virtual IEnumerator Start()
        {
            if (terminal == null)
            {
                yield break;
            }

            string themeFile = ThemeFile;
            Debug.Log($"Attempting to initialize from {themeFile}...", this);
            yield return CheckAndPersistAnyChanges();
        }

        protected virtual void Update()
        {
            if (!savePeriodically)
            {
                return;
            }

            if (Time.time <= _nextUpdateTime)
            {
                return;
            }

            if (terminal == null)
            {
                return;
            }

            if (
                _lastSeenFont == terminal._font
                && string.Equals(
                    _lastSeenTheme,
                    terminal._currentTheme,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                _nextUpdateTime = Time.time + savePeriod;
                return;
            }

            if (_persisting)
            {
                return;
            }

            if (_persistence != null)
            {
                return;
            }

            _persistence = StartCoroutine(CheckAndPersistAnyChanges());
        }

        protected virtual IEnumerator CheckAndPersistAnyChanges()
        {
            _lastSeenFont = terminal._font;
            _lastSeenTheme = terminal._currentTheme;
            _persisting = true;
            try
            {
                if (terminal == null)
                {
                    yield break;
                }

                TerminalThemeConfiguration? maybeCurrentConfiguration = GetConfiguration();
                if (maybeCurrentConfiguration == null)
                {
                    yield break;
                }

                TerminalThemeConfiguration currentConfiguration = maybeCurrentConfiguration.Value;

                string themeFile = ThemeFile;
                string directoryPath = Path.GetDirectoryName(themeFile);
                if (!string.IsNullOrWhiteSpace(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                List<TerminalThemeConfiguration> configurations;
                if (File.Exists(themeFile))
                {
                    Task<string> readerTask = File.ReadAllTextAsync(themeFile);
                    while (!readerTask.IsCompleted)
                    {
                        yield return null;
                    }

                    if (!readerTask.IsCompletedSuccessfully)
                    {
                        _lastSeenFont = null;
                        _lastSeenTheme = null;
                        Debug.LogError(
                            $"Failed to read theme file {themeFile}: {readerTask.Exception}.",
                            this
                        );
                        yield break;
                    }

                    string inputJson = readerTask.Result;

                    configurations = JsonUtility.FromJson<List<TerminalThemeConfiguration>>(
                        inputJson
                    );
                    int existingIndex = configurations.FindIndex(config =>
                        string.Equals(terminal.id, config.terminalId)
                    );
                    if (0 <= existingIndex)
                    {
                        TerminalThemeConfiguration configuration = configurations[existingIndex];
                        if (configuration.Equals(currentConfiguration))
                        {
                            Debug.Log(
                                $"Theme file {themeFile} already matches current configuration.",
                                this
                            );
                            yield break;
                        }

                        Debug.Log(
                            $"Updating theme file {themeFile} for terminal {terminal.id} ...",
                            this
                        );
                        ;
                        configurations[existingIndex] = currentConfiguration;
                    }
                    else
                    {
                        Debug.Log(
                            $"Adding new theme file {themeFile} for terminal {terminal.id} ...",
                            this
                        );
                        configurations.Add(currentConfiguration);
                    }
                }
                else
                {
                    Debug.Log(
                        $"Creating new theme file {themeFile} for terminal {terminal.id} ...",
                        this
                    );
                    ;
                    configurations = new List<TerminalThemeConfiguration> { currentConfiguration };
                }

                string outputJson = JsonUtility.ToJson(configurations);
                Task writerTask = File.WriteAllTextAsync(themeFile, outputJson);
                while (!writerTask.IsCompleted)
                {
                    yield return null;
                }

                if (!writerTask.IsCompletedSuccessfully)
                {
                    _lastSeenFont = null;
                    _lastSeenTheme = null;
                    Debug.LogError(
                        $"Failed to write theme file {themeFile} (terminal {terminal.id}): {writerTask.Exception}",
                        this
                    );
                }
                else
                {
                    Debug.Log($"Theme file {themeFile} successfully updated.", this);
                }
            }
            finally
            {
                _nextUpdateTime = Time.time + savePeriod;
                _persisting = false;
                _persistence = null;
            }
        }

        public virtual TerminalThemeConfiguration? GetConfiguration()
        {
            if (terminal == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(terminal.id))
            {
                return null;
            }

            return new TerminalThemeConfiguration()
            {
                terminalId = terminal.id,
                font = (terminal._font == null ? string.Empty : terminal._font.name),
                theme = terminal._currentTheme ?? string.Empty,
            };
        }
    }
}
