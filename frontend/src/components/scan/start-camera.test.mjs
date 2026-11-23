// Run with `npm test`. startCameraScan with a fake camera and video element:
// the order of requests, and the race that blanked the preview when React ran
// the scan effect twice in development.
import { test } from "node:test";
import assert from "node:assert/strict";
import { cameraConstraintLadder } from "./camera-errors.js";
import { startCameraScan } from "./barcode.js";

globalThis.window ??= globalThis;
console.debug = () => {};

function fakeStream() {
  const track = { stopped: false, stop() { this.stopped = true; }, getCapabilities: () => ({}) };
  return { track, getTracks: () => [track], getVideoTracks: () => [track] };
}

function fakeVideo() {
  return { srcObject: null, videoWidth: 0, clientWidth: 0, clientHeight: 0, play: async () => {} };
}

const detector = async () => ({ detect: async () => [] });

/** A getUserMedia whose answers the test hands out one by one. */
function manualCamera() {
  const pending = [];
  return {
    mediaDevices: { getUserMedia: () => new Promise((resolve, reject) => pending.push({ resolve, reject })) },
    pending,
  };
}

const flush = () => new Promise((resolve) => setImmediate(resolve));

for (const order of ["first request answers first", "second request answers first"]) {
  test(`a cancelled start never blanks the preview of the newer one (${order})`, async () => {
    const video = fakeVideo();
    const camera = manualCamera();
    let cancelled1 = false;

    // Effect run 1, then its cleanup, then effect run 2: what StrictMode does.
    const first = startCameraScan(video, () => {}, { isCancelled: () => cancelled1, mediaDevices: camera.mediaDevices, makeDetector: detector });
    cancelled1 = true;
    const second = startCameraScan(video, () => {}, { isCancelled: () => false, mediaDevices: camera.mediaDevices, makeDetector: detector });
    await flush();
    assert.equal(camera.pending.length, 2);

    const stream1 = fakeStream();
    const stream2 = fakeStream();
    const answers = order.startsWith("first")
      ? [[0, stream1], [1, stream2]]
      : [[1, stream2], [0, stream1]];
    for (const [index, stream] of answers) {
      camera.pending[index].resolve(stream);
      await flush();
    }

    assert.equal(await first, null);
    const controls = await second;
    assert.ok(controls);
    assert.equal(video.srcObject, stream2, "the video still shows the newer stream");
    assert.equal(stream1.track.stopped, true, "the cancelled stream is released");
    assert.equal(stream2.track.stopped, false, "the live stream keeps running");

    controls.stop();
    assert.equal(stream2.track.stopped, true);
    assert.equal(video.srcObject, null);
  });
}

test("stopping an old scan does not clear a stream another scan put in the video", async () => {
  const video = fakeVideo();
  const stream1 = fakeStream();
  const stream2 = fakeStream();
  const media = (stream) => ({ getUserMedia: async () => stream });

  const old = await startCameraScan(video, () => {}, { mediaDevices: media(stream1), makeDetector: detector });
  const current = await startCameraScan(video, () => {}, { mediaDevices: media(stream2), makeDetector: detector });
  old.stop();

  assert.equal(video.srcObject, stream2);
  assert.equal(stream2.track.stopped, false);
  current.stop();
});

test("the requests are tried in ladder order until one works", async () => {
  const seen = [];
  const stream = fakeStream();
  const mediaDevices = {
    getUserMedia: async (constraints) => {
      seen.push(constraints);
      if (seen.length === 1) throw Object.assign(new Error("x"), { name: "OverconstrainedError" });
      if (seen.length === 2) throw Object.assign(new Error("x"), { name: "AbortError" });
      return stream;
    },
  };

  const controls = await startCameraScan(fakeVideo(), () => {}, { mediaDevices, makeDetector: detector });

  assert.deepEqual(seen, cameraConstraintLadder());
  controls.stop();
});

test("a refused permission stops the ladder at once and keeps the error name", async () => {
  let calls = 0;
  const mediaDevices = {
    getUserMedia: async () => {
      calls += 1;
      throw Object.assign(new Error("x"), { name: "NotAllowedError" });
    },
  };

  await assert.rejects(startCameraScan(fakeVideo(), () => {}, { mediaDevices, makeDetector: detector }), { name: "NotAllowedError" });
  assert.equal(calls, 1);
});

test("when every request fails the last error is reported", async () => {
  let calls = 0;
  const mediaDevices = {
    getUserMedia: async () => {
      calls += 1;
      throw Object.assign(new Error("x"), { name: calls === 3 ? "NotReadableError" : "OverconstrainedError" });
    },
  };

  await assert.rejects(startCameraScan(fakeVideo(), () => {}, { mediaDevices, makeDetector: detector }), { name: "NotReadableError" });
  assert.equal(calls, 3);
});

test("a reader that cannot load releases the camera and says so", async () => {
  const video = fakeVideo();
  const stream = fakeStream();
  const mediaDevices = { getUserMedia: async () => stream };

  await assert.rejects(
    startCameraScan(video, () => {}, { mediaDevices, makeDetector: async () => { throw new Error("wasm"); } }),
    { name: "ReaderLoadError" }
  );
  assert.equal(stream.track.stopped, true);
  assert.equal(video.srcObject, null);
});

test("the stream is attached and played before the reader has loaded", async () => {
  const video = fakeVideo();
  const stream = fakeStream();
  let releaseReader;
  const slowReader = () => new Promise((resolve) => { releaseReader = () => resolve({ detect: async () => [] }); });

  const starting = startCameraScan(video, () => {}, { mediaDevices: { getUserMedia: async () => stream }, makeDetector: slowReader });
  await flush();
  assert.equal(video.srcObject, stream, "the picture is on screen while the reader loads");

  releaseReader();
  const controls = await starting;
  controls.stop();
});
