"use client";

import { useEffect, useRef, useState } from "react";
import { ImageUp } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useReturnFocus } from "@/hooks/use-return-focus";
import { cameraProblem, decodeBarcodeFromFile, startCameraScan } from "@/components/scan/barcode";
import { iccidFromScan } from "@/lib/iccid";
import { copy } from "@/lib/copy";

const text = copy.scan;

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

  useEffect(() => {
    onResultRef.current = onResult;
  });

  useEffect(() => {
    let cancelled = false;
    let stop = null;

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
      },
      (error) => {
        if (!cancelled) setCamera(cameraProblem(error));
      }
    );

    return () => {
      cancelled = true;
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
    try {
      const barcode = await decodeBarcodeFromFile(file);
      if (barcode === null) return setMessage(text.noBarcode);
      const found = iccidFromScan(barcode);
      if (found.ok) onResultRef.current(found.iccid);
      else setMessage(text.invalid);
    } catch {
      setMessage(text.noBarcode);
    } finally {
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
        {message && <p role="alert" className="text-risk-flagged-foreground">{message}</p>}
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <input ref={fileRef} type="file" accept="image/*" className="sr-only" tabIndex={-1} aria-hidden="true" onChange={handleFile} />
        <Button type="button" variant="outline" className="h-10 px-4" disabled={busy} onClick={() => fileRef.current?.click()}>
          <ImageUp aria-hidden="true" />
          {text.upload}
        </Button>
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
