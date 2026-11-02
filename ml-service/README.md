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
| `READER` | `parquet` | Where daily usage comes from. `parquet` reads `data/synthetic_cdr_daily_agg.csv`. `postgres` is a stub and fails at startup. |
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
