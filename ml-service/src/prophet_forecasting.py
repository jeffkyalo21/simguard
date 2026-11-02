"""
prophet_forecasting  --  source of truth for SimGuard Prophet-based forecasting.

This module contains the canonical forecasting logic for the SimGuard pipeline.
Both **training feature generation** and the **production scoring path** must
derive their Prophet features exclusively from :func:`attach_to_daily`, which
guarantees temporal causal correctness (no look-ahead leakage).

``prophet_burn_rate.ipynb`` is historical and is retained only for provenance
and reproducibility of the original full-cycle forecasts stored in
``data/prophet_features.parquet``.

Key design choices
------------------
* **Checkpoint forecasting** -- instead of fitting on the entire billing cycle
  and broadcasting one forecast to every day (which leaks future information
  into early days), we fit at discrete *checkpoint days* (10, 20, 25) using
  only data strictly before the checkpoint.  Each daily row receives the
  latest checkpoint forecast whose ``available_from_date <= row date``.

* **Speed** -- ``uncertainty_samples=0`` (only ``yhat`` is used downstream)
  and ``n_changepoints=8`` (a 30-day cycle has little data for the default 25)
  cut single-fit times roughly in half.

* **Parallelism** -- :func:`fit_many` distributes fits across cores with
  ``joblib.Parallel``.
"""

from __future__ import annotations

import logging
import os
import warnings
from typing import List, Optional, Sequence, Tuple

import numpy as np
import pandas as pd
from joblib import Parallel, delayed

# Prophet is heavy; defer import to first use so tests that mock it aren't
# penalised at import time.
_Prophet = None


def _get_prophet():
    """Lazy-import Prophet with verbose logging suppressed."""
    global _Prophet
    if _Prophet is None:
        from prophet import Prophet as _P
        for name in ("cmdstanpy", "prophet"):
            lg = logging.getLogger(name)
            lg.setLevel(logging.ERROR)
            for h in lg.handlers:
                h.setLevel(logging.ERROR)
        logging.getLogger("cmdstanpy").disabled = True
        warnings.filterwarnings("ignore", module="prophet")
        warnings.filterwarnings("ignore", module="cmdstanpy")
        _Prophet = _P
    return _Prophet


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

CYCLE_LEN: int = 30
"""Fixed billing-cycle length in days."""

CHECKPOINTS: Tuple[int, ...] = (10, 20, 25)
"""Day-in-cycle boundaries at which forecasts are generated.
A checkpoint *C* uses rows with ``day_in_cycle < C`` (i.e. days 0..C-1)."""

MIN_HISTORY_DAYS: int = 7
"""Cold-start threshold:  at least this many daily rows are needed
before Prophet can produce a meaningful fit."""

TIER_BUNDLE_GB_SORTED: List[Tuple[str, int]] = [
    ("support", 20),
    ("professional", 40),
    ("manager", 60),
    ("field", 100),
]
"""Tier name and corresponding bundle in GB, sorted from smallest to largest."""

TIER_RANK = {t: i for i, (t, _) in enumerate(TIER_BUNDLE_GB_SORTED)}
"""Map tier name -> ordinal rank (0 = smallest bundle)."""

TIER_BUNDLE_MB = {t: gb * 1024 for t, gb in TIER_BUNDLE_GB_SORTED}
"""Map tier name -> bundle in MB."""

SIM_START = pd.Timestamp("2025-01-01")
"""Global day-0 for offset -> date mapping (must match the generator)."""


# ---------------------------------------------------------------------------
# recommend_tier  (verbatim from prophet_burn_rate.ipynb, Cell 11)
# ---------------------------------------------------------------------------

def recommend_tier(predicted_final_cum_mb: float) -> Optional[str]:
    """Return the cheapest tier whose bundle covers the predicted cumulative MB.

    Returns ``None`` when ``predicted_final_cum_mb`` is NaN (cold-start).
    """
    if pd.isna(predicted_final_cum_mb):
        return None
    predicted_gb = predicted_final_cum_mb / 1024
    for tier_name, bundle_gb in TIER_BUNDLE_GB_SORTED:
        if predicted_gb <= bundle_gb:
            return tier_name
    return "field"


def _recommendation_label(
    current_tier: str,
    recommended: Optional[str],
) -> str:
    """Compare ranks to produce 'upgrade' / 'downgrade' / 'no_change' /
    'insufficient_history'."""
    if recommended is None:
        return "insufficient_history"
    rec_rank = TIER_RANK.get(recommended)
    cur_rank = TIER_RANK.get(current_tier)
    if rec_rank is None or cur_rank is None:
        return "insufficient_history"
    if rec_rank < cur_rank:
        return "downgrade"
    elif rec_rank > cur_rank:
        return "upgrade"
    return "no_change"


# ---------------------------------------------------------------------------
# forecast_cycle  (verbatim logic from prophet_burn_rate.ipynb, Cell 6,
#                   with speed tuning applied)
# ---------------------------------------------------------------------------

def forecast_cycle(
    sim_df: pd.DataFrame,
    bundle_mb: float,
) -> Optional[dict]:
    """Fit Prophet on cumulative ``total_mb`` within a billing cycle and
    forecast to the last day of that cycle.

    Parameters
    ----------
    sim_df : DataFrame
        Rows within ONE ``(sim_id, billing_cycle_num)`` group.  Must contain
        columns ``[date, total_mb, cycle_end_date]``, sorted by date.
    bundle_mb : float
        The SIM tier bundle in MB.

    Returns
    -------
    dict or None
        ``None`` when ``len(sim_df) < MIN_HISTORY_DAYS`` (cold-start).
        Otherwise a dict with keys:
        ``forecast_df``, ``predicted_final_cum``, ``projected_overage_mb``,
        ``depletion_flag``, ``days_to_depletion``, ``cycle_end``,
        ``last_obs_date``.
    """
    Prophet = _get_prophet()

    sim_df = sim_df.sort_values("date").reset_index(drop=True)
    n_elapsed = len(sim_df)

    if n_elapsed < MIN_HISTORY_DAYS:
        return None  # cold-start: not enough history

    # Cumulative usage within cycle
    cum_mb = sim_df["total_mb"].cumsum().values

    prophet_df = pd.DataFrame({
        "ds": sim_df["date"],
        "y": cum_mb,
    })

    # Last day of the fixed 30-day billing cycle
    cycle_end = pd.Timestamp(sim_df["cycle_end_date"].iloc[0])

    # Future dates: from the day after last observed to cycle end
    last_obs = sim_df["date"].iloc[-1]
    future_dates = pd.date_range(
        start=last_obs + pd.Timedelta(days=1),
        end=cycle_end,
        freq="D",
    )

    m = Prophet(
        daily_seasonality=False,
        weekly_seasonality=True,
        yearly_seasonality=False,
        changepoint_prior_scale=0.3,
        n_changepoints=8,
        uncertainty_samples=0,
    )
    m.fit(prophet_df)

    if len(future_dates) == 0:
        forecast = m.predict(prophet_df[["ds"]])
        pred_final = float(forecast["yhat"].iloc[-1])
        full_forecast = forecast
    else:
        future_df = pd.DataFrame({"ds": pd.concat(
            [prophet_df["ds"], pd.Series(future_dates)],
            ignore_index=True,
        )})
        forecast = m.predict(future_df)
        pred_final = float(
            forecast[forecast["ds"] == cycle_end]["yhat"].values[0]
        )
        full_forecast = forecast

    overage_mb = pred_final - bundle_mb
    dep_flag = bool(overage_mb > 0)

    cross = full_forecast[full_forecast["yhat"] >= bundle_mb]
    if len(cross) > 0:
        first_cross_date = cross["ds"].iloc[0]
        days_to_dep = int((first_cross_date - last_obs).days)
    else:
        days_to_dep = float("nan")

    return {
        "forecast_df": full_forecast,
        "predicted_final_cum": pred_final,
        "projected_overage_mb": overage_mb,
        "depletion_flag": dep_flag,
        "days_to_depletion": days_to_dep,
        "cycle_end": cycle_end,
        "last_obs_date": last_obs,
    }


# ---------------------------------------------------------------------------
# Checkpoint forecasting
# ---------------------------------------------------------------------------

def forecast_checkpoint(
    grp: pd.DataFrame,
    bundle_mb: float,
    checkpoint_day: int,
) -> Optional[dict]:
    """Fit Prophet using only rows with ``day_in_cycle < checkpoint_day``.

    This ensures that forecasts generated at checkpoint *C* never see data
    from day *C* or later, preventing look-ahead leakage.

    Parameters
    ----------
    grp : DataFrame
        All daily rows for ONE ``(sim_id, billing_cycle_num)`` group.
        Must contain columns ``[date, total_mb, cycle_end_date, day_in_cycle]``.
    bundle_mb : float
        Tier bundle in MB.
    checkpoint_day : int
        Only rows with ``day_in_cycle < checkpoint_day`` are used for fitting.

    Returns
    -------
    dict or None
        ``None`` when there are fewer than ``MIN_HISTORY_DAYS`` rows before the
        checkpoint.  Otherwise the same fields as :func:`forecast_cycle` plus:
        ``checkpoint_day``, ``fit_end_date``, ``available_from_date``.
    """
    grp = grp.sort_values("date").reset_index(drop=True)

    # Rows strictly before the checkpoint day
    fit_rows = grp[grp["day_in_cycle"] < checkpoint_day].copy()

    if len(fit_rows) < MIN_HISTORY_DAYS:
        return None

    # cycle_start is needed to compute available_from_date
    cycle_start = pd.Timestamp(grp["cycle_start_date"].iloc[0]) if "cycle_start_date" in grp.columns else (
        pd.Timestamp(grp["date"].min()) - pd.Timedelta(days=int(grp["day_in_cycle"].min()))
    )

    # Feed through forecast_cycle (which handles all Prophet mechanics)
    result = forecast_cycle(fit_rows, bundle_mb)

    if result is None:
        return None

    fit_end_date = result["last_obs_date"]
    available_from_date = cycle_start + pd.Timedelta(days=checkpoint_day)

    assert fit_end_date < available_from_date, (
        f"Causality violated: fit_end_date={fit_end_date} >= "
        f"available_from_date={available_from_date} "
        f"(checkpoint_day={checkpoint_day})"
    )

    result["checkpoint_day"] = checkpoint_day
    result["fit_end_date"] = fit_end_date
    result["available_from_date"] = available_from_date

    return result


def _fit_one(
    grp: pd.DataFrame,
    sim_id: int,
    cycle: int,
    tier: str,
    bundle_mb: float,
    checkpoint_day: int,
) -> Tuple[Optional[dict], Optional[dict]]:
    """Worker function for one (sim_id, cycle, checkpoint) fit."""
    try:
        res = forecast_checkpoint(grp, bundle_mb, checkpoint_day)
    except Exception as exc:
        return None, {
            "sim_id": sim_id,
            "billing_cycle_num": cycle,
            "checkpoint_day": checkpoint_day,
            "error": str(exc),
        }

    if res is None:
        return None, None  # cold-start, not an error

    rec_tier = recommend_tier(res["predicted_final_cum"])
    rec_label = _recommendation_label(tier, rec_tier)

    row = {
        "sim_id": sim_id,
        "billing_cycle_num": cycle,
        "checkpoint_day": checkpoint_day,
        "fit_end_date": res["fit_end_date"],
        "available_from_date": res["available_from_date"],
        "predicted_final_cum": res["predicted_final_cum"],
        "projected_overage_mb": res["projected_overage_mb"],
        "days_to_depletion": res["days_to_depletion"],
        "recommendation": rec_label,
        "recommended_tier": rec_tier,
    }

    return row, None


def fit_many(
    groups: pd.core.groupby.DataFrameGroupBy,
    checkpoints: Sequence[int] = CHECKPOINTS,
    n_jobs: int = -1,
) -> Tuple[pd.DataFrame, List[dict]]:
    """Run :func:`forecast_checkpoint` for every (sim, cycle, checkpoint).

    Parameters
    ----------
    groups : DataFrameGroupBy
        ``daily_agg.groupby(['sim_id', 'billing_cycle_num'])``.
    checkpoints : sequence of int
        Checkpoint days to fit (default: ``CHECKPOINTS``).
    n_jobs : int
        Parallelism level passed to ``joblib.Parallel`` (``-1`` = all cores).

    Returns
    -------
    (forecasts_df, errors)
        ``forecasts_df`` has one row per successful
        ``(sim_id, billing_cycle_num, checkpoint_day)`` fit.
        ``errors`` is a list of dicts describing any fit failures.
    """
    tasks = []
    for (sim_id, cycle), grp in groups:
        tier = str(grp["tier"].iloc[0])
        bundle_mb = float(grp["bundle_mb"].iloc[0])
        for cp in checkpoints:
            tasks.append((grp, sim_id, cycle, tier, bundle_mb, cp))

    results = Parallel(n_jobs=n_jobs, verbose=0)(
        delayed(_fit_one)(*t) for t in tasks
    )

    rows = []
    errors: List[dict] = []
    for row, err in results:
        if err is not None:
            errors.append(err)
        elif row is not None:
            rows.append(row)

    forecasts_df = pd.DataFrame(rows)
    if not forecasts_df.empty:
        forecasts_df["fit_end_date"] = pd.to_datetime(forecasts_df["fit_end_date"])
        forecasts_df["available_from_date"] = pd.to_datetime(forecasts_df["available_from_date"])

    return forecasts_df, errors


# ---------------------------------------------------------------------------
# attach_to_daily  --  causal join of checkpoint forecasts onto daily rows
# ---------------------------------------------------------------------------

_FORECAST_VALUE_COLS = [
    "checkpoint_day",
    "fit_end_date",
    "predicted_final_cum",
    "projected_overage_mb",
    "days_to_depletion",
    "recommendation",
    "recommended_tier",
]


def attach_to_daily(
    daily_df: pd.DataFrame,
    forecasts_df: pd.DataFrame,
) -> pd.DataFrame:
    """Attach the latest causal checkpoint forecast to every daily row.

    For each ``(sim_id, date)`` row, we pick the checkpoint forecast of the
    same billing cycle whose ``available_from_date <= date`` using
    ``pd.merge_asof`` (direction ``'backward'``).

    Rows before the first available checkpoint get NaN forecast fields and
    ``prophet_missing_flag = 1``.

    Causality invariant
    -------------------
    For every row with a non-NaN forecast, ``fit_end_date < date`` is
    asserted, meaning the forecast never saw the current day's data.

    Parameters
    ----------
    daily_df : DataFrame
        Must contain ``[sim_id, date, billing_cycle_num]``.
    forecasts_df : DataFrame
        Output of :func:`fit_many`.

    Returns
    -------
    DataFrame
        ``daily_df`` with forecast columns attached.
    """
    daily = daily_df.copy()
    daily["date"] = pd.to_datetime(daily["date"])

    if forecasts_df.empty:
        for col in _FORECAST_VALUE_COLS:
            daily[col] = np.nan
        daily["prophet_missing_flag"] = 1
        return daily

    fc = forecasts_df.copy()
    fc["available_from_date"] = pd.to_datetime(fc["available_from_date"])
    fc["fit_end_date"] = pd.to_datetime(fc["fit_end_date"])

    # merge_asof requires the left_on key to be monotonically sorted.
    # Save original order so we can restore it after the merge.
    daily["_orig_order"] = np.arange(len(daily))
    daily = daily.sort_values("date").reset_index(drop=True)
    fc = fc.sort_values("available_from_date").reset_index(drop=True)

    merged = pd.merge_asof(
        daily,
        fc[["sim_id", "billing_cycle_num", "available_from_date"] + _FORECAST_VALUE_COLS],
        by=["sim_id", "billing_cycle_num"],
        left_on="date",
        right_on="available_from_date",
        direction="backward",
    )

    # prophet_missing_flag: 1 when no checkpoint was available for this row
    merged["prophet_missing_flag"] = merged["predicted_final_cum"].isna().astype(int)

    # Drop the merge key (available_from_date is already in _FORECAST_VALUE_COLS
    # via fit_end_date; the right-side key is renamed automatically)
    if "available_from_date" in merged.columns:
        merged.drop(columns=["available_from_date"], inplace=True)

    # Causality assertion:  fit_end_date must be strictly before the row's date
    has_forecast = merged["fit_end_date"].notna()
    if has_forecast.any():
        violations = merged.loc[has_forecast, "fit_end_date"] >= merged.loc[has_forecast, "date"]
        assert not violations.any(), (
            f"Causality violation: {violations.sum()} rows where "
            f"fit_end_date >= date"
        )

    # Restore original row order and drop the temp column
    merged = merged.sort_values("_orig_order").drop(columns=["_orig_order"]).reset_index(drop=True)

    return merged


# ---------------------------------------------------------------------------
# should_refresh_forecast  --  for scheduled batch jobs
# ---------------------------------------------------------------------------

def should_refresh_forecast(
    day_in_cycle: int,
    last_checkpoint_fitted: Optional[int],
) -> bool:
    """Return ``True`` when ``day_in_cycle`` is a checkpoint day and that
    checkpoint has not been fitted yet.

    This function is intended for a **scheduled batch job** that runs once per
    day and decides whether to kick off new Prophet fits.  It is *not* used on
    the fast scoring path (which simply reads pre-computed forecasts via
    :func:`attach_to_daily`).

    Parameters
    ----------
    day_in_cycle : int
        Current day within the billing cycle (0-indexed, 0..29).
    last_checkpoint_fitted : int or None
        The highest checkpoint_day that has already been fitted for this
        ``(sim_id, billing_cycle_num)``.  ``None`` if no checkpoint has been
        fitted yet.

    Returns
    -------
    bool
    """
    if day_in_cycle not in CHECKPOINTS:
        return False

    if last_checkpoint_fitted is None:
        return True

    return day_in_cycle > last_checkpoint_fitted


# ---------------------------------------------------------------------------
# __main__  --  smoke test
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    import time

    DATA_DIR = os.path.join(os.path.dirname(__file__), "..", "data")
    CSV_PATH = os.path.join(DATA_DIR, "synthetic_cdr_daily_agg.csv")

    print("=" * 70)
    print("prophet_forecasting.py  --  SMOKE TEST")
    print("=" * 70)

    #  Load a few SIMs 
    agg = pd.read_csv(CSV_PATH, parse_dates=["date"])
    agg["date"] = pd.to_datetime(agg["date"])
    agg["day_offset"] = (agg["date"] - SIM_START).dt.days
    agg["billing_cycle_num"] = (agg["day_offset"] // CYCLE_LEN).astype(int)
    agg["day_in_cycle"] = (agg["day_offset"] % CYCLE_LEN).astype(int)
    agg["cycle_start_date"] = SIM_START + pd.to_timedelta(
        agg["billing_cycle_num"] * CYCLE_LEN, unit="D",
    )
    agg["cycle_end_date"] = agg["cycle_start_date"] + pd.Timedelta(days=CYCLE_LEN - 1)
    agg["bundle_mb"] = agg["tier"].map(TIER_BUNDLE_MB)

    # Pick the first 5 SIMs and cycle 0
    sample_sims = sorted(agg["sim_id"].unique())[:5]
    sample = agg[(agg["sim_id"].isin(sample_sims)) & (agg["billing_cycle_num"] == 0)]
    groups = sample.groupby(["sim_id", "billing_cycle_num"])

    print(f"\nSample SIMs: {sample_sims}")
    print(f"Cycle: 0 | Groups: {groups.ngroups}")

    # fit_many 
    t0 = time.time()
    fc_df, errors = fit_many(groups, checkpoints=CHECKPOINTS, n_jobs=1)
    elapsed = time.time() - t0

    print(f"\nfit_many completed in {elapsed:.1f}s")
    print(f"  Forecast rows : {len(fc_df)}")
    print(f"  Errors        : {len(errors)}")
    if not fc_df.empty:
        print(f"\n  Columns: {list(fc_df.columns)}")
        print(fc_df.to_string(index=False))

    # attach_to_daily 
    daily_with_fc = attach_to_daily(sample, fc_df)

    print(f"\nattach_to_daily output shape: {daily_with_fc.shape}")
    print(f"  prophet_missing_flag distribution:")
    print(f"    {daily_with_fc['prophet_missing_flag'].value_counts().to_dict()}")

    # Causality check 
    has_fc = daily_with_fc["fit_end_date"].notna()
    if has_fc.any():
        violations = daily_with_fc.loc[has_fc, "fit_end_date"] >= daily_with_fc.loc[has_fc, "date"]
        n_violations = violations.sum()
        print(f"\n  Causality check: {n_violations} violations out of "
              f"{has_fc.sum()} rows with forecast")
        assert n_violations == 0, f"CAUSALITY VIOLATED: {n_violations} rows!"
        print("  [PASS] No attached forecast used the row's own date or later.")
    else:
        print("\n  No forecasts to check (all cold-start).")

    #  should_refresh_forecast quick test 
    print("\n  should_refresh_forecast tests:")
    assert should_refresh_forecast(10, None) is True
    assert should_refresh_forecast(10, 10) is False
    assert should_refresh_forecast(20, 10) is True
    assert should_refresh_forecast(25, 20) is True
    assert should_refresh_forecast(5, None) is False
    assert should_refresh_forecast(15, 10) is False
    print("  [PASS] All should_refresh_forecast assertions passed.")

    print("\n" + "=" * 70)
    print("SMOKE TEST COMPLETE -- ALL CHECKS PASSED")
    print("=" * 70)
