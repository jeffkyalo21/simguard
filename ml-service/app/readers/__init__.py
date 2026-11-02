"""Usage readers: where the service gets daily usage rows from."""

from __future__ import annotations

from app.config import READERS, Settings
from app.readers.base import UsageReader


def make_reader(settings: Settings) -> UsageReader:
    """Reader chosen by the READER environment variable (default parquet)."""
    if settings.reader == "parquet":
        from app.readers.parquet_reader import ParquetUsageReader

        return ParquetUsageReader(settings.daily_usage_path)
    if settings.reader == "postgres":
        from app.readers.postgres_reader import PostgresUsageReader

        return PostgresUsageReader()
    raise ValueError(f"Unknown READER {settings.reader!r}. Use one of: {', '.join(READERS)}")
