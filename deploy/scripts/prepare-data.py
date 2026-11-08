#!/usr/bin/env python3
"""Build the demo data pack in deploy/data-pack/ (git-ignored).

    python3 deploy/scripts/prepare-data.py [--out DIR] [--no-sessions] [--rotate-keys]

It reuses the rules of ml-service/scripts/make_import_files.py (same fake
ICCIDs, phone numbers, departments, carriers and column names, taken from the
generator output ml-service/data/synthetic_cdr_daily_agg.csv) and writes:

    <out>/NN_<type>[_partMM].csv   the import files, in import order
    <out>/README.txt               the order to import them in the Imports screen
    <out>/mock-data/               what the carrier mock reads (sims.csv, usage.csv,
                                   sim_mapping.csv and, unless --no-sessions, the
                                   per-session file)
    <out>/mock.env                 CarrierMock__Keys__<carrier> for the three carriers

Files that would break the import limits (25 MB, 250000 data rows) are split
into numbered parts that repeat the header row; import the parts in order.
mock.env holds secrets: it is created with owner-only permissions, its content
is never printed, and an existing one is kept unless --rotate-keys is given.
Standard library only.
"""

from __future__ import annotations

import argparse
import csv
import importlib.util
import os
import secrets
import shutil
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
GENERATOR = REPO / "ml-service" / "scripts" / "make_import_files.py"
DEFAULT_OUT = REPO / "deploy" / "data-pack"

# Import limits (backend/docs/imports.md). Parts stay a little under them.
MAX_ROWS = 250_000
MAX_BYTES = 25 * 1024 * 1024
PART_ROWS = 200_000
PART_BYTES = 20 * 1024 * 1024

# The Imports screen order. Each name is one import type of the API.
ORDER = ("departments", "employees", "sims", "allocations", "usage")


def load_generator():
    spec = importlib.util.spec_from_file_location("make_import_files", GENERATOR)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def write_parts(out: Path, number: int, kind: str, header, rows) -> list[tuple[str, int, int]]:
    """Write rows as NN_kind.csv, or NN_kind_partMM.csv when over a limit.

    Returns [(file name, data rows, bytes)]. Rows are streamed; a part is
    closed when the next row would pass PART_ROWS or PART_BYTES.
    """
    written: list[tuple[str, int, int]] = []
    state = {"handle": None, "writer": None, "name": "", "count": 0, "part": 0}

    def open_part() -> None:
        state["part"] += 1
        name = f"{number:02d}_{kind}_part{state['part']:02d}.csv"
        handle = (out / name).open("w", newline="", encoding="utf-8")
        writer = csv.writer(handle, lineterminator="\r\n")
        writer.writerow(header)
        state.update(handle=handle, writer=writer, name=name, count=0)

    def close_part() -> None:
        handle = state["handle"]
        handle.flush()
        size = handle.tell()
        handle.close()
        written.append((state["name"], state["count"], size))

    open_part()
    for row in rows:
        handle = state["handle"]
        handle.flush()
        line = len(",".join(str(c) for c in row)) + 4  # upper bound: quotes and CRLF
        if state["count"] and (state["count"] >= PART_ROWS or handle.tell() + line > PART_BYTES):
            close_part()
            open_part()
        state["writer"].writerow(row)
        state["count"] += 1
    close_part()

    if len(written) == 1:  # no split needed: drop the part suffix
        new = f"{number:02d}_{kind}.csv"
        (out / written[0][0]).replace(out / new)
        written[0] = (new, written[0][1], written[0][2])
    return written


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    parser.add_argument("--no-sessions", action="store_true", help="leave the large per-session file out of mock-data")
    parser.add_argument("--rotate-keys", action="store_true", help="replace the keys in an existing mock.env")
    args = parser.parse_args()
    out: Path = args.out.resolve()

    gen = load_generator()
    if not gen.SOURCE.exists():
        sys.exit(f"Generator output not found: {gen.SOURCE}")

    # Start clean so a smaller run never leaves stale parts behind. mock.env is kept.
    out.mkdir(parents=True, exist_ok=True)
    for old in out.glob("[0-9][0-9]_*.csv"):
        old.unlink()
    shutil.rmtree(out / "mock-data", ignore_errors=True)

    rows, tiers = gen.read_source()
    mapping = gen.load_mapping(out / "mock-data" / "sim_mapping.csv", tiers)  # file absent: derived from sim_id
    sims = [mapping[s] for s in sorted(tiers)]
    rows.sort(key=lambda r: (int(r["sim_id"]), r["date"]))

    def usage_rows():
        return ([mapping[int(r["sim_id"])]["iccid"], r["date"], *(r[c] for c in gen.USAGE_FIELDS)] for r in rows)

    sim_rows = lambda: ([m["iccid"], m["phone_number"], m["carrier"], m["tier"], ""] for m in sims)  # noqa: E731
    usage_header = ["iccid", "date", *gen.USAGE_FIELDS]
    sims_header = ["iccid", "phone_number", "carrier", "tier", "status"]

    files: dict[str, list[tuple[str, int, int]]] = {
        "departments": write_parts(out, 1, "departments", ["name"], ([n] for n in gen.DEPARTMENTS.values())),
        "employees": write_parts(
            out, 2, "employees", ["name", "email", "department", "job_role"],
            ([f"Demo Employee {int(m['sim_id']):04d}", m["employee_email"], m["department"], gen.JOB_ROLES[m["tier"]]] for m in sims),
        ),
        "sims": write_parts(out, 3, "sims", sims_header, sim_rows()),
        "allocations": write_parts(
            out, 4, "allocations", ["iccid", "employee_email", "start_date"],
            ([m["iccid"], m["employee_email"], ""] for m in sims),
        ),
        "usage": write_parts(out, 5, "usage", usage_header, usage_rows()),
    }

    for parts in files.values():
        for file_name, n, size in parts:
            if n > MAX_ROWS or size > MAX_BYTES:
                sys.exit(f"{file_name} is over the import limits ({n} rows, {size} bytes).")

    # README.txt: the import order.
    lines = [
        "SimGuard demo data pack",
        "",
        "Sign in as a Security Officer, open Imports, and import these files in",
        "this order. For each one choose the import type shown, preview it, then commit.",
        "Wait for each commit to finish before starting the next one.",
        "Files split into parts: import the parts in numbered order, same import type.",
        "",
    ]
    step = 0
    for kind in ORDER:
        for file_name, n, size in files[kind]:
            step += 1
            lines.append(f"{step:2d}. {file_name:32} type: {kind:12} {n:7d} rows  {size / 1_000_000:6.2f} MB")
    lines += [
        "",
        f"Expected totals after all imports: {len(gen.DEPARTMENTS)} departments, {len(sims)} employees, "
        f"{len(sims)} SIMs, {len(sims)} allocations, {len(rows)} usage rows.",
        "Committing the usage files queues a scoring run.",
        "",
        "mock-data/ and mock.env are for the carrier mock service, not for the Imports screen.",
    ]
    (out / "README.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")

    # Carrier mock data: whole sims and usage files (the mock has no import limits).
    mock = out / "mock-data"
    mock.mkdir(parents=True, exist_ok=True)
    gen.write_csv(mock / "sim_mapping.csv", gen.MAPPING_FIELDS, ([m[c] for c in gen.MAPPING_FIELDS] for m in sims))
    gen.write_csv(mock / "sims.csv", sims_header, sim_rows())
    gen.write_csv(mock / "usage.csv", usage_header, usage_rows())
    sessions = gen.ML_SERVICE_ROOT / "data" / "synthetic_cdr_sessions.csv"
    if not args.no_sessions and sessions.exists():
        shutil.copyfile(sessions, mock / "synthetic_cdr_sessions.csv")

    # mock.env: one generated key per carrier. Never printed.
    env_path = out / "mock.env"
    if args.rotate_keys or not env_path.exists():
        body = "".join(f"CarrierMock__Keys__{c}={secrets.token_hex(24)}\n" for c in gen.CARRIERS)
        body += "CarrierMock__DataDir=/data/mock-data\nCarrierMock__SessionsFile=/data/mock-data/synthetic_cdr_sessions.csv\n"
        fd = os.open(env_path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            f.write(body)
        keys_state = "created"
    else:
        keys_state = "kept"

    print(f"Data pack written to {out}")
    for kind in ORDER:
        for file_name, n, size in files[kind]:
            print(f"  {file_name:32} {n:7d} rows  {size / 1_000_000:6.2f} MB")
    print(f"  mock-data/                       {len(sims)} SIMs, {len(rows)} usage rows")
    print(f"  mock.env                         {keys_state} (carriers: {', '.join(gen.CARRIERS)})")


if __name__ == "__main__":
    main()
