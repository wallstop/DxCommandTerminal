namespace CommandTerminal
{
    using System.Collections.Generic;
    using System.Text;
    using Attributes;
    using UnityEngine;
    using UnityEngine.Assertions;
    using UnityEngine.Serialization;

    public enum TerminalState
    {
        Close,
        OpenSmall,
        OpenFull,
    }

    public class Terminal : MonoBehaviour
    {
        [FormerlySerializedAs("MaxHeight")]
        [Header("Window")]
        [Range(0, 1)]
        [SerializeField]
        private float _maxHeight = 0.7f;

        [FormerlySerializedAs("SmallTerminalRatio")]
        [SerializeField]
        [Range(0, 1)]
        private float _smallTerminalRatio = 0.33f;

        [FormerlySerializedAs("ToggleSpeed")]
        [Range(100, 1000)]
        [SerializeField]
        private float _toggleSpeed = 360;

        [FormerlySerializedAs("ToggleHotkey")]
        [SerializeField]
        private string _toggleHotkey = "`";

        [FormerlySerializedAs("ToggleFullHotkey")]
        [SerializeField]
        private string _toggleFullHotkey = "#`";

        [FormerlySerializedAs("BufferSize")]
        [SerializeField]
        private int _bufferSize = 512;

        [FormerlySerializedAs("ConsoleFont")]
        [Header("Input")]
        [SerializeField]
        private Font _consoleFont;

        [FormerlySerializedAs("InputCaret")]
        [SerializeField]
        private string _inputCaret = ">";

        [FormerlySerializedAs("ShowGUIButtons")]
        [SerializeField]
        private bool _showGUIButtons;

        [FormerlySerializedAs("RightAlignButtons")]
        [SerializeField]
        private bool _rightAlignButtons;

        [FormerlySerializedAs("InputContrast")]
        [Header("Theme")]
        [Range(0, 1)]
        [SerializeField]
        private float _inputContrast;

        [FormerlySerializedAs("_nputAlpha")]
        [FormerlySerializedAs("InputAlpha")]
        [Range(0, 1)]
        [SerializeField]
        private float _inputAlpha = 0.5f;

        [FormerlySerializedAs("BackgroundColor")]
        [SerializeField]
        private Color _backgroundColor = Color.black;

        [FormerlySerializedAs("ForegroundColor")]
        [SerializeField]
        private Color _foregroundColor = Color.white;

        [FormerlySerializedAs("ShellColor")]
        [SerializeField]
        private Color _shellColor = Color.white;

        [FormerlySerializedAs("InputColor")]
        [SerializeField]
        private Color _inputColor = Color.cyan;

        [FormerlySerializedAs("WarningColor")]
        [SerializeField]
        private Color _warningColor = Color.yellow;

        [FormerlySerializedAs("ErrorColor")]
        [SerializeField]
        private Color _errorColor = Color.red;

        [SerializeField]
        internal List<string> _disabledCommands = new();

        private TerminalState _state;
        private TextEditor _editorState;
        private bool _inputFix;
        private bool _moveCursor;
        private bool _initialOpen; // Used to focus on TextField when console opens
        private Rect _window;
        private float _currentOpenT;
        private float _openTarget;
        private float _realWindowSize;
        private string _commandText;
        private string _cachedCommandText;
        private Vector2 _scrollPosition;
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _inputStyle;
        private Texture2D _backgroundTexture;
        private Texture2D _inputBackgroundTexture;

        public static CommandLog Buffer { get; private set; }
        public static CommandShell Shell { get; private set; }
        public static CommandHistory History { get; private set; }
        public static CommandAutocomplete Autocomplete { get; private set; }

        public static bool IssuedError => Shell.IssuedErrorMessage != null;

        public bool IsClosed =>
            _state == TerminalState.Close && Mathf.Approximately(_currentOpenT, _openTarget);

        public static void Log(string format, params object[] message)
        {
            Log(TerminalLogType.ShellMessage, format, message);
        }

        public static void Log(TerminalLogType type, string format, params object[] message)
        {
            Buffer.HandleLog(string.Format(format, message), type);
        }

        public void SetState(TerminalState newState)
        {
            _inputFix = true;
            _cachedCommandText = _commandText;
            _commandText = "";

            switch (newState)
            {
                case TerminalState.Close:
                {
                    _openTarget = 0;
                    break;
                }
                case TerminalState.OpenSmall:
                {
                    _openTarget = Screen.height * _maxHeight * _smallTerminalRatio;
                    if (_currentOpenT > _openTarget)
                    {
                        // Prevent resizing from OpenFull to OpenSmall if window y position
                        // is greater than OpenSmall's target
                        _openTarget = 0;
                        _state = TerminalState.Close;
                        return;
                    }
                    _realWindowSize = _openTarget;
                    _scrollPosition.y = int.MaxValue;
                    break;
                }
                case TerminalState.OpenFull:
                default:
                {
                    _realWindowSize = Screen.height * _maxHeight;
                    _openTarget = _realWindowSize;
                    break;
                }
            }

            _state = newState;
        }

        public void ToggleState(TerminalState newState)
        {
            SetState(_state == newState ? TerminalState.Close : newState);
        }

        private void OnEnable()
        {
            Buffer = new CommandLog(_bufferSize);
            Shell = new CommandShell();
            History = new CommandHistory();
            Autocomplete = new CommandAutocomplete();

            // Hook Unity log events
            Application.logMessageReceivedThreaded += HandleUnityLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= HandleUnityLog;
        }

        private void Start()
        {
            if (_consoleFont == null)
            {
                _consoleFont = Font.CreateDynamicFontFromOSFont("Courier New", 16);
                Debug.LogWarning("Command Console Warning: Please assign a font.");
            }

            _commandText = "";
            _cachedCommandText = _commandText;
            Assert.AreNotEqual(
                _toggleHotkey.ToLower(),
                "return",
                "Return is not a valid ToggleHotkey"
            );

            SetupWindow();
            SetupInput();
            SetupLabels();

            Shell.RegisterCommands();

            if (IssuedError)
            {
                Log(TerminalLogType.Error, "Error: {0}", Shell.IssuedErrorMessage);
            }

            foreach (var command in Shell.Commands)
            {
                Autocomplete.Register(command.Key);
            }
        }

        private void OnGUI()
        {
            if (Event.current.Equals(Event.KeyboardEvent(_toggleHotkey)))
            {
                SetState(TerminalState.OpenSmall);
                _initialOpen = true;
            }
            else if (Event.current.Equals(Event.KeyboardEvent(_toggleFullHotkey)))
            {
                SetState(TerminalState.OpenFull);
                _initialOpen = true;
            }

            if (_showGUIButtons)
            {
                DrawGUIButtons();
            }

            if (IsClosed)
            {
                return;
            }

            HandleOpenness();
            _window = GUILayout.Window(88, _window, DrawConsole, "", _windowStyle);
        }

        private void SetupWindow()
        {
            _realWindowSize = Screen.height * _maxHeight / 3;
            _window = new Rect(0, _currentOpenT - _realWindowSize, Screen.width, _realWindowSize);

            // Set background color
            _backgroundTexture = new Texture2D(1, 1);
            _backgroundTexture.SetPixel(0, 0, _backgroundColor);
            _backgroundTexture.Apply();

            _windowStyle = new GUIStyle();
            _windowStyle.normal.background = _backgroundTexture;
            _windowStyle.padding = new RectOffset(4, 4, 4, 4);
            _windowStyle.normal.textColor = _foregroundColor;
            _windowStyle.font = _consoleFont;
        }

        private void SetupLabels()
        {
            _labelStyle = new GUIStyle
            {
                font = _consoleFont,
                normal = { textColor = _foregroundColor },
                wordWrap = true,
            };
        }

        private void SetupInput()
        {
            _inputStyle = new GUIStyle
            {
                padding = new RectOffset(4, 4, 4, 4),
                font = _consoleFont,
                fixedHeight = _consoleFont.fontSize * 1.6f,
                normal = { textColor = _inputColor },
            };

            Color darkBackground = new()
            {
                r = _backgroundColor.r - _inputContrast,
                g = _backgroundColor.g - _inputContrast,
                b = _backgroundColor.b - _inputContrast,
                a = _inputAlpha,
            };

            _inputBackgroundTexture = new Texture2D(1, 1);
            _inputBackgroundTexture.SetPixel(0, 0, darkBackground);
            _inputBackgroundTexture.Apply();
            _inputStyle.normal.background = _inputBackgroundTexture;
        }

        private void DrawConsole(int window2D)
        {
            GUILayout.BeginVertical();

            _scrollPosition = GUILayout.BeginScrollView(
                _scrollPosition,
                false,
                false,
                GUIStyle.none,
                GUIStyle.none
            );
            GUILayout.FlexibleSpace();
            DrawLogs();
            GUILayout.EndScrollView();

            if (_moveCursor)
            {
                CursorToEnd();
                _moveCursor = false;
            }

            if (Event.current.Equals(Event.KeyboardEvent("escape")))
            {
                SetState(TerminalState.Close);
            }
            else if (
                Event.current.Equals(Event.KeyboardEvent("return"))
                || Event.current.Equals(Event.KeyboardEvent("[enter]"))
            )
            {
                EnterCommand();
            }
            else if (Event.current.Equals(Event.KeyboardEvent("up")))
            {
                _commandText = History.Previous();
                _moveCursor = true;
            }
            else if (Event.current.Equals(Event.KeyboardEvent("down")))
            {
                _commandText = History.Next();
            }
            else if (Event.current.Equals(Event.KeyboardEvent(_toggleHotkey)))
            {
                ToggleState(TerminalState.OpenSmall);
            }
            else if (Event.current.Equals(Event.KeyboardEvent(_toggleFullHotkey)))
            {
                ToggleState(TerminalState.OpenFull);
            }
            else if (Event.current.Equals(Event.KeyboardEvent("tab")))
            {
                CompleteCommand();
                _moveCursor = true; // Wait till next draw call
            }

            GUILayout.BeginHorizontal();

            if (_inputCaret != "")
            {
                GUILayout.Label(_inputCaret, _inputStyle, GUILayout.Width(_consoleFont.fontSize));
            }

            GUI.SetNextControlName("command_text_field");
            _commandText = GUILayout.TextField(_commandText, _inputStyle);

            if (_inputFix && _commandText.Length > 0)
            {
                _commandText = _cachedCommandText; // Otherwise the TextField picks up the ToggleHotkey character event
                _inputFix = false; // Prevents checking string Length every draw call
            }

            if (_initialOpen)
            {
                GUI.FocusControl("command_text_field");
                _initialOpen = false;
            }

            if (
                _showGUIButtons
                && GUILayout.Button("| run", _inputStyle, GUILayout.Width(Screen.width / 10f))
            )
            {
                EnterCommand();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawLogs()
        {
            foreach (LogItem log in Buffer.Logs)
            {
                _labelStyle.normal.textColor = GetLogColor(log.type);
                GUILayout.Label(log.message, _labelStyle);
            }
        }

        private void DrawGUIButtons()
        {
            int size = _consoleFont.fontSize;
            float xPosition = _rightAlignButtons ? Screen.width - 7 * size : 0;

            // 7 is the number of chars in the button plus some padding, 2 is the line height.
            // The layout will resize according to the font size.
            GUILayout.BeginArea(new Rect(xPosition, _currentOpenT, 7 * size, size * 2));
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Small", _windowStyle))
            {
                ToggleState(TerminalState.OpenSmall);
            }
            else if (GUILayout.Button("Full", _windowStyle))
            {
                ToggleState(TerminalState.OpenFull);
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void HandleOpenness()
        {
            float dt = _toggleSpeed * Time.unscaledDeltaTime;

            if (_currentOpenT < _openTarget)
            {
                _currentOpenT += dt;
                if (_currentOpenT > _openTarget)
                {
                    _currentOpenT = _openTarget;
                }
            }
            else if (_currentOpenT > _openTarget)
            {
                _currentOpenT -= dt;
                if (_currentOpenT < _openTarget)
                {
                    _currentOpenT = _openTarget;
                }
            }
            else
            {
                if (_inputFix)
                {
                    _inputFix = false;
                }
                return; // Already at target
            }

            _window = new Rect(0, _currentOpenT - _realWindowSize, Screen.width, _realWindowSize);
        }

        private void EnterCommand()
        {
            Log(TerminalLogType.Input, "{0}", _commandText);
            Shell.RunCommand(_commandText);
            History.Push(_commandText);

            if (IssuedError)
            {
                Log(TerminalLogType.Error, "Error: {0}", Shell.IssuedErrorMessage);
            }

            _commandText = "";
            _scrollPosition.y = int.MaxValue;
        }

        private void CompleteCommand()
        {
            string headText = _commandText;
            int formatWidth = 0;

            string[] completionBuffer = Autocomplete.Complete(ref headText, ref formatWidth);
            int completionLength = completionBuffer.Length;

            if (completionLength != 0)
            {
                _commandText = headText;
            }

            if (completionLength > 1)
            {
                // Print possible completions
                StringBuilder logBuffer = new();

                foreach (string completion in completionBuffer)
                {
                    logBuffer.Append(completion.PadRight(formatWidth + 4));
                }

                Log("{0}", logBuffer);
                _scrollPosition.y = int.MaxValue;
            }
        }

        private void CursorToEnd()
        {
            if (_editorState == null)
            {
                _editorState = (TextEditor)
                    GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            }

            _editorState.MoveCursorToPosition(new Vector2(999, 999));
        }

        private void HandleUnityLog(string message, string stackTrace, LogType type)
        {
            Buffer.HandleLog(message, stackTrace, (TerminalLogType)type);
            _scrollPosition.y = int.MaxValue;
        }

        private Color GetLogColor(TerminalLogType type)
        {
            switch (type)
            {
                case TerminalLogType.Message:
                    return _foregroundColor;
                case TerminalLogType.Warning:
                    return _warningColor;
                case TerminalLogType.Input:
                    return _inputColor;
                case TerminalLogType.ShellMessage:
                    return _shellColor;
                default:
                    return _errorColor;
            }
        }
    }
}
