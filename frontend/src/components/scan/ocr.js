// Printed-number fallback of the SIM scanner. tesseract.js runs in a Web
// Worker on this device and is imported only when the person asks for it. The
// picture is never sent anywhere; the library itself fetches its engine and
// English model from its default CDN the first time (see frontend/docs).

/**
 * Reads digits from a picture.
 * @param {HTMLCanvasElement | Blob} source
 * @param {(percent: number) => void} [onProgress]
 * @returns {Promise<string>} the recognised text (digits, spaces and dashes)
 */
export async function readPrintedText(source, onProgress) {
  const { createWorker } = await import("tesseract.js");

  const worker = await createWorker("eng", 1, {
    logger: (event) => {
      if (event.status === "recognizing text" && typeof event.progress === "number") {
        onProgress?.(Math.round(event.progress * 100));
      }
    },
  });

  try {
    await worker.setParameters({
      tessedit_char_whitelist: "0123456789 -",
      // One uniform block of text: the number printed on the card.
      tessedit_pageseg_mode: "6",
    });
    const { data } = await worker.recognize(source);
    return data.text ?? "";
  } finally {
    await worker.terminate();
  }
}

/** Copies the current video frame to a canvas, or null when it has no picture yet. */
export function frameFromVideo(video) {
  if (!video || !video.videoWidth) return null;
  const canvas = document.createElement("canvas");
  canvas.width = video.videoWidth;
  canvas.height = video.videoHeight;
  canvas.getContext("2d").drawImage(video, 0, 0);
  return canvas;
}
