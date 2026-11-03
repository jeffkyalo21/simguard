"""The Postgres reader returns exactly what the parquet reader returns.

Needs a database where scripts/load_daily_usage.py has loaded
data/synthetic_cdr_daily_agg.csv. Set ML_TEST_DATABASE_URL to it; without it
the database test is skipped. The test only reads.
"""

import datetime as dt
import os

import pandas as pd
import pytest

from app.config import Settings
from app.readers.parquet_reader import ParquetUsageReader
from app.readers.postgres_reader import PostgresUsageReader

URL = os.environ.get("ML_TEST_DATABASE_URL", "").strip()


def test_postgres_reader_needs_url():
    with pytest.raises(ValueError, match="ML_DATABASE_URL"):
        PostgresUsageReader("")


@pytest.mark.skipif(not URL, reason="ML_TEST_DATABASE_URL is not set (database with loaded daily usage)")
def test_same_rows_and_dtypes_as_parquet():
    pg = PostgresUsageReader(URL)
    pg.check()
    ids = sorted(pg.known_sim_ids())[:3]
    assert ids, "no daily usage in the test database; run scripts/load_daily_usage.py"
    parquet = ParquetUsageReader(Settings().daily_usage_path)
    as_of, days = dt.date(2025, 2, 15), 30
    expected = parquet.get_daily_usage(ids, as_of, days)
    actual = pg.get_daily_usage(ids, as_of, days)
    assert len(actual) == len(ids) * days
    assert actual.dtypes.to_dict() == expected.dtypes.to_dict()
    pd.testing.assert_frame_equal(actual, expected, check_exact=True)
