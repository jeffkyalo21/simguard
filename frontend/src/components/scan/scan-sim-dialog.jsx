"use client";

import { useEffect, useRef, useState } from "react";
import { ImageUp, TextCursorInput } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useReturnFocus } from "@/hooks/use-return-focus";
import { cameraProblem, decodeBarcodeFromFile, startCameraScan } from "@/components/scan/barcode";
import { frameFromVideo, readPrintedText } from "@/components/scan/ocr";
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
  // The uploaded photo, when the number is read from it instead of the camera.
  const photoRef = useRef(null);

  useEffect(() => {
    onResultRef.current = onResult;
  });

  useEffect(() => {
    let cancelled = false;
    let stop = null;
    let timer = 0;

    startCameraScan(videoRef.current, (barcode) => {
      if (cancelled) return;
      const found = iccidFromScan(barcode);
      if (found.ok) {
        cancelled = true;
        stop?.();
        onResultRef.current(found.iccid);
      } else {
        // Keep looking: a stray barcode in view must not end the scan.
        setMessage(text.invalid);
      }
    }).then(
      (stopCamera) => {
        if (cancelled) return stopCamera();
        stop = stopCamera;
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
      stop?.();
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
      const barcode = await decodeBarcodeFromFile(file);
      if (barcode === null) {
        setOfferOcr(true);
        return setMessage(text.noBarcode);
      }
      const found = iccidFromScan(barcode);
      if (found.ok) onResultRef.current(found.iccid);
      else setMessage(text.invalid);
    } catch {
      setOfferOcr(true);
      setMessage(text.noBarcode);
    } finally {
      setBusy(false);
    }
  }

  async function handleReadPrinted() {
    const source = photoRef.current ?? frameFromVideo(videoRef.current);
    if (!source) return setMessage(text.noNumber);

    setBusy(true);
    setMessage("");
    setProgress(0);
    try {
      const printed = await readPrintedText(source, setProgress);
      const found = iccidFromScan(printed);
      if (found.ok) onResultRef.current(found.iccid);
      else setMessage(text.noNumber);
    } catch {
      setMessage(text.noNumber);
    } finally {
      setProgress(null);
      setBusy(false);
    }
  }

  const problem = PROBLEM_TEXT[camera];
  const status = problem ?? (camera === "starting" ? text.starting : text.scanning);

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{text.intro}</p>
      <div className="relative aspect-[4/3] w-full overflow-hidden rounded-xl border border-glass-border bg-muted">
        <video ref={videoRef} aria-label={text.video} className="size-full object-cover" muted playsInline autoPlay />
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
      <DialogContent className="sm:max-w-md" aria-describedby={undefined} onCloseAutoFocus={returnFocus}>
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
