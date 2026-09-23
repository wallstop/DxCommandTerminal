/*
    Shared helpers for asserting on exported .unitypackage artifacts: a minimal
    tar reader plus the deterministic-header assertions used by both the fixture
    and the real-package export tests.
*/
import assert from "node:assert";
import zlib from "node:zlib";

const TAR_HEADER_FIELDS = {
  name: [0, 100],
  mode: [100, 8],
  uid: [108, 8],
  gid: [116, 8],
  size: [124, 12],
  mtime: [136, 12],
  chksum: [148, 8],
  typeflag: [156, 1],
  magic: [257, 6],
  version: [263, 2]
};

export function* untar(tarBuffer) {
  let offset = 0;
  while (offset + TAR_HEADER_FIELDS.name[1] <= tarBuffer.length) {
    const block = tarBuffer.subarray(offset, offset + 512);
    if (block.every((byte) => byte === 0)) {
      return;
    }
    const field = (name) => {
      const [start, width] = TAR_HEADER_FIELDS[name];
      return block.subarray(start, start + width);
    };
    const checksum = parseInt(field("chksum").toString("utf8").trim(), 8);
    const computed = block.subarray(0, 148).reduce((sum, byte) => sum + byte, 0) +
      8 * 32 +
      block.subarray(156).reduce((sum, byte) => sum + byte, 0);
    const size = parseInt(field("size").toString("utf8").replace(/\0.*$/, "").trim() || "0", 8);
    const name = field("name").toString("utf8").replace(/\0.*$/, "");
    yield {
      name,
      typeflag: field("typeflag").toString("utf8"),
      mode: field("mode").toString("utf8").replace(/\0.*$/, ""),
      uid: field("uid").toString("utf8").replace(/\0.*$/, ""),
      gid: field("gid").toString("utf8").replace(/\0.*$/, ""),
      mtime: field("mtime").toString("utf8").replace(/\0.*$/, ""),
      magic: field("magic").toString("utf8").replace(/\0.*$/, ""),
      checksumMatches: checksum === computed,
      content: tarBuffer.subarray(offset + 512, offset + 512 + size)
    };
    offset += 512 + Math.ceil(size / 512) * 512;
  }
}

export function readArtifact(buffer) {
  assert.strictEqual(buffer[4] | buffer[5] | buffer[6] | buffer[7], 0, "gzip MTIME must be 0");
  assert.strictEqual(buffer[9], 0xff, "gzip OS byte must be normalized to 0xff");
  const tar = zlib.gunzipSync(buffer);
  assert.strictEqual(tar.length % (20 * 512), 0, "tar must be padded to a full record");
  const entries = [...untar(tar)];
  assert.ok(entries.length > 0);
  assert.ok(entries.every((entry) => entry.checksumMatches), "every tar header checksum must validate");
  assert.ok(entries.every((entry) => entry.magic === "ustar"), "entries must carry the ustar magic");
  assert.ok(entries.every((entry) => entry.uid === "0000000" && entry.gid === "0000000"), "uid/gid must be 0");
  assert.ok(entries.every((entry) => entry.mtime === "00000000000"), "mtime must be the epoch");
  return entries;
}

export function entryMap(entries) {
  return new Map(entries.map((entry) => [entry.name, entry]));
}
