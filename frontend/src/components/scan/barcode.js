// Barcode reading for the SIM scanner. The reader is the browser's own
// BarcodeDetector when it supports every format below, and the
// "barcode-detector" package (zxing-cpp compiled to WASM) otherwise. The
// package is imported only when the scan dialog opens, and its WASM file is
// served from /wasm (copied by scripts/copy-wasm.mjs), never from a CDN.

/** The 1D codes SIM cards use. */
export const BARCODE_FORMATS = ["code_128", "code_39", "code_93", "itf", "ean_13", "ean_8", "codabar"];

// A frame is looked at about every 100 ms. A frame still being decoded makes
// the next tick skip, so decoding never queues up behind itself.
const FRAME_INTERVAL_MS = 100;
// After a tap to focus, go back to continuous focus.
const FOCUS_HOLD_MS = 2000;

/** A detector limited to BARCODE_FORMATS: native when it can do them all. */
export async function createDetector() {
  const Native = typeof globalThis.BarcodeDetector === "function" ? globalThis.BarcodeDetector : null;

  if (Native) {
    try {
      const supported = await Native.getSupportedFormats();
      if (BARCODE_FORMATS.every((format) => supported.includes(format))) {
        return new Native({ formats: BARCODE_FORMATS });
      }
    } catch {
      // Fall through to the package.
    }
  }

  const { BarcodeDetector, setZXingModuleOverrides } = await import("barcode-detector/ponyfill");
  setZXingModuleOverrides({
    locateFile: (path, prefix) => (path.endsWith(".wasm") ? `/wasm/${path}` : prefix + path),
  });
  return new BarcodeDetector({ formats: BARCODE_FORMATS });
}

/** Stops every track of the stream a video element is showing. */
export function stopVideo(video) {
  const stream = video?.srcObject;
  if (stream && typeof stream.getTracks === "function") {
    for (const track of stream.getTracks()) track.stop();
  }
  if (video) video.srcObject = null;
}

function capabilitiesOf(track) {
  try {
    return track.getCapabilities?.() ?? {};
  } catch {
    return {};
  }
}

/**
 * Starts the rear camera at the best size it gives (ideal 1920x1080, no
 * downscaling) and calls `onText` with the text of every barcode seen.
 * Resolves, once the picture is running, with controls:
 *   stop()            stops the loop and every camera track
 *   pause(), resume() stop and restart looking at frames (the camera keeps running)
 *   torch             null, or { set(on) } when the camera has a torch
 *   focusAt(x, y)     null, or a function taking a point in 0..1 of the picture
 * @param {HTMLVideoElement} video
 * @param {(text: string) => void} onText
 */
export async function startCameraScan(video, onText) {
  const stream = await navigator.mediaDevices.getUserMedia({
    audio: false,
    video: {
      facingMode: { ideal: "environment" },
      width: { ideal: 1920 },
      height: { ideal: 1080 },
      advanced: [{ focusMode: "continuous" }],
    },
  });

  let detector;
  try {
    video.srcObject = stream;
    // The picture shows while the reader loads: no waiting state for it.
    const playing = video.play().catch(() => {});
    [detector] = await Promise.all([createDetector(), playing]);
  } catch (error) {
    for (const track of stream.getTracks()) track.stop();
    video.srcObject = null;
    throw error;
  }

  const track = stream.getVideoTracks()[0];
  const capabilities = track ? capabilitiesOf(track) : {};
  const focusModes = capabilities.focusMode ?? [];

  if (track && focusModes.includes("continuous")) {
    track.applyConstraints({ advanced: [{ focusMode: "continuous" }] }).catch(() => {});
  }

  let stopped = false;
  let paused = false;
  let busy = false;
  let timer = 0;
  let focusTimer = 0;

  const tick = async () => {
    if (stopped) return;
    timer = window.setTimeout(tick, FRAME_INTERVAL_MS);
    if (busy || paused || !video.videoWidth) return;

    busy = true;
    const started = performance.now();
    try {
      const found = await detector.detect(video);
      if (!stopped && !paused) {
        for (const code of found) if (code.rawValue) onText(code.rawValue);
        // Dev only, and never the value: how long the frame with a code took.
        if (found.length && process.env.NODE_ENV !== "production") {
          console.debug(`scan: frame with a barcode decoded in ${Math.round(performance.now() - started)} ms`);
        }
      }
    } catch {
      // A frame that cannot be read is skipped; the next one is tried.
    } finally {
      busy = false;
    }
  };
  timer = window.setTimeout(tick, FRAME_INTERVAL_MS);

  const canTapFocus = Boolean(track) && (focusModes.includes("single-shot") || focusModes.includes("manual"));

  return {
    stop() {
      stopped = true;
      window.clearTimeout(timer);
      window.clearTimeout(focusTimer);
      for (const each of stream.getTracks()) each.stop();
      video.srcObject = null;
    },
    pause() {
      paused = true;
    },
    resume() {
      paused = false;
    },
    torch: track && capabilities.torch ? { set: (on) => track.applyConstraints({ advanced: [{ torch: on }] }) } : null,
    focusAt: canTapFocus
      ? (x, y) => {
          const mode = focusModes.includes("single-shot") ? "single-shot" : "manual";
          const constraint = { focusMode: mode };
          if (capabilities.pointsOfInterest) constraint.pointsOfInterest = [{ x, y }];
          track.applyConstraints({ advanced: [constraint] }).catch(() => {});
          window.clearTimeout(focusTimer);
          if (focusModes.includes("continuous")) {
            focusTimer = window.setTimeout(() => {
              track.applyConstraints({ advanced: [{ focusMode: "continuous" }] }).catch(() => {});
            }, FOCUS_HOLD_MS);
          }
        }
      : null,
  };
}

/**
 * Reads the barcodes of a still image. Resolves with their texts (an empty
 * list when the image holds none).
 * @param {Blob} file
 * @returns {Promise<string[]>}
 */
export async function decodeBarcodeFromFile(file) {
  const detector = await createDetector();
  const bitmap = await createImageBitmap(file);

  try {
    const found = await detector.detect(bitmap);
    return found.map((code) => code.rawValue).filter(Boolean);
  } finally {
    bitmap.close();
  }
}

/** Why the camera did not start: "blocked", "missing", "insecure" or "failed". */
export function cameraProblem(error) {
  if (typeof window !== "undefined" && !window.isSecureContext) return "insecure";
  if (!navigator.mediaDevices?.getUserMedia) return "insecure";
  if (error?.name === "NotAllowedError" || error?.name === "SecurityError") return "blocked";
  if (error?.name === "NotFoundError" || error?.name === "OverconstrainedError") return "missing";
  return "failed";
}
