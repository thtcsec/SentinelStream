"""
SentinelStream monitoring agent: streams log lines to the WPF dashboard over WebSockets.

All behavior is driven by environment variables (see agent/.env.example).
"""

from __future__ import annotations

import asyncio
import json
import logging
import os
from datetime import datetime, timezone
from pathlib import Path

from dotenv import load_dotenv
from fastapi import FastAPI, WebSocket

load_dotenv()

app = FastAPI(title="SentinelStream Log Exporter")

logging.basicConfig(
    level=logging.INFO, format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
)
logger = logging.getLogger(__name__)


def _env_float(name: str, default: float) -> float:
    raw = os.getenv(name, "").strip()
    if not raw:
        return default
    try:
        return float(raw)
    except ValueError:
        logger.warning("Invalid %s=%r, using default %s", name, raw, default)
        return default


def _env_path(name: str) -> Path | None:
    raw = os.getenv(name, "").strip()
    return Path(raw) if raw else None


def _tail_paths_from_env() -> list[Path]:
    """Comma-separated LOG_TAIL_PATHS wins; else single LOG_TAIL_PATH. Max 32 paths."""
    raw = os.getenv("LOG_TAIL_PATHS", "").strip()
    if raw:
        out: list[Path] = []
        for part in raw.split(","):
            p = part.strip()
            if p:
                out.append(Path(p))
        return out[:32]
    one = _env_path("LOG_TAIL_PATH")
    return [one] if one else []


def _build_log_entry(
    *,
    message: str,
    severity: str = "info",
    source: str = "agent",
    raw: str | None = None,
) -> dict:
    return {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "severity": severity,
        "source": source,
        "message": message,
        "rawData": raw if raw is not None else message,
    }


async def send_json(websocket: WebSocket, payload: dict) -> None:
    await websocket.send_text(json.dumps(payload, ensure_ascii=False))


async def tail_file_task(websocket: WebSocket, file_path: Path) -> None:
    """Follow a growing text file and emit each new line as JSON."""
    source = f"tail:{file_path.name}"
    try:
        # Wait until file exists (or log and exit task)
        for _ in range(600):  # up to ~5 min @ 0.5s
            if file_path.is_file():
                break
            await asyncio.sleep(0.5)
        else:
            await send_json(
                websocket,
                _build_log_entry(
                    message=f"LOG_TAIL_PATH file not found: {file_path}",
                    severity="warning",
                    source=source,
                ),
            )
            return

        with file_path.open("r", encoding="utf-8", errors="replace") as handle:
            handle.seek(0, os.SEEK_END)
            while True:
                line = handle.readline()
                if line:
                    text = line.rstrip("\r\n")
                    if text:
                        sev = "info"
                        ul = text.upper()
                        if "ERROR" in ul or " ERR " in ul:
                            sev = "error"
                        elif "WARN" in ul:
                            sev = "warning"
                        elif "CRITICAL" in ul or "ALERT" in ul:
                            sev = "critical"
                        await send_json(
                            websocket,
                            _build_log_entry(
                                message=text, severity=sev, source=source, raw=text
                            ),
                        )
                else:
                    await asyncio.sleep(0.4)
    except asyncio.CancelledError:
        raise
    except Exception as e:
        logger.exception("tail_file_task failed")
        try:
            await send_json(
                websocket,
                _build_log_entry(
                    message=f"Tail error: {e}", severity="error", source=source
                ),
            )
        except Exception:
            pass


async def mock_interval_task(websocket: WebSocket, interval_sec: float) -> None:
    template = os.getenv(
        "AGENT_MOCK_MESSAGE",
        "Synthetic heartbeat — set LOG_TAIL_PATH(S) or AGENT_MOCK_INTERVAL_SEC",
    )
    source = os.getenv("AGENT_MOCK_SOURCE", "mock")
    while True:
        msg = template.replace("{iso}", datetime.now(timezone.utc).isoformat())
        await send_json(
            websocket,
            _build_log_entry(message=msg, severity="info", source=source),
        )
        await asyncio.sleep(max(interval_sec, 0.5))


@app.get("/")
def read_root():
    return {"status": "running", "component": "SentinelStream.Agent"}


@app.get("/health")
def health():
    paths = _tail_paths_from_env()
    mock_iv = _env_float("AGENT_MOCK_INTERVAL_SEC", 0.0)
    return {
        "log_tail_paths": [str(p) for p in paths],
        "log_tail_paths_existing": [str(p) for p in paths if p.is_file()],
        "mock_interval_sec": mock_iv,
    }


@app.websocket("/ws/logs")
async def websocket_logs(websocket: WebSocket):
    await websocket.accept()
    logger.info("WebSocket connected.")

    tail_paths = _tail_paths_from_env()
    mock_interval = _env_float("AGENT_MOCK_INTERVAL_SEC", 0.0)

    tasks: list[asyncio.Task] = []
    try:
        for path in tail_paths:
            tasks.append(asyncio.create_task(tail_file_task(websocket, path)))

        if mock_interval > 0:
            tasks.append(
                asyncio.create_task(mock_interval_task(websocket, mock_interval))
            )

        if not tasks:
            await send_json(
                websocket,
                _build_log_entry(
                    message=(
                        "Agent idle: set LOG_TAIL_PATH or LOG_TAIL_PATHS and/or "
                        "AGENT_MOCK_INTERVAL_SEC>0 (see agent/.env.example)."
                    ),
                    severity="warning",
                    source="agent",
                ),
            )
            # Keep connection open for dashboard until client disconnects
            while True:
                await asyncio.sleep(60)
        else:
            await asyncio.gather(*tasks)
    except asyncio.CancelledError:
        raise
    except Exception as e:
        logger.error("WebSocket handler error: %s", e)
    finally:
        for t in tasks:
            t.cancel()
        await asyncio.gather(*tasks, return_exceptions=True)
        await websocket.close()
        logger.info("WebSocket closed.")


if __name__ == "__main__":
    print("Use: uvicorn log_exporter:app --host 0.0.0.0 --port 8000")
