"""scripts/load_daily_usage.py parts that need no database.

The database round trip (load twice, read back through the Postgres reader) is
in tests/test_postgres_reader.py and runs only when ML_TEST_DATABASE_URL is set.
"""

import pandas as pd
import pytest

from scripts.load_daily_usage import (
    DEFAULT_FILE,
    LoadResult,
    _records,
    main,
    read_file,
    report,
    split_known,
)


@pytest.fixture(scope="module")
def daily():
    return read_file(DEFAULT_FILE)


def test_read_file_matches_contract(daily):
    assert len(daily) == 90000
    assert not daily.duplicated(["sim_id", "date"]).any()


def test_split_known_keeps_only_existing_sims(daily):
    rows, unknown = split_known(daily, {3, 5, 123456})
    assert sorted(rows["sim_id"].unique().tolist()) == [3, 5]
    assert len(rows) == 180
    assert 123456 not in unknown
    assert 0 in unknown and 3 not in unknown and 5 not in unknown
    assert unknown == sorted(unknown)


def test_records_are_plain_python_values(daily):
    first = next(_records(daily.head(1)))
    assert type(first[0]) is int
    assert type(first[1]).__name__ == "date"
    assert all(type(v) in (int, float, str) for v in first[2:])


def test_report_lists_unknown_sims():
    result = LoadResult(
        rows_in_file=10,
        sims_in_file=2,
        unknown_sim_ids=[9],
        rows_unknown_sim=5,
        inserted=3,
        updated=1,
        unchanged=1,
    )
    text = report(result, DEFAULT_FILE)
    assert "inserted: 3" in text
    assert "updated: 1" in text
    assert "skipped: 6 (1 unchanged, 5 for unknown SIMs)" in text
    assert "unknown SIM ids (no row in sims, not loaded): 1: 9" in text


def test_main_needs_connection_string(monkeypatch, capsys):
    monkeypatch.delenv("SIMGUARD_DATABASE_URL", raising=False)
    assert main([]) == 2
    assert "SIMGUARD_DATABASE_URL" in capsys.readouterr().err


def test_split_known_with_no_known_sims():
    df = pd.DataFrame({"sim_id": [1, 1, 2]})
    rows, unknown = split_known(df, set())
    assert rows.empty
    assert unknown == [1, 2]
