"""Raw tier recommendation for /v1/recommend-tier.

Uses src/prophet_forecasting.py as is: the day 10, 20 and 25 checkpoints,
forecast_checkpoint through fit_many, recommend_tier and the recommendation
labels. For each SIM it returns the forecast of the latest checkpoint the
data reaches in the requested billing cycle.

No "sustained pattern" or cooldown rule is applied here. The backend does that.
"""

from __future__ import annotations

import math
import time
from typing import Optional, Sequence

import pandas as pd

from src.prophet_forecasting import (
    CHECKPOINTS,
    CYCLE_LEN,
    SIM_START,
    TIER_BUNDLE_MB,
    fit_many,
)

PRECOMPUTED_COLUMNS = (
    "sim_id",
    "billing_cycle_num",
    "checkpoint_day",
    "projected_overage_mb",
    "days_to_depletion",
    "recommendation",
    "recommended_tier",
)


def cycle_bounds(billing_cycle: int) -> tuple[pd.Timestamp, pd.Timestamp]:
    """Fixed 30 day cycles counted from SIM_START, as in prophet_forecasting."""
    start = SIM_START + pd.Timedelta(days=CYCLE_LEN * billing_cycle)
    return start, start + pd.Timedelta(days=CYCLE_LEN - 1)


def latest_checkpoint(max_day_in_cycle: int) -> Optional[int]:
    """Latest checkpoint C whose fit rows (days 0..C-1) are all in the data."""
    reached = [c for c in CHECKPOINTS if c <= max_day_in_cycle + 1]
    return max(reached) if reached else None


def load_precomputed(path) -> pd.DataFrame:
    df = pd.read_parquet(path, columns=list(PRECOMPUTED_COLUMNS))
    return df.set_index(["sim_id", "billing_cycle_num", "checkpoint_day"]).sort_index()


def _days(value) -> Optional[int]:
    return None if value is None or (isinstance(value, float) and math.isnan(value)) else int(value)


def _overage(value) -> Optional[float]:
    return None if value is None or (isinstance(value, float) and math.isnan(value)) else float(value)


def recommend(
    usage: pd.DataFrame,
    sim_ids: Sequence[int],
    billing_cycle: int,
    precomputed: Optional[pd.DataFrame],
    n_jobs: int = -1,
) -> tuple[list[dict], list[int], list[dict], dict]:
    """usage: reader rows for the cycle. Returns (results, not_found, failed, timings)."""
    t0 = time.perf_counter()
    start, end = cycle_bounds(billing_cycle)
    usage = usage[(usage["date"] >= start) & (usage["date"] <= end)].copy()
    usage["billing_cycle_num"] = billing_cycle
    usage["day_in_cycle"] = (usage["date"] - start).dt.days.astype(int)
    usage["cycle_start_date"] = start
    usage["cycle_end_date"] = end
    usage["bundle_mb"] = usage["tier"].map(TIER_BUNDLE_MB)

    found = set(usage["sim_id"].unique().tolist())
    not_found = [s for s in sim_ids if s not in found]

    plan: dict[int, dict] = {}
    to_fit: dict[int, list[int]] = {}
    for sim_id, grp in usage.groupby("sim_id", sort=False):
        sim_id = int(sim_id)
        # fit_many takes the tier from the first row of the cycle.
        tier = str(grp["tier"].iloc[0])
        cp = latest_checkpoint(int(grp["day_in_cycle"].max()))
        base = {"sim_id": sim_id, "billing_cycle": billing_cycle, "current_tier": tier, "checkpoint_day": cp}
        if cp is None:
            plan[sim_id] = {**base, "recommendation": "insufficient_history", "recommended_tier": None,
                            "projected_overage_mb": None, "days_to_depletion": None, "source": "none"}
            continue
        key = (sim_id, billing_cycle, cp)
        if precomputed is not None and key in precomputed.index:
            row = precomputed.loc[key]
            plan[sim_id] = {**base, "recommendation": row["recommendation"],
                            "recommended_tier": row["recommended_tier"],
                            "projected_overage_mb": _overage(row["projected_overage_mb"]),
                            "days_to_depletion": _days(row["days_to_depletion"]), "source": "precomputed"}
        else:
            plan[sim_id] = base
            to_fit.setdefault(cp, []).append(sim_id)
    t_read = time.perf_counter()

    failed: list[dict] = []
    n_fitted = 0
    for cp, ids in to_fit.items():
        groups = usage[usage["sim_id"].isin(ids)].groupby(["sim_id", "billing_cycle_num"])
        forecasts, errors = fit_many(groups, checkpoints=(cp,), n_jobs=n_jobs)
        n_fitted += len(ids)
        by_sim = {} if forecasts.empty else forecasts.set_index("sim_id").to_dict("index")
        for err in errors:
            failed.append({"sim_id": int(err["sim_id"]), "checkpoint_day": cp, "error": err["error"]})
        failed_ids = {f["sim_id"] for f in failed}
        for sim_id in ids:
            if sim_id in failed_ids:
                plan.pop(sim_id, None)
                continue
            fc = by_sim.get(sim_id)
            if fc is None:  # fewer than MIN_HISTORY_DAYS rows before the checkpoint
                plan[sim_id].update(recommendation="insufficient_history", recommended_tier=None,
                                    projected_overage_mb=None, days_to_depletion=None, source="fitted")
            else:
                plan[sim_id].update(recommendation=fc["recommendation"], recommended_tier=fc["recommended_tier"],
                                    projected_overage_mb=_overage(fc["projected_overage_mb"]),
                                    days_to_depletion=_days(fc["days_to_depletion"]), source="fitted")
    t_fit = time.perf_counter()

    results = [plan[s] for s in sim_ids if s in plan]
    timings = {
        "read_ms": round((t_read - t0) * 1000, 1),
        "fit_ms": round((t_fit - t_read) * 1000, 1),
        "total_ms": round((t_fit - t0) * 1000, 1),
        "fitted": n_fitted,
        "precomputed": sum(1 for r in results if r["source"] == "precomputed"),
    }
    return results, not_found, failed, timings
