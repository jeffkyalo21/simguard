"""SimGuard ML service.

Run from ml-service/:
    venv/Scripts/python -m uvicorn app.main:app --port 8001

Routes:
    POST /v1/score            Stage 1 to 3 risk score per SIM for one day
    POST /v1/recommend-tier   raw Prophet tier recommendation per SIM
    GET  /health              artifacts loaded or not
    GET  /version             model files (size, sha256) and library versions

Errors are JSON {title, status, detail}. 422 for bad input, 503 when
artifacts are missing. The service refuses to start if a required artifact
is missing or the reader cannot load its data.
"""

from __future__ import annotations

import hashlib
import platform
import time
from contextlib import asynccontextmanager
from http import HTTPStatus
from importlib.metadata import PackageNotFoundError, version as package_version
from typing import Optional

import pandas as pd
from fastapi import Depends, FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException as StarletteHTTPException

from app.config import API_VERSION, REQUIRED_ARTIFACTS, SCORE_LOOKBACK_DAYS, Settings
from app.readers import make_reader
from app.schemas import (
    HealthResponse,
    Problem,
    RecommendRequest,
    RecommendResponse,
    ScoreRequest,
    ScoreResponse,
    VersionResponse,
)
from app.tiering import cycle_bounds, load_precomputed, recommend

LIBRARIES = ("fastapi", "pydantic", "pandas", "numpy", "scikit-learn", "xgboost", "shap", "prophet", "joblib")
PROBLEM_JSON = "application/problem+json"


def _problem(status: int, detail: str, errors: Optional[list] = None) -> JSONResponse:
    body = Problem(title=HTTPStatus(status).phrase, status=status, detail=detail, errors=errors)
    return JSONResponse(body.model_dump(exclude_none=True), status_code=status, media_type=PROBLEM_JSON)


def _sha256(path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def _model_files(settings: Settings) -> list[dict]:
    out = []
    for name in REQUIRED_ARTIFACTS:
        path = settings.artifact_dir / name
        if path.is_file():
            out.append({"file": name, "size_bytes": path.stat().st_size, "sha256": _sha256(path)})
    return out


def _libraries() -> dict[str, str]:
    libs = {"python": platform.python_version()}
    for name in LIBRARIES:
        try:
            libs[name] = package_version(name)
        except PackageNotFoundError:
            libs[name] = "not installed"
    return libs


def _warm_models(settings: Settings) -> None:
    # Load (and cache) every artifact once, so the first request is not slow
    # and a broken file fails at startup instead of on a request.
    from src import feature_engineering, risk_scoring

    path = settings.artifact_dir
    feature_engineering._load_scaler(path)
    feature_engineering._load_fill_values(path)
    risk_scoring._load_models(path)


def create_app(settings: Optional[Settings] = None, fail_fast: bool = True) -> FastAPI:
    """Build the app. fail_fast=False lets tests start it with missing artifacts."""
    settings = settings or Settings()

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        state = app.state
        state.settings = settings
        state.reader = None
        state.precomputed = None
        missing = settings.missing_artifacts()
        if missing and fail_fast:
            raise RuntimeError(f"Required artifacts missing in {settings.artifact_dir}: {missing}")
        if not missing:
            _warm_models(settings)
        try:
            reader = make_reader(settings)
            reader.check()
            state.reader = reader
        except Exception:
            if fail_fast:
                raise
        if (
            state.reader is not None
            and state.reader.name == "parquet"
            and settings.use_precomputed_prophet
            and settings.prophet_checkpoints_path.is_file()
        ):
            state.precomputed = load_precomputed(settings.prophet_checkpoints_path)
        state.model_files = _model_files(settings)
        state.libraries = _libraries()
        yield

    app = FastAPI(title="SimGuard ML service", version=API_VERSION, lifespan=lifespan)

    @app.exception_handler(RequestValidationError)
    async def _validation(request: Request, exc: RequestValidationError):
        errors = [
            {"field": ".".join(str(p) for p in e["loc"] if p != "body"), "message": e["msg"]}
            for e in exc.errors()
        ]
        detail = "; ".join(f"{e['field'] or 'body'}: {e['message']}" for e in errors)
        return _problem(422, detail, errors)

    @app.exception_handler(StarletteHTTPException)
    async def _http(request: Request, exc: StarletteHTTPException):
        return _problem(exc.status_code, str(exc.detail))

    @app.exception_handler(Exception)
    async def _unhandled(request: Request, exc: Exception):
        return _problem(500, f"{type(exc).__name__}: {exc}")

    def ready(request: Request):
        state = request.app.state
        missing = state.settings.missing_artifacts()
        if missing:
            raise HTTPException(503, f"Model artifacts missing: {', '.join(missing)}")
        if state.reader is None:
            raise HTTPException(503, f"Usage reader {state.settings.reader!r} is not available")
        return state

    @app.post("/v1/score", response_model=ScoreResponse)
    def score(body: ScoreRequest, state=Depends(ready)):
        from src.risk_scoring import score as risk_score

        sim_ids = list(dict.fromkeys(body.sim_ids))
        as_of = pd.Timestamp(body.as_of_date)
        usage = state.reader.get_daily_usage(sim_ids, body.as_of_date, SCORE_LOOKBACK_DAYS)
        # A SIM is scored only if it has a row on asOfDate; otherwise it is notFound.
        scored_ids = set(usage.loc[usage["date"] == as_of, "sim_id"].tolist())
        usage = usage[usage["sim_id"].isin(scored_ids)]
        results = []
        if scored_ids:
            out = risk_score(usage, as_of_dates=as_of, artifact_dir=state.settings.artifact_dir)
            by_sim = {int(r.sim_id): r for r in out.itertuples(index=False)}
            for sim_id in sim_ids:
                r = by_sim.get(sim_id)
                if r is not None:
                    results.append({
                        "sim_id": sim_id,
                        "date": pd.Timestamp(r.date).date(),
                        "probability": float(r.probability),
                        "bucket": r.bucket,
                        "reasons": r.reasons,
                        "similar_cases": [],  # CBR pending (Stage 4)
                    })
        not_found = [s for s in sim_ids if s not in scored_ids]
        return {"as_of_date": body.as_of_date, "results": results, "not_found": not_found}

    @app.post("/v1/recommend-tier", response_model=RecommendResponse)
    def recommend_tier(body: RecommendRequest, state=Depends(ready)):
        sim_ids = list(dict.fromkeys(body.sim_ids))
        t0 = time.perf_counter()
        start, end = cycle_bounds(body.billing_cycle)
        usage = state.reader.get_daily_usage(sim_ids, end.date(), (end - start).days + 1)
        read_ms = (time.perf_counter() - t0) * 1000
        results, not_found, failed, timings = recommend(
            usage, sim_ids, body.billing_cycle, state.precomputed, state.settings.prophet_n_jobs
        )
        timings["read_ms"] = round(timings["read_ms"] + read_ms, 1)
        timings["total_ms"] = round(timings["total_ms"] + read_ms, 1)
        return {"billing_cycle": body.billing_cycle, "results": results, "not_found": not_found,
                "failed": failed, "timings": timings}

    @app.get("/health", response_model=HealthResponse, responses={503: {"model": HealthResponse}})
    def health(request: Request):
        state = request.app.state
        missing = state.settings.missing_artifacts()
        ok = not missing and state.reader is not None
        body = HealthResponse(
            status="ok" if ok else "unavailable",
            artifacts_loaded=not missing,
            missing_artifacts=missing,
            reader=state.settings.reader,
        )
        return JSONResponse(body.model_dump(by_alias=True), status_code=200 if ok else 503)

    @app.get("/version", response_model=VersionResponse)
    def version(request: Request):
        state = request.app.state
        return {"service": "simguard-ml", "api_version": API_VERSION,
                "models": state.model_files, "libraries": state.libraries}

    return app


app = create_app()
