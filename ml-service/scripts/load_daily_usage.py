"""Load daily usage rows into the backend database (table daily_usage).

Usage, from ml-service/:

    $env:SIMGUARD_DATABASE_URL = 'postgresql://user:password@localhost:5432/simguard'
    venv/Scripts/python scripts/load_daily_usage.py [--file PATH]

The file defaults to data/synthetic_cdr_daily_agg.csv (a .parquet file with the
same columns also works). Columns and row rules: docs/daily_usage_schema.md.

The file's sim_id is the backend's sims.id. Rows for a sim_id that has no SIM
in the database are skipped and listed; the script never creates SIMs. Add the
SIMs through the API first if their usage should be loaded.

Rows are upserted on (sim_id, date): a new day is inserted, a changed day is
updated, an identical day is left alone. Running the script twice leaves the
row count unchanged.

The connection string comes from SIMGUARD_DATABASE_URL (a libpq URI or
key=value string) and needs INSERT and UPDATE on daily_usage. It is never
printed.
"""

from __future__ import annotations

import argparse
import os
import sys
from dataclasses import dataclass
from pathlib import Path

import pandas as pd

ML_SERVICE_ROOT = Path(__file__).resolve().parents[1]
if str(ML_SERVICE_ROOT) not in sys.path:
    sys.path.insert(0, str(ML_SERVICE_ROOT))

from app.readers.base import DAILY_USAGE_COLUMNS, validate_daily_usage  # noqa: E402

DEFAULT_FILE = ML_SERVICE_ROOT / "data" / "synthetic_cdr_daily_agg.csv"
URL_VARIABLE = "SIMGUARD_DATABASE_URL"

# Every contract column except the key.
VALUE_COLUMNS = tuple(c for c in DAILY_USAGE_COLUMNS if c not in ("sim_id", "date"))


@dataclass
class LoadResult:
    rows_in_file: int
    sims_in_file: int
    unknown_sim_ids: list[int]
    rows_unknown_sim: int
    inserted: int
    updated: int
    unchanged: int

    @property
    def skipped(self) -> int:
        return self.rows_unknown_sim + self.unchanged


def read_file(path: Path) -> pd.DataFrame:
    """Reads and checks the file like the parquet reader does."""
    if not path.is_file():
        raise FileNotFoundError(f"daily usage file not found: {path}")
    columns = list(DAILY_USAGE_COLUMNS)
    if path.suffix == ".parquet":
        raw = pd.read_parquet(path, columns=columns)
    else:
        raw = pd.read_csv(path, usecols=columns)
    return validate_daily_usage(raw)


def split_known(df: pd.DataFrame, known_ids: set[int]) -> tuple[pd.DataFrame, list[int]]:
    """Rows whose SIM exists, and the sorted sim_ids that do not."""
    file_ids = set(int(i) for i in df["sim_id"].unique())
    unknown = sorted(file_ids - known_ids)
    return df[df["sim_id"].isin(known_ids)], unknown


def _records(df: pd.DataFrame):
    """Rows as plain Python values for COPY (no numpy scalars)."""
    out = df[list(DAILY_USAGE_COLUMNS)].copy()
    out["date"] = out["date"].dt.date
    for row in out.itertuples(index=False, name=None):
        yield tuple(v.item() if hasattr(v, "item") else v for v in row)


def load(df: pd.DataFrame, conninfo: str) -> LoadResult:
    import psycopg

    columns = ", ".join(DAILY_USAGE_COLUMNS)
    updates = ", ".join(f"{c} = EXCLUDED.{c}" for c in VALUE_COLUMNS)
    current = ", ".join(f"daily_usage.{c}" for c in VALUE_COLUMNS)
    incoming = ", ".join(f"EXCLUDED.{c}" for c in VALUE_COLUMNS)

    with psycopg.connect(conninfo) as conn:
        known = {row[0] for row in conn.execute("SELECT id FROM sims")}
        rows, unknown = split_known(df, known)

        inserted = updated = 0
        if len(rows):
            with conn.cursor() as cur:
                cur.execute(
                    f"CREATE TEMP TABLE daily_usage_load ON COMMIT DROP AS "
                    f"SELECT {columns} FROM daily_usage WITH NO DATA"
                )
                with cur.copy(f"COPY daily_usage_load ({columns}) FROM STDIN") as copy:
                    for record in _records(rows):
                        copy.write_row(record)

                # xmax = 0 marks a freshly inserted row; the WHERE skips days
                # whose values are already the same.
                cur.execute(
                    f"INSERT INTO daily_usage ({columns}) "
                    f"SELECT {columns} FROM daily_usage_load "
                    f"ON CONFLICT (sim_id, date) DO UPDATE SET {updates} "
                    f"WHERE ({current}) IS DISTINCT FROM ({incoming}) "
                    f"RETURNING (xmax = 0)"
                )
                flags = [r[0] for r in cur.fetchall()]
                inserted = sum(1 for f in flags if f)
                updated = len(flags) - inserted
        conn.commit()

    return LoadResult(
        rows_in_file=len(df),
        sims_in_file=int(df["sim_id"].nunique()),
        unknown_sim_ids=unknown,
        rows_unknown_sim=len(df) - len(rows),
        inserted=inserted,
        updated=updated,
        unchanged=len(rows) - inserted - updated,
    )


def _short_list(ids: list[int], limit: int = 20) -> str:
    shown = ", ".join(str(i) for i in ids[:limit])
    return shown + (f", ... ({len(ids) - limit} more)" if len(ids) > limit else "")


def report(result: LoadResult, path: Path) -> str:
    lines = [
        f"file: {path}",
        f"rows in file: {result.rows_in_file} ({result.sims_in_file} SIMs)",
        f"inserted: {result.inserted}",
        f"updated: {result.updated}",
        f"skipped: {result.skipped} "
        f"({result.unchanged} unchanged, {result.rows_unknown_sim} for unknown SIMs)",
    ]
    if result.unknown_sim_ids:
        lines.append(
            f"unknown SIM ids (no row in sims, not loaded): {len(result.unknown_sim_ids)}: "
            + _short_list(result.unknown_sim_ids)
        )
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Load daily usage into the SimGuard database.")
    parser.add_argument("--file", type=Path, default=DEFAULT_FILE, help="CSV or parquet file")
    args = parser.parse_args(argv)

    conninfo = os.environ.get(URL_VARIABLE, "").strip()
    if not conninfo:
        print(f"Set {URL_VARIABLE} to the database connection string.", file=sys.stderr)
        return 2

    df = read_file(args.file)
    result = load(df, conninfo)
    print(report(result, args.file))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
