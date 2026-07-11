"""Pydantic šeme za REST API."""
from __future__ import annotations

from pydantic import BaseModel, Field


class PredictRequest(BaseModel):
    """Telo zahteva za /predict — prosleđuje se `readings` objekat iz MQTT payload-a.

    Dozvoljeni su i dodatni ključevi (npr. co_gt, c6h6_gt); model koristi samo
    features iz FEATURE_COLUMNS, ostalo ignoriše.
    """

    readings: dict[str, float] = Field(
        ...,
        examples=[{
            "pt08_s1_co": 1050, "pt08_s2_nmhc": 900, "pt08_s3_nox": 800,
            "pt08_s4_no2": 1500, "pt08_s5_o3": 1100, "temperature": 21.5,
            "relative_humidity": 48.2, "absolute_humidity": 1.1,
        }],
    )


class BatchPredictRequest(BaseModel):
    items: list[dict[str, float]]


class Prediction(BaseModel):
    air_quality: str
    probabilities: dict[str, float]
    model_version: str


class BatchPrediction(BaseModel):
    predictions: list[Prediction]


class HealthResponse(BaseModel):
    status: str
    model_loaded: bool
    model_version: str | None = None
