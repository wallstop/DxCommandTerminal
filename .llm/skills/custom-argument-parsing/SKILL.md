---
name: custom-argument-parsing
description: Parse CommandArg values with TryGet<T>, register custom CommandArgParser functions, and control input cleaning, delimiters, and quote handling sets. Use when writing command handlers that read arguments, adding parsers for new types (e.g. JSON), or fixing "failed to parse" behavior.
metadata:
  category: Feature
---

# Custom Argument Parsing

## The TryGet contract

`CommandArg.TryGet<T>(out T parsed)` returns `false` on failure - it never silently defaults.
`arg.contents` exposes the raw input string.

```csharp
CommandArg arg = new CommandArg("1");
Assert.IsTrue(arg.TryGet(out int parsedInt));        // 1
Assert.IsTrue(arg.TryGet(out Color color));          // via name or RGBA text
Assert.IsFalse(arg.TryGet(out Vector2 invalid));     // failed parse
```

Built-in support: `string`, `char`, `bool`, all numeric primitives, `decimal`, `Guid`,
`DateTime`, `DateTimeOffset`, enums; Unity types `Vector2/3/4`, `Vector2Int/3Int`, `Quaternion`,
`Rect`, `RectInt`, `Color`.

Named-constant matching: `TryGet` also resolves `public static`/`public const` fields -
`"MaxValue"` parses as `double.MaxValue`, `"red"` as `Color.red`. Multi-value types accept
logical formats: `"RGBA(0.7, 0.5, 0.1, 1.0)"`, `"(0.7, 0.5, 0.1)"`, `"red"`.

## Custom parsers

Per-call parser (highest precedence for that call):

```csharp
bool MyParser(string input, out int value) { value = 32; return true; }
arg.TryGet(out int value, MyParser);
```

Global registration (overrides built-ins for that type):

```csharp
bool ok = CommandArgParser.RegisterParser<T>(parser, force: false);  // false if already registered
CommandArgParser.UnregisterParser<T>();
CommandArgParser.UnregisterParser(typeof(T));
CommandArgParser.UnregisterAllParsers();
```

Built-in parser functions cannot be unregistered; user registrations can. Custom parsers receive a
*cleaned* input string unless `T` is in `CommandArg.DoNotCleanTypes`.

## Input control sets (static on `CommandArg`)

| Set | Controls |
| --- | --- |
| `Delimiters` | Splits multi-value types; first matching delimiter wins (`"1,2,3"` -> 3 parts) |
| `Quotes` | Characters that capture spaces/ignored chars as one argument; unmatched quote consumes to end of line (`say "hello world"`) |
| `IgnoredValuesForCleanedTypes` | Substrings replaced with empty string during cleaning (`"\r"`, `"\n"`) |
| `DoNotCleanTypes` | Types exempt from cleaning (raw `contents` passed to parser) |
| `IgnoredValuesForComplexTypes` | Substrings stripped from complex-type input before splitting |

Mutate these sets only during initialization, not per-frame or per-command, and document any
non-default configuration - it changes parsing for every terminal in the process.

## House rules for parsers

- Parser signature is `bool X(string input, out T value)`; return `false` on any invalid input,
  never throw.
- Parsers must be deterministic and allocation-light (they run on every command invocation).
- When adding built-in parser support for a new type, extend the README's supported-types list in
  the same change (the README is the public contract).
