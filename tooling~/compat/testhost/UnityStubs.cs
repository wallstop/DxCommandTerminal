/*
    The stand-in UnityEngine surface for the Unity-free test host (see
    testhost.csproj). Scope rules:

    - A member exists here only because an included source names it.
    - A behavior a fixture pins is implemented genuinely: the caret
      fields really hold what the setters wrote, the system clipboard
      really remembers, Debug.Log really routes through unityLogger and
      honors logEnabled, Mathf really does the math. A test that fails
      against these semantics has found something.
    - Engine-only behavior (rendering, the panel, asset databases) has
      no body here: fixtures that need it are excluded by name in the
      csproj, with the reason, and the live-editor suite answers them.

    This file is never compiled by Unity: it lives under tooling~,
    which Unity never imports.
 */
namespace UnityEngine
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    public enum LogType
    {
        Error,
        Assert,
        Warning,
        Log,
        Exception,
    }

    public enum LogOption
    {
        None,
        Raw,
    }

    public enum EventModifiers
    {
        None = 0,
        Shift = 1,
        Control = 2,
        Alt = 4,
        Command = 8,
        Numeric = 16,
        CapsLock = 32,
        FunctionKey = 64,
    }

    /*
        The full Unity KeyCode table (the engine's public values). The
        keymap fixtures enumerate it broadly, and a missing member would
        turn a keyboard contract into a compile error in the host only -
        the wrong trade. Values are the engine's, so anything that leaks
        a code into a message reads the same as it would in Unity.
     */
    public enum KeyCode
    {
        None = 0,
        Backspace = 8,
        Tab = 9,
        Clear = 12,
        Return = 13,
        Pause = 19,
        Escape = 27,
        Space = 32,
        Exclaim = 33,
        DoubleQuote = 34,
        Hash = 35,
        Dollar = 36,
        Ampersand = 38,
        Quote = 39,
        LeftParen = 40,
        RightParen = 41,
        Asterisk = 42,
        Plus = 43,
        Comma = 44,
        Minus = 45,
        Period = 46,
        Slash = 47,
        Alpha0 = 48,
        Alpha1 = 49,
        Alpha2 = 50,
        Alpha3 = 51,
        Alpha4 = 52,
        Alpha5 = 53,
        Alpha6 = 54,
        Alpha7 = 55,
        Alpha8 = 56,
        Alpha9 = 57,
        Colon = 58,
        Semicolon = 59,
        Less = 60,
        Equals = 61,
        Greater = 62,
        Question = 63,
        At = 64,
        LeftBracket = 91,
        Backslash = 92,
        RightBracket = 93,
        Caret = 94,
        Underscore = 95,
        BackQuote = 96,
        A = 97,
        B = 98,
        C = 99,
        D = 100,
        E = 101,
        F = 102,
        G = 103,
        H = 104,
        I = 105,
        J = 106,
        K = 107,
        L = 108,
        M = 109,
        N = 110,
        O = 111,
        P = 112,
        Q = 113,
        R = 114,
        S = 115,
        T = 116,
        U = 117,
        V = 118,
        W = 119,
        X = 120,
        Y = 121,
        Z = 122,
        Delete = 127,
        Keypad0 = 256,
        Keypad1 = 257,
        Keypad2 = 258,
        Keypad3 = 259,
        Keypad4 = 260,
        Keypad5 = 261,
        Keypad6 = 262,
        Keypad7 = 263,
        Keypad8 = 264,
        Keypad9 = 265,
        KeypadPeriod = 266,
        KeypadDivide = 267,
        KeypadMultiply = 268,
        KeypadMinus = 269,
        KeypadPlus = 270,
        KeypadEnter = 271,
        KeypadEquals = 272,
        UpArrow = 273,
        DownArrow = 274,
        RightArrow = 275,
        LeftArrow = 276,
        Insert = 277,
        Home = 278,
        End = 279,
        PageUp = 280,
        PageDown = 281,
        F1 = 282,
        F2 = 283,
        F3 = 284,
        F4 = 285,
        F5 = 286,
        F6 = 287,
        F7 = 288,
        F8 = 289,
        F9 = 290,
        F10 = 291,
        F11 = 292,
        F12 = 293,
        F13 = 294,
        F14 = 295,
        F15 = 296,
        Numlock = 300,
        CapsLock = 301,
        ScrollLock = 302,
        RightShift = 303,
        LeftShift = 304,
        RightControl = 305,
        LeftControl = 306,
        RightAlt = 307,
        LeftAlt = 308,
        RightCommand = 309,
        LeftCommand = 310,
        RightWindows = 311,
        LeftWindows = 312,
        AltGr = 313,
        Help = 315,
        Print = 316,
        SysReq = 317,
        Break = 318,
        Menu = 319,
        Mouse0 = 323,
        Mouse1 = 324,
        Mouse2 = 325,
        Mouse3 = 326,
        Mouse4 = 327,
        Mouse5 = 328,
        Mouse6 = 329,
    }

    public interface ILogHandler
    {
        void LogFormat(LogType logType, Object context, string format, params object[] args);

        void LogException(Exception exception, Object context);
    }

    public interface ILogger : ILogHandler
    {
        bool logEnabled { get; set; }

        void Log(LogType logType, object message);

        void Log(LogType logType, object message, Object context);

        void LogException(Exception exception);
    }

    public class Logger : ILogger
    {
        public bool logEnabled { get; set; } = true;

        public void Log(LogType logType, object message)
        {
            if (!logEnabled)
            {
                return;
            }

            Console.WriteLine($"[{logType}] {message}");
        }

        public void Log(LogType logType, object message, Object context)
        {
            Log(logType, message);
        }

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            if (!logEnabled)
            {
                return;
            }

            Console.WriteLine(
                $"[{logType}] " + string.Format(CultureInfo.InvariantCulture, format, args)
            );
        }

        public void LogException(Exception exception)
        {
            if (!logEnabled)
            {
                return;
            }

            Console.WriteLine($"[Exception] {exception}");
        }

        public void LogException(Exception exception, Object context)
        {
            LogException(exception);
        }
    }

    /*
        Routes exactly like the engine documents: the convenience family
        forwards to unityLogger, which is swappable, and logEnabled gates
        the record. Records also land in Records so a fixture (or a
        diagnosis) can read what a run logged without owning the logger.
     */
    public static class Debug
    {
        public static List<string> Records { get; } = new List<string>();

        private static readonly Logger DefaultLogger = new Logger();

        private static ILogger _unityLogger = DefaultLogger;

        public static ILogger unityLogger
        {
            get => _unityLogger;
            set => _unityLogger = value ?? DefaultLogger;
        }

        public static bool isDebugBuild => true;

        public static void Log(object message)
        {
            Record("Log", message);
            _unityLogger.Log(LogType.Log, message);
        }

        public static void Log(object message, Object context)
        {
            Record("Log", message);
            _unityLogger.Log(LogType.Log, message, context);
        }

        public static void LogWarning(object message)
        {
            Record("Warning", message);
            _unityLogger.Log(LogType.Warning, message);
        }

        public static void LogWarning(object message, Object context)
        {
            Record("Warning", message);
            _unityLogger.Log(LogType.Warning, message, context);
        }

        public static void LogError(object message)
        {
            Record("Error", message);
            _unityLogger.Log(LogType.Error, message);
        }

        public static void LogError(object message, Object context)
        {
            Record("Error", message);
            _unityLogger.Log(LogType.Error, message, context);
        }

        public static void LogException(Exception exception)
        {
            Record("Exception", exception);
            _unityLogger.LogException(exception);
        }

        public static void LogException(Exception exception, Object context)
        {
            Record("Exception", exception);
            _unityLogger.LogException(exception, context);
        }

        public static void Assert(bool condition, object message = null)
        {
            if (condition)
            {
                return;
            }

            string text = message != null ? "Assertion failed: " + message : "Assertion failed";
            Record("Assert", text);
            _unityLogger.Log(LogType.Assert, text);
        }

        private static void Record(string level, object message)
        {
            string text = $"[{level}] {message}";
            Records.Add(text);
            Application.RaiseLog(text, string.Empty, LogType.Log);
        }
    }

    public class Object
    {
        private static int _nextInstanceId = 1;

        public int GetInstanceID() => _nextInstanceId++;

        public string name { get; set; } = string.Empty;

        public override string ToString() => string.IsNullOrEmpty(name) ? GetType().Name : name;
    }

    public class MonoBehaviour : Object { }

    public class ScriptableObject : Object
    {
        /*
            The engine pins a live native object behind CreateInstance; the
            host pins a plain constructed instance. Fixtures only ever read
            the serialized fields off the result, and those are the same
            fields either way.
         */
        public static T CreateInstance<T>()
            where T : ScriptableObject, new()
        {
            return new T();
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponentAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName { get; set; }

        public string menuName { get; set; }

        public int order { get; set; }
    }

    public class PropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : PropertyAttribute
    {
        public string tooltip { get; }

        public TooltipAttribute(string tooltip)
        {
            this.tooltip = tooltip;
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : PropertyAttribute
    {
        public string header { get; }

        public HeaderAttribute(string header)
        {
            this.header = header;
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RangeAttribute : PropertyAttribute
    {
        public float min { get; }

        public float max { get; }

        public RangeAttribute(float min, float max)
        {
            this.min = min;
            this.max = max;
        }

        public RangeAttribute(int min, int max)
            : this((float)min, (float)max) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : PropertyAttribute
    {
        public float min { get; }

        public MinAttribute(float min)
        {
            this.min = min;
        }

        public MinAttribute(int min)
            : this((float)min) { }
    }

    public static class Mathf
    {
        public const float Epsilon = 1e-6f;

        public static int Max(int a, int b) => a >= b ? a : b;

        public static int Min(int a, int b) => a <= b ? a : b;

        public static float Max(float a, float b) => (double)a >= (double)b ? a : b;

        public static float Min(float a, float b) => (double)a <= (double)b ? a : b;

        public static int Clamp(int value, int min, int max) =>
            value < min ? min : (value > max ? max : value);

        public static float Clamp(float value, float min, float max) =>
            (double)value < (double)min ? min : ((double)value > (double)max ? max : value);

        public static float Clamp01(float value) => Clamp(value, 0f, 1f);

        public static bool Approximately(float a, float b) =>
            Math.Abs((double)a - b)
            <= Math.Max(1e-6 * Math.Max(Math.Abs((double)a), Math.Abs((double)b)), 1e-44);

        public static int RoundToInt(float value) =>
            (int)Math.Round((double)value, MidpointRounding.AwayFromZero);

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
    }

    public static class GUIUtility
    {
        /*
            The engine's clipboard is a native round-trip with per-platform
            limits; the host's is the process-wide string the last write
            left. Every fixture that swaps it restores it in TearDown, so
            the field is as private as the engine's.
         */
        public static string systemCopyBuffer { get; set; } = string.Empty;
    }

    public static class Application
    {
        public static bool isPlaying => false;

        public static bool isEditor => false;

        public static string dataPath => string.Empty;

        public static string persistentDataPath => string.Empty;

        private static LogCallback _logMessageReceived;

        private static LogCallback _logMessageReceivedThreaded;

        public static event LogCallback logMessageReceived
        {
            add => _logMessageReceived += value;
            remove => _logMessageReceived -= value;
        }

        public static event LogCallback logMessageReceivedThreaded
        {
            add => _logMessageReceivedThreaded += value;
            remove => _logMessageReceivedThreaded -= value;
        }

        /*
            The engine raises these from every Debug record; the stub does
            the same, so a subscriber sees what the console would have.
         */
        internal static void RaiseLog(string condition, string stackTrace, LogType type)
        {
            _logMessageReceived?.Invoke(condition, stackTrace, type);
            _logMessageReceivedThreaded?.Invoke(condition, stackTrace, type);
        }

        public delegate void LogCallback(string condition, string stackTrace, LogType type);

        public static void Quit() { }
    }

    public static class Screen
    {
        public static int width => 0;

        public static int height => 0;
    }

    public static class Time
    {
        public static float time => 0f;

        public static float timeScale { get; set; } = 1f;

        public static float unscaledDeltaTime => 0f;

        public static float deltaTime => 0f;

        public static int frameCount => 0;
    }

    public static class Input
    {
        private static readonly HashSet<KeyCode> Pressed = new HashSet<KeyCode>();

        public static bool GetKey(KeyCode key) => Pressed.Contains(key);

        public static bool GetKeyDown(KeyCode key) => Pressed.Contains(key);

        public static bool GetKeyUp(KeyCode key) => Pressed.Contains(key);
    }

    public struct Vector2 : IEquatable<Vector2>
    {
        public const float kEpsilon = 1e-05f;

        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2 zero => new Vector2(0f, 0f);

        public static Vector2 one => new Vector2(1f, 1f);

        public static Vector2 up => new Vector2(0f, 1f);

        public static Vector2 down => new Vector2(0f, -1f);

        public static Vector2 left => new Vector2(-1f, 0f);

        public static Vector2 right => new Vector2(1f, 0f);

        public float magnitude => MathF.Sqrt((x * x) + (y * y));

        public float sqrMagnitude => (x * x) + (y * y);

        public Vector2 normalized
        {
            get
            {
                float length = magnitude;
                return length > 9.99999974737875163e-06 ? this * (1f / length) : zero;
            }
        }

        public void Normalize()
        {
            float length = magnitude;
            if (length > 9.99999974737875163e-06)
            {
                this = this * (1f / length);
            }
            else
            {
                this = zero;
            }
        }

        public bool Equals(Vector2 other) => x == other.x && y == other.y;

        public override bool Equals(object other) => other is Vector2 v && Equals(v);

        public override int GetHashCode() => (x.GetHashCode() * 397) ^ y.GetHashCode();

        public override string ToString() =>
            $"({x.ToString(CultureInfo.InvariantCulture)}, {y.ToString(CultureInfo.InvariantCulture)})";

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);

        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);

        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);

        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);

        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);

        public static Vector2 operator *(Vector2 a, Vector2 b) => new Vector2(a.x * b.x, a.y * b.y);

        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);

        public static Vector2 operator /(Vector2 a, Vector2 b) => new Vector2(a.x / b.x, a.y / b.y);

        public static bool operator ==(Vector2 a, Vector2 b) => a.Equals(b);

        public static bool operator !=(Vector2 a, Vector2 b) => !a.Equals(b);

        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);

        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);

        public static implicit operator Vector4(Vector2 v) => new Vector4(v.x, v.y, 0f, 0f);
    }

    public struct Vector3 : IEquatable<Vector3>
    {
        public const float kEpsilon = 1e-05f;

        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3(float x, float y)
        {
            this.x = x;
            this.y = y;
            this.z = 0f;
        }

        public static Vector3 zero => new Vector3(0f, 0f, 0f);

        public static Vector3 one => new Vector3(1f, 1f, 1f);

        public static Vector3 up => new Vector3(0f, 1f, 0f);

        public static Vector3 down => new Vector3(0f, -1f, 0f);

        public static Vector3 left => new Vector3(-1f, 0f, 0f);

        public static Vector3 right => new Vector3(1f, 0f, 0f);

        public static Vector3 forward => new Vector3(0f, 0f, 1f);

        public static Vector3 back => new Vector3(0f, 0f, -1f);

        public static Vector3 positiveInfinity =>
            new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

        public static Vector3 negativeInfinity =>
            new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        public float magnitude => MathF.Sqrt((x * x) + (y * y) + (z * z));

        public float sqrMagnitude => (x * x) + (y * y) + (z * z);

        public Vector3 normalized
        {
            get
            {
                float length = magnitude;
                return length > 9.99999974737875163e-06 ? this * (1f / length) : zero;
            }
        }

        public void Normalize()
        {
            float length = magnitude;
            if (length > 9.99999974737875163e-06)
            {
                this = this * (1f / length);
            }
            else
            {
                this = zero;
            }
        }

        public bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;

        public override bool Equals(object other) => other is Vector3 v && Equals(v);

        public override int GetHashCode() =>
            (x.GetHashCode() * 397) ^ (y.GetHashCode() * 31) ^ z.GetHashCode();

        public override string ToString() =>
            $"({x.ToString(CultureInfo.InvariantCulture)}, {y.ToString(CultureInfo.InvariantCulture)}, {z.ToString(CultureInfo.InvariantCulture)})";

        public static Vector3 operator +(Vector3 a, Vector3 b) =>
            new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);

        public static Vector3 operator -(Vector3 a, Vector3 b) =>
            new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);

        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);

        public static Vector3 operator *(Vector3 a, float d) =>
            new Vector3(a.x * d, a.y * d, a.z * d);

        public static Vector3 operator *(float d, Vector3 a) =>
            new Vector3(a.x * d, a.y * d, a.z * d);

        public static Vector3 operator /(Vector3 a, float d) =>
            new Vector3(a.x / d, a.y / d, a.z / d);

        public static bool operator ==(Vector3 a, Vector3 b) => a.Equals(b);

        public static bool operator !=(Vector3 a, Vector3 b) => !a.Equals(b);

        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);
    }

    public struct Vector4 : IEquatable<Vector4>
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Vector4(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public Vector4(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = 0f;
        }

        public Vector4(float x, float y)
        {
            this.x = x;
            this.y = y;
            this.z = 0f;
            this.w = 0f;
        }

        public static Vector4 zero => new Vector4(0f, 0f, 0f, 0f);

        public static Vector4 one => new Vector4(1f, 1f, 1f, 1f);

        public static Vector4 positiveInfinity =>
            new Vector4(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity
            );

        public static Vector4 negativeInfinity =>
            new Vector4(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity
            );

        public float magnitude => MathF.Sqrt((x * x) + (y * y) + (z * z) + (w * w));

        public float sqrMagnitude => (x * x) + (y * y) + (z * z) + (w * w);

        public Vector4 normalized
        {
            get
            {
                float length = magnitude;
                return length > 9.99999974737875163e-06 ? this * (1f / length) : zero;
            }
        }

        public void Normalize()
        {
            float length = magnitude;
            if (length > 9.99999974737875163e-06)
            {
                this = this * (1f / length);
            }
            else
            {
                this = zero;
            }
        }

        public bool Equals(Vector4 other) =>
            x == other.x && y == other.y && z == other.z && w == other.w;

        public override bool Equals(object other) => other is Vector4 v && Equals(v);

        public override int GetHashCode() =>
            (x.GetHashCode() * 397)
            ^ (y.GetHashCode() * 31)
            ^ (z.GetHashCode() * 17)
            ^ w.GetHashCode();

        public override string ToString() =>
            $"({x.ToString(CultureInfo.InvariantCulture)}, {y.ToString(CultureInfo.InvariantCulture)}, {z.ToString(CultureInfo.InvariantCulture)}, {w.ToString(CultureInfo.InvariantCulture)})";

        public static Vector4 operator +(Vector4 a, Vector4 b) =>
            new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);

        public static Vector4 operator -(Vector4 a, Vector4 b) =>
            new Vector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);

        public static Vector4 operator -(Vector4 a) => new Vector4(-a.x, -a.y, -a.z, -a.w);

        public static Vector4 operator *(Vector4 a, float d) =>
            new Vector4(a.x * d, a.y * d, a.z * d, a.w * d);

        public static Vector4 operator *(float d, Vector4 a) =>
            new Vector4(a.x * d, a.y * d, a.z * d, a.w * d);

        public static Vector4 operator /(Vector4 a, float d) =>
            new Vector4(a.x / d, a.y / d, a.z / d, a.w / d);

        public static bool operator ==(Vector4 a, Vector4 b) => a.Equals(b);

        public static bool operator !=(Vector4 a, Vector4 b) => !a.Equals(b);

        public static implicit operator Vector4(Vector2 v) => new Vector4(v.x, v.y, 0f, 0f);

        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);

        public static implicit operator Vector2(Vector4 v) => new Vector2(v.x, v.y);

        public static implicit operator Vector3(Vector4 v) => new Vector3(v.x, v.y, v.z);
    }

    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x;
        public int y;

        public Vector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2Int zero => new Vector2Int(0, 0);

        public static Vector2Int one => new Vector2Int(1, 1);

        public static Vector2Int up => new Vector2Int(0, 1);

        public static Vector2Int down => new Vector2Int(0, -1);

        public static Vector2Int left => new Vector2Int(-1, 0);

        public static Vector2Int right => new Vector2Int(1, 0);

        public float magnitude => MathF.Sqrt((x * x) + (y * y));

        public int sqrMagnitude => (x * x) + (y * y);

        public bool Equals(Vector2Int other) => x == other.x && y == other.y;

        public override bool Equals(object other) => other is Vector2Int v && Equals(v);

        public override int GetHashCode() => (x * 397) ^ y;

        public override string ToString() => $"({x}, {y})";

        public static Vector2Int operator +(Vector2Int a, Vector2Int b) =>
            new Vector2Int(a.x + b.x, a.y + b.y);

        public static Vector2Int operator -(Vector2Int a, Vector2Int b) =>
            new Vector2Int(a.x - b.x, a.y - b.y);

        public static Vector2Int operator -(Vector2Int a) => new Vector2Int(-a.x, -a.y);

        public static Vector2Int operator *(Vector2Int a, int d) =>
            new Vector2Int(a.x * d, a.y * d);

        public static Vector2Int operator *(int d, Vector2Int a) =>
            new Vector2Int(a.x * d, a.y * d);

        public static Vector2Int operator *(Vector2Int a, Vector2Int b) =>
            new Vector2Int(a.x * b.x, a.y * b.y);

        public static Vector2Int operator /(Vector2Int a, int d) =>
            new Vector2Int(a.x / d, a.y / d);

        public static bool operator ==(Vector2Int a, Vector2Int b) => a.Equals(b);

        public static bool operator !=(Vector2Int a, Vector2Int b) => !a.Equals(b);

        public static implicit operator Vector2(Vector2Int v) => new Vector2(v.x, v.y);

        public static explicit operator Vector3Int(Vector2Int v) => new Vector3Int(v.x, v.y, 0);
    }

    public struct Vector3Int : IEquatable<Vector3Int>
    {
        public int x;
        public int y;
        public int z;

        public Vector3Int(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3Int(int x, int y)
        {
            this.x = x;
            this.y = y;
            this.z = 0;
        }

        public static Vector3Int zero => new Vector3Int(0, 0, 0);

        public static Vector3Int one => new Vector3Int(1, 1, 1);

        public static Vector3Int up => new Vector3Int(0, 1, 0);

        public static Vector3Int down => new Vector3Int(0, -1, 0);

        public static Vector3Int left => new Vector3Int(-1, 0, 0);

        public static Vector3Int right => new Vector3Int(1, 0, 0);

        public static Vector3Int forward => new Vector3Int(0, 0, 1);

        public static Vector3Int back => new Vector3Int(0, 0, -1);

        public int magnitude => (int)MathF.Sqrt((float)((x * x) + (y * y) + (z * z)));

        public int sqrMagnitude => (x * x) + (y * y) + (z * z);

        public bool Equals(Vector3Int other) => x == other.x && y == other.y && z == other.z;

        public override bool Equals(object other) => other is Vector3Int v && Equals(v);

        public override int GetHashCode() => (x * 397) ^ (y * 31) ^ z;

        public override string ToString() => $"({x}, {y}, {z})";

        public static Vector3Int operator +(Vector3Int a, Vector3Int b) =>
            new Vector3Int(a.x + b.x, a.y + b.y, a.z + b.z);

        public static Vector3Int operator -(Vector3Int a, Vector3Int b) =>
            new Vector3Int(a.x - b.x, a.y - b.y, a.z - b.z);

        public static Vector3Int operator -(Vector3Int a) => new Vector3Int(-a.x, -a.y, -a.z);

        public static Vector3Int operator *(Vector3Int a, int d) =>
            new Vector3Int(a.x * d, a.y * d, a.z * d);

        public static Vector3Int operator *(int d, Vector3Int a) =>
            new Vector3Int(a.x * d, a.y * d, a.z * d);

        public static Vector3Int operator *(Vector3Int a, Vector3Int b) =>
            new Vector3Int(a.x * b.x, a.y * b.y, a.z * b.z);

        public static Vector3Int operator /(Vector3Int a, int d) =>
            new Vector3Int(a.x / d, a.y / d, a.z / d);

        public static bool operator ==(Vector3Int a, Vector3Int b) => a.Equals(b);

        public static bool operator !=(Vector3Int a, Vector3Int b) => !a.Equals(b);

        public static explicit operator Vector2Int(Vector3Int v) => new Vector2Int(v.x, v.y);
    }

    public struct Color : IEquatable<Color>
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color red => new Color(1f, 0f, 0f, 1f);

        public static Color green => new Color(0f, 1f, 0f, 1f);

        public static Color blue => new Color(0f, 0f, 1f, 1f);

        public static Color white => new Color(1f, 1f, 1f, 1f);

        public static Color black => new Color(0f, 0f, 0f, 1f);

        public static Color yellow => new Color(1f, 0.922f, 0.016f, 1f);

        public static Color cyan => new Color(0f, 1f, 1f, 1f);

        public static Color magenta => new Color(1f, 0f, 1f, 1f);

        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1f);

        public static Color grey => new Color(0.5f, 0.5f, 0.5f, 1f);

        public static Color clear => new Color(0f, 0f, 0f, 0f);

        public bool Equals(Color other) =>
            r == other.r && g == other.g && b == other.b && a == other.a;

        public override bool Equals(object other) => other is Color c && Equals(c);

        public override int GetHashCode() =>
            (r.GetHashCode() * 397)
            ^ (g.GetHashCode() * 31)
            ^ (b.GetHashCode() * 17)
            ^ a.GetHashCode();

        public override string ToString() =>
            $"RGBA({r.ToString(CultureInfo.InvariantCulture)}, {g.ToString(CultureInfo.InvariantCulture)}, {b.ToString(CultureInfo.InvariantCulture)}, {a.ToString(CultureInfo.InvariantCulture)})";

        public static bool operator ==(Color a, Color b) => a.Equals(b);

        public static bool operator !=(Color a, Color b) => !a.Equals(b);
    }

    public struct Rect : IEquatable<Rect>
    {
        public float x;
        public float y;
        public float width;
        public float height;

        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public static Rect zero => new Rect(0f, 0f, 0f, 0f);

        public bool Equals(Rect other) =>
            x == other.x && y == other.y && width == other.width && height == other.height;

        public override bool Equals(object other) => other is Rect r && Equals(r);

        public override int GetHashCode() =>
            (x.GetHashCode() * 397)
            ^ (y.GetHashCode() * 31)
            ^ (width.GetHashCode() * 17)
            ^ height.GetHashCode();

        public override string ToString() =>
            $"(x:{x.ToString(CultureInfo.InvariantCulture)}, y:{y.ToString(CultureInfo.InvariantCulture)}, width:{width.ToString(CultureInfo.InvariantCulture)}, height:{height.ToString(CultureInfo.InvariantCulture)})";

        public static bool operator ==(Rect a, Rect b) => a.Equals(b);

        public static bool operator !=(Rect a, Rect b) => !a.Equals(b);
    }

    public struct RectInt : IEquatable<RectInt>
    {
        public int x { get; }

        public int y { get; }

        public int width { get; }

        public int height { get; }

        public Vector2Int position => new Vector2Int(x, y);

        public Vector2Int size => new Vector2Int(width, height);

        public Vector2Int min => new Vector2Int(x, y);

        public Vector2Int max => new Vector2Int(x + width, y + height);

        public RectInt(int x, int y, int width, int height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public bool Contains(Vector2Int point) =>
            point.x >= x && point.y >= y && point.x < (x + width) && point.y < (y + height);

        public bool Equals(RectInt other) =>
            x == other.x && y == other.y && width == other.width && height == other.height;

        public override bool Equals(object other) => other is RectInt r && Equals(r);

        public override int GetHashCode() => (x * 397) ^ (y * 31) ^ (width * 17) ^ height;

        /*
            Unity's labeled form; the parser strips these very labels, so
            the round-trip the fixtures run holds.
         */
        public override string ToString() => $"(x:{x}, y:{y}, width:{width}, height:{height})";

        public static bool operator ==(RectInt a, RectInt b) => a.Equals(b);

        public static bool operator !=(RectInt a, RectInt b) => !a.Equals(b);
    }

    public struct Bounds : IEquatable<Bounds>
    {
        public Vector3 center { get; }

        public Vector3 size { get; }

        public Vector3 extents => size * 0.5f;

        public Vector3 min => center - extents;

        public Vector3 max => center + extents;

        public Bounds(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
        }

        public bool Contains(Vector3 point) =>
            point.x >= min.x
            && point.x <= max.x
            && point.y >= min.y
            && point.y <= max.y
            && point.z >= min.z
            && point.z <= max.z;

        public bool Equals(Bounds other) => center == other.center && size == other.size;

        public override bool Equals(object other) => other is Bounds b && Equals(b);

        public override int GetHashCode() => (center.GetHashCode() * 397) ^ size.GetHashCode();

        /* Unity's form: center plus half-sizes, exactly what the parser doubles. */
        public override string ToString() => $"Center: {center}, Extents: {extents}";

        public static bool operator ==(Bounds a, Bounds b) => a.Equals(b);

        public static bool operator !=(Bounds a, Bounds b) => !a.Equals(b);
    }

    public struct BoundsInt : IEquatable<BoundsInt>
    {
        public Vector3Int position { get; }

        public Vector3Int size { get; }

        public int x => position.x;

        public int y => position.y;

        public int z => position.z;

        public int width => size.x;

        public int height => size.y;

        public int depth => size.z;

        public Vector3Int min => position;

        public Vector3Int max => position + size;

        public BoundsInt(Vector3Int position, Vector3Int size)
        {
            this.position = position;
            this.size = size;
        }

        public bool Contains(Vector3Int point) =>
            point.x >= min.x
            && point.y >= min.y
            && point.z >= min.z
            && point.x < max.x
            && point.y < max.y
            && point.z < max.z;

        public bool Equals(BoundsInt other) => position == other.position && size == other.size;

        public override bool Equals(object other) => other is BoundsInt b && Equals(b);

        public override int GetHashCode() => (position.GetHashCode() * 397) ^ size.GetHashCode();

        /* Unity's labeled form; the parser strips these very labels. */
        public override string ToString() =>
            $"Position: {position.ToString()}, Size: {size.ToString()}";

        public static bool operator ==(BoundsInt a, BoundsInt b) => a.Equals(b);

        public static bool operator !=(BoundsInt a, BoundsInt b) => !a.Equals(b);
    }

    public struct Plane
    {
        public Vector3 normal { get; }

        public float distance { get; }

        public Plane(Vector3 normal, float distance)
        {
            this.normal = normal;
            this.distance = distance;
        }

        /* Unity's labeled form; the parser strips these very labels. */
        public override string ToString() => $"(normal:{normal}, distance: {distance})";
    }

    public struct Ray
    {
        public Vector3 origin { get; }

        public Vector3 direction { get; }

        public Ray(Vector3 origin, Vector3 direction)
        {
            this.origin = origin;
            this.direction = direction;
        }

        /* Unity's labeled form; the parser strips these very labels. */
        public override string ToString() => $"Origin: {origin}, Dir: {direction}";
    }

    public class RectOffset
    {
        public int left { get; set; }

        public int right { get; set; }

        public int top { get; set; }

        public int bottom { get; set; }

        public RectOffset(int left, int right, int top, int bottom)
        {
            this.left = left;
            this.right = right;
            this.top = top;
            this.bottom = bottom;
        }
    }

    public struct Quaternion : IEquatable<Quaternion>
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static Quaternion identity => new Quaternion(0f, 0f, 0f, 1f);

        public bool Equals(Quaternion other) =>
            x == other.x && y == other.y && z == other.z && w == other.w;

        public override bool Equals(object other) => other is Quaternion q && Equals(q);

        public override int GetHashCode() =>
            (x.GetHashCode() * 397)
            ^ (y.GetHashCode() * 31)
            ^ (z.GetHashCode() * 17)
            ^ w.GetHashCode();

        public override string ToString() =>
            $"({x.ToString(CultureInfo.InvariantCulture)}, {y.ToString(CultureInfo.InvariantCulture)}, {z.ToString(CultureInfo.InvariantCulture)}, {w.ToString(CultureInfo.InvariantCulture)})";

        public static bool operator ==(Quaternion a, Quaternion b) => a.Equals(b);

        public static bool operator !=(Quaternion a, Quaternion b) => !a.Equals(b);
    }

    public static class JsonUtility
    {
        /*
            The engine serializes [SerializeField] fields of primitives,
            strings, and containers of them, and so does this stand-in,
            over System.Text.Json with field reflection. Round-trip
            identity for the shapes the fixtures use is the contract; a
            field the engine would not serialize is out of scope here.
         */
        public static string ToJson(object obj) =>
            System.Text.Json.JsonSerializer.Serialize(
                obj,
                new System.Text.Json.JsonSerializerOptions
                {
                    IncludeFields = true,
                    WriteIndented = false,
                }
            );

        public static T FromJson<T>(string json) =>
            System.Text.Json.JsonSerializer.Deserialize<T>(
                json,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true }
            );
    }

    public static class PlayerPrefs
    {
        private static readonly Dictionary<string, string> Values =
            new Dictionary<string, string>();

        public static void SetString(string key, string value) => Values[key] = value;

        public static string GetString(string key) =>
            Values.TryGetValue(key, out string value) ? value : "";

        public static string GetString(string key, string defaultValue) =>
            Values.TryGetValue(key, out string value) ? value : defaultValue;

        public static void SetInt(string key, int value) =>
            Values[key] = value.ToString(CultureInfo.InvariantCulture);

        public static int GetInt(string key) => GetInt(key, 0);

        public static int GetInt(string key, int defaultValue) =>
            Values.TryGetValue(key, out string value) && int.TryParse(value, out int parsed)
                ? parsed
                : defaultValue;

        public static void SetFloat(string key, float value) =>
            Values[key] = value.ToString(CultureInfo.InvariantCulture);

        public static float GetFloat(string key) => GetFloat(key, 0f);

        public static float GetFloat(string key, float defaultValue) =>
            Values.TryGetValue(key, out string value) && float.TryParse(value, out float parsed)
                ? parsed
                : defaultValue;

        public static bool HasKey(string key) => Values.ContainsKey(key);

        public static void DeleteKey(string key) => Values.Remove(key);

        public static void DeleteAll() => Values.Clear();

        public static void Save() { }
    }

    public static class Random
    {
        private static readonly System.Random Rng = new System.Random(12345);

        public static int Range(int minInclusive, int maxExclusive) =>
            Rng.Next(minInclusive, maxExclusive);

        public static float Range(float minInclusive, float maxInclusive) =>
            (float)((Rng.NextDouble() * (maxInclusive - (double)minInclusive)) + minInclusive);
    }

    /*
        CoreModule carries a StackTraceUtility, which is how CommandLog
        compiles in the gate. The engine's extractor walks native frames;
        the host's answer is the managed call stack at the extract - real
        frames, same consumers - so the trace-capture contracts are
        exercised against a trace a caller actually produced.
     */
    public static class StackTraceUtility
    {
        public static string ExtractStackTrace() => Environment.StackTrace;

        public static string ExtractStringStackTrace() => Environment.StackTrace;
    }
}

namespace UnityEngine.UIElements
{
    using System;

    /*
        The smallest UI Toolkit surface the included sources name: the
        paste fixtures drive a TextField's value and caret, and hand a
        KeyDownEvent to TextFieldPaste.TryApply. Pooling, the panel, and
        rendering do not exist here; the field is its value and its caret,
        which is exactly what those fixtures pin.
     */
    public class TextField
    {
        public string value { get; set; } = string.Empty;

        public int cursorIndex { get; set; }

        public int selectIndex { get; set; }

        public bool isReadOnly { get; set; }

        public int maxLength { get; set; }
    }

    public class KeyDownEvent : IDisposable
    {
        public char character { get; set; }

        public KeyCode keyCode { get; set; }

        public EventModifiers modifiers { get; set; }

        public bool ctrlKey => (modifiers & EventModifiers.Control) != 0;

        public bool commandKey => (modifiers & EventModifiers.Command) != 0;

        public bool shiftKey => (modifiers & EventModifiers.Shift) != 0;

        public bool altKey => (modifiers & EventModifiers.Alt) != 0;

        public static KeyDownEvent GetPooled(
            char character,
            KeyCode keyCode,
            EventModifiers modifiers
        )
        {
            return new KeyDownEvent
            {
                character = character,
                keyCode = keyCode,
                modifiers = modifiers,
            };
        }

        public void Dispose() { }
    }
}

/*
    Unity's 2021.3.33 CoreModule embeds the JetBrains annotations, so
    sources using [UsedImplicitly] compile against it in the gate; the
    host needs the same names. Only the attributes included sources name
    live here.
 */
namespace JetBrains.Annotations
{
    using System;

    [AttributeUsage(
        AttributeTargets.Method
            | AttributeTargets.Class
            | AttributeTargets.Field
            | AttributeTargets.Property
            | AttributeTargets.Parameter
            | AttributeTargets.Delegate
            | AttributeTargets.Event
    )]
    public sealed class UsedImplicitlyAttribute : Attribute
    {
        public UsedImplicitlyAttribute() { }

        public UsedImplicitlyAttribute(UseSpaceFlags useSpaceFlags) { }
    }

    [Flags]
    public enum UseSpaceFlags
    {
        None = 0,
    }

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method)]
    public sealed class StringFormatMethodAttribute : Attribute
    {
        public string formatParameterName { get; }

        public StringFormatMethodAttribute(string formatParameterName)
        {
            this.formatParameterName = formatParameterName;
        }
    }
}

namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;

    /*
        Compile shells for the product types the backend's built-in
        commands name, whose real sources are excluded from this host
        because they are the UI Toolkit surfaces (see testhost.csproj).
        They are not the product: Instance is genuinely null - the same
        no-UI state an EditMode session has - so BuiltinCommands' own
        "No Terminal UI found" branches are the code that runs, and every
        instance member throws rather than pretending a surface answered.
        A built-in command that starts reading a new TerminalUI member
        breaks this host's compile, which is the loud direction.
     */
    public sealed class Font
    {
        public string name { get; set; } = string.Empty;
    }

    public sealed class TerminalThemePack
    {
        public List<string> _themeNames;
    }

    public sealed class TerminalFontPack
    {
        public List<Font> _fonts;
    }

    public sealed class TerminalUI
    {
        public static TerminalUI Instance => null;

        public TerminalThemePack _themePack;

        public TerminalFontPack _fontPack;

        private static InvalidOperationException NoSurface() =>
            new InvalidOperationException(
                "The Unity-free test host has no Terminal UI surface; "
                    + "run this fixture in a live editor."
            );

        public Font CurrentFont => throw NoSurface();

        public string CurrentFriendlyTheme => throw NoSurface();

        public void SetFont(Font font) => throw NoSurface();

        public Font SetRandomFont() => throw NoSurface();

        public bool SetTheme(string themeName) => throw NoSurface();

        public string SetRandomTheme() => throw NoSurface();

        public void StepLogFilter(bool forward) => throw NoSurface();

        public void SetLogFilter(string query) => throw NoSurface();

        public void ClearLogFilter() => throw NoSurface();
    }
}
