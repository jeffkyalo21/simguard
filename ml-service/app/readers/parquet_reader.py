"""File reader for local development and tests.

Reads data/synthetic_cdr_daily_agg.csv (or a .parquet file with the same
columns) once, keeps it in memory, and filters it per request.
"""

from __future__ import annotations

import datetime as dt
from pathlib import Path
from typing import Sequence

import pandas as pd

from app.readers.base import DAILY_USAGE_COLUMNS, UsageReader, validate_daily_usage


class ParquetUsageReader(UsageReader):
    name = "parquet"

    def __init__(self, path: Path):
        self.path = Path(path)
        self._df: pd.DataFrame | None = None

    def check(self) -> None:
        self._load()

    def _load(self) -> pd.DataFrame:
        if self._df is None:
            if not self.path.is_file():
                raise FileNotFoundError(f"daily usage file not found: {self.path}")
            if self.path.suffix == ".parquet":
                raw = pd.read_parquet(self.path, columns=list(DAILY_USAGE_COLUMNS))
            else:
                raw = pd.read_csv(self.path, usecols=list(DAILY_USAGE_COLUMNS))
            self._df = validate_daily_usage(raw).set_index("sim_id", drop=False)
        return self._df

    def known_sim_ids(self) -> set[int]:
        return set(self._load().index.unique().tolist())

    def get_daily_usage(
        self, sim_ids: Sequence[int], as_of_date: dt.date, lookback_days: int
    ) -> pd.DataFrame:
        df = self._load()
        end = pd.Timestamp(as_of_date)
        start = end - pd.Timedelta(days=lookback_days - 1)
        wanted = df.index.intersection(pd.Index(list(sim_ids)).unique())
        rows = df.loc[wanted]
        rows = rows[(rows["date"] >= start) & (rows["date"] <= end)]
        return rows.reset_index(drop=True).sort_values(["sim_id", "date"]).reset_index(drop=True)
