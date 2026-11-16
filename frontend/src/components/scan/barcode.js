// Barcode reading for the SIM scanner. The libraries are imported only when
// the scan dialog opens (dynamic import), so no page pays for them until then.

/** A reader limited to the 1D codes SIM cards use. */
export async function createBarcodeReader() {
  const [{ BrowserMultiFormatReader }, { BarcodeFormat, DecodeHintType }] = await Promise.all([
    import("@zxing/browser"),
    import("@zxing/library"),
  ]);

  const hints = new Map();
  hints.set(DecodeHintType.POSSIBLE_FORMATS, [
    BarcodeFormat.CODE_128,
    BarcodeFormat.CODE_39,
    BarcodeFormat.CODABAR,
    BarcodeFormat.ITF,
    BarcodeFormat.EAN_13,
  ]);
  hints.set(DecodeHintType.TRY_HARDER, true);

  return new BrowserMultiFormatReader(hints, { delayBetweenScanAttempts: 150 });
}

/** Stops every track of the stream a video element is showing. */
export function stopVideo(video) {
  const stream = video?.srcObject;
  if (stream && typeof stream.getTracks === "function") {
    for (const track of stream.getTracks()) track.stop();
  }
  if (video) video.srcObject = null;
}

/**
 * Starts the camera (rear camera when there is one) and calls `onText` with
 * the text of every barcode seen. Returns a function that stops everything.
 * @param {HTMLVideoElement} video
 * @param {(text: string) => void} onText
 */
export async function startCameraScan(video, onText) {
  const reader = await createBarcodeReader();
  const controls = await reader.decodeFromConstraints(
    { audio: false, video: { facingMode: { ideal: "environment" }, width: { ideal: 1280 } } },
    video,
    (result) => {
      if (result) onText(result.getText());
    }
  );

  return () => {
    controls.stop();
    stopVideo(video);
  };
}

/**
 * Decodes a still image. Resolves with the barcode text, or null when the
 * image holds no barcode.
 * @param {Blob} file
 */
export async function decodeBarcodeFromFile(file) {
  const reader = await createBarcodeReader();
  const url = URL.createObjectURL(file);

  try {
    const result = await reader.decodeFromImageUrl(url);
    return result.getText();
  } catch (error) {
    // zxing reports "nothing found" as NotFoundException (or Checksum/Format).
    const kind = error?.getKind?.() ?? error?.name ?? error?.constructor?.name;
    if (["NotFoundException", "ChecksumException", "FormatException"].includes(kind)) return null;
    throw error;
  } finally {
    URL.revokeObjectURL(url);
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
