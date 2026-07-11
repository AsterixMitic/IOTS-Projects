# MaaS — Model-as-a-Service (klasifikacija kvaliteta vazduha)

Python + **FastAPI** + **scikit-learn** mikroservis koji klasifikuje kvalitet vazduha u
tri klase: **good / moderate / unhealthy** na osnovu senzorskih očitavanja (Projekat 3, tačka 1b i 3).

## Model

- **Zadatak:** klasifikacija (RandomForestClassifier, `class_weight="balanced"`).
- **Features (X):** senzorski odzivi koje uređaj/simulator emituje —
  `pt08_s1_co, pt08_s2_nmhc, pt08_s3_nox, pt08_s4_no2, pt08_s5_o3,
  temperature, relative_humidity, absolute_humidity`.
- **Label (y):** izvedena iz referentnih zagađivača (`co_gt, no2_gt, nox_gt`) po pragovima
  (vidi `app/labeling.py`). Priča: jeftini MOX senzori predviđaju klasu koju bi dodelili
  referentni instrumenti.
- **Podaci:** Air Quality UCI (`db/dataset/AirQualityUCI.csv`), `-200` → NaN, imputacija medijanom.
- **Rezultat:** 70/15/15 split, test accuracy ≈ **0.85**, balansirane klase. Detalji u
  `model/metadata.json`.

## REST API (port 8000)

| Metoda | Ruta | Opis |
|---|---|---|
| `GET` | `/health` | status + da li je model učitan |
| `GET` | `/model/info` | tip modela, verzija, features, klase, metrike |
| `POST` | `/predict` | jedno očitavanje → klasa + verovatnoće |
| `POST` | `/predict/batch` | lista očitavanja → liste predikcija |

Primer:
```bash
curl -X POST localhost:8000/predict -H 'Content-Type: application/json' -d '{
  "readings": { "pt08_s1_co":1050, "pt08_s2_nmhc":900, "pt08_s3_nox":800,
    "pt08_s4_no2":1500, "pt08_s5_o3":1100, "temperature":21.5,
    "relative_humidity":48.2, "absolute_humidity":1.1 }
}'
# → { "air_quality": "moderate", "probabilities": {...}, "model_version": "1.0.0" }
```
Dodatni ključevi u `readings` (npr. `co_gt`) se ignorišu — model koristi samo svoje features.

## Trening (retrening)

Artefakt (`model/model.joblib`) je komitovan pa se kontejner diže bez treninga. Za retrening:

```bash
python -m venv .venv && ./.venv/Scripts/pip install -r requirements.txt   # Windows
python train.py    # čita ../../db/dataset/AirQualityUCI.csv, piše model/
```

## Struktura

```
services/maas/
├── app/            # FastAPI (main, model, schemas, labeling, constants)
├── train.py        # trening pipeline
├── model/          # model.joblib + metadata.json (komitovani artefakti)
├── requirements.txt
└── Dockerfile
```
