---
name: text-io-boundaries
description: Encode and escape text that crosses a process or file boundary - a claim or report protocol, a percent-encoded field, a decoded value printed into line-oriented output - and keep index arithmetic that counts into text honest. Use when writing a line- or whitespace-delimited format, adding or decoding a protocol field, hand-writing a character range like \u0000-\u001f, or moving an offset that counts characters: a caret, a selection, a substring bound, a surrogate pair. Covers allowlist encoders that fail closed, Unicode property escapes over enumerated ranges, escaping over UTF-8 bytes not code units, a caret snapped against the wrong string, and a cross-language contract no CI lane compiles.
metadata:
  category: Core
---

# Text I/O Boundaries

## The one rule

Text that arrives from outside the process is data, not text. Two boundaries care
about it, and each needs a different defense:

- **Writing** into a line-based or whitespace-delimited grammar. Encode every
  character the grammar reserves, before the value crosses the boundary.
- **Printing** a value that was decoded from somewhere. Escape every character
  the display sink reserves, at the point of printing.

The two are not the same set, and encoding is not escaping. Percent-encoding
protects the wire; escaping protects the reader's terminal.

## Allowlists fail closed, denylists fail open

A writer with an allowlist cannot leak: anything it does not name is encoded.
`DxTerminalTestRunReporter.AllowedNameCharacter` allows only
`A-Z a-z 0-9 . ( ) [ ] _`, so every other byte of every name is escaped and a
future character cannot slip through.

A denylist is only as good as the author's memory. The first `printableName` in
`unity-mcp.mjs` enumerated its range and was **measured to leak 151
characters**: U+2028 and U+2029, which are line separators that split a printed
result line and let a name forge the line after it, plus 149 format characters,
127 of them above the BMP. A reviewer independently proposed a range that missed
17 of the same characters.

## Never enumerate a character set. Derive it.

Ask the Unicode category, not the author's memory. In JavaScript:

```js
// Cc is C0 + DEL + C1, Cf is zero-width/bidi/format, Zl and Zp are the line
// and paragraph separators. Ordinary spaces are Zs and stay, so a real name is
// still readable. Measured: no gaps across the BMP.
const UNPRINTABLE = /[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]/gu;
```

In C# the same reasoning already applies in this repo, and is used in
`Runtime/CommandTerminal/Backend/CommandTokenizer.cs:27,287,292`, which asks
`char.IsWhiteSpace` rather than listing the separators. Prefer the API that
answers the question over a range written by hand - `char.IsControl`,
`char.IsSeparator`, or a Unicode category - and where none fits, name the
category in a helper so the question is asked once.

**Deriving an encoding is the same trap, and it is arithmetic rather than
memory.** The surrogate pair for a code point is `(cp - 0x10000) >> 10` from
`0xD800`; forget the subtraction and every astral constant is off by a plane.
That is not hypothetical: the flag letters, the skin tones, and the tag
characters of `StringExtensions.SnapToTextBoundary` were all wrong that way
at first, and a 3.3M-position differential oracle agreed with the broken
code the whole time, because the fuzz corpus had been written from the same
arithmetic. **Take the constants from a table and the corpus by a different
route** - `chr(cp).encode("utf-16-le")` beside a hand-written `\uXXXX` - so a
wrong derivation has to be wrong twice to get through.

Replace with a **visible** escape, not a placeholder. `?` is indistinguishable
from a question mark in the name; `\u202E` is not. Two rules make the escape
trustworthy:

- **Match the width to the magnitude.** `\u` takes exactly four hex digits, so a
  supplementary-plane character needs `\U` and eight. Padding to four without
  capping writes `\uE0001`, which reads as a different character than it is.
- **Escape the escape.** A backslash must be escaped too, or a name holding the
  literal text `\u000A` prints exactly like a name holding a real newline.

## Encode bytes, not code units

A percent escape is two hex digits. Writing one per UTF-16 code unit breaks on
anything outside Latin-1: `U+2003` became `%2003`, which decodes as a space and
`03`. Encode the UTF-8 bytes of the string:

```csharp
foreach (byte value in Encoding.UTF8.GetBytes(name)) { /* %XX per byte */ }
```

The one lossy case is a lone surrogate, which UTF-8 replaces with `U+FFFD`. No
test name holds one; say so in a comment rather than pretending it is byte-exact.

## An index is an offset into one specific string

A caret, a selection end, a substring bound, a `Remove` offset: each is a
**character** position, not a code-unit count. An emoji is one character and
two units, so an index between the units points inside it. This repo has one
answer - `StringExtensions.SnapToTextBoundary` - and one rule for using it:
snap where the offset is **computed**, against the text that offset indexes.

Both instances of this class were the same mistake, the string:

- `TerminalUI` queued a completion caret that indexes the input the write
  *produces*, then snapped it where the field applies that value on its own
  schedule - against the text it is replacing. Corrected by snapping where
  the position is computed.
- `TextFieldPaste` computes a caret that indexes the value the paste
  produces, and the value it held was the one before the paste. Measured,
  snapping it there makes the case worse, so it is a stated limit instead.

So the question is never only "is this a character boundary" but **which
string is this an offset into?** Sweep the writers and the readers, not
`Substring`: `cursorIndex`, `selectIndex`, every `Remove`/`Insert` pair, and
every `Length` in an arithmetic expression. A caret **read** from a field is
engine data, so it is snapped at each read; a caret the package **computes**
is snapped where it is computed. One grep settles the enumeration: the caret
writes in `Runtime/` are four sites, and each one is accounted for.

Two limits to state rather than fix:

- **A caret after inserted text is left where the paste ended.** The one way
  it can be inside a character is a clipboard that ends mid-sequence. The
  snap floors it past the joiner and past the character in front of it -
  measured, a caret of 10 lands at 7 - which is *before the text just
  pasted*. Traded on purpose, and pinned by a row so it is not reversed by
  accident.
- **An index against an ASCII delimiter is safe**; one against a character
  position is not. `parseRunClaim`'s `indexOf("=")` cannot fall between two
  units, because `=` is a unit of its own. Do not "fix" that one, and do not
  copy it as a pattern for a caret.

## A queued position has one owner

A caret the console writes later is *queued*, and a queued field is volatile:
any reset helper called after the queue takes it away.
`TerminalUI.RecallHistoryLine` assigned `_pendingCaretIndex` and then called
`ResetAutoComplete`, which runs `ResetTokenCompletion` and nulls the marker.
The queue never reached `ApplyPendingCaret`, so history recall left the caret
exactly where it was and the next character landed mid-line - a fix that
compiled, passed every Unity-free gate, and did nothing. Two reviews and
Cursor Bugbot found it; nothing runnable here could, because only a live panel
moves a caret.

The rule is mechanical: **queue last**, and for every write to a `_pending*`
field list what the rest of that method calls. One grep settles the
enumeration - two queued fields, `_pendingCaretIndex` on each surface, and
five writers between them.

The mirror question is the same one asked of derived state. A field that
records what was *rendered* is written where the render happens, never in a
`finally` that runs whatever happened: a `finally` records intent, so a row
loop that stopped early would leave the mirror claiming rows that were never
written, the next pass would compare equal, and the stale rows would never be
corrected. `TerminalUI.RefreshAutoCompleteHints` writes its candidate mirror
with the rows it describes, and only when every row was written; the log-list
version stamp next to it has been written that way all along.

## Parse the encoded text, print the escaped text

The format is only safe because of where the boundary sits. `parseRunClaim`
splits the **encoded** line on `/\s+/u`, so a name holding a space, a comma, or
an equals cannot move a field boundary. `printableName` escapes the **decoded**
name at the point of printing. Reversing either half reintroduces the bug.

## Pin the property, not a list

A test that enumerates the expected characters drifts exactly like the code it
guards. Derive the hostile set from the Unicode categories, then enumerate the
scalars and assert the exact escape - a test that compares the two regex
literals can only catch a narrowing, never a widening:

```js
for (let code = 0; code <= 0x10ffff; ++code) {
  if (code >= 0xd800 && code <= 0xdfff) continue; // a lone surrogate, not a scalar
  const character = String.fromCodePoint(code);
  if (!/[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]/u.test(character)) continue;
  // assert the printed block holds this character's exact escape
}
```

Walk the whole scalar range, not the BMP: a tag character like U+E0001 arrives
intact from a UTF-8 encoder and needs the eight-digit escape form.

**An expectation the subject already satisfies is not a pin.** A test that
clicked a suggestion and then asserted the input holds that candidate passed
with the fix removed, because the field had been left holding it. Put the
subject in a state only the fix can produce - the caret at the head of a
longer line, the input sitting on a different candidate - and assert the
transition. The same rig rule worth stating out loud: a poll that accepts the
queued position pins that the surface *queues* the right end, not that a frame
lands it, so the comment beside the helper should say which one it claims.

**Re-derive the whole table, and run the derivation.** An expected value
counted by hand out of an implementation is a second implementation of it,
and it is wrong often enough to matter. Three of five rows in one paste
table were wrong: a reported caret that held the insertion point instead of
the position after the pasted text, and an expected value that kept the
letter the caret had just moved in front of. Both were found in seconds by a
throwaway program that models the arithmetic over the same inputs - and that
is the mode to be in when the editor cannot run the suite at all, because the
model is the one place the numbers live, so a corrected row never has to be
derived by hand again.

Assert the other direction too: a name a real suite produces (a quoted argument
with a space, a quote, a comma, an equals, non-ASCII letters) must survive
untouched, or the filter is only deleting information. A filter that mangles
readable text is its own defect.

State the one limit rather than implying full coverage: widening the production
class to a category the oracle does not name is a semantic decision no test
will second-guess.

## Pins are the fallback, not the goal

When a writer only compiles in a place CI cannot reach, pinning its source text
holds the line until a lane can run it. `DxTerminalTestRunReporter` was pinned
that way: each contract constant at its declaration and its use, the byte-wise
cast, the escape sites, the allowlist's exact literals. A mutation probe
measured those pins at 11 of 13 tried mutations, and the two survivors were C#
behavior no text pin can see.

The fix is to give the contract a lane, not more pins. `tooling~/scripts/mcp/grammar`
links that file into a Unity-free project, drives it through the callbacks a real
run uses, and decodes the claims it wrote with the real reader (`npm run mcp:grammar`).
The mutation probe then catches the behavior mutations too, and a
behavior-preserving refactor no longer turns the suite red.

A hand-written stand-in for the foreign types is what makes that cheap; state its
limit next to it, because it declares a surface rather than the real one. The
generator, not the pin, is what covers a character the fixture did not think of -
a generated corpus that walks the range fails closed where a listed set fails
open.

## Sweep both directions, and both tools

Ask the class, not the pattern. A grep for `\uXXXX` literals finds an
enumerated range and misses the same defect written as a comparison - which is
how `DxTerminalStateCapture` had `character < ' '` in its JSON writer, covering
the C0 controls only while a DEL, a C1 character, or a line separator wrote raw
into every manifest a capture produces. The same file also appended raw console
messages to a one-entry-per-line file, so a message carrying a newline (a stack
trace, or a test name holding one) added lines that read as further entries.

For every writer in the family, ask: does this value carry a character the format
reserves, and does anything print it back without asking again?

## Checklist for a new field or format

1. Which characters does the grammar reserve? Encode them, with an allowlist.
2. Is the escape two hex digits? Then it is over bytes, not code units.
3. Who prints a decoded value? Escape at that point, by Unicode category.
4. Is the separator a character a name can hold? A comma-joined list needs a
   separator the encoder also escapes.
5. What test walks the property rather than a list, in both directions?
6. If a second language reads this, which CI lane compiles it? If none, the
   source-text pin is a stopgap, and it needs a follow-up issue.
7. Does an index cross into the text? A caret, a selection, or a substring
   bound counts characters: is it snapped, and is it snapped against the
   string it is an offset into rather than the one that was there before?

## References

- Claim file contract: `tooling~/scripts/mcp/README.md`, section "Test run claim
  file"; the maintained reporter source next to it.
- A worked example of a hand-written range and its replacement, plus the
  mutation probe results, in `progress/session-080-failed-test-names.md`.
