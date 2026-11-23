// Barcode reading for the SIM scanner. The reader is the browser's own
// BarcodeDetector when it supports every format below, and the
// "barcode-detector" package (zxing-cpp compiled to WASM) otherwise. The
// package is imported only when the scan dialog opens, and its WASM file is
// served from /wasm (copied by scripts/copy-wasm.mjs), never from a CDN.

import { cameraConstraintLadder, cameraErrorKind, tryNextConstraint } from "@/components/scan/camera-errors";
import { guideFrameRect, printedNumberRect } from "@/components/scan/crop-region";

/** The 1D codes SIM cards use. */
export const BARCODE_FORMATS = ["code_128", "code_39", "code_93", "itf", "ean_13", "ean_8", "codabar"];

// A frame is looked at about every 100 ms. A frame still being decoded makes
// the next tick skip, so decoding never queues up behind itself.
const FRAME_INTERVAL_MS = 100;
// Nothing found inside the guide frame for this long: look at the whole
// picture once, so a barcode held off centre is still read.
const FALLBACK_AFTER_MS = 5000;
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

const isDev = () => typeof process !== "undefined" && process.env.NODE_ENV !== "production";

/**
 * Opens the camera with the first request in the ladder that works, giving up
 * wishes after an error that a simpler request can get past. In development
 * the console shows the error names and the request that finally worked
 * (never anything that was scanned).
 */
export async function openCamera(mediaDevices) {
  if (!mediaDevices?.getUserMedia) throw Object.assign(new Error("No camera access."), { name: "TypeError" });

  let last;
  for (const constraints of cameraConstraintLadder()) {
    try {
      const stream = await mediaDevices.getUserMedia(constraints);
      if (isDev()) console.debug("scan: camera opened with", JSON.stringify(constraints));
      return stream;
    } catch (error) {
      last = error;
      if (isDev()) console.debug("scan: camera request failed:", error?.name);
      if (!tryNextConstraint(error)) break;
    }
  }
  throw last;
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
 * downscaling) and calls `onText` with the text of every barcode seen inside
 * the guide frame (the whole picture when nothing is found there for a while).
 * Resolves, once the picture is running, with controls:
 *   stop()            stops the loop and every camera track
 *   printedNumberArea()  a canvas of the guide frame plus a small margin below
 *                     it (never the whole picture), or null before the first frame
 *   pause(), resume() stop and restart looking at frames (the camera keeps running)
 *   torch             null, or { set(on) } when the camera has a torch
 *   focusAt(x, y)     null, or a function taking a point in 0..1 of the picture
 * `isCancelled` says the caller no longer wants the camera (the dialog closed,
 * or React ran the effect twice in development). A cancelled start releases
 * its own stream and never touches the video element, which a newer start may
 * already be using. Then it resolves with null.
 * @param {HTMLVideoElement} video
 * @param {(text: string) => void} onText
 * @param {{ isCancelled?: () => boolean, mediaDevices?: MediaDevices,
 *   makeDetector?: () => Promise<{ detect: (source: any) => Promise<any[]> }> }} [options]
 */
export async function startCameraScan(video, onText, options = {}) {
  const { isCancelled = () => false, mediaDevices = navigator.mediaDevices, makeDetector = createDetector } = options;
  const stream = await openCamera(mediaDevices);
  const release = () => {
    for (const track of stream.getTracks()) track.stop();
    // Only clear the element when it still shows this stream.
    if (video.srcObject === stream) video.srcObject = null;
  };

  if (isCancelled()) {
    release();
    return null;
  }

  let detector;
  try {
    video.srcObject = stream;
    // The picture shows while the reader loads: no waiting state for it.
    const playing = Promise.resolve(video.play()).catch(() => {});
    const reader = makeDetector().catch((cause) => {
      throw Object.assign(new Error("The barcode reader could not load.", { cause }), { name: "ReaderLoadError" });
    });
    [detector] = await Promise.all([reader, playing]);
  } catch (error) {
    release();
    throw error;
  }

  if (isCancelled()) {
    release();
    return null;
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
  let quietSince = performance.now();

  // One canvas for the cropped frames, resized only when the crop size changes.
  let cropCanvas = null;
  let cropContext = null;
  function crop(rect) {
    if (!cropCanvas) {
      cropCanvas = document.createElement("canvas");
      cropContext = cropCanvas.getContext("2d", { willReadFrequently: true });
    }
    if (cropCanvas.width !== rect.width) cropCanvas.width = rect.width;
    if (cropCanvas.height !== rect.height) cropCanvas.height = rect.height;
    cropContext.drawImage(video, rect.x, rect.y, rect.width, rect.height, 0, 0, rect.width, rect.height);
    return cropCanvas;
  }

  const sizes = () => ({
    view: { width: video.clientWidth, height: video.clientHeight },
    frame: { width: video.videoWidth, height: video.videoHeight },
  });

  const tick = async () => {
    if (stopped) return;
    timer = window.setTimeout(tick, FRAME_INTERVAL_MS);
    if (busy || paused || !video.videoWidth) return;

    busy = true;
    const started = performance.now();
    try {
      const { view, frame } = sizes();
      const fallback = started - quietSince >= FALLBACK_AFTER_MS || !view.width || !view.height;
      const source = fallback ? video : crop(guideFrameRect(view, frame));
      const found = await detector.detect(source);
      if (fallback) quietSince = started;
      else if (found.length) quietSince = started;
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
      release();
    },
    pause() {
      paused = true;
    },
    resume() {
      paused = false;
    },
    printedNumberArea() {
      const { view, frame } = sizes();
      if (!frame.width || !view.width || !view.height) return null;
      const rect = printedNumberRect(view, frame);
      const canvas = document.createElement("canvas");
      canvas.width = rect.width;
      canvas.height = rect.height;
      canvas.getContext("2d").drawImage(video, rect.x, rect.y, rect.width, rect.height, 0, 0, rect.width, rect.height);
      return canvas;
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

/** Why the camera did not start: a key of the scan messages (see camera-errors.js). */
export function cameraProblem(error) {
  return cameraErrorKind(error, {
    secure: typeof window === "undefined" || window.isSecureContext,
    hasApi: Boolean(navigator.mediaDevices?.getUserMedia),
  });
}
