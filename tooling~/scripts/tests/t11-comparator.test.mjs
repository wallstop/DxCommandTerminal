import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import zlib from "node:zlib";
import { describe, it } from "node:test";
import {
  MAX_VIOLATION_FRACTION,
  PIXEL_TOLERANCE,
  compareImages,
  compareScenario,
  decodePng,
  diffImage,
  encodePng,
  environmentKey,
  overlayImage,
  provenanceOf,
  provenanceProblems,
  readBaselineIndex,
  writeBaselineIndex
} from "../t11/comparator.mjs";

const WIDTH = 64;
const HEIGHT = 32;

/** Deterministic fixture: light gradient plus block shapes that read as "text". */
function fixtureImage(seed = 0) {
  const data = Buffer.alloc(WIDTH * HEIGHT * 4);
  for (let y = 0; y < HEIGHT; ++y) {
    for (let x = 0; x < WIDTH; ++x) {
      const offset = (y * WIDTH + x) * 4;
      const glyph = x % 8 < 4 && y % 8 < 4;
      data[offset] = glyph ? 30 + seed : 200 + seed;
      data[offset + 1] = glyph ? 30 + seed : 200;
      data[offset + 2] = glyph ? 30 + seed : 200;
      data[offset + 3] = 255;
    }
  }
  return { width: WIDTH, height: HEIGHT, data };
}

function shiftedImage(image, shift) {
  const data = Buffer.from(image.data);
  for (let y = 0; y < image.height; ++y) {
    for (let x = image.width - 1; x >= shift; --x) {
      const target = (y * image.width + x) * 4;
      const source = (y * image.width + x - shift) * 4;
      data.copy(data, target, source, source + 4);
    }
  }
  return { width: image.width, height: image.height, data };
}

const manifest = (overrides = {}) => ({
  scenario: "CapturesTerminalSmallSurface",
  capturedUtc: "2026-09-22T04:00:00.0000000Z",
  complete: true,
  resolution: { width: WIDTH, height: HEIGHT },
  logicalScale: 1,
  colorSpace: "Linear",
  unityVersion: "6000.4.6f1",
  graphicsApi: "Metal",
  theme: "dark-theme",
  font: "FiraMono-Regular",
  png: "CapturesTerminalSmallSurface.png",
  ...overrides
});

describe("PNG codec", () => {
  it("roundtrips RGBA bytes through encode/decode", () => {
    const image = fixtureImage(7);
    const decoded = decodePng(encodePng(image.width, image.height, image.data));
    assert.equal(decoded.width, WIDTH);
    assert.equal(decoded.height, HEIGHT);
    assert.deepEqual([...decoded.data], [...image.data]);
  });

  it("roundtrips a gray+alpha PNG (color type 4)", () => {
    const width = 3;
    const height = 2;
    const raw = Buffer.alloc((width * 2 + 1) * height);
    const gray = [0, 128, 255, 7, 200, 42];
    for (let y = 0; y < height; ++y) {
      raw[y * (width * 2 + 1)] = 0;
      for (let x = 0; x < width; ++x) {
        raw[y * (width * 2 + 1) + 1 + x * 2] = gray[y * width + x];
        raw[y * (width * 2 + 1) + 2 + x * 2] = 255 - gray[y * width + x];
      }
    }
    const ihdr = Buffer.alloc(13);
    ihdr.writeUInt32BE(width, 0);
    ihdr.writeUInt32BE(height, 4);
    ihdr[8] = 8;
    ihdr[9] = 4;
    const crc = (chunk) => {
      const table = [];
      for (let n = 0; n < 256; ++n) {
        let c = n;
        for (let k = 0; k < 8; ++k) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        table[n] = c;
      }
      let crcValue = -1;
      for (const byte of chunk) crcValue = table[(crcValue ^ byte) & 0xff] ^ (crcValue >>> 8);
      return (crcValue ^ -1) >>> 0;
    };
    const chunk = (type, data) => {
      const head = Buffer.alloc(8);
      head.writeUInt32BE(data.length, 0);
      head.write(type, 4, "latin1");
      const tail = Buffer.alloc(4);
      tail.writeUInt32BE(crc(Buffer.concat([head.subarray(4), data])), 0);
      return Buffer.concat([head, data, tail]);
    };
    const png = Buffer.concat([
      Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
      chunk("IHDR", ihdr),
      chunk("IDAT", zlib.deflateSync(raw)),
      chunk("IEND", Buffer.alloc(0))
    ]);
    const decoded = decodePng(png);
    assert.equal(decoded.width, width);
    for (let index = 0; index < gray.length; ++index) {
      assert.equal(decoded.data[index * 4], gray[index]);
      assert.equal(decoded.data[index * 4 + 3], 255 - gray[index]);
    }
  });

  it("rejects corrupted and unsupported PNGs loudly", () => {
    const valid = encodePng(WIDTH, HEIGHT, fixtureImage().data);
    const withIhdrDepth16 = (() => {
      // Mutate the IHDR bit-depth byte and repair that chunk's CRC so the
      // decoder's bit-depth branch (not CRC validation) rejects it.
      const copy = Buffer.from(valid);
      copy[24] = 16;
      const ihdr = copy.subarray(12, 12 + 8 + 13);
      const table = [];
      for (let n = 0; n < 256; ++n) {
        let c = n;
        for (let k = 0; k < 8; ++k) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        table[n] = c;
      }
      let crc = -1;
      for (const byte of copy.subarray(12 + 4, 12 + 8 + 13)) {
        crc = table[(crc ^ byte) & 0xff] ^ (crc >>> 8);
      }
      copy.writeUInt32BE((crc ^ -1) >>> 0, 12 + 8 + 13);
      return copy;
    })();
    const cases = [
      ["bad signature", Buffer.concat([Buffer.from([1, 2, 3, 4, 5, 6, 7, 8]), valid.subarray(8)])],
      ["truncated body", valid.subarray(0, valid.length - 12)],
      ["flipped payload byte", (() => {
        const copy = Buffer.from(valid);
        copy[copy.length - 6] ^= 0xff;
        return copy;
      })()],
      ["bit depth 16", withIhdrDepth16],
      ["missing IHDR", Buffer.concat([valid.subarray(0, 8), valid.subarray(8 + 12 + 13 + 4)])]
    ];
    for (const [name, png] of cases) {
      assert.throws(() => decodePng(png), /undecodable PNG/, `expected ${name} to fail`);
    }
    assert.throws(() => decodePng("not a buffer"), /not a Buffer/);
  });
});

describe("compareImages", () => {
  it("passes identical images and reports them as byte-identical", () => {
    const image = fixtureImage();
    const result = compareImages(image, fixtureImage());
    assert.equal(result.pass, true);
    assert.equal(result.identical, true);
    assert.equal(result.violationCount, 0);
  });

  it("passes 1-byte noise within the tolerance and violation budget", () => {
    const baseline = fixtureImage();
    const actual = fixtureImage();
    const total = WIDTH * HEIGHT;
    const withinBudget = Math.floor(total * MAX_VIOLATION_FRACTION);
    for (let index = 0; index < withinBudget; ++index) {
      actual.data[index * 4] = (actual.data[index * 4] + PIXEL_TOLERANCE + 5) & 0xff;
    }
    const result = compareImages(baseline, actual);
    assert.equal(result.pass, true, `${withinBudget}/${total} must pass`);
    assert.equal(result.identical, false);
    assert.equal(result.violationCount, withinBudget);
    assert.equal(result.maxChannelDelta, PIXEL_TOLERANCE + 5);
  });

  it("fails at the first pixel past the violation budget", () => {
    const baseline = fixtureImage();
    const actual = fixtureImage();
    const total = WIDTH * HEIGHT;
    const overBudget = Math.floor(total * MAX_VIOLATION_FRACTION) + 1;
    for (let index = 0; index < overBudget; ++index) {
      actual.data[index * 4] = (actual.data[index * 4] + PIXEL_TOLERANCE + 5) & 0xff;
    }
    const result = compareImages(baseline, actual);
    assert.equal(result.pass, false, `${overBudget}/${total} must fail`);
    assert.equal(result.violationCount, overBudget);
  });

  it("honors a custom tolerance in the gate and the diff renderer", () => {
    const baseline = fixtureImage();
    const actual = fixtureImage();
    actual.data[0] = (actual.data[0] + 4) & 0xff;
    const result = compareImages(baseline, actual, { tolerance: 4 });
    assert.equal(result.pass, true);
    assert.equal(result.tolerance, 4);
    const diff = decodePng(diffImage(baseline, actual, result));
    assert.equal(diff.data[0], 180);
  });

  it("fails when the exceeding-pixel share passes the budget", () => {
    const baseline = fixtureImage();
    const actual = fixtureImage();
    for (let index = 0; index < 16; ++index) actual.data[index * 4] = 255 - actual.data[index * 4];
    const result = compareImages(baseline, actual);
    assert.equal(result.pass, false);
    assert.equal(result.violationCount, 16);
    assert.ok(result.samples.length > 0 && result.samples.length <= 8);
    assert.ok(result.violationFraction > MAX_VIOLATION_FRACTION);
  });

  it("fails a shifted layout", () => {
    const result = compareImages(fixtureImage(), shiftedImage(fixtureImage(), 5));
    assert.equal(result.pass, false);
  });

  it("fails changed content: different text, swapped colors, blank output, missing region", () => {
    const baseline = fixtureImage();
    const wrongText = fixtureImage();
    for (let y = 0; y < HEIGHT; ++y) {
      for (let x = 0; x < WIDTH; ++x) {
        if (x % 8 < 4 && y % 8 < 4) continue;
        const offset = (y * WIDTH + x) * 4;
        wrongText.data[offset] = 10;
      }
    }
    const swappedColors = fixtureImage();
    for (let index = 0; index < swappedColors.data.length; index += 4) {
      swappedColors.data[index] = 255 - swappedColors.data[index];
    }
    const blank = { width: WIDTH, height: HEIGHT, data: Buffer.alloc(WIDTH * HEIGHT * 4) };
    const missingRegion = fixtureImage();
    for (let y = 4; y < 12; ++y) {
      for (let x = 16; x < 40; ++x) {
        const offset = (y * WIDTH + x) * 4;
        missingRegion.data[offset] = missingRegion.data[offset + 1] = missingRegion.data[offset + 2] = 0;
      }
    }
    for (const [name, actual] of [
      ["wrong text", wrongText],
      ["swapped colors", swappedColors],
      ["blank output", blank],
      ["missing region", missingRegion]
    ]) {
      const result = compareImages(baseline, actual);
      assert.equal(result.pass, false, `expected ${name} to fail`);
      assert.equal(result.dimensionMismatch, null);
    }
  });

  it("fails on dimension mismatch and reports both sides", () => {
    const result = compareImages(fixtureImage(), { width: WIDTH + 1, height: HEIGHT, data: Buffer.alloc(4) });
    assert.equal(result.pass, false);
    assert.deepEqual(result.dimensionMismatch, {
      baseline: { width: WIDTH, height: HEIGHT },
      actual: { width: WIDTH + 1, height: HEIGHT }
    });
  });

  it("renders a diff image that marks violations", () => {
    const baseline = fixtureImage();
    const actual = fixtureImage();
    actual.data[0] = 255 - actual.data[0];
    const result = compareImages(baseline, actual);
    const diff = decodePng(diffImage(baseline, actual, result));
    assert.equal(diff.width, WIDTH);
    assert.equal(diff.data[0], 255);
  });

  it("renders an overlay that marks every violation, not just the samples", () => {
    const baseline = fixtureImage();
    const actual = fixtureImage();
    // violations live beyond the 8-pixel sample cap
    const far = (HEIGHT - 1) * WIDTH + (WIDTH - 1);
    actual.data[far * 4] = 255 - actual.data[far * 4];
    const result = compareImages(baseline, actual);
    assert.ok(0 < result.violationCount);
    const overlay = decodePng(overlayImage(baseline, actual, result));
    assert.equal(overlay.data[far * 4], 255);
    assert.equal(overlay.data[far * 4 + 1], 0);
    assert.equal(overlay.data[0], Math.floor(fixtureImage().data[0] * 0.12));
  });
});

describe("provenance", () => {
  it("flattens a manifest into comparable provenance", () => {
    const provenance = provenanceOf(manifest());
    assert.deepEqual(provenance, {
      unityVersion: "6000.4.6f1",
      graphicsApi: "Metal",
      colorSpace: "Linear",
      width: WIDTH,
      height: HEIGHT,
      logicalScale: 1,
      theme: "dark-theme",
      font: "FiraMono-Regular"
    });
  });

  it("detects every mismatching provenance field, and only those", () => {
    const baseline = provenanceOf(manifest());
    const cases = [
      [{}, []],
      [{ unityVersion: "2021.3.45f1" }, ["unityVersion"]],
      [{ graphicsApi: "Vulkan" }, ["graphicsApi"]],
      [{ colorSpace: "Gamma" }, ["colorSpace"]],
      [{ theme: "light-theme" }, ["theme"]],
      [{ font: null }, ["font"]],
      [{ width: 1, height: 1 }, ["width", "height"]],
      [{ logicalScale: 2 }, ["logicalScale"]]
    ];
    for (const [overrides, expected] of cases) {
      const problems = provenanceProblems(baseline, { ...baseline, ...overrides });
      assert.equal(problems.length, expected.length, JSON.stringify(overrides));
      for (const prefix of expected) {
        assert.ok(problems.some((problem) => problem.startsWith(`${prefix}:`)));
      }
    }
  });
});

describe("environmentKey", () => {
  it("derives a stable, filesystem-safe key from flat provenance", () => {
    assert.equal(
      environmentKey(provenanceOf(manifest())),
      "6000.4.6f1-metal-linear-64x32-scale1"
    );
    assert.equal(
      environmentKey(
        provenanceOf(manifest({ logicalScale: 1.5, graphicsApi: "Direct3D 11", colorSpace: "Gamma" }))
      ),
      "6000.4.6f1-direct3d-11-gamma-64x32-scale1.5"
    );
    assert.equal(
      environmentKey(provenanceOf(manifest({ unityVersion: "///" }))),
      "metal-linear-64x32-scale1"
    );
  });

  it("rejects provenance without usable identity", () => {
    assert.throws(
      () => environmentKey(provenanceOf(manifest({ logicalScale: "big" }))),
      /finite positive number/
    );
    assert.throws(
      () => environmentKey(provenanceOf(manifest({ logicalScale: Number.NaN }))),
      /finite positive number/
    );
    assert.throws(
      () => environmentKey({ ...provenanceOf(manifest()), width: undefined }),
      /integer width\/height/
    );
  });
});

describe("compareScenario", () => {
  const baselineProvenance = provenanceOf(manifest());
  const actualProvenance = provenanceOf(manifest({ capturedUtc: "later" }));

  it("passes matching images with matching provenance", () => {
    const outcome = compareScenario(
      encodePng(WIDTH, HEIGHT, fixtureImage().data),
      encodePng(WIDTH, HEIGHT, fixtureImage().data),
      baselineProvenance,
      actualProvenance
    );
    assert.equal(outcome.pass, true);
    assert.deepEqual(outcome.problems, []);
    assert.equal(outcome.result.identical, true);
  });

  it("refuses to compare across provenance without decoding pixels", () => {
    const outcome = compareScenario(
      encodePng(WIDTH, HEIGHT, fixtureImage().data),
      encodePng(WIDTH, HEIGHT, fixtureImage(1).data),
      baselineProvenance,
      { ...actualProvenance, theme: "light-theme" }
    );
    assert.equal(outcome.pass, false);
    assert.equal(outcome.result, null);
    assert.equal(outcome.problems.length, 1);
  });

  it("fails with pixel statistics when content drifts", () => {
    const drifted = fixtureImage();
    for (let index = 0; index < 16; ++index) drifted.data[index * 4] = 255 - drifted.data[index * 4];
    const outcome = compareScenario(
      encodePng(WIDTH, HEIGHT, fixtureImage().data),
      encodePng(WIDTH, HEIGHT, drifted.data),
      baselineProvenance,
      actualProvenance
    );
    assert.equal(outcome.pass, false);
    assert.ok(outcome.problems[0].includes("exceed the 1-byte channel tolerance"));
    assert.ok(outcome.problems[0].includes("(0,0)"));
  });
});

describe("baseline index helpers", () => {
  it("writes scenarios sorted and reads them back", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "t11-index-"));
    try {
      writeBaselineIndex(root, "env-1", {
        version: 1,
        environment: { unityVersion: "u" },
        scenarios: { "B-surface": { png: "b.png" }, "A-surface": { png: "a.png" } }
      });
      const raw = fs.readFileSync(path.join(root, "env-1", "index.json"), "utf8");
      assert.ok(raw.indexOf('"A-surface"') < raw.indexOf('"B-surface"'));
      assert.ok(raw.endsWith("}\n"));
      const index = readBaselineIndex(root, "env-1");
      assert.deepEqual(Object.keys(index.scenarios), ["A-surface", "B-surface"]);
      assert.equal(readBaselineIndex(root, "missing"), null);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});
