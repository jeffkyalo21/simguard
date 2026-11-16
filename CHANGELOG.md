# Changelog

Main milestones, taken from the merged pull requests (newest first). Dates are
merge dates. No release tags exist yet; see the end of this file.

## Unreleased: SIM scanning and employee search on allocations

- Scan a SIM card to fill its ICCID, in the SIM form and the allocation form.
  Barcode first (`@zxing/browser`), then an optional printed number reading
  (`tesseract.js`). Both run in the browser, nothing is uploaded or stored, and
  both load only when the scan dialog opens. The ICCID must be 19 or 20 digits,
  start with 89 and pass the Luhn check.
- The allocation form finds the employee by name or email, shows their details,
  and lets the Security Officer add a new employee on the spot. Department is no
  longer required there.
- New dependencies and licences: `@zxing/browser` 0.2.1 (MIT), `@zxing/library`
  0.23.0 (Apache-2.0), `tesseract.js` 7.0.0 (Apache-2.0). tesseract.js fetches its
  engine and English model from its default CDN on first use.
- Added code loaded on demand only: about 600 KB raw (about 170 KB gzip) across
  the scan chunks; the main bundles are unchanged. No backend or database change.

## Unreleased: Forgot password, landing page motion and dashboard alert counts

- Self-service "Forgot password" for every account, the Admin included.
  `POST /api/auth/forgot-password` answers the same for every email and sends
  the mail in the background; `POST /api/auth/reset-password` sets the password
  from an emailed link. The link token is 32 random bytes, only its SHA-256 hash
  is stored (new table `password_reset_requests`, migration
  `AddPasswordResetRequests`), it lasts 30 minutes, works once and is replaced by
  a newer request. A reset ends existing sessions, keeps MFA required and does not
  clear a lockout. Rate limited per IP and per email. New auth events
  `password_reset_requested`, `password_reset_completed`, `password_reset_failed`
  and Admin audit rows `auth.password_reset_*`. New pages `/forgot-password` and
  `/reset-password`; the login link is real. Brevo and `BREVO_SIGN_IN_URL` must be
  set per organisation stack for the email to go out. The server side emergency
  recovery stays as the fallback.

- Landing page motion uses GSAP 3.15.1 (with ScrollTrigger), added from npm and
  loaded only by the landing page through a dynamic import. Licence checked once:
  the package declares the "Standard 'no charge' license"
  (https://gsap.com/standard-license), which allows free use, including
  commercial use, with GSAP and its plugins at no charge. Its one restriction is
  not to use it to build a visual animation tool that competes with Webflow.
- The dashboard counts an alert in a 7, 30 or 90 day window when its first or its
  last detected day is inside the window, so a SIM that is still misbehaving
  shows for the current period.
- How detection works has four numbered steps; plan sizing (Prophet) replaces the
  similar cases card and the dashed note.

## 2026-11-09 and 2026-11-08: Deployment, several organisations and onboarding

- #56 Several organisations on one VM: one shared Caddy stack and one Compose
  project per organisation, scripts to create, list, adopt and remove an
  organisation, organisation data packs, backups and updates per organisation,
  and realistic Kenyan demo names in the data pack.
- #55 Production deployment: Docker Compose stack with Caddy, PostgreSQL,
  backup, restore and update scripts, read-only ML database role, emergency
  Admin recovery, carrier mock for the demo, fresh VM procedure.
- #54 Admin dashboard and onboarding.
- #53 Backend, frontend and ML service connected end to end.

## 2026-11-03: Ingestion, alerts and the full set of screens

- #52 Usage ingestion and carriers API.
- #50 Audit log, allocation and lifecycle screens.
- #49 Usage analytics, alerts and investigation screens.
- #48 Alerts, risk score and audit log endpoints.
- #47 API authentication and authorization (Identity, TOTP MFA, JWT, three roles).

## 2026-11-02: Ensemble scoring and first screens

- #46 Models validated against synthetic anomalies.
- #45 Ensemble scoring pipeline and FastAPI service.
- #44 Login, dashboard, SIM management and SIM profile screens.

## 2026-10-22 to 2026-10-29: Backend and frontend foundations

- #43 Next.js app shell, routes and API client.
- #42 SIM, employee, department and allocation endpoints.
- #41 ASP.NET Core API scaffold with PostgreSQL and EF Core.

## 2026-09-28 to 2026-10-07: Data and models

- #40 Stage 3 XGBoost triage gate and SHAP reasons.
- #39 Prophet forecasting for tier recommendations.
- #38 Cohort z-score adjustment, second version datasets, evaluation artifacts.
- #37 Isolation Forest anomaly detection.
- #35, #36 Feature extraction, datasets and preprocessing artifacts.
- #34 Issue to branch automation (`feat/<number>-<title>`).
- #33 Synthetic call data record generator.

## Tags

No tags have been created. Suggested: `v0.1.0-models` (#33 to #40),
`v0.2.0-platform` (#41 to #54), `v0.3.0-deployment` (#55) and
`v0.4.0-multi-org` (#56).
