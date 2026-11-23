"use client";

import { useEffect, useRef, useState } from "react";
import { Flashlight, ImageUp, TextCursorInput } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useReturnFocus } from "@/hooks/use-return-focus";
import { cameraProblem, decodeBarcodeFromFile, startCameraScan } from "@/components/scan/barcode";
import { prepareFeedback, scanFeedback } from "@/components/scan/feedback";
import { GUIDE_MARGIN_PX } from "@/components/scan/crop-region";
import { readPrintedText } from "@/components/scan/ocr";
import { iccidFromScan } from "@/lib/iccid";
import { copy } from "@/lib/copy";

const text = copy.scan;

// No barcode after this long: offer to read the printed number.
const OCR_OFFER_MS = 6000;

const PROBLEM_TEXT = {
  blocked: text.cameraBlocked,
  missing: text.cameraMissing,
  insecure: text.cameraInsecure,
  failed: text.cameraFailed,
};

/**
 * The camera view and the upload option. Mounted only while the dialog is
 * open, so closing it (or leaving the page) stops the camera at once.
 */
function ScanPanel({ onResult }) {
  const videoRef = useRef(null);
  const fileRef = useRef(null);
  const onResultRef = useRef(onResult);
  const [camera, setCamera] = useState("starting");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [offerOcr, setOfferOcr] = useState(false);
  const [progress, setProgress] = useState(null);
  // What the running camera offers: { torch, focusAt }, both null when absent.
  const controlsRef = useRef(null);
  const [hasTorch, setHasTorch] = useState(false);
  const [torchOn, setTorchOn] = useState(false);
  // The uploaded photo, when the number is read from it instead of the camera.
  const photoRef = useRef(null);

  useEffect(() => {
    onResultRef.current = onResult;
  });

  useEffect(() => {
    let cancelled = false;
    let controls = null;
    let timer = 0;
    prepareFeedback();

    startCameraScan(videoRef.current, (barcode) => {
      if (cancelled) return;
      const found = iccidFromScan(barcode);
      if (found.ok) {
        // Accepted at once: beep, vibrate, release the camera, hand it on.
        cancelled = true;
        scanFeedback();
        controls?.stop();
        onResultRef.current(found.iccid);
      } else {
        // Keep looking: a stray barcode in view must not end the scan.
        setMessage(text.invalid);
      }
    }).then(
      (started) => {
        if (cancelled) return started.stop();
        controls = started;
        controlsRef.current = started;
        setHasTorch(Boolean(started.torch));
        setCamera("scanning");
        timer = window.setTimeout(() => setOfferOcr(true), OCR_OFFER_MS);
      },
      (error) => {
        if (!cancelled) setCamera(cameraProblem(error));
      }
    );

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
      controlsRef.current = null;
      controls?.stop();
    };
  }, []);

  async function handleFile(event) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    if (!file.type.startsWith("image/")) return setMessage(text.notAnImage);

    setBusy(true);
    setMessage("");
    photoRef.current = file;
    setOfferOcr(false);
    try {
      const barcodes = await decodeBarcodeFromFile(file);
      if (barcodes.length === 0) {
        setOfferOcr(true);
        return setMessage(text.noBarcode);
      }
      const found = barcodes.map(iccidFromScan).find((each) => each.ok);
      if (found) {
        scanFeedback();
        onResultRef.current(found.iccid);
      } else setMessage(text.invalid);
    } catch {
      setOfferOcr(true);
      setMessage(text.noBarcode);
    } finally {
      setBusy(false);
    }
  }

  async function handleReadPrinted() {
    // A photo is read whole; the camera only inside the guide frame and a margin below it.
    const source = photoRef.current ?? controlsRef.current?.printedNumberArea();
    if (!source) return setMessage(text.noNumber);

    setBusy(true);
    setMessage("");
    setProgress(0);
    // Never in parallel with the barcode reader: it waits while the printed
    // number is read, then carries on.
    controlsRef.current?.pause();
    let accepted = false;
    try {
      // The recognised text is checked and dropped; only a matched ICCID is kept.
      const found = iccidFromScan(await readPrintedText(source, setProgress));
      if (found.ok) {
        accepted = true;
        scanFeedback();
        controlsRef.current?.stop();
        onResultRef.current(found.iccid);
      } else setMessage(text.noNumber);
    } catch {
      setMessage(text.noNumber);
    } finally {
      if (!accepted) controlsRef.current?.resume();
      setProgress(null);
      setBusy(false);
    }
  }

  async function handleTorch() {
    const next = !torchOn;
    try {
      await controlsRef.current?.torch?.set(next);
      setTorchOn(next);
    } catch {
      setHasTorch(false);
    }
  }

  // Tap to focus, where the camera can: the point is relative to the picture.
  function handleTapFocus(event) {
    const focusAt = controlsRef.current?.focusAt;
    if (!focusAt) return;
    const box = event.currentTarget.getBoundingClientRect();
    focusAt((event.clientX - box.left) / box.width, (event.clientY - box.top) / box.height);
  }

  const problem = PROBLEM_TEXT[camera];
  const status = problem ?? (camera === "starting" ? text.starting : text.scanning);

  return (
    <div className="flex min-w-0 flex-col gap-4">
      <p className="text-sm text-muted-foreground">{text.intro}</p>
      <div className="relative h-36 w-full max-w-full min-w-0 overflow-hidden rounded-xl border border-glass-border bg-muted">
        <video ref={videoRef} aria-label={text.video} className="size-full object-cover" muted playsInline autoPlay onClick={handleTapFocus} />
        {/* The guide frame: the barcode is looked for only inside it. */}
        <div
          aria-hidden="true"
          className="pointer-events-none absolute rounded-md border-2 border-white shadow-[0_0_0_9999px_rgba(0,0,0,0.35)]"
          style={{ inset: GUIDE_MARGIN_PX }}
        >
          <div className="absolute inset-x-2 top-1/2 h-px bg-white/80" />
        </div>
      </div>
      <div className="flex min-h-10 flex-col gap-1 text-sm" aria-live="polite">
        <p className={problem ? "text-risk-flagged-foreground" : "text-muted-foreground"}>{status}</p>
        {progress !== null && <p className="text-muted-foreground">{text.reading} {progress}%</p>}
        {message && <p role="alert" className="text-risk-flagged-foreground">{message}</p>}
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <input ref={fileRef} type="file" accept="image/*" className="sr-only" tabIndex={-1} aria-hidden="true" onChange={handleFile} />
        <Button type="button" variant="outline" className="h-10 px-4" disabled={busy} onClick={() => fileRef.current?.click()}>
          <ImageUp aria-hidden="true" />
          {text.upload}
        </Button>
        {hasTorch && (
          <Button type="button" variant="outline" className="h-10 px-4" aria-pressed={torchOn} onClick={handleTorch}>
            <Flashlight aria-hidden="true" />
            {torchOn ? text.torchOff : text.torchOn}
          </Button>
        )}
        {offerOcr && (
          <Button type="button" variant="outline" className="h-10 px-4" disabled={busy} onClick={handleReadPrinted}>
            <TextCursorInput aria-hidden="true" />
            {text.readPrinted}
          </Button>
        )}
      </div>
      <p className="text-xs text-muted-foreground">{text.privacy}</p>
    </div>
  );
}

/**
 * Reads a SIM's barcode with the camera or from an uploaded photo, in the
 * browser only: the picture is never uploaded or saved. `onResult` gets an
 * ICCID that passed the check (src/lib/iccid.js); the caller shows it for
 * the person to confirm.
 * @param {{ open: boolean, onOpenChange: (open: boolean) => void,
 *   onResult: (iccid: string) => void }} props
 */
export function ScanSimDialog({ open, onOpenChange, onResult }) {
  const returnFocus = useReturnFocus(open);

  function handleResult(iccid) {
    onOpenChange(false);
    onResult(iccid);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="grid-cols-[minmax(0,1fr)] sm:max-w-md" aria-describedby={undefined} onCloseAutoFocus={returnFocus}>
        <DialogHeader>
          <DialogTitle className="text-lg font-semibold">{text.title}</DialogTitle>
        </DialogHeader>
        {open && <ScanPanel onResult={handleResult} />}
        <DialogFooter className="mx-0 mb-0 rounded-none border-0 bg-transparent p-0">
          <Button type="button" variant="outline" className="h-10 px-4" onClick={() => onOpenChange(false)}>
            {text.close}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
