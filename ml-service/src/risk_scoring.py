"""Stages 1 to 3 of SimGuard scoring: Isolation Forest, cohort Z-scores, XGBoost gate.

KEEP IN SYNC with the notebooks this follows:
  isolation_forest.ipynb   model trained on the 42 scaled features (feature_columns.json)
  cohort_zscore.ipynb      cells 5 to 8: iso_anomaly_score = -decision_function,
                           cohort_z = (x - median[tier]) / max(iqr[tier], 0.05)
  xgboost_final.ipynb      triage model on xgboost_feature_columns.json (42 columns)
                           and the gate thresholds

score_features(features_df) takes the output of
feature_engineering.scale_features and returns one row per SIM-day.
score(daily_df, as_of_dates) runs the whole chain on raw daily rows.

Artifacts read from artifact_dir (default: env ML_ARTIFACT_DIR):
  isolation_forest.joblib, cohort_stats.json, xgboost_triage.joblib,
  xgboost_feature_columns.json, shap_base_value.json
plus the two read by feature_engineering. No database or network access.
Stage 4 (CBR, similar cases) is not part of this module.
"""

from __future__ import annotations

import json
import os
from functools import lru_cache
from pathlib import Path

import joblib
import numpy as np
import pandas as pd
import xgboost as xgb

from src.feature_engineering import (
    FEATURE_COLUMNS,
    KEY_COLUMNS,
    TIER_VALUES,
    _artifact_path,
    build_features,
    scale_features,
)

# Stage 3 gate. Strictly below clear_below is "clear", strictly above
# flag_above is "flag", everything else (including both thresholds) is
# "escalated" to Stage 4.
GATE = {"clear_below": 0.10, "flag_above": 0.90}
BUCKETS = ("clear", "escalated", "flag")

# cohort_zscore.ipynb
COHORT_SOURCE_COLUMNS = ("iso_anomaly_score", "total_mb", "vol_zscore_7d", "vol_zscore_14d")
COHORT_Z_COLUMNS = tuple(f"cohort_z_{c}" for c in COHORT_SOURCE_COLUMNS)
IQR_FLOOR = 0.05
TIER_COLUMNS = tuple(f"tier_{t}" for t in TIER_VALUES)

N_REASONS = 5
OUTPUT_COLUMNS = ("sim_id", "date", "probability", "bucket", "reasons")

ISO_FILE = "isolation_forest.joblib"
COHORT_FILE = "cohort_stats.json"
XGB_FILE = "xgboost_triage.joblib"
XGB_COLUMNS_FILE = "xgboost_feature_columns.json"
SHAP_FILE = "shap_base_value.json"


@lru_cache(maxsize=4)
def _load_models(path: Path) -> dict:
    iso = joblib.load(path / ISO_FILE)
    xgb_model = joblib.load(path / XGB_FILE)
    with open(path / COHORT_FILE, encoding="utf-8") as f:
        cohort = json.load(f)
    with open(path / XGB_COLUMNS_FILE, encoding="utf-8") as f:
        xgb_columns = json.load(f)
    with open(path / SHAP_FILE, encoding="utf-8") as f:
        shap_meta = json.load(f)

    if tuple(iso.feature_names_in_) != FEATURE_COLUMNS:
        raise ValueError("isolation_forest.joblib columns do not match FEATURE_COLUMNS")
    if list(xgb_model.feature_names_in_) != xgb_columns:
        raise ValueError("xgboost_triage.joblib columns do not match xgboost_feature_columns.json")

    # The 0.05 floor is applied when the stats are used, as in cohort_zscore.ipynb cell 8.
    stats = {
        col: (
            pd.Series(cohort[col]["median"], dtype=float),
            pd.Series(cohort[col]["iqr"], dtype=float).clip(lower=IQR_FLOOR),
        )
        for col in COHORT_SOURCE_COLUMNS
    }
    return {
        "iso": iso,
        "xgb": xgb_model,
        "xgb_columns": xgb_columns,
        "iteration_range": (0, int(xgb_model.best_iteration) + 1),
        "cohort": stats,
        # xgboost_final.ipynb saves pred_contribs limited to best_iteration
        # and records the source here.
        "shap_source": shap_meta.get("shap_source", "xgboost pred_contribs"),
    }


def gate(probability) -> np.ndarray:
    """Bucket per probability: clear below 0.10, flag above 0.90, else escalated."""
    p = np.asarray(probability, dtype=float)
    return np.where(
        p < GATE["clear_below"], "clear", np.where(p > GATE["flag_above"], "flag", "escalated")
    ).astype(object)


def _tier(features_df: pd.DataFrame) -> pd.Series:
    # cohort_zscore.ipynb cell 5: reverse the one-hot. The scaler leaves the
    # tier_* columns as 0/1 (center 0, scale 1).
    return features_df[list(TIER_COLUMNS)].idxmax(axis=1).str.replace("tier_", "", regex=False)


def iso_anomaly_score(features_df: pd.DataFrame, artifact_dir=None) -> np.ndarray:
    """Negated IsolationForest.decision_function: higher means more anomalous."""
    models = _load_models(_artifact_path(artifact_dir))
    return -models["iso"].decision_function(features_df[list(FEATURE_COLUMNS)])


def add_cohort_z(features_df: pd.DataFrame, iso_score, artifact_dir=None) -> pd.DataFrame:
    """Add the four cohort_z_* columns (cohort_zscore.ipynb cell 8)."""
    models = _load_models(_artifact_path(artifact_dir))
    out = features_df.copy()
    tier = _tier(out)
    source = {"iso_anomaly_score": np.asarray(iso_score, dtype=float)}
    for col in COHORT_SOURCE_COLUMNS:
        median, iqr = models["cohort"][col]
        values = source[col] if col in source else out[col].to_numpy(dtype=float)
        out[f"cohort_z_{col}"] = (values - tier.map(median).to_numpy()) / tier.map(iqr).to_numpy()
    return out


def _reasons(contrib: np.ndarray, values: np.ndarray, columns: list[str]) -> list[list[dict]]:
    order = np.argsort(-np.abs(contrib), axis=1, kind="stable")[:, :N_REASONS]
    return [
        [
            {
                "feature": columns[j],
                "value": float(values[i, j]),
                "contribution": float(contrib[i, j]),
                "direction": "increases_risk" if contrib[i, j] > 0 else "decreases_risk",
            }
            for j in order[i]
        ]
        for i in range(len(order))
    ]


def _empty_output() -> pd.DataFrame:
    return pd.DataFrame({c: pd.Series(dtype=object) for c in OUTPUT_COLUMNS}).astype(
        {"sim_id": "int64", "date": "datetime64[ns]", "probability": "float64"}
    )


def score_features(
    features_df: pd.DataFrame, artifact_dir=None, return_details: bool = False
) -> pd.DataFrame:
    """Scaled features (sim_id, date, FEATURE_COLUMNS) to one scored row per SIM-day.

    Columns: sim_id, date, probability, bucket (clear, escalated, flag) and
    reasons, the top 5 features by absolute SHAP contribution in log-odds,
    each {feature, value, contribution, direction}. value is the model input
    (scaled where the feature is scaled).

    return_details=True also returns the model inputs, SHAP contributions,
    base value and margin, for tests and debugging.
    """
    if features_df.empty:
        return (_empty_output(), None) if return_details else _empty_output()
    path = _artifact_path(artifact_dir)
    models = _load_models(path)
    cols = models["xgb_columns"]

    iso = iso_anomaly_score(features_df, path)
    enriched = add_cohort_z(features_df, iso, path)
    X = enriched[cols]

    booster = models["xgb"].get_booster()
    dmat = xgb.DMatrix(X)
    probability = models["xgb"].predict_proba(X)[:, 1].astype(float)
    # The booster keeps every tree grown before early stopping ended training,
    # but predict_proba uses only trees 0..best_iteration. Margin and
    # contributions must use the same trees, or the reasons explain a
    # different model than the probability.
    trees = models["iteration_range"]
    margin = booster.predict(dmat, output_margin=True, iteration_range=trees)

    if models["shap_source"] != "xgboost pred_contribs":
        raise NotImplementedError(f"unsupported shap_source: {models['shap_source']}")
    raw = booster.predict(dmat, pred_contribs=True, iteration_range=trees)
    contrib, base = raw[:, :-1], raw[:, -1]

    out = pd.DataFrame(
        {
            "sim_id": features_df["sim_id"].to_numpy(),
            "date": pd.to_datetime(features_df["date"]).to_numpy(),
            "probability": probability,
            "bucket": gate(probability),
            "reasons": _reasons(contrib, X.to_numpy(dtype=float), cols),
        }
    )
    if not return_details:
        return out
    details = {
        "iso_anomaly_score": iso,
        "model_input": X.reset_index(drop=True),
        "contributions": contrib,
        "base_value": base,
        "margin": margin,
    }
    return out, details


def score(daily_df: pd.DataFrame, as_of_dates=None, artifact_dir=None) -> pd.DataFrame:
    """Raw daily rows to scored SIM-days.

    Pass the whole daily history: rolling features look back up to 14 days.
    as_of_dates (a date or a list of dates) limits the returned rows to those
    days; None returns every day in daily_df.
    """
    if daily_df.empty:
        return _empty_output()
    path = _artifact_path(artifact_dir)
    features = build_features(daily_df, path)
    if as_of_dates is not None:
        wanted = pd.to_datetime(pd.Series(np.atleast_1d(as_of_dates))).dt.normalize()
        features = features[features["date"].isin(list(wanted))]
        if features.empty:
            return _empty_output()
    scaled = scale_features(features.reset_index(drop=True), path)
    return score_features(scaled, path)
