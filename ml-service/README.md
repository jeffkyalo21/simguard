# ML Service - Python

## Scoring API (FastAPI)

The service lives in `app/` and wraps `src/risk_scoring.py` (Stages 1 to 3)
and `src/prophet_forecasting.py` (tier recommendation). Example requests and
responses are in `contracts/`.

### Install

From `ml-service/`:

```bash
python -m venv venv
venv/Scripts/python -m pip install -r requirements.txt
```

### Environment variables

| Variable | Default | Meaning |
| --- | --- | --- |
| `ML_ARTIFACT_DIR` | `ml-service/artifacts` | Folder with the model artifacts. The service will not start if one is missing. |
| `READER` | `parquet` | Where daily usage comes from. `parquet` reads `data/synthetic_cdr_daily_agg.csv`. `postgres` reads the backend view `ml_daily_usage` (load it with `scripts/load_daily_usage.py`). |
| `ML_DATABASE_URL` | none | Connection string for the postgres reader (libpq URI or key=value). Required when `READER=postgres`. Never commit it. |
| `ML_DAILY_USAGE_PATH` | `data/synthetic_cdr_daily_agg.csv` | Daily usage file for the parquet reader (CSV or parquet). |
| `ML_PROPHET_CHECKPOINTS` | `data/prophet_checkpoints.parquet` | Precomputed checkpoint forecasts, parquet reader only. |
| `ML_USE_PRECOMPUTED_PROPHET` | `1` | `0` always fits Prophet instead of reading the precomputed forecasts. |
| `ML_PROPHET_N_JOBS` | `-1` | joblib workers for Prophet fits (`-1` is all cores). |

### Run

```bash
venv/Scripts/python -m uvicorn app.main:app --port 8001
```

Interactive docs are at `http://localhost:8001/docs`.

### Run the tests

```bash
venv/Scripts/python -m pytest -q
```

### Routes

JSON field names are camelCase. Errors are JSON with `title`, `status` and
`detail` (plus `errors` for 422).

| Route | Body | Returns |
| --- | --- | --- |
| `POST /v1/score` | `{simIds, asOfDate}`, 1 to 5,000 SIMs, date `YYYY-MM-DD` | Per SIM: `simId`, `date`, `probability`, `bucket` (`clear`, `escalated`, `flag`), `reasons` (top 5 SHAP features), `similarCases` (empty until CBR is built). SIMs with no usage row on `asOfDate` go in `notFound`. |
| `POST /v1/recommend-tier` | `{simIds, billingCycle}`, 1 to 200 SIMs | Raw Prophet result of the latest checkpoint (day 10, 20 or 25) the data reaches: `recommendation`, `recommendedTier`, `projectedOverageMb`, `daysToDepletion`, `checkpointDay`. No sustained pattern or cooldown rule; the backend applies those. Unknown SIMs go in `notFound`. |
| `GET /health` | | `200` with `status: ok` when artifacts and data are loaded, `503` otherwise. |
| `GET /version` | | Model file names, sizes and sha256, and library versions. |

Status codes: `422` for bad input (empty list, too many SIMs, bad date,
unknown field), `503` when artifacts are missing.

## Demo import files

`scripts/make_import_files.py` turns the generator output into files for the
backend CSV import (`backend/docs/imports.md`):

```
venv/Scripts/python scripts/make_import_files.py
```

It writes `departments.csv`, `employees.csv`, `sims.csv`, `usage.csv`
(the 90,000 generator rows), `usage_perf_100k.csv` (100,000 more rows for the
timing test) and `sim_mapping.csv` into `data/import_samples/`, which is
git-ignored. Import them in that order.

The generator has no department, employee or carrier data, so the script
assigns them:

- Departments by tier: field = Field Operations, manager = Management,
  professional = Professional Services, support = Customer Support.
- One employee per SIM ("Demo Employee 0042",
  `demo.employee.0042@simguard.local`) in the SIM's tier department, with a
  job role by tier. SIMs are not allocated to them.
- Carrier by `sim_id` modulo 3: Safaricom, Airtel, Telkom.
- Fake ICCID `8925400` + `sim_id` (11 digits) + a Luhn digit, and phone
  `+2547` + `10000000 + sim_id`. Both are kept in `sim_mapping.csv` and
  reused when it exists, so they never change between runs.

## Known limitations

- The fill value for the first day of each SIM's rolling volume mean
  (`total_mb_median` in `artifacts/feature_fill_values.json`, 415.90892) was
  computed by `notebooks/feature_engineering.ipynb` over all 90,000 rows,
  before the train, validation and test split. It is kept because the saved
  models were trained with it. The train-only value (415.95535) is stored
  next to it as `total_mb_median_train_only`, for reference only.
- It only affects the first day of each SIM, the one day with no history.
- The largest effect found was a z-score change of 0.023 on 11 of 2,700 test
  rows (`vol_zscore_7d` and `vol_zscore_14d`, parity fixture of 30 SIMs).
- At inference time it is a fixed constant read from the artifact, so it
  does not change with new data.
- The 42-column model sends 28.8% of rows to the escalated band
  (probability 0.10 to 0.90). Case based reasoning (Stage 4) was evaluated
  to settle part of the band and was not adopted (see `docs/cbr_design.md`).
  Analysts review the band using the SHAP reasons.

## Retraining checklist

After any retrain (see `docs/evaluation_report.md`, stability section):

1. Refit the scaler, cohort stats, Isolation Forest and XGBoost on the new training SIMs only.
2. Do not reuse the 0.10 and 0.90 gate thresholds. The probabilities are uncalibrated (scale_pos_weight is about 48), so their range changes with every retrain.
3. Re-derive both thresholds on the validation split. Flag: the lowest threshold with validation flag precision of at least 0.95. Clear: the highest threshold that leaves at most a stated number of anomalies in the cleared bucket. Record that number next to the thresholds.
4. Check the clear, escalated and flag shares before deploying. Stop if more than 60% of rows are escalated.
5. Regenerate SHAP with `iteration_range` up to `best_iteration`.
6. Refresh the artifact versions shown by `/version`.
