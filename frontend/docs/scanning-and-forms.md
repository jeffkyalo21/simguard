# Scanning a SIM and the allocation form

## Scanning a SIM card

The SIM form (Add SIM) and the allocation form (New allocation) have a Scan
button. It opens one shared dialog that reads the ICCID printed on the SIM card
as a barcode.

- **What is read:** the barcode (Code 128 and similar 1D codes) on the card. If
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
- **Check:** the number must be digits only (spaces and dashes are ignored), 19
  or 20 digits, start with 89 and pass the Luhn check digit (`src/lib/iccid.js`).
  Otherwise the dialog says "That does not look like a SIM number, try again or
  type it."
- **Confirmation:** the scanned number only fills the field. Nothing saves by
  itself.
- **Printed number (OCR):** tesseract.js runs on the device, restricted to
  digits, and only results that pass the check are accepted. The library
  downloads its engine and English model from its default CDN the first time it
  is used (the picture itself never leaves the device). To serve them from the
  app instead, pass `workerPath`, `corePath` and `langPath` to `createWorker` in
  `src/components/scan/ocr.js`.
- **Loading:** `@zxing/browser` and `tesseract.js` are imported only when the
  dialog opens (and OCR is asked for).

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
Department is no longer required. The form validates `simId`, `employeeId` and
`assignedBy` only; "Handed over by" works as before.

"Add new employee" appears under the results, and when there are none, for the
Security Officer only (`employees.edit`). The dialog asks for name, email,
department and job role and calls `POST /employees`. Field errors (400) show
under the fields. A duplicate email (409) says so and offers "Use that employee".
The new employee is selected on success. The Auditor stays read only.
