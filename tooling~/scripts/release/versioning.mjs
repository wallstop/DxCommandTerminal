/*
    Release versioning primitives (T14 phase 2, issue #85).

    Pure functions only: strict semver parsing, version bumping, release
    subject recognition, changelog Unreleased extraction/rotation, and the
    tag-push decision table behind release-tag.yml. No filesystem, process,
    or git access lives here - the release CLI (release.mjs) owns every side
    effect, so everything in this module is unit-testable without fixtures.

    Conventions locked in PLAN.md:
    - Versions are full semver including prerelease identifiers
      (1.0.0-rc25.0). Tags are the version with a "v" prefix; historical
      repository tags are unprefixed and predate the pipeline.
    - CHANGELOG.md follows Keep a Changelog 1.1.0 with "## Unreleased" as the
      accumulation target and dated "## [version] - YYYY-MM-DD" headings.
*/
const SEMVER_SOURCE =
  "(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)" +
  "(?:-((?:0|[1-9]\\d*|\\d*[A-Za-z-][0-9A-Za-z-]*)(?:\\.(?:0|[1-9]\\d*|\\d*[A-Za-z-][0-9A-Za-z-]*))*))?" +
  "(?:\\+([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?";
const SEMVER_PATTERN = new RegExp(`^${SEMVER_SOURCE}$`);
const RELEASE_SUBJECT_PATTERN = new RegExp(`^release: v${SEMVER_SOURCE}(?: \\(#(\\d+)\\))?$`);
const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const REGEX_ESCAPE_PATTERN = /[.*+?^${}()|[\]\\]/g;
const UNRELEASED_HEADING = "## Unreleased";

function parseVersion(value) {
  if (typeof value !== "string") {
    return null;
  }
  const match = SEMVER_PATTERN.exec(value);
  if (match === null) {
    return null;
  }
  return {
    major: Number(match[1]),
    minor: Number(match[2]),
    patch: Number(match[3]),
    prerelease: match[4] === undefined ? null : match[4].split("."),
    build: match[5] === undefined ? null : match[5].split("."),
    raw: value
  };
}

function formatVersion(version) {
  let raw = `${version.major}.${version.minor}.${version.patch}`;
  if (version.prerelease !== null) {
    raw += `-${version.prerelease.join(".")}`;
  }
  if (version.build !== null) {
    raw += `+${version.build.join(".")}`;
  }
  return raw;
}

/*
    Standard semver bump: patch/minor/major raise the core numbers and drop
    any prerelease/build suffix, so a prerelease version releases stable
    (1.0.0-rc25.0 + patch -> 1.0.1). Iterating the rc series needs an
    explicit version instead (1.0.0-rc26.0), which the release CLI accepts.
*/
function bumpVersion(value, kind) {
  const version = parseVersion(value);
  if (version === null) {
    throw new Error(`invalid semver version: ${value}`);
  }
  if (kind === "patch") {
    version.patch += 1;
  } else if (kind === "minor") {
    version.minor += 1;
    version.patch = 0;
  } else if (kind === "major") {
    version.major += 1;
    version.minor = 0;
    version.patch = 0;
  } else {
    throw new Error(`unknown bump kind: ${kind} (expected patch, minor, or major)`);
  }
  version.prerelease = null;
  version.build = null;
  return formatVersion(version);
}

/*
    Release-tag.yml decision input: the squash-merge subject of the push that
    touched package.json. GitHub appends " (#N)" to squash subjects; the
    match is pinned to the first line of the message.
*/
function parseReleaseSubject(subject) {
  if (typeof subject !== "string") {
    return null;
  }
  const firstLine = subject.split("\n", 1)[0].trim();
  const match = RELEASE_SUBJECT_PATTERN.exec(firstLine);
  if (match === null) {
    return null;
  }
  const version = `${match[1]}.${match[2]}.${match[3]}${match[4] === undefined ? "" : `-${match[4]}`}${match[5] === undefined ? "" : `+${match[5]}`}`;
  return parseVersion(version) === null ? null : version;
}

function isValidReleaseDate(value) {
  if (!DATE_PATTERN.test(value ?? "")) {
    return false;
  }
  const parsed = new Date(`${value}T00:00:00Z`);
  return Number.isInteger(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}

function sectionHeading(version, date) {
  if (parseVersion(version) === null) {
    throw new Error(`invalid semver version: ${version}`);
  }
  if (!isValidReleaseDate(date)) {
    throw new Error(`invalid release date (expected YYYY-MM-DD): ${date}`);
  }
  return `## [${version}] - ${date}`;
}

/*
    Splits the changelog into lines, accepting CRLF input. Returns null when
    the heading is absent. The body runs to the next "## " heading (or EOF)
    with its edges trimmed, so an empty section yields "". The publish
    workflow (release.yml, next milestone) uses this to read the shipped
    release notes for its GitHub Release body; tests pin it now.
*/
function extractSection(changelogText, heading) {
  const lines = changelogText.split("\n").map((line) => line.replace(/\r$/, ""));
  const start = lines.findIndex((line) => line === heading);
  if (start < 0) {
    return null;
  }
  let end = lines.length;
  for (let index = start + 1; index < lines.length; index += 1) {
    if (lines[index].startsWith("## ")) {
      end = index;
      break;
    }
  }
  return lines
    .slice(start + 1, end)
    .join("\n")
    .replace(/^\s+|\s+$/g, "");
}

function changelogHasVersionHeading(changelogText, version) {
  if (parseVersion(version) === null) {
    throw new Error(`invalid semver version: ${version}`);
  }
  const escaped = version.replace(REGEX_ESCAPE_PATTERN, "\\$&");
  return new RegExp(`^## \\[${escaped}\\] - \\d{4}-\\d{2}-\\d{2}\\s*$`, "m").test(changelogText);
}

/*
    Rotates "## Unreleased" content under a dated "## [version] - date"
    heading, leaving an empty "## Unreleased" as the next accumulation
    target. Throws (fail closed) when Unreleased is missing or has no
    content - a release without user-facing entries is an operator mistake,
    not something to paper over.
*/
function rotateUnreleased(changelogText, version, date) {
  const heading = sectionHeading(version, date);
  const lines = changelogText.split("\n").map((line) => line.replace(/\r$/, ""));
  const start = lines.findIndex((line) => line === UNRELEASED_HEADING);
  if (start < 0) {
    throw new Error(`CHANGELOG has no "${UNRELEASED_HEADING}" section`);
  }
  let end = lines.length;
  for (let index = start + 1; index < lines.length; index += 1) {
    if (lines[index].startsWith("## ")) {
      end = index;
      break;
    }
  }
  if (end === lines.length) {
    throw new Error(`"${UNRELEASED_HEADING}" is the last section; expected a released version heading after it`);
  }
  const body = lines
    .slice(start + 1, end)
    .join("\n")
    .replace(/^\s+|\s+$/g, "");
  if (body.length === 0) {
    throw new Error(`"${UNRELEASED_HEADING}" is empty; nothing to release`);
  }
  return [...lines.slice(0, start), UNRELEASED_HEADING, "", heading, "", ...body.split("\n"), "", ...lines.slice(end)].join("\n");
}

/*
    Decision table for release-tag.yml (one push event on the default branch
    that touched package.json). Actions:
    - "tag":  push the annotated tag; release.yml (next milestone) picks it up.
    - "noop": silently do nothing (ordinary package.json push, or the version
              is already tagged).
    - "warn": ::warning:: plus manual fallback commands - the changelog
              documents the version but the push subject is not the expected
              release subject, so auto-tagging is refused, not failed.
    - "fail": the subject names this release but the changelog heading is
              missing - fail closed so a release commit cannot ship
              undocumented.
*/
function evaluateTagPush({ version, subject, tagExists, changelogHasVersion }) {
  if (parseVersion(version) === null) {
    return { action: "fail", tag: null, reason: `package.json carries an invalid version: ${version}` };
  }
  const tag = `v${version}`;
  if (tagExists) {
    return { action: "noop", tag, reason: `tag ${tag} already exists; nothing to do` };
  }
  const subjectVersion = parseReleaseSubject(subject);
  if (subjectVersion !== null && subjectVersion !== version) {
    return {
      action: changelogHasVersion ? "warn" : "noop",
      tag,
      reason: `push subject names release v${subjectVersion} but package.json carries ${version}`
    };
  }
  if (subjectVersion !== null) {
    if (changelogHasVersion) {
      return { action: "tag", tag, reason: `release subject names v${version} and the changelog heading exists` };
    }
    return {
      action: "fail",
      tag,
      reason: `release subject names v${version} but CHANGELOG has no "## [${version}] - date" heading`
    };
  }
  if (changelogHasVersion) {
    return {
      action: "warn",
      tag,
      reason: `CHANGELOG documents ${version} but the push subject is not "release: v${version}"`
    };
  }
  return { action: "noop", tag, reason: "ordinary package.json push; not a release" };
}

/*
    Line diff for dry-run printing (LCS edit script, no context lines). The
    changelog rotation leaves the Unreleased body in place and only inserts
    the new dated heading, so the LCS aligns the body as context and the
    diff stays small - typically the heading insert plus surrounding
    whitespace adjustments.
*/
function diffLines(beforeText, afterText) {
  const before = beforeText.length === 0 ? [] : beforeText.split("\n");
  const after = afterText.length === 0 ? [] : afterText.split("\n");
  const rows = before.length + 1;
  const columns = after.length + 1;
  const lengths = new Array(rows * columns).fill(0);
  const at = (row, column) => row * columns + column;
  for (let row = before.length - 1; 0 <= row; row -= 1) {
    for (let column = after.length - 1; 0 <= column; column -= 1) {
      if (before[row] === after[column]) {
        lengths[at(row, column)] = lengths[at(row + 1, column + 1)] + 1;
      } else {
        lengths[at(row, column)] = Math.max(lengths[at(row + 1, column)], lengths[at(row, column + 1)]);
      }
    }
  }
  const lines = [];
  let row = 0;
  let column = 0;
  while (row < before.length && column < after.length) {
    if (before[row] === after[column]) {
      lines.push(`  ${before[row]}`);
      row += 1;
      column += 1;
    } else if (lengths[at(row + 1, column)] >= lengths[at(row, column + 1)]) {
      lines.push(`- ${before[row]}`);
      row += 1;
    } else {
      lines.push(`+ ${after[column]}`);
      column += 1;
    }
  }
  while (row < before.length) {
    lines.push(`- ${before[row]}`);
    row += 1;
  }
  while (column < after.length) {
    lines.push(`+ ${after[column]}`);
    column += 1;
  }
  return lines;
}

export {
  UNRELEASED_HEADING,
  bumpVersion,
  changelogHasVersionHeading,
  diffLines,
  evaluateTagPush,
  extractSection,
  parseReleaseSubject,
  parseVersion,
  rotateUnreleased,
  sectionHeading
};
