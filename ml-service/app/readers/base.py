"""Reader interface for daily usage rows.

Every reader returns a dataframe that matches docs/daily_usage_schema.md:
one row per SIM per day, sorted by sim_id then date, no nulls, the columns
listed in DAILY_USAGE_COLUMNS. Label columns are never returned.
"""

from __future__ import annotations

import datetime as dt
from abc import ABC, abstractmethod
from typing import Sequence

import pandas as pd

# docs/daily_usage_schema.md, in table order.
DAILY_USAGE_COLUMNS = (
    "sim_id",
    "date",
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
    "tier",
    "voice_minutes_used",
    "sms_count",
    "voice_minutes_remaining",
    "sms_remaining",
)


class UsageReader(ABC):
    """Source of daily usage rows."""

    name: str = "base"

    @abstractmethod
    def get_daily_usage(
        self, sim_ids: Sequence[int], as_of_date: dt.date, lookback_days: int
    ) -> pd.DataFrame:
        """Rows for sim_ids with date in [as_of_date - lookback_days + 1, as_of_date].

        lookback_days counts calendar days and includes as_of_date. SIM ids
        with no rows in the window are simply absent from the result; the
        caller reports them as not found.
        """

    def check(self) -> None:
        """Raise if the reader cannot serve requests. Called at startup."""


def validate_daily_usage(df: pd.DataFrame) -> pd.DataFrame:
    """Check a reader result against docs/daily_usage_schema.md and normalise it."""
    missing = [c for c in DAILY_USAGE_COLUMNS if c not in df.columns]
    if missing:
        raise ValueError(f"daily usage is missing columns: {missing}")
    out = df[list(DAILY_USAGE_COLUMNS)].copy()
    nulls = [c for c in DAILY_USAGE_COLUMNS if out[c].isna().any()]
    if nulls:
        raise ValueError(f"daily usage has nulls in: {nulls}")
    out["date"] = pd.to_datetime(out["date"]).dt.normalize()
    if out.duplicated(["sim_id", "date"]).any():
        raise ValueError("daily usage has duplicate (sim_id, date) rows")
    return out.sort_values(["sim_id", "date"]).reset_index(drop=True)
