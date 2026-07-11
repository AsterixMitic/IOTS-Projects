"""Izvođenje labele kvaliteta vazduha iz referentnih zagađivača.

Priča modela: jeftini MOX senzori (PT08.*) + okruženje (T/RH/AH) predviđaju klasu
kvaliteta vazduha koju bi dodelili referentni instrumenti (CO/NO2/NOx GT).
Pragovi su orijentacioni i tunirani za razuman balans klasa na Air Quality UCI datasetu.
"""
from __future__ import annotations

import numpy as np
import pandas as pd

# Prag ISPOD kog je vazduh "good" (svi uslovi moraju da važe).
GOOD = {"co_gt": 2.0, "no2_gt": 100.0, "nox_gt": 150.0}

# Prag IZNAD kog je vazduh "unhealthy" (dovoljno da jedan uslov važi — dominantni zagađivač).
UNHEALTHY = {"co_gt": 3.5, "no2_gt": 160.0, "nox_gt": 280.0}


def derive_labels(df: pd.DataFrame) -> pd.Series:
    """Vrati Series labela ('good' | 'moderate' | 'unhealthy') za dati DataFrame.

    Očekuje kolone: co_gt, no2_gt, nox_gt (bez NaN — filtrirati pre poziva).
    """
    co, no2, nox = df["co_gt"], df["no2_gt"], df["nox_gt"]

    unhealthy = (
        (co >= UNHEALTHY["co_gt"])
        | (no2 >= UNHEALTHY["no2_gt"])
        | (nox >= UNHEALTHY["nox_gt"])
    )
    good = (
        (co < GOOD["co_gt"])
        & (no2 < GOOD["no2_gt"])
        & (nox < GOOD["nox_gt"])
    )

    labels = np.where(unhealthy, "unhealthy", np.where(good, "good", "moderate"))
    return pd.Series(labels, index=df.index, name="air_quality")
