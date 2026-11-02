"""Postgres reader. STUB: not implemented yet.

When it is built it will:

- connect as a read-only database user that can only SELECT;
- read from SQL views owned by the backend (the backend owns the tables and
  the view definitions, this service never queries the tables directly), with
  the columns and row rules in docs/daily_usage_schema.md;
- take its connection string from an environment variable
  (ML_DATABASE_URL), never from code or a committed file.

Until then, READER=postgres makes the service fail at startup.
"""

from __future__ import annotations

import datetime as dt
from typing import Sequence

import pandas as pd

from app.readers.base import UsageReader

NOT_IMPLEMENTED = "The Postgres reader is not implemented yet. Use READER=parquet."


class PostgresUsageReader(UsageReader):
    name = "postgres"

    def __init__(self) -> None:
        raise NotImplementedError(NOT_IMPLEMENTED)

    def get_daily_usage(
        self, sim_ids: Sequence[int], as_of_date: dt.date, lookback_days: int
    ) -> pd.DataFrame:
        raise NotImplementedError(NOT_IMPLEMENTED)
