"""Daily feature engineering for the SimGuard scoring pipeline.

KEEP IN SYNC: this module reproduces notebooks/feature_engineering.ipynb
(cells 3, 4, 5 and 7). The notebook is the source of truth. Any change to a
feature there must be made here too, and tests/test_feature_engineering.py
must still pass against the saved X_* parquet files.

build_features(daily_df) turns raw daily usage rows (see
docs/daily_usage_schema.md) into unscaled features. scale_features(features_df)
clips the two z-score columns and applies the saved RobustScaler.

The only files this module reads are two artifacts:
  feature_scaler.joblib     RobustScaler fitted in notebook cell 7
  feature_fill_values.json  fill value for the rolling means (cell 4)
They are read from artifact_dir, which defaults to the ML_ARTIFACT_DIR
environment variable. No other file or database access happens here.
"""

from __future__ import annotations

import json
import os
from functools import lru_cache
from pathlib import Path

import joblib
import numpy as np
import pandas as pd

ARTIFACT_DIR_ENV = "ML_ARTIFACT_DIR"
SCALER_FILE = "feature_scaler.joblib"
FILL_VALUES_FILE = "feature_fill_values.json"

# Key in feature_fill_values.json. The notebook fills the 7d and 14d rolling
# means with the median of total_mb. The value is read per artifact folder,
# so it is not a module constant.
TOTAL_MB_MEDIAN = "total_mb_median"

# Constant fills from notebook cell 4.
ROLLING_STD_FILL = 1.0
ROLLING_SUM_FILL = 0
TREND_SLOPE_FILL = 0.0

# Notebook cell 5: pd.get_dummies(df, columns=["tier"], prefix="tier").
# Listed explicitly so a batch with only some tiers still gets every column.
TIER_VALUES = ("field", "manager", "professional", "support")

RAW_COLUMNS = (
    "total_download_mb",
    "total_upload_mb",
    "data_session_count",
    "data_duration_s",
    "hotspot_sessions",
    "public_apn_sessions",
    "unique_towers",
    "geo_flag_count",
    "peak_mb",
    "offpeak_mb",
    "total_mb",
    "roaming_sessions",
    "billed_mb",
    "voice_minutes_used",
    "sms_count",
    "voice_minutes_remaining",
    "sms_remaining",
)

# Notebook cell 7: already relative, so clipped to [-50, 50] and not scaled.
UNSCALED_CLIPPED_COLUMNS = ("vol_zscore_7d", "vol_zscore_14d")
ZSCORE_CLIP = 50

# Columns the RobustScaler was fitted on, in its fit order (40).
SCALED_COLUMNS = (
    *RAW_COLUMNS,
    "billing_gap_mb",
    "billing_ratio",
    "offpeak_ratio",
    "public_apn_ratio",
    "upload_ratio",
    "bytes_per_session",
    "mean_session_duration_s",
    "hotspot_session_ratio",
    "has_geo_anomaly",
    "vol_rolling_7d_mean",
    "vol_rolling_7d_std",
    "hotspot_sum_7d",
    "geo_sum_7d",
    "vol_rolling_14d_mean",
    "vol_rolling_14d_std",
    "hotspot_sum_14d",
    "geo_sum_14d",
    "trend_slope_14d",
    *(f"tier_{tier}" for tier in TIER_VALUES),
    "voice_utilization_ratio",
)

KEY_COLUMNS = ("sim_id", "date")

# Output column order of build_features, matching X_train.parquet (44).
FEATURE_COLUMNS = (
    *RAW_COLUMNS,
    "billing_gap_mb",
    "billing_ratio",
    "offpeak_ratio",
    "public_apn_ratio",
    "upload_ratio",
    "bytes_per_session",
    "mean_session_duration_s",
    "hotspot_session_ratio",
    "has_geo_anomaly",
    "vol_rolling_7d_mean",
    "vol_rolling_7d_std",
    "vol_zscore_7d",
    "hotspot_sum_7d",
    "geo_sum_7d",
    "vol_rolling_14d_mean",
    "vol_rolling_14d_std",
    "vol_zscore_14d",
    "hotspot_sum_14d",
    "geo_sum_14d",
    "trend_slope_14d",
    *(f"tier_{tier}" for tier in TIER_VALUES),
    "voice_utilization_ratio",
)

SLOPE_WINDOW = 14


def _artifact_path(artifact_dir: str | os.PathLike | None) -> Path:
    if artifact_dir is None:
        artifact_dir = os.environ.get(ARTIFACT_DIR_ENV)
    if not artifact_dir:
        raise ValueError(
            f"No artifact folder given. Pass artifact_dir or set {ARTIFACT_DIR_ENV}."
        )
    return Path(artifact_dir).resolve()


@lru_cache(maxsize=8)
def _load_fill_values(path: Path) -> dict:
    with open(path / FILL_VALUES_FILE, encoding="utf-8") as f:
        return json.load(f)


@lru_cache(maxsize=8)
def _load_scaler(path: Path):
    return joblib.load(path / SCALER_FILE)


def _compute_slope(y: np.ndarray) -> float:
    """Least squares slope of y over 0..13 (notebook compute_slope)."""
    return np.polyfit(np.arange(SLOPE_WINDOW), y, 1)[0]


def build_features(
    daily_df: pd.DataFrame, artifact_dir: str | os.PathLike | None = None
) -> pd.DataFrame:
    """Raw daily rows to unscaled features.

    Steps, as in the notebook: sort by SIM and date, base ratios, per-SIM
    rolling aggregates (shift 1, so only past days), 14 day trend slope
    (includes today), tier one-hot and voice utilisation ratio.

    Returns sim_id, date and FEATURE_COLUMNS, one row per input row, sorted
    by sim_id then date. Rolling features for a SIM only see the rows passed
    in, so pass at least 14 prior days per SIM to match training.
    """
    missing = [c for c in (*KEY_COLUMNS, *RAW_COLUMNS, "tier") if c not in daily_df.columns]
    if missing:
        raise ValueError(f"daily_df is missing columns: {missing}")
    unknown = set(daily_df["tier"].dropna().unique()) - set(TIER_VALUES)
    if unknown or daily_df["tier"].isna().any():
        raise ValueError(f"Unknown or missing tier values: {sorted(map(str, unknown))}")

    total_mb_median = float(_load_fill_values(_artifact_path(artifact_dir))[TOTAL_MB_MEDIAN])

    df = daily_df[[*KEY_COLUMNS, *RAW_COLUMNS, "tier"]].copy()
    df["date"] = pd.to_datetime(df["date"])

    # Cell 3: billing discrepancy, diurnal and network shifts, session shape.
    df["billing_gap_mb"] = (df["billed_mb"] - df["total_mb"]).clip(lower=0)
    df["billing_ratio"] = (df["billed_mb"] + 1.0) / (df["total_mb"] + 1.0)
    df["offpeak_ratio"] = df["offpeak_mb"] / (df["total_mb"] + 1e-4)
    df["public_apn_ratio"] = df["public_apn_sessions"] / (df["data_session_count"] + 1e-4)
    df["upload_ratio"] = df["total_upload_mb"] / (df["total_mb"] + 1e-4)
    df["bytes_per_session"] = df["total_mb"] / (df["data_session_count"] + 1e-4)
    df["mean_session_duration_s"] = df["data_duration_s"] / (df["data_session_count"] + 1e-4)
    df["hotspot_session_ratio"] = df["hotspot_sessions"] / (df["data_session_count"] + 1e-4)
    df["has_geo_anomaly"] = (df["geo_flag_count"] > 0).astype(int)

    # Cell 4: per-SIM rolling windows on past days only (shift 1).
    df = df.sort_values(["sim_id", "date"]).reset_index(drop=True)
    groups = df.groupby("sim_id")

    def past(col: str, window: int, how: str) -> pd.Series:
        return groups[col].transform(
            lambda x: getattr(x.shift(1).rolling(window, min_periods=1), how)()
        )

    for window in (7, 14):
        mean_col = f"vol_rolling_{window}d_mean"
        std_col = f"vol_rolling_{window}d_std"
        df[mean_col] = past("total_mb", window, "mean").fillna(total_mb_median)
        df[std_col] = past("total_mb", window, "std").fillna(ROLLING_STD_FILL)
        df[f"vol_zscore_{window}d"] = (df["total_mb"] - df[mean_col]) / (df[std_col] + 1.0)
        df[f"hotspot_sum_{window}d"] = past("hotspot_sessions", window, "sum").fillna(ROLLING_SUM_FILL)
        df[f"geo_sum_{window}d"] = past("geo_flag_count", window, "sum").fillna(ROLLING_SUM_FILL)

    # Trend slope includes today (shift 0), as in the notebook.
    df["trend_slope_14d"] = groups["total_mb"].transform(
        lambda x: x.rolling(SLOPE_WINDOW, min_periods=SLOPE_WINDOW).apply(_compute_slope, raw=True)
    ).fillna(TREND_SLOPE_FILL)

    # Cell 5: tier one-hot and voice utilisation.
    for tier in TIER_VALUES:
        df[f"tier_{tier}"] = (df["tier"] == tier).astype(int)
    df["voice_utilization_ratio"] = df["voice_minutes_used"] / (
        df["voice_minutes_used"] + df["voice_minutes_remaining"].clip(lower=0) + 1.0
    )

    return df[[*KEY_COLUMNS, *FEATURE_COLUMNS]]


def scale_features(
    features_df: pd.DataFrame, artifact_dir: str | os.PathLike | None = None
) -> pd.DataFrame:
    """Notebook cell 7: clip the z-scores to [-50, 50], RobustScale the rest.

    Returns a copy with the same columns and order as the input.
    """
    scaler = _load_scaler(_artifact_path(artifact_dir))
    fitted = tuple(scaler.feature_names_in_)
    if fitted != SCALED_COLUMNS:
        raise ValueError("feature_scaler.joblib columns do not match SCALED_COLUMNS")

    out = features_df.copy()
    for col in UNSCALED_CLIPPED_COLUMNS:
        out[col] = out[col].clip(-ZSCORE_CLIP, ZSCORE_CLIP)
    out[list(SCALED_COLUMNS)] = scaler.transform(out[list(SCALED_COLUMNS)])
    return out
