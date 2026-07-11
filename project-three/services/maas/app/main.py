"""MaaS — FastAPI servis za klasifikaciju kvaliteta vazduha (Projekat 3)."""
from __future__ import annotations

from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException

from .constants import MODEL_VERSION
from .model import AirQualityModel
from .schemas import (
    BatchPredictRequest,
    BatchPrediction,
    HealthResponse,
    Prediction,
    PredictRequest,
)

model = AirQualityModel()


@asynccontextmanager
async def lifespan(app: FastAPI):
    try:
        model.load()
        print(f"[maas] Model učitan | version={model.version} | classes={model.classes}")
    except Exception as exc:  # noqa: BLE001 — servis se diže i bez modela, /health to prijavljuje
        print(f"[maas] UPOZORENJE: model nije učitan: {exc}")
    yield


app = FastAPI(
    title="MaaS — Air Quality Classifier",
    version=MODEL_VERSION,
    lifespan=lifespan,
)


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(
        status="ok" if model.is_ready else "degraded",
        model_loaded=model.is_ready,
        model_version=model.version if model.is_ready else None,
    )


@app.get("/model/info")
def model_info() -> dict:
    if not model.is_ready:
        raise HTTPException(status_code=503, detail="Model nije učitan.")
    return model.metadata


@app.post("/predict", response_model=Prediction)
def predict(request: PredictRequest) -> Prediction:
    if not model.is_ready:
        raise HTTPException(status_code=503, detail="Model nije učitan.")
    return Prediction(**model.predict_one(request.readings))


@app.post("/predict/batch", response_model=BatchPrediction)
def predict_batch(request: BatchPredictRequest) -> BatchPrediction:
    if not model.is_ready:
        raise HTTPException(status_code=503, detail="Model nije učitan.")
    if not request.items:
        return BatchPrediction(predictions=[])
    preds = [Prediction(**p) for p in model.predict(request.items)]
    return BatchPrediction(predictions=preds)
