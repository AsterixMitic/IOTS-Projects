"""Deljene konstante MaaS servisa (koriste ih i train.py i serving)."""
from pathlib import Path

# Features koje simulator/uređaj emituje i koje model koristi na inference-u
# (jeftini MOX senzorski odzivi + okruženje).
FEATURE_COLUMNS = [
    "pt08_s1_co",
    "pt08_s2_nmhc",
    "pt08_s3_nox",
    "pt08_s4_no2",
    "pt08_s5_o3",
    "temperature",
    "relative_humidity",
    "absolute_humidity",
]

# Referentni zagađivači od kojih izvodimo labelu (samo u treningu).
LABEL_POLLUTANTS = ["co_gt", "no2_gt", "nox_gt"]

# Klase kvaliteta vazduha.
CLASSES = ["good", "moderate", "unhealthy"]

MODEL_VERSION = "1.0.0"

# Putanje do artefakata (services/maas/model/).
MODEL_DIR = Path(__file__).resolve().parent.parent / "model"
MODEL_PATH = MODEL_DIR / "model.joblib"
METADATA_PATH = MODEL_DIR / "metadata.json"
