"""Trening pipeline za MaaS klasifikator kvaliteta vazduha (Air Quality UCI).

Pokretanje (iz services/maas, sa aktiviranim venv-om):
    python train.py

Produkuje: model/model.joblib i model/metadata.json.
"""
from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import pandas as pd
import sklearn
from sklearn.ensemble import RandomForestClassifier
from sklearn.impute import SimpleImputer
from sklearn.metrics import accuracy_score, classification_report, confusion_matrix
from sklearn.model_selection import train_test_split
from sklearn.pipeline import Pipeline

from app.constants import (
    CLASSES,
    FEATURE_COLUMNS,
    LABEL_POLLUTANTS,
    METADATA_PATH,
    MODEL_DIR,
    MODEL_PATH,
    MODEL_VERSION,
)
from app.labeling import GOOD, UNHEALTHY, derive_labels

# Mapiranje kolona iz CSV-a u kanonske nazive (isti kao u simulatoru).
COLUMN_RENAME = {
    "CO(GT)": "co_gt",
    "PT08.S1(CO)": "pt08_s1_co",
    "NMHC(GT)": "nmhc_gt",
    "C6H6(GT)": "c6h6_gt",
    "PT08.S2(NMHC)": "pt08_s2_nmhc",
    "NOx(GT)": "nox_gt",
    "PT08.S3(NOx)": "pt08_s3_nox",
    "NO2(GT)": "no2_gt",
    "PT08.S4(NO2)": "pt08_s4_no2",
    "PT08.S5(O3)": "pt08_s5_o3",
    "T": "temperature",
    "RH": "relative_humidity",
    "AH": "absolute_humidity",
}

DEFAULT_CSV = (
    Path(__file__).resolve().parent.parent.parent / "db" / "dataset" / "AirQualityUCI.csv"
)
RANDOM_STATE = 42


def load_dataset(csv_path: Path) -> pd.DataFrame:
    df = pd.read_csv(csv_path, sep=";", decimal=",", na_values=["-200"])
    df = df.dropna(axis=1, how="all")          # ukloni prazne trailing kolone (;;)
    df = df.dropna(subset=["Date"])            # ukloni prazne trailing redove
    df = df.rename(columns=COLUMN_RENAME)
    df = df.replace(-200, np.nan)              # preostali markeri nedostajuće vrednosti
    return df


def build_pipeline() -> Pipeline:
    return Pipeline([
        ("imputer", SimpleImputer(strategy="median")),
        ("clf", RandomForestClassifier(
            n_estimators=150,
            max_depth=20,
            min_samples_leaf=5,
            class_weight="balanced",
            random_state=RANDOM_STATE,
            n_jobs=-1,
        )),
    ])


def main(csv_path: Path = DEFAULT_CSV) -> None:
    print(f"[train] Učitavam dataset: {csv_path}")
    df = load_dataset(csv_path)
    print(f"[train] Redova posle čišćenja: {len(df)}")

    # Labeliranje zahteva referentne zagađivače bez NaN.
    labeled = df.dropna(subset=LABEL_POLLUTANTS).copy()
    labeled["air_quality"] = derive_labels(labeled)
    print(f"[train] Redova sa labelom: {len(labeled)}")

    dist = labeled["air_quality"].value_counts().reindex(CLASSES).fillna(0).astype(int)
    print(f"[train] Distribucija klasa:\n{dist.to_string()}")

    X = labeled[FEATURE_COLUMNS]
    y = labeled["air_quality"]

    # 70 / 15 / 15 stratifikovano.
    X_train, X_temp, y_train, y_temp = train_test_split(
        X, y, test_size=0.30, stratify=y, random_state=RANDOM_STATE
    )
    X_val, X_test, y_val, y_test = train_test_split(
        X_temp, y_temp, test_size=0.50, stratify=y_temp, random_state=RANDOM_STATE
    )
    print(f"[train] Split: train={len(X_train)} val={len(X_val)} test={len(X_test)}")

    pipeline = build_pipeline()
    pipeline.fit(X_train, y_train)

    val_acc = accuracy_score(y_val, pipeline.predict(X_val))
    test_pred = pipeline.predict(X_test)
    test_acc = accuracy_score(y_test, test_pred)
    print(f"[train] Validation accuracy: {val_acc:.4f}")
    print(f"[train] Test accuracy:       {test_acc:.4f}")
    print("[train] Classification report (test):")
    print(classification_report(y_test, test_pred, zero_division=0))

    report = classification_report(
        y_test, test_pred, output_dict=True, zero_division=0
    )
    labels_sorted = list(pipeline.classes_)
    cm = confusion_matrix(y_test, test_pred, labels=labels_sorted).tolist()

    metadata = {
        "model_version": MODEL_VERSION,
        "model_type": "RandomForestClassifier",
        "library": f"scikit-learn {sklearn.__version__}",
        "task": "air-quality-classification",
        "trained_at": datetime.now(timezone.utc).isoformat(),
        "features": FEATURE_COLUMNS,
        "classes": labels_sorted,
        "label_thresholds": {"good_below": GOOD, "unhealthy_at_or_above": UNHEALTHY},
        "dataset_rows_labeled": int(len(labeled)),
        "class_distribution": {k: int(v) for k, v in dist.items()},
        "split": {"train": len(X_train), "val": len(X_val), "test": len(X_test)},
        "metrics": {
            "val_accuracy": round(float(val_acc), 4),
            "test_accuracy": round(float(test_acc), 4),
            "test_report": report,
            "test_confusion_matrix": {"labels": labels_sorted, "matrix": cm},
        },
    }

    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    import joblib
    joblib.dump(pipeline, MODEL_PATH, compress=3)
    METADATA_PATH.write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    print(f"[train] Sačuvano: {MODEL_PATH}")
    print(f"[train] Sačuvano: {METADATA_PATH}")


if __name__ == "__main__":
    main()
