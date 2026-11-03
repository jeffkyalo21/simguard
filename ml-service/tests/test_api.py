"""Tests for the FastAPI service in app/ (parquet reader, real artifacts)."""

import hashlib
import shutil
from dataclasses import replace
from pathlib import Path

import numpy as np
import pandas as pd
import pytest
from fastapi.testclient import TestClient

from app.config import REQUIRED_ARTIFACTS, Settings
from app.main import create_app
from src.risk_scoring import score as risk_score

ROOT = Path(__file__).resolve().parents[1]
ARTIFACT_DIR = ROOT / "artifacts"
DAILY_CSV = ROOT / "data" / "synthetic_cdr_daily_agg.csv"
AS_OF = "2025-03-15"
UNKNOWN = 999_999


@pytest.fixture(scope="module")
def client():
    with TestClient(create_app(Settings(artifact_dir=ARTIFACT_DIR, reader="parquet"))) as c:
        yield c


@pytest.fixture(scope="module")
def daily():
    return pd.read_csv(DAILY_CSV, parse_dates=["date"])


def assert_problem(resp, status):
    assert resp.status_code == status, resp.text
    body = resp.json()
    assert body["status"] == status
    assert isinstance(body["title"], str) and body["title"]
    assert isinstance(body["detail"], str) and body["detail"]
    return body


# ------------------------------------------------------------------ /v1/score


def test_score_happy_path(client):
    sims = list(range(30))
    resp = client.post("/v1/score", json={"simIds": sims, "asOfDate": AS_OF})
    assert resp.status_code == 200, resp.text
    body = resp.json()
    assert body["asOfDate"] == AS_OF and body["notFound"] == []
    assert [r["simId"] for r in body["results"]] == sims
    first = body["results"][0]
    assert set(first) == {"simId", "date", "probability", "bucket", "reasons", "similarCases"}
    assert first["date"] == AS_OF and first["similarCases"] == []
    assert len(first["reasons"]) == 5
    assert set(first["reasons"][0]) == {"feature", "value", "contribution", "direction"}
    for r in body["results"]:
        p = r["probability"]
        assert 0.0 <= p <= 1.0
        assert r["bucket"] == ("clear" if p < 0.10 else "flag" if p > 0.90 else "escalated")


def test_score_matches_risk_scoring(client, daily):
    # Same SIMs, same day, whole history straight into risk_scoring.score.
    sims = list(range(0, 1000, 7))
    resp = client.post("/v1/score", json={"simIds": sims, "asOfDate": AS_OF})
    api = pd.DataFrame(resp.json()["results"])
    ref = risk_score(daily[daily["sim_id"].isin(sims)], as_of_dates=AS_OF, artifact_dir=ARTIFACT_DIR)
    ref = ref.set_index("sim_id").loc[api["simId"]]
    assert len(api) == len(sims)
    assert np.array_equal(api["probability"].to_numpy(), ref["probability"].to_numpy())
    assert list(api["bucket"]) == list(ref["bucket"])
    for got, want in zip(api["reasons"], ref["reasons"]):
        assert [g["feature"] for g in got] == [w["feature"] for w in want]
        assert np.allclose([g["contribution"] for g in got], [w["contribution"] for w in want], atol=0, rtol=0)


def test_score_unknown_sim(client):
    resp = client.post("/v1/score", json={"simIds": [3, UNKNOWN, 4], "asOfDate": AS_OF})
    assert resp.status_code == 200
    body = resp.json()
    assert [r["simId"] for r in body["results"]] == [3, 4]
    assert body["notFound"] == [UNKNOWN]


def test_score_date_without_data(client):
    resp = client.post("/v1/score", json={"simIds": [3, 4], "asOfDate": "2030-01-01"})
    assert resp.status_code == 200
    assert resp.json()["results"] == [] and resp.json()["notFound"] == [3, 4]


def test_score_duplicate_ids_scored_once(client):
    body = client.post("/v1/score", json={"simIds": [5, 5, 6], "asOfDate": AS_OF}).json()
    assert [r["simId"] for r in body["results"]] == [5, 6]


def test_score_empty_list(client):
    body = assert_problem(client.post("/v1/score", json={"simIds": [], "asOfDate": AS_OF}), 422)
    assert body["errors"][0]["field"] == "simIds"


def test_score_too_many_sims(client):
    body = assert_problem(client.post("/v1/score", json={"simIds": list(range(5001)), "asOfDate": AS_OF}), 422)
    assert "5000" in body["detail"]


def test_score_5000_sims_allowed(client):
    resp = client.post("/v1/score", json={"simIds": list(range(5000)), "asOfDate": AS_OF})
    assert resp.status_code == 200
    assert len(resp.json()["results"]) == 1000 and len(resp.json()["notFound"]) == 4000


@pytest.mark.parametrize("bad", ["2025-02-30", "15/03/2025", "2025-3-15", "2025-03-15T00:00:00", 20250315, None])
def test_score_bad_date(client, bad):
    body = assert_problem(client.post("/v1/score", json={"simIds": [1], "asOfDate": bad}), 422)
    assert body["errors"][0]["field"] == "asOfDate"


@pytest.mark.parametrize("payload", [
    {"simIds": ["1"], "asOfDate": AS_OF},          # strings are not SIM ids
    {"simIds": [-1], "asOfDate": AS_OF},
    {"simIds": [1]},                                # missing asOfDate
    {"simIds": [1], "asOfDate": AS_OF, "extra": 1},  # unknown field
    {"sim_ids": [1], "as_of_date": AS_OF},          # snake_case is not the contract
])
def test_score_bad_body(client, payload):
    assert_problem(client.post("/v1/score", json=payload), 422)


# --------------------------------------------------------- /v1/recommend-tier


def test_recommend_happy_path(client):
    sims = [187, 407, 12]
    resp = client.post("/v1/recommend-tier", json={"simIds": sims, "billingCycle": 1})
    assert resp.status_code == 200, resp.text
    body = resp.json()
    assert body["billingCycle"] == 1 and body["notFound"] == [] and body["failed"] == []
    assert [r["simId"] for r in body["results"]] == sims
    pre = pd.read_parquet(ROOT / "data" / "prophet_checkpoints.parquet").set_index(
        ["sim_id", "billing_cycle_num", "checkpoint_day"])
    for r in body["results"]:
        assert set(r) >= {"recommendation", "recommendedTier", "projectedOverageMb", "daysToDepletion", "checkpointDay"}
        assert r["checkpointDay"] == 25
        want = pre.loc[(r["simId"], 1, 25)]
        assert r["recommendation"] == want["recommendation"]
        assert r["recommendedTier"] == want["recommended_tier"]
        assert r["projectedOverageMb"] == pytest.approx(want["projected_overage_mb"], abs=0)
    assert body["results"][0]["recommendation"] == "upgrade" and body["results"][0]["daysToDepletion"] == 5
    assert set(body["timings"]) == {"readMs", "fitMs", "totalMs", "fitted", "precomputed"}


def test_recommend_fitted_matches_precomputed():
    settings = Settings(artifact_dir=ARTIFACT_DIR, reader="parquet", use_precomputed_prophet=False, prophet_n_jobs=1)
    with TestClient(create_app(settings)) as c:
        body = c.post("/v1/recommend-tier", json={"simIds": [187, 12], "billingCycle": 1}).json()
    pre = pd.read_parquet(ROOT / "data" / "prophet_checkpoints.parquet").set_index(
        ["sim_id", "billing_cycle_num", "checkpoint_day"])
    assert body["timings"]["fitted"] == 2
    for r in body["results"]:
        assert r["source"] == "fitted"
        want = pre.loc[(r["simId"], 1, 25)]
        assert r["recommendation"] == want["recommendation"]
        assert r["projectedOverageMb"] == pytest.approx(want["projected_overage_mb"], rel=1e-9)


def test_recommend_checkpoint_follows_data(tmp_path, daily):
    # Cycle 2 starts 2025-03-02. SIM 0 has days 0..14 (checkpoint 10 reached),
    # SIM 1 only days 0..5 (no checkpoint yet).
    start = pd.Timestamp("2025-03-02")
    part = pd.concat([
        daily[(daily["sim_id"] == 0) & (daily["date"] < start + pd.Timedelta(days=15))],
        daily[(daily["sim_id"] == 1) & (daily["date"] < start + pd.Timedelta(days=6))],
    ])
    path = tmp_path / "daily.csv"
    part.to_csv(path, index=False)
    settings = Settings(artifact_dir=ARTIFACT_DIR, reader="parquet", daily_usage_path=path,
                        use_precomputed_prophet=False, prophet_n_jobs=1)
    with TestClient(create_app(settings)) as c:
        body = c.post("/v1/recommend-tier", json={"simIds": [0, 1], "billingCycle": 2}).json()
    sim0, sim1 = body["results"]
    assert sim0["checkpointDay"] == 10 and sim0["source"] == "fitted"
    assert sim0["recommendation"] in {"upgrade", "downgrade", "no_change"}
    assert sim1["checkpointDay"] is None and sim1["recommendation"] == "insufficient_history"
    assert sim1["recommendedTier"] is None and sim1["projectedOverageMb"] is None


def test_recommend_unknown_sim(client):
    body = client.post("/v1/recommend-tier", json={"simIds": [12, UNKNOWN], "billingCycle": 1}).json()
    assert [r["simId"] for r in body["results"]] == [12]
    assert body["notFound"] == [UNKNOWN]


def test_recommend_empty_list(client):
    assert_problem(client.post("/v1/recommend-tier", json={"simIds": [], "billingCycle": 1}), 422)


def test_recommend_too_many_sims(client):
    body = assert_problem(client.post("/v1/recommend-tier", json={"simIds": list(range(201)), "billingCycle": 1}), 422)
    assert "200" in body["detail"]


@pytest.mark.parametrize("cycle", [-1, "1", 1.5, None])
def test_recommend_bad_cycle(client, cycle):
    assert_problem(client.post("/v1/recommend-tier", json={"simIds": [1], "billingCycle": cycle}), 422)


# ---------------------------------------------------------- health, version


def test_health_ok(client):
    resp = client.get("/health")
    assert resp.status_code == 200
    assert resp.json() == {"status": "ok", "artifactsLoaded": True, "missingArtifacts": [], "reader": "parquet"}


def test_version(client):
    body = client.get("/version").json()
    assert body["apiVersion"] == "v1"
    assert [m["file"] for m in body["models"]] == list(REQUIRED_ARTIFACTS)
    xgb_file = next(m for m in body["models"] if m["file"] == "xgboost_triage.joblib")
    data = (ARTIFACT_DIR / "xgboost_triage.joblib").read_bytes()
    assert xgb_file["sizeBytes"] == len(data)
    assert xgb_file["sha256"] == hashlib.sha256(data).hexdigest()
    for lib in ("python", "xgboost", "scikit-learn", "shap", "prophet", "fastapi", "pandas"):
        assert body["libraries"][lib]


def test_unknown_route_is_json(client):
    assert_problem(client.get("/v1/nope"), 404)


# ------------------------------------------------------- missing artifacts


@pytest.fixture()
def partial_artifacts(tmp_path):
    for name in REQUIRED_ARTIFACTS:
        if name != "xgboost_triage.joblib":
            shutil.copy(ARTIFACT_DIR / name, tmp_path / name)
    return tmp_path


def test_missing_artifact_returns_503(partial_artifacts):
    settings = Settings(artifact_dir=partial_artifacts, reader="parquet")
    with TestClient(create_app(settings, fail_fast=False)) as c:
        body = assert_problem(c.post("/v1/score", json={"simIds": [1], "asOfDate": AS_OF}), 503)
        assert "xgboost_triage.joblib" in body["detail"]
        assert_problem(c.post("/v1/recommend-tier", json={"simIds": [1], "billingCycle": 1}), 503)
        health = c.get("/health")
        assert health.status_code == 503
        assert health.json()["artifactsLoaded"] is False
        assert health.json()["missingArtifacts"] == ["xgboost_triage.joblib"]


def test_missing_artifact_fails_startup(partial_artifacts):
    app = create_app(Settings(artifact_dir=partial_artifacts, reader="parquet"))
    with pytest.raises(RuntimeError, match="xgboost_triage.joblib"):
        with TestClient(app):
            pass


def test_postgres_reader_needs_database_url():
    app = create_app(Settings(artifact_dir=ARTIFACT_DIR, reader="postgres", database_url=""))
    with pytest.raises(ValueError, match="ML_DATABASE_URL"):
        with TestClient(app):
            pass


def test_unknown_reader_fails_startup():
    with pytest.raises(ValueError, match="READER"):
        with TestClient(create_app(replace(Settings(artifact_dir=ARTIFACT_DIR), reader="mysql"))):
            pass
