namespace WallstopStudios.DxCommandTerminal.Persistence
{
    using System;
    using System.Collections;
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

        protected Font _lastSeenFont;
        protected string _lastSeenTheme;
        protected float? _nextUpdateTime;

        protected bool _persisting;
        protected Coroutine _persistence;

        protected virtual void Awake()
        {
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
            yield return CheckAndPersistAnyChanges(hydrate: true);
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
                _lastSeenFont == terminal.CurrentFont
                && string.Equals(
                    _lastSeenTheme,
                    terminal.CurrentTheme,
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

            _persistence = StartCoroutine(CheckAndPersistAnyChanges(hydrate: false));
        }

        protected virtual IEnumerator CheckAndPersistAnyChanges(bool hydrate)
        {
            _lastSeenFont = terminal.CurrentFont;
            _lastSeenTheme = terminal.CurrentTheme;
            _persisting = true;
            try
            {
                if (terminal == null)
                {
                    yield break;
                }

                string themeFile = ThemeFile;
                string directoryPath = Path.GetDirectoryName(themeFile);
                if (!string.IsNullOrWhiteSpace(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                TerminalThemeConfigurations configurations;
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
                    try
                    {
                        configurations = JsonUtility.FromJson<TerminalThemeConfigurations>(
                            inputJson
                        );
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(
                            $"Failed to parse theme file {themeFile}, defaulting to empty theme file: {e}",
                            this
                        );
                        configurations = new TerminalThemeConfigurations();
                    }
                }
                else
                {
                    Debug.Log(
                        $"Creating new theme file {themeFile} for terminal {terminal.id} ...",
                        this
                    );
                    configurations = new TerminalThemeConfigurations();
                }

                if (hydrate)
                {
                    if (
                        configurations.TryGetConfiguration(
                            terminal,
                            out TerminalThemeConfiguration existingConfiguration
                        )
                    )
                    {
                        int fontIndex;
                        if (terminal._fontPack == null)
                        {
                            fontIndex = -1;
                        }
                        else
                        {
                            fontIndex = terminal._fontPack._fonts.FindIndex(font =>
                                string.Equals(
                                    font.name,
                                    existingConfiguration.font,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            );
                        }

                        if (0 <= fontIndex)
                        {
                            _lastSeenFont = terminal._fontPack._fonts[fontIndex];
                            terminal.SetFont(_lastSeenFont, persist: true);
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"Failed to find persisted font {existingConfiguration.font} for terminal {terminal.id} while hydrating.",
                                this
                            );
                        }

                        int themeIndex;
                        if (terminal._themePack == null)
                        {
                            themeIndex = -1;
                        }
                        else
                        {
                            themeIndex = terminal._themePack._themeNames.FindIndex(theme =>
                                string.Equals(
                                    theme,
                                    existingConfiguration.theme,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            );
                        }

                        if (0 <= themeIndex)
                        {
                            _lastSeenTheme = terminal._themePack._themeNames[themeIndex];
                            terminal.SetTheme(_lastSeenTheme, persist: true);
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"Failed to find persisted theme {existingConfiguration.theme} for terminal {terminal.id} while hydrating.",
                                this
                            );
                        }

                        yield break;
                    }
                    else
                    {
                        Debug.Log(
                            $"Failed to find persisted configuration for terminal {terminal.id} while hydrating, defaulting to Prefab configuration.",
                            this
                        );
                    }
                }

                TerminalThemeConfiguration? maybeCurrentConfiguration = GetConfiguration();
                if (maybeCurrentConfiguration == null)
                {
                    yield break;
                }

                TerminalThemeConfiguration currentConfiguration = maybeCurrentConfiguration.Value;
                if (!configurations.AddOrUpdate(currentConfiguration))
                {
                    yield break;
                }

                string outputJson = JsonUtility.ToJson(configurations, prettyPrint: true);
                Debug.Log(
                    $"Writing theme file {themeFile} with contents:{Environment.NewLine}{outputJson}",
                    this
                );
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

            return new TerminalThemeConfiguration
            {
                terminalId = terminal.id,
                font = terminal.CurrentFont == null ? string.Empty : terminal.CurrentFont.name,
                theme = terminal.CurrentTheme ?? string.Empty,
            };
        }
    }
}
