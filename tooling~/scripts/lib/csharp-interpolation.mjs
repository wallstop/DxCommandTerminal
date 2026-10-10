/*
    A C# source scanner shared by the gates that read call shapes out of
    shipped code (the display-text rule, the exception-logging lint).

    One left-to-right pass classifies the file into string literals and
    comments. Interpolated strings are scanned brace-depth aware: inside a
    {...} hole a quote starts a nested string, which may itself be
    interpolated, and doubled braces are literal text. Before that, a hole
    like {string.Join(", ", names)} split the literal at the nested quote
    and every rule reading holes went blind to the whole call (issue #213).

    Consumers walk the spans, not a regex over raw source: comment text is
    not code, literal text is data, and a mention inside either is not a
    call.
 */

/**
 * One left-to-right pass that classifies the file into the spans a
 * consumer has to step over: string literals (whose text may hold
 * brackets, braces, and a searched token) and comments (whose text is not
 * code at all). Interpolated literals record their holes as absolute
 * [start, end) spans.
 */
export function classify(text) {
  const literals = [];
  const comments = [];
  let index = 0;
  while (index < text.length) {
    const character = text[index];
    const next = text[index + 1];

    if (character === "/" && next === "/") {
      const start = index;
      index = text.indexOf("\n", index);
      if (index < 0) {
        index = text.length;
      }

      comments.push({ start, end: index });
      continue;
    }

    if (character === "/" && next === "*") {
      const start = index;
      index = text.indexOf("*/", index + 2);
      index = index < 0 ? text.length : index + 2;
      comments.push({ start, end: index });
      continue;
    }

    if (character === "'") {
      index += 1;
      while (index < text.length) {
        if (text[index] === "\\") {
          index += 2;
          continue;
        }

        if (text[index] === "'") {
          index += 1;
          break;
        }

        ++index;
      }

      continue;
    }

    if (
      character === '"' ||
      (character === "@" && (next === '"' || (next === "$" && text[index + 2] === '"')))
    ) {
      const start = index;
      // Enter the scan at the literal's first character: the `$` of an
      // interpolated string (`$"..."`, `$@"..."`, and `@$"..."` all), so the
      // flags read forward (stringStartAt's contract), while the recorded
      // span still starts at the first prefix character. For `@$"..."` the
      // first character is the `@`, which stringStartAt reads as
      // verbatim-interpolated itself.
      const scanFrom = text[start - 1] === "$" ? start - 1 : start;
      const parsed = stringStartAt(text, scanFrom);
      const literal = { start, end: 0, interpolated: parsed.interpolated, holes: [] };
      index = skipStringFrom(text, scanFrom, literal.holes);
      literal.end = index;
      literals.push(literal);
      continue;
    }

    ++index;
  }

  return { literals, comments };
}

/*
    What begins at index: a plain or verbatim string, an interpolated or
    verbatim-interpolated string, or a char literal. `index` names the first
    character of the literal - the `$` of `$"..."`, the `@` of `@"..."` - so
    the interpolated flag reads forward, never back. Null when none does: a
    lone `$` or `@` in a hole is code text, not a literal start.
 */
export function stringStartAt(text, index) {
  const character = text[index];
  if (character === "'") return { quote: index, verbatim: false, interpolated: false, charLiteral: true };
  if (character === '"') return { quote: index, verbatim: false, interpolated: false };
  if (character === "$" && text[index + 1] === '"') {
    return { quote: index + 1, verbatim: false, interpolated: true };
  }

  if (character === "$" && text[index + 1] === "@" && text[index + 2] === '"') {
    return { quote: index + 2, verbatim: true, interpolated: true };
  }

  if (character === "@" && text[index + 1] === "$" && text[index + 2] === '"') {
    return { quote: index + 2, verbatim: true, interpolated: true };
  }

  if (character === "@" && text[index + 1] === '"') {
    return { quote: index + 1, verbatim: true, interpolated: false };
  }

  return null;
}

/** The index just past a char literal that opens at index. */
export function skipCharLiteral(text, index) {
  index += 1;
  while (index < text.length) {
    if (text[index] === "\\") {
      index += 2;
      continue;
    }

    if (text[index] === "'") {
      return index + 1;
    }

    ++index;
  }

  return index;
}

/*
    Consume the literal that starts at index (one of stringStartAt's shapes)
    and return the index just past its close. Interpolation holes are
    recorded as absolute [start, end) spans into `holes`: the spans see the
    code inside the hole, quotes and nested strings included, while doubled
    braces - literal text in both verbatim and plain interpolated strings -
    never open one.
 */
export function skipStringFrom(text, index, holes) {
  const start = stringStartAt(text, index);
  if (start === null) {
    return index + 1;
  }

  if (start.charLiteral) {
    return skipCharLiteral(text, index);
  }

  let holeDepth = 0;
  let holeStart = 0;
  index = start.quote + 1;
  while (index < text.length) {
    const character = text[index];

    if (holeDepth > 0) {
      if (character === "{") {
        holeDepth += 1;
        index += 1;
        continue;
      }

      if (character === "}") {
        holeDepth -= 1;
        if (holeDepth === 0) {
          holes.push({ start: holeStart, end: index });
        }

        index += 1;
        continue;
      }

      if (stringStartAt(text, index) !== null) {
        index = skipStringFrom(text, index, []);
        continue;
      }

      index += 1;
      continue;
    }

    if (character === "\\" && !start.verbatim) {
      index += 2;
      continue;
    }

    if (character === "{") {
      if (!start.interpolated) {
        index += 1;
        continue;
      }

      if (text[index + 1] === "{") {
        // A doubled brace is literal text in an interpolated string too.
        index += 2;
        continue;
      }

      holeDepth = 1;
      holeStart = index + 1;
      index += 1;
      continue;
    }

    if (character === "}" && start.interpolated && text[index + 1] === "}") {
      index += 2;
      continue;
    }

    if (character === '"') {
      if (start.verbatim && text[index + 1] === '"') {
        index += 2;
        continue;
      }

      return index + 1;
    }

    index += 1;
  }

  return index;
}

export function startsInside(spans, index) {
  for (const span of spans) {
    if (span.start > index) {
      return false;
    }

    if (span.start <= index && index < span.end) {
      return true;
    }
  }

  return false;
}

/** The index of the paren that closes the call opened at openParen. */
export function callCloseParen(text, openParen, literals) {
  let depth = 0;
  let index = openParen;
  let literal = 0;
  while (index < text.length) {
    while (literal < literals.length && literals[literal].start < index) {
      ++literal;
    }

    if (literal < literals.length && literals[literal].start === index) {
      index = literals[literal].end;
      continue;
    }

    if (text[index] === "(") {
      ++depth;
    } else if (text[index] === ")") {
      --depth;
      if (depth === 0) {
        return index;
      }
    }

    ++index;
  }

  return -1;
}

export function lineOf(text, index) {
  return text.slice(0, index).split("\n").length;
}

/** Interpolated holes of every interpolated literal inside the given text. */
export function interpolationHoles(source, literals) {
  const holes = [];
  for (const literal of literals) {
    if (!literal.interpolated) {
      continue;
    }

    /*
        The spans come from the brace-depth-aware scan: doubled braces never
        opened a hole, and a quote inside a hole stayed inside it, so the
        slice is the expression the compiler sees.
     */
    for (const span of literal.holes) {
      holes.push({ hole: source.slice(span.start, span.end).trim(), offset: span.start });
    }
  }

  return holes;
}

/** A copy of text with every comment span blanked to spaces, length kept. */
export function blankComments(text, comments) {
  const characters = text.split("");
  for (const span of comments) {
    for (let index = span.start; index < span.end; ++index) {
      if (characters[index] !== "\n") {
        characters[index] = " ";
      }
    }
  }

  return characters.join("");
}

/** The [start, end) of a call's first argument: to its top-level comma or closing paren. */
export function firstArgumentSpan(text, openParen, closeParen, literals) {
  let depth = 0;
  let literal = 0;
  for (let index = openParen + 1; index < closeParen; ++index) {
    while (literal < literals.length && literals[literal].start < index) {
      ++literal;
    }

    if (literal < literals.length && literals[literal].start === index) {
      index = literals[literal].end - 1;
      continue;
    }

    const character = text[index];
    if (character === "(") {
      ++depth;
    } else if (character === ")") {
      --depth;
    } else if (character === "," && depth === 0) {
      return [openParen + 1, index];
    }
  }

  return [openParen + 1, closeParen];
}

/*
    The file's own code, with every comment and string literal removed: a
    backstop satisfied by a TODO or a token quoted in a comment or string is
    not a backstop, and a mention in either is the cheapest way to write one
    by accident.
 */
export function codeText(text, literals, comments) {
  let result = "";
  let index = 0;
  /*
    Ordered by position, and the order matters: the two lists are collected in
    separate passes, so concatenating them walks backwards the moment a
    literal precedes a comment, and every region between is emitted twice -
    comment text included, which is the one thing this function removes.
   */
  for (const span of [...comments, ...literals].sort((a, b) => a.start - b.start)) {
    result += text.slice(index, span.start);
    index = span.end;
  }

  return result + text.slice(index);
}
