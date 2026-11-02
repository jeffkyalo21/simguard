"""Tests for src/risk_scoring.py against the saved X_*_v2 rows of the 30 SIM fixture.

Fixtures (see test_feature_engineering.py):
  fixtures/daily_raw_30_sims.parquet          raw daily rows
  fixtures/expected_features_30_sims.parquet  the same rows from X_{train,val,test}_v2
"""

import json
import warnings
from pathlib import Path

import joblib
import numpy as np
import pandas as pd
import pytest

from src.feature_engineering import FEATURE_COLUMNS, build_features, scale_features
from src.risk_scoring import (
    COHORT_Z_COLUMNS,
    GATE,
    IQR_FLOOR,
    OUTPUT_COLUMNS,
    gate,
    iso_anomaly_score,
    score,
    score_features,
)

FIXTURES = Path(__file__).resolve().parent / "fixtures"
ARTIFACT_DIR = Path(__file__).resolve().parents[1] / "artifacts"


@pytest.fixture(scope="module")
def raw():
    return pd.read_parquet(FIXTURES / "daily_raw_30_sims.parquet")


@pytest.fixture(scope="module")
def expected():
    e = pd.read_parquet(FIXTURES / "expected_features_30_sims.parquet")
    e["date"] = pd.to_datetime(e["date"])
    return e.sort_values(["sim_id", "date"]).reset_index(drop=True)


@pytest.fixture(scope="module")
def scored(raw):
    scaled = scale_features(build_features(raw, ARTIFACT_DIR), ARTIFACT_DIR)
    out, details = score_features(scaled, ARTIFACT_DIR, return_details=True)
    return scaled, out, details


@pytest.fixture(scope="module")
def xgb_model():
    return joblib.load(ARTIFACT_DIR / "xgboost_triage.joblib")


def _max_diff(a, b):
    return float(np.max(np.abs(np.asarray(a, dtype=float) - np.asarray(b, dtype=float))))


# 1. Cohort Z-scores
@pytest.mark.parametrize("column", COHORT_Z_COLUMNS)
def test_cohort_z_matches_saved(scored, expected, column):
    _, _, details = scored
    diff = _max_diff(details["model_input"][column], expected[column])
    assert diff <= 1e-6, f"{column}: max diff {diff:.3e}"


# 2. Isolation Forest scores under the installed scikit-learn
def test_iso_scores_identical_to_saved(expected):
    # X_*_v2 does not store iso_anomaly_score (cohort_zscore.ipynb cell 10
    # drops it), but it stores cohort_z_iso_anomaly_score, so the saved score
    # is recovered exactly: iso = z * max(iqr, 0.05) + median[tier].
    stats = json.loads((ARTIFACT_DIR / "cohort_stats.json").read_text())["iso_anomaly_score"]
    tier = expected[["tier_field", "tier_manager", "tier_professional", "tier_support"]].idxmax(axis=1).str[5:]
    med = tier.map(stats["median"]).to_numpy()
    iqr = np.maximum(tier.map(stats["iqr"]).to_numpy(), IQR_FLOOR)
    saved_iso = expected["cohort_z_iso_anomaly_score"].to_numpy() * iqr + med

    # Same input the notebook scored: the saved scaled 42 columns.
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always")
        now = iso_anomaly_score(expected[list(FEATURE_COLUMNS)], ARTIFACT_DIR)
    diff = _max_diff(now, saved_iso)
    assert diff <= 1e-9, f"iso_anomaly_score: max diff {diff:.3e}"


# 3. Final probabilities
def test_probability_matches_model_on_saved_rows(scored, expected, xgb_model):
    _, out, _ = scored
    cols = json.loads((ARTIFACT_DIR / "xgboost_feature_columns.json").read_text())
    want = xgb_model.predict_proba(expected[cols])[:, 1]
    assert (out["sim_id"].to_numpy() == expected["sim_id"].to_numpy()).all()
    diff = _max_diff(out["probability"], want)
    assert diff <= 1e-6, f"probability: max diff {diff:.3e}"


# 4. Gate boundaries
@pytest.mark.parametrize(
    "p, bucket",
    [(0.0999, "clear"), (0.10, "escalated"), (0.5, "escalated"), (0.90, "escalated"), (0.9001, "flag")],
)
def test_gate_boundaries(p, bucket):
    assert GATE == {"clear_below": 0.10, "flag_above": 0.90}
    assert gate([p])[0] == bucket


# 5. SHAP additivity
def test_shap_additive(scored):
    _, out, details = scored
    total = details["base_value"] + details["contributions"].sum(axis=1)
    diff = _max_diff(total, details["margin"])
    assert diff <= 1e-4, f"base + sum(contributions) vs margin: max diff {diff:.3e}"
    # The margin is the logit of the reported probability.
    p = out["probability"].to_numpy()
    assert _max_diff(1 / (1 + np.exp(-details["margin"])), p) <= 1e-6


def test_contributions_match_saved_shap():
    # The saved SHAP rows are the escalated validation rows (0.10 <= p <= 0.90),
    # in the order of shap_values_escalated_index.csv. Score those rows from
    # their saved 42 columns and compare contributions and reasons.
    cols = json.loads((ARTIFACT_DIR / "xgboost_feature_columns.json").read_text())
    saved = np.load(ARTIFACT_DIR / "shap_values_escalated.npy")
    meta = json.loads((ARTIFACT_DIR / "shap_base_value.json").read_text())
    index = pd.read_csv(ARTIFACT_DIR / "shap_values_escalated_index.csv", parse_dates=["date"])

    x_val = pd.read_parquet(ARTIFACT_DIR.parent / "data" / "X_val_v2.parquet")
    rows = index[["sim_id", "date"]].merge(x_val, on=["sim_id", "date"], how="left", validate="one_to_one")
    assert len(rows) == len(saved) == 3893

    # score_features rebuilds the cohort_z columns from the Isolation Forest,
    # so it needs the 42 FEATURE_COLUMNS, which X_val_v2 also stores.
    out, details = score_features(rows[["sim_id", "date", *FEATURE_COLUMNS]], ARTIFACT_DIR, return_details=True)
    assert (out["bucket"] == "escalated").all()
    assert _max_diff(details["model_input"][cols], rows[cols]) <= 1e-6

    diff = _max_diff(details["contributions"], saved)
    assert diff <= 1e-4, f"contributions vs shap_values_escalated.npy: max diff {diff:.3e}"
    assert _max_diff(details["base_value"], meta["base_value_log_odds"]) <= 1e-4

    for i in (0, len(out) // 2, len(out) - 1):
        want = np.argsort(-np.abs(saved[i]), kind="stable")[:5]
        got = [cols.index(r["feature"]) for r in out["reasons"].iat[i]]
        assert got == list(want)
        assert _max_diff([r["contribution"] for r in out["reasons"].iat[i]], saved[i, want]) <= 1e-4


def test_reasons_shape(scored):
    _, out, details = scored
    first = out["reasons"].iat[0]
    assert len(first) == 5
    assert set(first[0]) == {"feature", "value", "contribution", "direction"}
    mags = [abs(r["contribution"]) for r in first]
    assert mags == sorted(mags, reverse=True)
    assert all(r["direction"] == ("increases_risk" if r["contribution"] > 0 else "decreases_risk") for r in first)
    assert set(out["bucket"]) <= {"clear", "escalated", "flag"}
    assert list(out.columns) == list(OUTPUT_COLUMNS)


# 6. as_of_dates, short history, empty input
def test_as_of_dates_matches_full_run(raw, scored):
    _, full, _ = scored
    days = ["2025-02-15", "2025-03-31"]
    part = score(raw, as_of_dates=days, artifact_dir=ARTIFACT_DIR)
    assert len(part) == 30 * 2
    assert set(part["date"].dt.strftime("%Y-%m-%d")) == set(days)
    ref = full[full["date"].isin(pd.to_datetime(days))].reset_index(drop=True)
    assert _max_diff(part["probability"], ref["probability"]) == 0.0
    assert score(raw, as_of_dates="2025-03-31", artifact_dir=ARTIFACT_DIR)["date"].nunique() == 1


def test_short_history_sim(raw):
    sim = raw["sim_id"].iat[0]
    short = raw[raw["sim_id"] == sim].sort_values("date").head(5)
    out = score(short, artifact_dir=ARTIFACT_DIR)
    assert len(out) == 5
    assert np.isfinite(out["probability"]).all()
    features = build_features(short, ARTIFACT_DIR)
    assert (features["trend_slope_14d"] == 0.0).all()  # under 14 days: notebook fill


def test_empty_input(raw):
    out = score(raw.iloc[0:0], artifact_dir=ARTIFACT_DIR)
    assert out.empty and list(out.columns) == list(OUTPUT_COLUMNS)
    none = score(raw, as_of_dates=["2030-01-01"], artifact_dir=ARTIFACT_DIR)
    assert none.empty and list(none.columns) == list(OUTPUT_COLUMNS)
