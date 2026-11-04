"""Shared secret check: every route except /health needs X-SimGuard-ML-Secret."""

import logging

import pytest
from fastapi.testclient import TestClient

from app.config import SECRET_HEADER, Settings
from app.main import create_app
from tests.test_api import ARTIFACT_DIR, AS_OF, assert_problem

SECRET = "test-secret-not-real"


def make_settings(secret: str = SECRET, environment: str = "Production") -> Settings:
    return Settings(artifact_dir=ARTIFACT_DIR, reader="parquet", shared_secret=secret, environment=environment)


@pytest.fixture(scope="module")
def secured():
    with TestClient(create_app(make_settings())) as c:
        yield c


def score_body():
    return {"simIds": [21], "asOfDate": AS_OF}


@pytest.mark.parametrize(
    "method, path, body",
    [
        ("post", "/v1/score", {"simIds": [21], "asOfDate": AS_OF}),
        ("post", "/v1/recommend-tier", {"simIds": [187], "billingCycle": 1}),
        ("get", "/version", None),
        ("get", "/docs", None),
        ("get", "/openapi.json", None),
        ("get", "/v1/nope", None),
    ],
)
def test_routes_need_the_secret(secured, method, path, body):
    kwargs = {"json": body} if body is not None else {}
    resp = getattr(secured, method)(path, **kwargs)
    body = assert_problem(resp, 401)
    assert SECRET_HEADER in body["detail"]
    assert SECRET not in resp.text


def test_wrong_secret_is_401(secured):
    assert_problem(secured.post("/v1/score", json=score_body(), headers={SECRET_HEADER: "wrong"}), 401)
    assert_problem(secured.post("/v1/score", json=score_body(), headers={SECRET_HEADER: ""}), 401)
    assert_problem(secured.get("/version", headers={SECRET_HEADER: SECRET + "x"}), 401)


def test_right_secret_is_accepted(secured):
    headers = {SECRET_HEADER: SECRET}
    resp = secured.post("/v1/score", json=score_body(), headers=headers)
    assert resp.status_code == 200, resp.text
    assert resp.json()["results"][0]["simId"] == 21
    assert secured.get("/version", headers=headers).status_code == 200


def test_health_is_open(secured):
    resp = secured.get("/health")
    assert resp.status_code == 200
    assert resp.json()["status"] == "ok"


@pytest.mark.parametrize("environment", ["Production", "Staging", "production", ""])
def test_no_secret_outside_development_refuses_to_start(environment):
    app = create_app(make_settings(secret="", environment=environment or "Production"))
    with pytest.raises(RuntimeError, match="ML_SHARED_SECRET"):
        with TestClient(app):
            pass


def test_no_secret_in_development_turns_the_check_off(caplog):
    with caplog.at_level(logging.WARNING, logger="simguard.ml"):
        with TestClient(create_app(make_settings(secret="", environment="Development"))) as c:
            assert c.post("/v1/score", json=score_body()).status_code == 200
            assert c.get("/version").status_code == 200
    assert any("ML_SHARED_SECRET is not set" in r.getMessage() for r in caplog.records)


def test_secret_set_in_development_is_still_checked():
    with TestClient(create_app(make_settings(environment="Development"))) as c:
        assert_problem(c.get("/version"), 401)
        assert c.get("/version", headers={SECRET_HEADER: SECRET}).status_code == 200


def test_environment_variables_are_read(monkeypatch):
    monkeypatch.setenv("ML_SHARED_SECRET", "  from-env  ")
    monkeypatch.setenv("ML_ENVIRONMENT", "Staging")
    settings = Settings()
    assert settings.shared_secret == "from-env"
    assert settings.environment == "Staging"
    assert not settings.is_development
    assert "from-env" not in repr(settings)
    monkeypatch.delenv("ML_ENVIRONMENT")
    assert Settings().environment == "Production"
