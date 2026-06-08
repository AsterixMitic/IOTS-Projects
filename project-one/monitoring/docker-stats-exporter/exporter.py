import os
import time
import threading

import docker
from prometheus_client import Gauge, start_http_server


CPU = Gauge(
    "docker_container_cpu_usage_seconds_total",
    "Cumulative CPU usage in seconds from docker stats",
    ["service", "container"],
)
MEM = Gauge(
    "docker_container_memory_usage_bytes",
    "Working set memory usage in bytes from docker stats",
    ["service", "container"],
)
LIMIT = Gauge(
    "docker_container_memory_limit_bytes",
    "Memory limit in bytes from docker stats",
    ["service", "container"],
)
UP = Gauge(
    "docker_container_up",
    "Whether the container was successfully scraped",
    ["service", "container"],
)


def container_service(container):
    labels = container.attrs.get("Config", {}).get("Labels", {}) or {}
    return labels.get("com.docker.compose.service") or container.name.lstrip("/")


def working_set_bytes(stats):
    memory = stats.get("memory_stats", {}) or {}
    usage = int(memory.get("usage") or 0)
    cache = int((memory.get("stats") or {}).get("cache") or 0)
    return max(usage - cache, 0)


def collect_loop():
    client = docker.from_env()
    compose_project = os.getenv("COMPOSE_PROJECT", "project-one")
    interval = float(os.getenv("DOCKER_STATS_INTERVAL", "5"))

    while True:
        seen = set()
        for container in client.containers.list():
            labels = container.attrs.get("Config", {}).get("Labels", {}) or {}
            if labels.get("com.docker.compose.project") != compose_project:
                continue

            try:
                stats = container.stats(stream=False)
            except Exception:
                continue

            service = container_service(container)
            name = container.name.lstrip("/")
            seen.add((service, name))

            cpu_total_seconds = int(stats["cpu_stats"]["cpu_usage"]["total_usage"]) / 1_000_000_000
            mem_usage = working_set_bytes(stats)
            mem_limit = int(stats.get("memory_stats", {}).get("limit") or 0)

            CPU.labels(service=service, container=name).set(cpu_total_seconds)
            MEM.labels(service=service, container=name).set(mem_usage)
            LIMIT.labels(service=service, container=name).set(mem_limit)
            UP.labels(service=service, container=name).set(1)

        time.sleep(interval)


if __name__ == "__main__":
    start_http_server(9101)
    threading.Thread(target=collect_loop, daemon=True).start()
    while True:
        time.sleep(60)
