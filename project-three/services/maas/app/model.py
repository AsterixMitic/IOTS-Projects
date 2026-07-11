"""Učitavanje istreniranog modela i inference."""
from __future__ import annotations

import json

import numpy as np
import pandas as pd

from .constants import (
    CLASSES,
    FEATURE_COLUMNS,
    METADATA_PATH,
    MODEL_PATH,
    MODEL_VERSION,
)


class AirQualityModel:
    """Wrapper oko scikit-learn pipeline-a (imputer + klasifikator)."""

    def __init__(self) -> None:
        self.pipeline = None
        self.metadata: dict = {}
        self.classes: list[str] = list(CLASSES)

    def load(self) -> None:
        import joblib  # lazy import — brži startup ako model ne postoji

        self.pipeline = joblib.load(MODEL_PATH)
        self.classes = list(self.pipeline.classes_)
        if METADATA_PATH.exists():
            self.metadata = json.loads(METADATA_PATH.read_text(encoding="utf-8"))

    @property
    def is_ready(self) -> bool:
        return self.pipeline is not None

    @property
    def version(self) -> str:
        return self.metadata.get("model_version", MODEL_VERSION)

    def _to_frame(self, readings_list: list[dict]) -> pd.DataFrame:
        rows = [
            {col: r.get(col, np.nan) for col in FEATURE_COLUMNS}
            for r in readings_list
        ]
        return pd.DataFrame(rows, columns=FEATURE_COLUMNS)

    def predict(self, readings_list: list[dict]) -> list[dict]:
        if not self.is_ready:
            raise RuntimeError("Model nije učitan.")

        frame = self._to_frame(readings_list)
        proba = self.pipeline.predict_proba(frame)
        classes = list(self.pipeline.classes_)

        results: list[dict] = []
        for row in proba:
            idx = int(np.argmax(row))
            results.append({
                "air_quality": classes[idx],
                "probabilities": {cls: round(float(p), 4) for cls, p in zip(classes, row)},
                "model_version": self.version,
            })
        return results

    def predict_one(self, readings: dict) -> dict:
        return self.predict([readings])[0]
