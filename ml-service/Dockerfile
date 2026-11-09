# SimGuard ML service. Build context: ml-service/
# (docker build -f ml-service/Dockerfile ml-service).
# Model artifacts and data are NOT in the image: mount the artifacts folder and
# set ML_ARTIFACT_DIR. The image has the code only.
FROM python:3.13-slim AS deps
ENV PIP_NO_CACHE_DIR=1 PIP_DISABLE_PIP_VERSION_CHECK=1
WORKDIR /w
COPY requirements.txt .
# Drop what the service does not import: experiment tracking and the tests.
# xgboost pulls the NVIDIA GPU libraries (about 350 MB); this service runs on the CPU.
RUN grep -Ev '^(mlflow|pytest|httpx)==' requirements.txt > service-requirements.txt  && python -m venv /venv  && /venv/bin/pip install -r service-requirements.txt  && (/venv/bin/pip freeze | grep -i '^nvidia' | cut -d= -f1 | xargs -r /venv/bin/pip uninstall -y)

FROM python:3.13-slim AS runtime
ENV PATH=/venv/bin:$PATH \
    PYTHONUNBUFFERED=1 PYTHONDONTWRITEBYTECODE=1 \
    ML_ENVIRONMENT=Production READER=postgres \
    ML_ARTIFACT_DIR=/models/artifacts
COPY --from=deps /venv /venv
RUN useradd --system --uid 10001 --home /app --shell /usr/sbin/nologin ml \
 && mkdir -p /app /models && chown ml /app
WORKDIR /app
COPY --chown=ml app/ app/
COPY --chown=ml src/ src/
USER ml
EXPOSE 8001
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8001"]
