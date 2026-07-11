FROM python:3.12-slim

WORKDIR /app

# Zavisnosti prvo (bolji Docker layer cache).
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

# Kod + istrenirani artefakt (model se baked-in, brz startup, bez treninga u build-u).
COPY app ./app
COPY model ./model
COPY train.py ./

ENV PORT=8000
EXPOSE 8000

CMD ["sh", "-c", "uvicorn app.main:app --host 0.0.0.0 --port ${PORT:-8000}"]
