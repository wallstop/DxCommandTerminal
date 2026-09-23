# Typed arguments

Arguments parse through `CommandArg.TryGet<T>`: it returns `false` on
failure instead of silently defaulting, so bad input is rejected, not
misread.

## Built-in kinds

Primitives, `DateTime`, `Guid`, enums, Unity math types (`Vector2`,
`Color`, `Quaternion`, `Bounds`, `Rect`, `Plane`, `Ray`, ...), plus
named constants: `red` parses as `Color.red`, `MaxValue` as
`int.MaxValue`.

```csharp
if (args[0].TryGet<int>(out int count))
{
    Terminal.Log("count = {0}", count);
}
```

Inside a builder handler, parsed values come typed and validated:

[!code-csharp[ArgumentKinds](samples/ArgumentKinds.cs)]

`god` uses bool choices, `weather` completes and validates enum names,
`timescale` clamps rejection to a 0-10 range. Out-of-range or
unparsable input stops the command with a descriptive shell error.

## Custom parsers

Register a parser once for your own types; builder arguments and
`[RegisterCommand]` methods use it through the same path:

```csharp
CommandArgParser.RegisterParser<AccountId>(
    (string input, out AccountId value) =>
    {
        value = default;
        return long.TryParse(input, out long raw)
            && AccountId.TryCreate(raw, out value);
    });
```

`force: true` replaces an existing parser. For one-off parsing,
`TryGet<T>(out T value, parser)` takes a parser per call. The
culture-invariant built-in parsers are also public
(`CommandArgParsers.Float`, `.Int`, `.DateTime`, ...).

## Where next

- [Registering commands](commands.md) - the three registration forms.
- [Completion](completion.md) - Tab completion for argument values.
- [API Reference](xref:WallstopStudios.DxCommandTerminal.Backend.CommandArg) -
  `CommandArg`, `CommandArgParsers`, `CommandArgParser`.
