/*
    T11 deterministic comparator: pixel compare of T04 capture PNGs against
    committed golden baselines, plus the tiny PNG codec both the comparator
    and the self-tests need (stdlib zlib only - no dependencies).

    Gate (PLAN.md T11): per-channel tolerance <= 1 byte, failing when more
    than 0.1% of pixels exceed it. Provenance (Unity version, graphics API,
    color space, resolution, theme, font) must match exactly - baselines are
    never compared across environments.
*/
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";

export const PIXEL_TOLERANCE = 1;
export const MAX_VIOLATION_FRACTION = 0.001;
export const BASELINE_STORE_VERSION = 1;

const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const SAMPLE_LIMIT = 8;

const CRC_TABLE = (() => {
  const table = new Int32Array(256);
  for (let n = 0; n < 256; ++n) {
    let c = n;
    for (let k = 0; k < 8; ++k) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    table[n] = c;
  }
  return table;
})();

function crc32(buffer) {
  let crc = -1;
  for (let index = 0; index < buffer.length; ++index) {
    crc = CRC_TABLE[(crc ^ buffer[index]) & 0xff] ^ (crc >>> 8);
  }
  return (crc ^ -1) >>> 0;
}

function decodeFailed(reason) {
  throw new Error(`undecodable PNG: ${reason}`);
}

/*
    Decodes an 8-bit non-interlaced PNG (color types 0/2/4/6) into
    { width, height, data } with RGBA-ordered bytes. Unity's EncodeToPNG
    output is color type 6; the gray variants exist for hand-made fixtures.
    Any structural damage (bad signature, CRC mismatch, truncation,
    unsupported layout) fails loudly so a corrupted baseline can never pass.
*/
export function decodePng(buffer) {
  if (!Buffer.isBuffer(buffer)) decodeFailed("input is not a Buffer");
  if (buffer.length < PNG_SIGNATURE.length || !buffer.subarray(0, 8).equals(PNG_SIGNATURE)) {
    decodeFailed("bad signature");
  }

  let offset = 8;
  let width = 0;
  let height = 0;
  let colorType = 0;
  let seenIhdr = false;
  const idat = [];
  let seenIend = false;
  while (offset + 8 <= buffer.length) {
    const length = buffer.readUInt32BE(offset);
    const type = buffer.toString("latin1", offset + 4, offset + 8);
    const dataStart = offset + 8;
    const dataEnd = dataStart + length;
    if (dataEnd + 4 > buffer.length) decodeFailed(`truncated ${type} chunk`);
    const chunk = buffer.subarray(dataStart, dataEnd);
    if (crc32(buffer.subarray(offset + 4, dataEnd)) !== buffer.readUInt32BE(dataEnd)) {
      decodeFailed(`CRC mismatch in ${type} chunk`);
    }

    if (type === "IHDR") {
      if (seenIhdr || idat.length > 0) decodeFailed("IHDR must appear exactly once, first");
      seenIhdr = true;
      if (chunk.length < 13) decodeFailed("IHDR chunk is too short");
      width = chunk.readUInt32BE(0);
      height = chunk.readUInt32BE(4);
      const bitDepth = chunk[8];
      colorType = chunk[9];
      const interlace = chunk[12];
      if (width < 1 || height < 1) decodeFailed("empty image");
      if (bitDepth !== 8) decodeFailed(`unsupported bit depth ${bitDepth}`);
      if (interlace !== 0) decodeFailed("interlaced images are unsupported");
      if (colorType !== 0 && colorType !== 2 && colorType !== 4 && colorType !== 6) {
        decodeFailed(`unsupported color type ${colorType}`);
      }
    } else if (type === "IDAT") {
      idat.push(chunk);
    } else if (type === "IEND") {
      seenIend = true;
      break;
    }
    offset = dataEnd + 4;
  }
  if (!seenIhdr) decodeFailed("missing IHDR");
  if (!seenIend) decodeFailed("missing IEND");
  if (idat.length === 0) decodeFailed("missing IDAT");

  let raw;
  try {
    raw = zlib.inflateSync(Buffer.concat(idat));
  } catch (error) {
    decodeFailed(`inflate failed (${error.message})`);
  }

  const channels = colorType === 0 ? 1 : colorType === 2 ? 3 : colorType === 4 ? 2 : 4;
  const stride = width * channels;
  if (raw.length < (stride + 1) * height) decodeFailed("decompressed pixel data is short");

  const data = Buffer.alloc(width * height * 4);
  const previous = Buffer.alloc(stride);
  const current = Buffer.alloc(stride);
  for (let y = 0; y < height; ++y) {
    const rowStart = y * (stride + 1);
    const filter = raw[rowStart];
    raw.copy(current, 0, rowStart + 1, rowStart + 1 + stride);
    for (let x = 0; x < stride; ++x) {
      const left = x < channels ? 0 : current[x - channels];
      const up = y === 0 ? 0 : previous[x];
      const upLeft = x < channels || y === 0 ? 0 : previous[x - channels];
      if (filter === 1) {
        current[x] = (current[x] + left) & 0xff;
      } else if (filter === 2) {
        current[x] = (current[x] + up) & 0xff;
      } else if (filter === 3) {
        current[x] = (current[x] + ((left + up) >>> 1)) & 0xff;
      } else if (filter === 4) {
        const p = left + up - upLeft;
        const pa = Math.abs(p - left);
        const pb = Math.abs(p - up);
        const pc = Math.abs(p - upLeft);
        current[x] = (current[x] + (pa <= pb && pa <= pc ? left : pb <= pc ? up : upLeft)) & 0xff;
      } else if (filter !== 0) {
        decodeFailed(`unknown row filter ${filter}`);
      }
    }

    for (let x = 0; x < width; ++x) {
      const source = x * channels;
      const target = (y * width + x) * 4;
      if (colorType === 0) {
        data[target] = data[target + 1] = data[target + 2] = current[source];
        data[target + 3] = 255;
      } else if (colorType === 2) {
        data[target] = current[source];
        data[target + 1] = current[source + 1];
        data[target + 2] = current[source + 2];
        data[target + 3] = 255;
      } else if (colorType === 4) {
        data[target] = data[target + 1] = data[target + 2] = current[source];
        data[target + 3] = current[source + 1];
      } else {
        data[target] = current[source];
        data[target + 1] = current[source + 1];
        data[target + 2] = current[source + 2];
        data[target + 3] = current[source + 3];
      }
    }
    current.copy(previous);
  }

  return { width, height, data };
}

/** Encodes RGBA bytes as an 8-bit RGBA PNG (filter 0 rows, deflate level 9). */
export function encodePng(width, height, data) {
  if (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1) {
    throw new Error(`encodePng needs positive integer dimensions, got ${width}x${height}`);
  }
  if (!Buffer.isBuffer(data) || data.length !== width * height * 4) {
    throw new Error("encodePng data must be a width*height*4 byte RGBA buffer");
  }

  const stride = width * 4;
  const raw = Buffer.alloc((stride + 1) * height);
  for (let y = 0; y < height; ++y) {
    raw[y * (stride + 1)] = 0;
    data.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
  }

  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  return Buffer.concat([
    PNG_SIGNATURE,
    chunk("IHDR", ihdr),
    chunk("IDAT", zlib.deflateSync(raw, { level: 9 })),
    chunk("IEND", Buffer.alloc(0))
  ]);
}

function chunk(type, data) {
  const header = Buffer.alloc(8);
  header.writeUInt32BE(data.length, 0);
  header.write(type, 4, "latin1");
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(Buffer.concat([header.subarray(4), data])), 0);
  return Buffer.concat([header, data, crc]);
}

/**
 * Pixel compare per the T11 gate: identical buffers pass immediately;
 * otherwise each pixel must stay within `tolerance` per channel and the
 * share of exceeding pixels must stay within `maxViolationFraction`.
 */
export function compareImages(baseline, actual, options = {}) {
  const tolerance = options.tolerance ?? PIXEL_TOLERANCE;
  const maxViolationFraction = options.maxViolationFraction ?? MAX_VIOLATION_FRACTION;
  const result = {
    identical: false,
    pass: false,
    width: 0,
    height: 0,
    totalPixels: 0,
    violationCount: 0,
    violationFraction: 0,
    maxChannelDelta: 0,
    tolerance,
    dimensionMismatch: null,
    samples: []
  };

  if (baseline.width !== actual.width || baseline.height !== actual.height) {
    result.dimensionMismatch = {
      baseline: { width: baseline.width, height: baseline.height },
      actual: { width: actual.width, height: actual.height }
    };
    return result;
  }

  result.width = baseline.width;
  result.height = baseline.height;
  result.totalPixels = baseline.width * baseline.height;
  if (baseline.data.equals(actual.data)) {
    result.identical = true;
    result.pass = true;
    return result;
  }

  const data = baseline.data;
  const other = actual.data;
  for (let pixel = 0; pixel < result.totalPixels; ++pixel) {
    const offset = pixel * 4;
    let exceeded = false;
    for (let channel = 0; channel < 4; ++channel) {
      const delta = Math.abs(data[offset + channel] - other[offset + channel]);
      if (delta > result.maxChannelDelta) result.maxChannelDelta = delta;
      if (delta > tolerance) exceeded = true;
    }
    if (exceeded) {
      ++result.violationCount;
      if (result.samples.length < SAMPLE_LIMIT) {
        result.samples.push({
          x: pixel % result.width,
          y: Math.floor(pixel / result.width),
          baseline: [data[offset], data[offset + 1], data[offset + 2], data[offset + 3]],
          actual: [other[offset], other[offset + 1], other[offset + 2], other[offset + 3]]
        });
      }
    }
  }

  result.violationFraction = result.violationCount / result.totalPixels;
  result.pass = result.violationFraction <= maxViolationFraction;
  return result;
}

/** Red-on-dark difference image: violations red, tolerated deltas yellow, matches dark. */
export function diffImage(baseline, actual, result) {
  const size = result.width * result.height * 4;
  const data = Buffer.alloc(size, 20);
  if (result.dimensionMismatch !== null) {
    return encodePng(1, 1, data.subarray(0, 4));
  }
  const tolerance = result.tolerance;
  for (let pixel = 0; pixel < result.totalPixels; ++pixel) {
    const offset = pixel * 4;
    let exceeded = false;
    let delta = 0;
    for (let channel = 0; channel < 4; ++channel) {
      const d = Math.abs(baseline.data[offset + channel] - actual.data[offset + channel]);
      if (d > delta) delta = d;
      if (d > tolerance) exceeded = true;
    }
    if (exceeded) {
      data[offset] = 255;
      data[offset + 3] = 255;
    } else if (delta > 0) {
      data[offset] = data[offset + 1] = 180;
      data[offset + 3] = 255;
    }
  }
  return encodePng(result.width, result.height, data);
}

/** Baseline ghost (12% brightness) with every violating pixel painted solid red. */
export function overlayImage(baseline, actual, result) {
  const data = Buffer.from(baseline.data);
  if (result.dimensionMismatch !== null) {
    return encodePng(result.width || 1, result.height || 1, data.subarray(0, 4));
  }
  for (let pixel = 0; pixel < result.totalPixels; ++pixel) {
    const offset = pixel * 4;
    data[offset] = Math.floor(data[offset] * 0.12);
    data[offset + 1] = Math.floor(data[offset + 1] * 0.12);
    data[offset + 2] = Math.floor(data[offset + 2] * 0.12);
    data[offset + 3] = 255;
  }
  for (let pixel = 0; pixel < result.totalPixels; ++pixel) {
    const offset = pixel * 4;
    for (let channel = 0; channel < 4; ++channel) {
      if (Math.abs(baseline.data[offset + channel] - actual.data[offset + channel]) > result.tolerance) {
        data[offset] = 255;
        data[offset + 1] = data[offset + 2] = 0;
        data[offset + 3] = 255;
        break;
      }
    }
  }
  return encodePng(result.width, result.height, data);
}

/**
 * Provenance of one capture: everything that must match before two PNGs
 * may be compared. Theme and font are per-scenario; the rest is shared
 * environment identity.
 */
export function provenanceOf(manifest) {
  return {
    unityVersion: manifest.unityVersion,
    graphicsApi: manifest.graphicsApi,
    colorSpace: manifest.colorSpace,
    width: manifest.resolution?.width,
    height: manifest.resolution?.height,
    logicalScale: manifest.logicalScale,
    theme: manifest.theme,
    font: manifest.font
  };
}

export function provenanceProblems(expected, actual) {
  const problems = [];
  for (const field of ["unityVersion", "graphicsApi", "colorSpace", "theme", "font"]) {
    if (expected[field] !== actual[field]) {
      problems.push(`${field}: baseline ${JSON.stringify(expected[field])} vs actual ${JSON.stringify(actual[field])}`);
    }
  }
  for (const field of ["width", "height", "logicalScale"]) {
    if (expected[field] !== actual[field]) {
      problems.push(`${field}: baseline ${expected[field]} vs actual ${actual[field]}`);
    }
  }
  return problems;
}

/** Filesystem-safe environment directory name derived from flat provenance. */
export function environmentKey(provenance) {
  const scale = provenance.logicalScale;
  if (typeof scale !== "number" || !(scale > 0) || !Number.isFinite(scale)) {
    throw new Error(`environment logicalScale must be a finite positive number, got ${String(scale)}`);
  }
  if (!Number.isInteger(provenance.width) || !Number.isInteger(provenance.height)) {
    throw new Error("environment resolution must record integer width/height");
  }
  const raw = `${provenance.unityVersion}-${provenance.graphicsApi}-${provenance.colorSpace}-`
    + `${provenance.width}x${provenance.height}-scale${scale}`;
  const key = raw
    .toLowerCase()
    .replace(/[^a-z0-9.]+/g, "-")
    .replace(/^-+|-+$/g, "");
  if (key.length === 0) throw new Error("environment key collapsed to an empty string");
  return key;
}

/**
 * Flat comparable provenance from a baseline index's environment block plus
 * a scenario entry's theme/font - the exact shape provenanceOf produces, so
 * the two sides of every compare speak the same language.
 */
export function environmentProvenance(environment, theme, font) {
  return {
    unityVersion: environment.unityVersion,
    graphicsApi: environment.graphicsApi,
    colorSpace: environment.colorSpace,
    width: environment.resolution.width,
    height: environment.resolution.height,
    logicalScale: environment.logicalScale,
    theme,
    font
  };
}

/**
 * Runs the full gate for one scenario: provenance, dimensions, then pixels.
 * Returns { pass, problems, result } - problems carries human-readable
 * causes, result the pixel statistics (null when the compare never ran).
 */
export function compareScenario(baselinePng, actualPng, baselineProvenance, actualProvenance) {
  const problems = provenanceProblems(baselineProvenance, actualProvenance);
  if (problems.length > 0) return { pass: false, problems, result: null };

  const baseline = decodePng(baselinePng);
  const actual = decodePng(actualPng);
  const result = compareImages(baseline, actual);
  if (!result.pass) {
    if (result.dimensionMismatch !== null) {
      problems.push(
        `dimensions: baseline ${result.dimensionMismatch.baseline.width}x`
          + `${result.dimensionMismatch.baseline.height} vs actual `
          + `${result.dimensionMismatch.actual.width}x${result.dimensionMismatch.actual.height}`
      );
    } else {
      problems.push(
        `${result.violationCount}/${result.totalPixels} pixels `
          + `(${(result.violationFraction * 100).toFixed(4)}%) exceed the `
          + `${PIXEL_TOLERANCE}-byte channel tolerance (max delta `
          + `${result.maxChannelDelta}); first at `
          + result.samples
            .map((sample) => `(${sample.x},${sample.y})`)
            .join(" ")
      );
    }
  }
  return { pass: problems.length === 0, problems, result };
}

/**
 * Reads <store>/<envKey>/index.json; returns null when the store has no such
 * environment. Throws on an index that parses but has the wrong shape, so a
 * corrupted store fails loudly instead of reading as "no baseline yet".
 */
export function readBaselineIndex(storeDir, envKey) {
  const filePath = path.join(storeDir, envKey, "index.json");
  if (!fs.existsSync(filePath)) return null;
  const index = JSON.parse(fs.readFileSync(filePath, "utf8"));
  const shapeProblem = (reason) => {
    throw new Error(`baseline index ${filePath} is malformed: ${reason}`);
  };
  if (index === null || typeof index !== "object" || Array.isArray(index)) {
    shapeProblem("not a JSON object");
  }
  if (index.version !== BASELINE_STORE_VERSION) {
    shapeProblem(`unsupported version ${JSON.stringify(index.version)}`);
  }
  if (index.environment === null || typeof index.environment !== "object") {
    shapeProblem("missing environment block");
  }
  if (
    index.scenarios === null ||
    typeof index.scenarios !== "object" ||
    Array.isArray(index.scenarios)
  ) {
    shapeProblem("missing scenarios block");
  }
  return index;
}

/** Writes <store>/<envKey>/index.json byte-stably (sorted scenarios, 2-space indent). */
export function writeBaselineIndex(storeDir, envKey, index) {
  const environmentDir = path.join(storeDir, envKey);
  fs.mkdirSync(environmentDir, { recursive: true });
  const scenarios = {};
  for (const name of Object.keys(index.scenarios).sort()) {
    scenarios[name] = index.scenarios[name];
  }
  const stable = { version: BASELINE_STORE_VERSION, environment: index.environment, scenarios };
  fs.writeFileSync(
    path.join(environmentDir, "index.json"),
    `${JSON.stringify(stable, null, 2)}\n`
  );
}

/**
 * Writes the review artifacts for a failed (or human-reviewed) comparison:
 * baseline copy, actual copy, diff image, overlay image, and a report.json
 * with the pixel statistics and both provenances.
 */
export function emitComparisonArtifacts(outDir, baselinePng, actualPng, outcome) {
  fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(path.join(outDir, "baseline.png"), baselinePng);
  fs.writeFileSync(path.join(outDir, "actual.png"), actualPng);
  const report = { pass: outcome.pass, problems: outcome.problems, result: outcome.result };
  if (outcome.result !== null && outcome.result.dimensionMismatch === null) {
    const baseline = decodePng(baselinePng);
    const actual = decodePng(actualPng);
    fs.writeFileSync(path.join(outDir, "diff.png"), diffImage(baseline, actual, outcome.result));
    fs.writeFileSync(
      path.join(outDir, "overlay.png"),
      overlayImage(baseline, actual, outcome.result)
    );
  }
  fs.writeFileSync(path.join(outDir, "report.json"), `${JSON.stringify(report, null, 2)}\n`);
  return outDir;
}
