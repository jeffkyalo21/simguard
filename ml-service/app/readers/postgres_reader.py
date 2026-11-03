"""Postgres reader: daily usage from the backend database.

- Reads the ml_daily_usage view, which the backend owns (migration
  AddDailyUsage). The view has the columns of docs/daily_usage_schema.md;
  this service never queries the tables directly.
- Connects with the connection string in ML_DATABASE_URL (a libpq URI or
  key=value string), never from code or a committed file. A user that can
  only SELECT from ml_daily_usage is enough.
- Returns the same columns and dtypes as the parquet reader, so the scoring
  code cannot tell the two apart.

Rows get into the database with scripts/load_daily_usage.py.
"""

from __future__ import annotations

import datetime as dt
from typing import Sequence

import pandas as pd

from app.readers.base import DAILY_USAGE_COLUMNS, UsageReader, validate_daily_usage

VIEW = "ml_daily_usage"

INT_COLUMNS = (
    "sim_id",
    "data_session_count",
    "data_duration_s",
    "hotspot_sessions",
    "public_apn_sessions",
    "unique_towers",
    "geo_flag_count",
    "roaming_sessions",
    "sms_count",
    "sms_remaining",
)
FLOAT_COLUMNS = (
    "total_download_mb",
    "total_upload_mb",
    "peak_mb",
    "offpeak_mb",
    "total_mb",
    "billed_mb",
    "voice_minutes_used",
    "voice_minutes_remaining",
)

MISSING_URL = "READER=postgres needs ML_DATABASE_URL (the database connection string)."


def to_contract_dtypes(df: pd.DataFrame) -> pd.DataFrame:
    """Same dtypes as the parquet reader reading the CSV: int64, float64,
    datetime64[us] for date and the default string dtype for tier."""
    out = df.copy()
    for column in INT_COLUMNS:
        out[column] = out[column].astype("int64")
    for column in FLOAT_COLUMNS:
        out[column] = out[column].astype("float64")
    out["tier"] = out["tier"].astype("str")
    out["date"] = pd.to_datetime(out["date"]).astype("datetime64[us]")
    return out


class PostgresUsageReader(UsageReader):
    name = "postgres"

    def __init__(self, conninfo: str | None):
        if not conninfo or not conninfo.strip():
            raise ValueError(MISSING_URL)
        self._conninfo = conninfo.strip()

    def _connect(self):
        import psycopg

        return psycopg.connect(self._conninfo)

    def check(self) -> None:
        with self._connect() as conn:
            conn.execute(f"SELECT 1 FROM {VIEW} LIMIT 1").fetchall()

    def known_sim_ids(self) -> set[int]:
        with self._connect() as conn:
            return {row[0] for row in conn.execute(f"SELECT DISTINCT sim_id FROM {VIEW}")}

    def get_daily_usage(
        self, sim_ids: Sequence[int], as_of_date: dt.date, lookback_days: int
    ) -> pd.DataFrame:
        ids = sorted({int(i) for i in sim_ids})
        end = pd.Timestamp(as_of_date).date()
        start = end - dt.timedelta(days=lookback_days - 1)
        columns = ", ".join(DAILY_USAGE_COLUMNS)

        rows: list[tuple] = []
        if ids:
            with self._connect() as conn:
                rows = conn.execute(
                    f"SELECT {columns} FROM {VIEW} "
                    "WHERE sim_id = ANY(%s) AND date BETWEEN %s AND %s "
                    "ORDER BY sim_id, date",
                    (ids, start, end),
                ).fetchall()

        raw = pd.DataFrame.from_records(rows, columns=list(DAILY_USAGE_COLUMNS))
        return to_contract_dtypes(validate_daily_usage(to_contract_dtypes(raw)))
