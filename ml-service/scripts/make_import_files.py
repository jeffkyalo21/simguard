"""Write import-ready CSV files for the backend CSV import (issue #14).

Usage, from ml-service/:

    venv/Scripts/python scripts/make_import_files.py [--out DIR]

Reads the generator output data/synthetic_cdr_daily_agg.csv and writes, into
data/import_samples/ (git-ignored):

    departments.csv       one department per tier
    employees.csv         one employee per SIM
    sims.csv              one SIM per generator sim_id
    allocations.csv       each SIM to its employee (start date left empty)
    usage.csv             every generator row (90000), sim_id replaced by iccid
    usage_perf_100k.csv   100000 more rows for the timing test (see below)
    sim_mapping.csv       sim_id to iccid, phone number, carrier, employee

The formats are the ones in backend/docs/imports.md. Import them in that
order (departments, employees, sims, allocations, usage).

The generator has no department, employee or carrier data, so this script
derives a simple, documented assignment:

    department   by tier: field = Field Operations, manager = Management,
                 professional = Professional Services, support = Customer Support
    employee     one per SIM, "Demo Employee NNNN", demo.employee.NNNN@simguard.local,
                 in the SIM's tier department, job role by tier
    carrier      by sim_id modulo 3: 0 = Safaricom, 1 = Airtel, 2 = Telkom
    iccid        8925400 + sim_id as 11 digits + a Luhn check digit (19 digits)
    phone        +2547 + (10000000 + sim_id) as 8 digits (E.164, Kenyan mobile)
    tier         the SIM's tier in the generator (constant per SIM)

These are fake identifiers. They are computed from sim_id and stored in
sim_mapping.csv; when that file exists its values are reused, so they never
change between runs. The SIMs are not allocated to the employees: an import
never creates allocations.

usage_perf_100k.csv has 100 days per SIM after the generator's last date
(1000 SIMs x 100 days = 100000 rows). Each day repeats the SIM's generator
row from the same position in the 90 day cycle, so all values are realistic.
"""

from __future__ import annotations

import argparse
import csv
import sys
from collections import defaultdict
from datetime import date, timedelta
from pathlib import Path

ML_SERVICE_ROOT = Path(__file__).resolve().parents[1]
SOURCE = ML_SERVICE_ROOT / "data" / "synthetic_cdr_daily_agg.csv"
DEFAULT_OUT = ML_SERVICE_ROOT / "data" / "import_samples"

PERF_DAYS = 100

DEPARTMENTS = {
    "field": "Field Operations",
    "manager": "Management",
    "professional": "Professional Services",
    "support": "Customer Support",
}

JOB_ROLES = {
    "field": "Field Technician",
    "manager": "Manager",
    "professional": "Consultant",
    "support": "Support Agent",
}

CARRIERS = ("Safaricom", "Airtel", "Telkom")

USAGE_FIELDS = (
    "total_download_mb", "total_upload_mb", "data_session_count", "data_duration_s",
    "hotspot_sessions", "public_apn_sessions", "unique_towers", "geo_flag_count",
    "peak_mb", "offpeak_mb", "total_mb", "roaming_sessions", "billed_mb", "tier",
    "voice_minutes_used", "sms_count", "voice_minutes_remaining", "sms_remaining",
)

MAPPING_FIELDS = ("sim_id", "iccid", "phone_number", "carrier", "tier", "department", "employee_email")


def luhn_digit(number: str) -> str:
    """Check digit that makes number + digit pass the Luhn test."""
    total = 0
    for i, ch in enumerate(reversed(number)):
        d = int(ch)
        if i % 2 == 0:
            d *= 2
            if d > 9:
                d -= 9
        total += d
    return str((10 - total % 10) % 10)


def iccid_for(sim_id: int) -> str:
    body = f"8925400{sim_id:011d}"
    return body + luhn_digit(body)


def phone_for(sim_id: int) -> str:
    return f"+2547{10000000 + sim_id:08d}"


def employee_email(sim_id: int) -> str:
    return f"demo.employee.{sim_id:04d}@simguard.local"


def read_source() -> tuple[list[dict[str, str]], dict[int, str]]:
    with SOURCE.open(newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    tiers: dict[int, str] = {}
    for row in rows:
        sim_id = int(row["sim_id"])
        tier = row["tier"]
        if tiers.setdefault(sim_id, tier) != tier:
            sys.exit(f"sim_id {sim_id} has more than one tier in {SOURCE.name}.")
    return rows, tiers


def load_mapping(path: Path, tiers: dict[int, str]) -> dict[int, dict[str, str]]:
    mapping: dict[int, dict[str, str]] = {}
    if path.exists():
        with path.open(newline="", encoding="utf-8") as f:
            for row in csv.DictReader(f):
                mapping[int(row["sim_id"])] = row
    for sim_id, tier in sorted(tiers.items()):
        if sim_id not in mapping:
            mapping[sim_id] = {
                "sim_id": str(sim_id),
                "iccid": iccid_for(sim_id),
                "phone_number": phone_for(sim_id),
                "carrier": CARRIERS[sim_id % len(CARRIERS)],
                "tier": tier,
                "department": DEPARTMENTS[tier],
                "employee_email": employee_email(sim_id),
            }
    return mapping


def write_csv(path: Path, header: list[str] | tuple[str, ...], rows) -> int:
    count = 0
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f, lineterminator="\r\n")
        writer.writerow(header)
        for row in rows:
            writer.writerow(row)
            count += 1
    return count


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    args = parser.parse_args()
    out: Path = args.out
    out.mkdir(parents=True, exist_ok=True)

    rows, tiers = read_source()
    mapping_path = out / "sim_mapping.csv"
    mapping = load_mapping(mapping_path, tiers)
    sims = [mapping[s] for s in sorted(tiers)]

    counts = {}
    counts["sim_mapping.csv"] = write_csv(mapping_path, MAPPING_FIELDS, ([m[c] for c in MAPPING_FIELDS] for m in sims))
    counts["departments.csv"] = write_csv(out / "departments.csv", ["name"], ([n] for n in DEPARTMENTS.values()))
    counts["employees.csv"] = write_csv(
        out / "employees.csv",
        ["name", "email", "department", "job_role"],
        ([f"Demo Employee {int(m['sim_id']):04d}", m["employee_email"], m["department"], JOB_ROLES[m["tier"]]] for m in sims),
    )
    counts["sims.csv"] = write_csv(
        out / "sims.csv",
        ["iccid", "phone_number", "carrier", "tier", "status"],
        ([m["iccid"], m["phone_number"], m["carrier"], m["tier"], ""] for m in sims),
    )
    counts["allocations.csv"] = write_csv(
        out / "allocations.csv",
        ["iccid", "employee_email", "start_date"],
        ([m["iccid"], m["employee_email"], ""] for m in sims),
    )

    usage_header = ["iccid", "date", *USAGE_FIELDS]
    rows.sort(key=lambda r: (int(r["sim_id"]), r["date"]))
    counts["usage.csv"] = write_csv(
        out / "usage.csv",
        usage_header,
        ([mapping[int(r["sim_id"])]["iccid"], r["date"], *(r[c] for c in USAGE_FIELDS)] for r in rows),
    )

    by_sim: dict[int, list[dict[str, str]]] = defaultdict(list)
    for r in rows:
        by_sim[int(r["sim_id"])].append(r)
    last_date = max(date.fromisoformat(r["date"]) for r in rows)

    def perf_rows():
        for sim_id in sorted(by_sim):
            days = by_sim[sim_id]
            iccid = mapping[sim_id]["iccid"]
            for i in range(PERF_DAYS):
                source = days[i % len(days)]
                day = last_date + timedelta(days=i + 1)
                yield [iccid, day.isoformat(), *(source[c] for c in USAGE_FIELDS)]

    counts["usage_perf_100k.csv"] = write_csv(out / "usage_perf_100k.csv", usage_header, perf_rows())

    for name, n in counts.items():
        size = (out / name).stat().st_size / 1_000_000
        print(f"{name:22} {n:7} rows  {size:6.2f} MB")
    print(f"Written to {out}")


if __name__ == "__main__":
    main()
