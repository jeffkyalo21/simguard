"""Service settings, read from environment variables.

ML_ARTIFACT_DIR          model artifacts folder (default: ml-service/artifacts)
READER                   usage reader: "parquet" (default) or "postgres"
ML_DATABASE_URL          connection string for the postgres reader (libpq URI
                         or key=value; never committed)
ML_DAILY_USAGE_PATH      daily usage file for the parquet reader
                         (default: ml-service/data/synthetic_cdr_daily_agg.csv)
ML_PROPHET_CHECKPOINTS   precomputed checkpoint forecasts for the parquet reader
                         (default: ml-service/data/prophet_checkpoints.parquet)
ML_USE_PRECOMPUTED_PROPHET  "1" (default) to use the precomputed forecasts
                         where they cover the request, "0" to always fit
ML_PROPHET_N_JOBS        joblib workers for Prophet fits (default: -1, all cores)
"""

from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path

ML_SERVICE_ROOT = Path(__file__).resolve().parents[1]

API_VERSION = "v1"
MAX_SCORE_SIMS = 5000
MAX_RECOMMEND_SIMS = 200

# build_features needs the scored day plus 14 earlier days: the 14 day rolling
# windows look at the 14 rows before today (shift 1), and the 14 day trend
# slope includes today. 15 rows give exactly the same features as the whole
# history (checked by tests/test_api.py).
SCORE_LOOKBACK_DAYS = 15

# Files that must exist for /v1/score. The service refuses to start without them.
REQUIRED_ARTIFACTS = (
    "feature_scaler.joblib",
    "feature_fill_values.json",
    "isolation_forest.joblib",
    "cohort_stats.json",
    "xgboost_triage.joblib",
    "xgboost_feature_columns.json",
    "shap_base_value.json",
)

READERS = ("parquet", "postgres")


def _env_path(name: str, default: Path) -> Path:
    value = os.environ.get(name)
    return Path(value).resolve() if value else default


@dataclass(frozen=True)
class Settings:
    artifact_dir: Path = field(
        default_factory=lambda: _env_path("ML_ARTIFACT_DIR", ML_SERVICE_ROOT / "artifacts")
    )
    reader: str = field(default_factory=lambda: os.environ.get("READER", "parquet").strip().lower())
    database_url: str = field(default_factory=lambda: os.environ.get("ML_DATABASE_URL", ""), repr=False)
    daily_usage_path: Path = field(
        default_factory=lambda: _env_path(
            "ML_DAILY_USAGE_PATH", ML_SERVICE_ROOT / "data" / "synthetic_cdr_daily_agg.csv"
        )
    )
    prophet_checkpoints_path: Path = field(
        default_factory=lambda: _env_path(
            "ML_PROPHET_CHECKPOINTS", ML_SERVICE_ROOT / "data" / "prophet_checkpoints.parquet"
        )
    )
    use_precomputed_prophet: bool = field(
        default_factory=lambda: os.environ.get("ML_USE_PRECOMPUTED_PROPHET", "1") != "0"
    )
    prophet_n_jobs: int = field(default_factory=lambda: int(os.environ.get("ML_PROPHET_N_JOBS", "-1")))

    def missing_artifacts(self) -> list[str]:
        return [name for name in REQUIRED_ARTIFACTS if not (self.artifact_dir / name).is_file()]
