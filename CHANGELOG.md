# Changelog

Main milestones, taken from the merged pull requests (newest first). Dates are
merge dates. No release tags exist yet; see the end of this file.

## 2026-11-09 and 2026-11-08: Deployment and onboarding

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
`v0.2.0-platform` (#41 to #54) and `v1.0.0-deploy` (#55).
