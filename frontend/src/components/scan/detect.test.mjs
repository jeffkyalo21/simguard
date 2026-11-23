// Run with `npm test`. Feeds a still Code 128 image of a made-up ICCID to the
// same detector the scanner uses when the browser has none of its own
// (barcode-detector, zxing-cpp WASM), then through the ICCID check. The image
// is made here by zxing-wasm's writer, so nothing is read from a camera.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { inflateSync } from "node:zlib";
import { BarcodeDetector, setZXingModuleOverrides } from "barcode-detector/ponyfill";
import { setZXingModuleOverrides as setWriterOverrides, writeBarcode } from "zxing-wasm/writer";
import { BARCODE_FORMATS } from "./barcode.js";
import { guideFrameRect } from "./crop-region.js";
import { iccidFromScan } from "../../lib/iccid.js";

const ICCID = "89254021154220246430";

setZXingModuleOverrides({ wasmBinary: readFileSync("public/wasm/zxing_reader.wasm") });
setWriterOverrides({ wasmBinary: readFileSync("node_modules/zxing-wasm/dist/writer/zxing_writer.wasm") });

// Node has no ImageData; the detector checks for the global, so give it a minimal one.
globalThis.ImageData ??= class ImageData {
  constructor(data, width, height) {
    Object.assign(this, { data, width, height, colorSpace: "srgb" });
  }
};

// The detector also builds a DOMRectReadOnly for each box it finds.
globalThis.DOMRectReadOnly ??= class DOMRectReadOnly {
  constructor(x, y, width, height) {
    Object.assign(this, { x, y, width, height });
  }
};

/** Decodes the 8-bit grayscale PNG the writer makes into an ImageData. */
function pngToImageData(bytes) {
  let width = 0;
  let height = 0;
  const parts = [];
  for (let at = 8; at < bytes.length; ) {
    const length = bytes.readUInt32BE(at);
    const type = bytes.toString("ascii", at + 4, at + 8);
    const body = bytes.subarray(at + 8, at + 8 + length);
    if (type === "IHDR") {
      width = body.readUInt32BE(0);
      height = body.readUInt32BE(4);
      assert.equal(body[8], 8, "8-bit");
      assert.equal(body[9], 0, "grayscale");
    } else if (type === "IDAT") parts.push(body);
    at += 12 + length;
  }

  const raw = inflateSync(Buffer.concat(parts));
  const gray = Buffer.alloc(width * height);
  for (let y = 0; y < height; y += 1) {
    const filter = raw[y * (width + 1)];
    for (let x = 0; x < width; x += 1) {
      const value = raw[y * (width + 1) + 1 + x];
      const left = x ? gray[y * width + x - 1] : 0;
      const up = y ? gray[(y - 1) * width + x] : 0;
      const upLeft = x && y ? gray[(y - 1) * width + x - 1] : 0;
      let predicted = 0;
      if (filter === 1) predicted = left;
      else if (filter === 2) predicted = up;
      else if (filter === 3) predicted = (left + up) >> 1;
      else if (filter === 4) {
        const p = left + up - upLeft;
        const [pa, pb, pc] = [Math.abs(p - left), Math.abs(p - up), Math.abs(p - upLeft)];
        predicted = pa <= pb && pa <= pc ? left : pb <= pc ? up : upLeft;
      }
      gray[y * width + x] = (value + predicted) & 255;
    }
  }

  const data = new Uint8ClampedArray(width * height * 4);
  for (let i = 0; i < gray.length; i += 1) data.set([gray[i], gray[i], gray[i], 255], i * 4);
  return new ImageData(data, width, height);
}

test("a still Code 128 image of an ICCID is read by the detector and passes the ICCID check", async () => {
  const written = await writeBarcode(ICCID, { format: "Code128", scale: 3, withQuietZones: true });
  const image = pngToImageData(Buffer.from(await written.image.arrayBuffer()));
  const detector = new BarcodeDetector({ formats: BARCODE_FORMATS });

  await detector.detect(image); // loads the WASM once, so the timing below is a steady frame
  const started = performance.now();
  const found = await detector.detect(image);
  const elapsed = performance.now() - started;
  console.log(`still image ${image.width}x${image.height}: detect took ${elapsed.toFixed(1)} ms`);

  assert.equal(found.length, 1);
  assert.equal(found[0].format, "code_128");
  assert.deepEqual(iccidFromScan(found[0].rawValue), { ok: true, iccid: ICCID });
});

test("the same barcode inside a full 1920x1080 frame is read, and the time per frame is printed", async () => {
  const written = await writeBarcode(ICCID, { format: "Code128", scale: 3, withQuietZones: true });
  const small = pngToImageData(Buffer.from(await written.image.arrayBuffer()));
  const frame = new ImageData(new Uint8ClampedArray(1920 * 1080 * 4).fill(235), 1920, 1080);
  const left = Math.round((1920 - small.width) / 2);
  const top = Math.round((1080 - small.height) / 2);
  for (let y = 0; y < small.height; y += 1) {
    frame.data.set(small.data.subarray(y * small.width * 4, (y + 1) * small.width * 4), ((top + y) * 1920 + left) * 4);
  }

  const detector = new BarcodeDetector({ formats: BARCODE_FORMATS });
  await detector.detect(frame);
  const times = [];
  let found = [];
  for (let run = 0; run < 5; run += 1) {
    const started = performance.now();
    found = await detector.detect(frame);
    times.push(performance.now() - started);
  }
  console.log(`full frame 1920x1080: detect took ${Math.min(...times).toFixed(1)} to ${Math.max(...times).toFixed(1)} ms over 5 runs`);

  assert.equal(found.length, 1);
  assert.deepEqual(iccidFromScan(found[0].rawValue), { ok: true, iccid: ICCID });
});

test("a barcode in the middle of a 1920x1080 frame is read from the guide frame crop alone", async () => {
  const written = await writeBarcode(ICCID, { format: "Code128", scale: 3, withQuietZones: true });
  const small = pngToImageData(Buffer.from(await written.image.arrayBuffer()));
  const frame = new ImageData(new Uint8ClampedArray(1920 * 1080 * 4).fill(235), 1920, 1080);
  const left = Math.round((1920 - small.width) / 2);
  const top = Math.round((1080 - small.height) / 2);
  for (let y = 0; y < small.height; y += 1) {
    frame.data.set(small.data.subarray(y * small.width * 4, (y + 1) * small.width * 4), ((top + y) * 1920 + left) * 4);
  }

  // A 400x144 viewfinder over this frame, as on a phone.
  const rect = guideFrameRect({ width: 400, height: 144 }, { width: 1920, height: 1080 });
  const cropped = new ImageData(new Uint8ClampedArray(rect.width * rect.height * 4), rect.width, rect.height);
  for (let y = 0; y < rect.height; y += 1) {
    const from = ((rect.y + y) * 1920 + rect.x) * 4;
    cropped.data.set(frame.data.subarray(from, from + rect.width * 4), y * rect.width * 4);
  }

  const detector = new BarcodeDetector({ formats: BARCODE_FORMATS });
  await detector.detect(cropped);
  const started = performance.now();
  const found = await detector.detect(cropped);
  console.log(`guide crop ${rect.width}x${rect.height} of the 1920x1080 frame: detect took ${(performance.now() - started).toFixed(1)} ms`);

  assert.equal(found.length, 1);
  assert.deepEqual(iccidFromScan(found[0].rawValue), { ok: true, iccid: ICCID });

  // Sanity check on the test itself: the barcode's rows lie inside the crop.
  assert.ok(top >= rect.y && top + small.height <= rect.y + rect.height);
});
