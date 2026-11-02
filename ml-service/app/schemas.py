"""Request and response models. JSON field names are camelCase."""

from __future__ import annotations

import datetime as dt
from typing import Annotated, Literal, Optional

from pydantic import BaseModel, ConfigDict, Field, StrictInt, field_validator
from pydantic.alias_generators import to_camel

from app.config import MAX_RECOMMEND_SIMS, MAX_SCORE_SIMS

SimId = Annotated[StrictInt, Field(ge=0)]


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class RequestModel(CamelModel):
    # Requests take camelCase names only; snake_case keys are unknown fields.
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=False, extra="forbid")


# ---------------------------------------------------------------- /v1/score


class ScoreRequest(RequestModel):
    sim_ids: list[SimId] = Field(min_length=1, max_length=MAX_SCORE_SIMS)
    as_of_date: dt.date

    @field_validator("as_of_date", mode="before")
    @classmethod
    def _iso_date(cls, value):
        # Only "YYYY-MM-DD". Pydantic alone would also take timestamps and numbers.
        if not isinstance(value, str) or len(value) != 10:
            raise ValueError("asOfDate must be a date string in YYYY-MM-DD format")
        try:
            return dt.date.fromisoformat(value)
        except ValueError as exc:
            raise ValueError(f"asOfDate is not a valid date: {exc}") from None


class Reason(CamelModel):
    feature: str
    value: float
    contribution: float
    direction: Literal["increases_risk", "decreases_risk"]


class SimScore(CamelModel):
    sim_id: int
    date: dt.date
    probability: float
    bucket: Literal["clear", "escalated", "flag"]
    reasons: list[Reason]
    # CBR pending (Stage 4). Always an empty list until case retrieval is built.
    similar_cases: list[dict] = Field(default_factory=list)


class ScoreResponse(CamelModel):
    as_of_date: dt.date
    results: list[SimScore]
    not_found: list[int]


# ------------------------------------------------------- /v1/recommend-tier


class RecommendRequest(RequestModel):
    sim_ids: list[SimId] = Field(min_length=1, max_length=MAX_RECOMMEND_SIMS)
    billing_cycle: StrictInt = Field(ge=0)


class TierRecommendation(CamelModel):
    sim_id: int
    billing_cycle: int
    current_tier: str
    recommendation: Literal["upgrade", "downgrade", "no_change", "insufficient_history"]
    recommended_tier: Optional[str]
    projected_overage_mb: Optional[float]
    days_to_depletion: Optional[int]
    checkpoint_day: Optional[int]
    source: Literal["precomputed", "fitted", "none"]


class FitFailure(CamelModel):
    sim_id: int
    checkpoint_day: int
    error: str


class RecommendTimings(CamelModel):
    read_ms: float
    fit_ms: float
    total_ms: float
    fitted: int
    precomputed: int


class RecommendResponse(CamelModel):
    billing_cycle: int
    results: list[TierRecommendation]
    not_found: list[int]
    failed: list[FitFailure]
    timings: RecommendTimings


# ------------------------------------------------------------ health, version


class HealthResponse(CamelModel):
    status: Literal["ok", "unavailable"]
    artifacts_loaded: bool
    missing_artifacts: list[str]
    reader: str


class ModelFile(CamelModel):
    file: str
    size_bytes: int
    sha256: str


class VersionResponse(CamelModel):
    service: str
    api_version: str
    models: list[ModelFile]
    libraries: dict[str, str]


class Problem(CamelModel):
    title: str
    status: int
    detail: str
    errors: Optional[list[dict]] = None
