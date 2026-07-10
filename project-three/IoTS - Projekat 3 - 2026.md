# Internet stvari i servisa

## Projekat 3

## 1. Unaprediti Analytics mikroservis iz 2. projekta koji je pretplaćen na topic MQTT message brokera na

```
koji Data Storage Service mikroservis publikuje podatke tako da za analizu podataka ovaj mikroservis
koristi:
a. eKuiper streaming processing/CEP (Complex Event Processing) servis preko MQTT brokera
b. MaaS (Model as a Service) mikroservis i njegove REST endpointe.
```
## 2. eKuiper (https://ekuiper.org/ ) je pretplaćen je na isti topic na MQTT brokeru kao i Analytics , dobija

```
senzorske podatke, primenom defnisanih pravila detektuje događaje od interesa i šalje poruke o njima
na novi topic na MQTT koji preuzima Analytics servis.
```
## 3. MaaS mikroservis implementirati korišćenjem Python i Flask API (Fast API) i određeni model

```
mašinskog/dubokog učenja (po izboru) za klasifikaciju/regresiju primenjen na tokove senzorskih
podataka koji čine vremensku seriju. Trening, validaciju i testiranje modela obaviti korišćenjem
biblioteke scikit-learn ili TensorFlow/PyTorch (Keras). Korisititi neki od tutorial-a dostupnih na Webu,
poput:
```
```
a. Deploy ML Models as a Service: A Step-by-Step Guide Using Flask | Towards AI
b. Machine Learning Model Deployment with FastAPI and Docker - DEV Community
c. Flask Decoded: Your Gateway to Deploying ML Models Effortlessly | by Reza Shokrzad |
Medium
```
## 4. Mikroservise startovati kao Docker container-e i Web/mobilnu aplikaciju implementirati u

```
proizvoljnim tehnologijama.
```
## 5. Izvorni kod projekta kao postaviti na GitHub, sa kratkim opisom implementiranih mikroservisa.


