"""Parity: build_features then scale_features must reproduce the saved X_*_v2 rows.

Fixture: 30 SIMs (10 from each of train, val and test), all 90 days each.
  fixtures/daily_raw_30_sims.parquet          raw rows from data/synthetic_cdr_daily_agg.csv
  fixtures/expected_features_30_sims.parquet  the same rows from data/X_{train,val,test}_v2.parquet

The four cohort_z_* columns in X_*_v2 come from notebooks/cohort_zscore.ipynb,
not from this module, so they are not compared here.
"""

from pathlib import Path

import numpy as np
import pandas as pd
import pytest

from src.feature_engineering import FEATURE_COLUMNS, build_features, scale_features

TOLERANCE = 1e-6
FIXTURES = Path(__file__).resolve().parent / "fixtures"
ARTIFACT_DIR = Path(__file__).resolve().parents[1] / "artifacts"


@pytest.fixture(scope="module")
def frames():
    raw = pd.read_parquet(FIXTURES / "daily_raw_30_sims.parquet")
    expected = pd.read_parquet(FIXTURES / "expected_features_30_sims.parquet")
    expected["date"] = pd.to_datetime(expected["date"])
    expected = expected.sort_values(["sim_id", "date"]).reset_index(drop=True)

    actual = scale_features(build_features(raw, ARTIFACT_DIR), ARTIFACT_DIR)
    return actual, expected


def test_rows_line_up(frames):
    actual, expected = frames
    assert len(actual) == len(expected) == 30 * 90
    assert (actual["sim_id"].to_numpy() == expected["sim_id"].to_numpy()).all()
    assert (actual["date"].to_numpy() == expected["date"].to_numpy()).all()


def test_every_expected_column_is_compared(frames):
    _, expected = frames
    not_built = [c for c in expected.columns if c not in ("split", "sim_id", "date", *FEATURE_COLUMNS)]
    assert sorted(not_built) == sorted(
        ["cohort_z_iso_anomaly_score", "cohort_z_total_mb", "cohort_z_vol_zscore_7d", "cohort_z_vol_zscore_14d"]
    )


@pytest.mark.parametrize("column", FEATURE_COLUMNS)
def test_column_matches_saved_split(frames, column):
    actual, expected = frames
    a = actual[column].to_numpy(dtype=float)
    e = expected[column].to_numpy(dtype=float)
    diff = np.abs(a - e)
    worst = int(np.argmax(diff))
    assert diff.max() <= TOLERANCE, (
        f"{column}: max diff {diff.max():.3e} on sim_id {expected['sim_id'].iat[worst]}, "
        f"{expected['date'].iat[worst].date()} ({expected['split'].iat[worst]}); "
        f"{int((diff > TOLERANCE).sum())} of {len(diff)} rows over {TOLERANCE}"
    )
