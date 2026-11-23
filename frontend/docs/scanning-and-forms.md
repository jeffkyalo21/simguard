# Scanning a SIM and the allocation form

## Scanning a SIM card

The SIM form (Add SIM) and the allocation form (New allocation) have a Scan
button. It opens one shared dialog that reads the ICCID printed on the SIM card
as a barcode.

- **What is read:** the barcode (Code 128, Code 39, Code 93, ITF, EAN-13,
  EAN-8 and Codabar) on the card. If
  there is none, the person can ask to read the printed number instead. Only
  the SIM number is read. Nothing else is scanned, and ID cards or badges are
  not supported.
- **Nothing is uploaded or saved.** The camera picture or the uploaded photo is
  decoded in the browser and discarded. No request carries it.
- **Camera permission:** the browser asks once. A blocked or missing camera
  shows a message and "Upload a photo instead" still works. Every camera track
  is stopped when the dialog closes.
- **HTTPS:** browsers only give camera access on HTTPS pages or on
  `localhost`. On plain HTTP the dialog says so and the upload path still works.
- **Engine:** the browser's own `BarcodeDetector` when it supports all of the
  formats above (Chrome on Android, for example). Otherwise the
  `barcode-detector` package, which runs zxing-cpp as WebAssembly. Safari and
  iPhone use the package. The `.wasm` file is served by this app from
  `/wasm/zxing_reader.wasm`, not from a CDN. `scripts/copy-wasm.mjs` copies it
  from `node_modules` into `public/wasm` after `npm install` and before
  `next build` (the folder is generated and git ignored), and
  `next.config.mjs` sets its `Content-Type` and cache headers. The Docker build
  runs the same script.
- **Viewfinder and guide frame:** the preview is a wide, short strip, 144 px
  tall and the full inner width of the dialog (it never sets a width of its
  own, so it cannot push the dialog wider; the dialog's one grid column is
  `minmax(0, 1fr)`). It holds a guide frame with a thin centre line, 12 px from
  every edge, under the text "Line up the barcode inside the frame". The video
  fills the strip (`object-fit: cover`).
- **Only the guide frame is read:** each frame is cropped to the guide frame
  before it goes to the detector, at the camera's real resolution (no
  downscaling). `src/components/scan/crop-region.js` maps the guide rectangle
  from viewfinder coordinates to camera frame coordinates, allowing for
  `object-fit: cover` cutting off the sides or the top and bottom. One canvas is
  reused for the crops. If nothing is found inside the frame for about 5
  seconds, the whole picture is checked once, then again every 5 seconds while
  nothing is found, so a barcode held off centre is still read. Only a matched
  ICCID is kept.
- **Speed:** a camera frame is checked about every 100 ms at the size the
  camera gives (ideal 1920x1080, no downscaling). A frame still being decoded
  makes the next tick skip, so work never queues up, and nothing is shown as
  loading while it looks. The first valid ICCID is accepted at once with a short
  beep (Web Audio) and a brief vibration where the device has one, and the
  dialog closes. In development the console shows how long the accepted frame
  took to decode, never the scanned value.
- **Opening the camera:** the scanner asks for the rear camera at 1920x1080
  with continuous focus, and if that fails with a constraint or device error it
  tries the rear camera alone, then any camera (`camera-errors.js`). A refusal
  or a missing camera stops the ladder at once. The messages are specific:
  blocked, no camera, camera in use by another app or tab, unsupported
  settings, barcode reader could not load, secure page needed. "Upload a photo
  instead" always stays. In development the console shows the error names and
  the request that worked, never anything scanned.
- **Starting twice:** React runs the scan effect twice in development. A start
  that was cancelled releases its own camera stream and never touches the video
  element, so it cannot blank the newer start's preview.
- **Camera:** the rear camera is asked for at 1920x1080 with continuous focus
  where the device supports it. Tapping the picture focuses where the device
  supports that. A light button shows only when the camera reports a torch.
- **Check:** the number must be digits only (spaces and dashes are ignored), 19
  or 20 digits, start with 89 and pass the Luhn check digit (`src/lib/iccid.js`).
  Printed text can hold the number in groups, for example
  `SERIAL NUMBER 89254 02115 42202 46430 ACTIVATE BY 18/04/2022`. Letters are
  never read as digits, so a number with an "O" in it is refused. Otherwise the dialog says "That does not look like a SIM number, try again or
  type it."
- **Confirmation:** the scanned number only fills the field. Nothing saves by
  itself.
- **Printed number (OCR):** only when the person presses "Read the printed
  number", never by itself. From the camera it reads the guide frame plus the
  12 px gap below it (the digits sit under the barcode), never the whole
  picture and never anything outside what the viewfinder shows. An uploaded
  photo is still read whole. The barcode reader pauses while it reads and carries
  on if no number is found. tesseract.js runs on the device, restricted to
  digits, and only results that pass the check are accepted. The recognised text
  is checked and dropped at once: it is not logged or stored, and only the
  matched ICCID is kept. The library
  downloads its engine and English model from its default CDN the first time it
  is used (the picture itself never leaves the device). To serve them from the
  app instead, pass `workerPath`, `corePath` and `langPath` to `createWorker` in
  `src/components/scan/ocr.js`.
- **Loading:** `barcode-detector` is imported only when the dialog opens and
  `tesseract.js` only when OCR is asked for.

### In the SIM form

A scanned ICCID fills the field. If a SIM already has it, the form says "This
SIM is already registered" with a link to that SIM (found with the existing SIM
list search).

### In the allocation form

The scanned ICCID is looked up in the SIM list:

| Result | What the form does |
| --- | --- |
| Registered and available | selects it in the SIM field |
| Registered and allocated | says so and shows who holds it |
| Registered but inactive | says it cannot be allocated |
| Not registered | offers "Register this SIM" (Security Officer), which opens the SIM form with the ICCID filled and selects the new SIM when saved |

## Employee search in the allocation form

The department and employee selects are replaced by one search field (name or
email, 2 letters or more, debounced). Results show name, department and job
role. The pick shows a read-only summary (name, email, department, job role).
Department is no longer required. The form validates `simId` and `employeeId`
only; "Handed over by (optional)" can be left empty and is then not sent.

"Add new employee" appears under the results, and when there are none, for the
Security Officer only (`employees.edit`). The dialog asks for name, email,
department and job role and calls `POST /employees`. Field errors (400) show
under the fields. A duplicate email (409) says so and offers "Use that employee".
The new employee is selected on success. The Auditor stays read only.
