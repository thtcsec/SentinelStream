# SentinelStream (StreamSentinel)

Desktop **war room** shell for security operations: a WPF dashboard that shows a **live log pane**, **participant list**, and **chat**, while a small **Python agent** can tail real log files or emit timed JSON events over WebSockets.

## What this is for (today)

This repo is a **structured prototype**, not a finished SOC product.

- **Useful now:** plug in a log source (file tail or mock interval), point the WPF client at the agent with `LOG_SERVER_URL`, and you get a **single place to watch lines** during drills, demos, or light monitoring—similar in spirit to tailing logs in a shared terminal, but in a dedicated UI.
- **Not implemented yet:** real Agora RTC (voice/video), true E2EE on media, shared remote terminal, ASP.NET token service, and automated forensic hashing of recordings. The C# layer includes **stubs and helpers** (`AgoraWarRoomClient`, `AesGcmEncryptor`, `ForensicHasher`) for a future integration path; the UI copy reflects **actual** behavior where we changed it.

**Rule of thumb:** only deploy against systems you are **authorized** to monitor.

## Architecture

| Piece | Role |
|--------|------|
| **WPF app** (`src/SentinelStream.App`) | Login → War Room UI; connects to the log agent when configured. |
| **LogStreamClient** (`SentinelStream.Services`) | `ClientWebSocket` → JSON or plain text lines → `LogEntry`. |
| **AgoraWarRoomClient** (`SentinelStream.Core`) | **Simulation** of channel join/leave/chat (no Agora NuGet wired yet). |
| **Python agent** (`agent/log_exporter.py`) | FastAPI + WebSocket `/ws/logs`; behavior from **environment variables** only. |

## Configuration (no hardcoded URLs in code paths)

### Repo root `.env` (WPF)

Copy `.env.example` to `.env` and run `.\setup_env.ps1` to rotate crypto-related keys.

| Variable | Meaning |
|----------|---------|
| `LOG_SERVER_URL` | e.g. `ws://127.0.0.1:8000/ws/logs` — empty = no agent connection. |
| `DEMO_LOG_FEED` | `true` / `false` — extra **demo** lines generated inside the app (independent of the agent). |
| `AGORA_APP_ID` | Reserved for future Agora integration. |
| `ENCRYPTION_KEY`, `FORENSIC_SALT` | Reserved for future features; used by library code when you wire them in. |

The app resolves `.env` from the working directory or next to the executable.

### Agent `agent/.env` (optional)

Copy `agent/.env.example` to `agent/.env`. All sources are optional; combine as needed.

| Variable | Meaning |
|----------|---------|
| `LOG_TAIL_PATH` | Path to a UTF-8 text file to **follow** (like `tail -f`). |
| `AGENT_MOCK_INTERVAL_SEC` | Seconds between **synthetic** JSON log events (`0` = off). |
| `AGENT_MOCK_MESSAGE` | Mock template; `{iso}` → UTC timestamp. |
| `AGENT_MOCK_SOURCE` | `source` field for mock lines. |

## Quick start

### 1. Keys (optional for log-only use)

```powershell
.\setup_env.ps1
```

Edit `.env`: set `LOG_SERVER_URL` if the agent runs locally:

```env
LOG_SERVER_URL=ws://127.0.0.1:8000/ws/logs
DEMO_LOG_FEED=false
```

Use `DEMO_LOG_FEED=true` if you want fake SOC-style lines **without** running the agent.

### 2. Monitoring agent

```powershell
cd agent
python -m venv .venv
.\.venv\Scripts\activate
pip install -r requirements.txt
copy .env.example .env
# Edit .env: set LOG_TAIL_PATH and/or AGENT_MOCK_INTERVAL_SEC
python -m uvicorn log_exporter:app --host 0.0.0.0 --port 8000
```

Health check: `GET http://127.0.0.1:8000/health` shows which modes are enabled.

### 3. WPF client

```powershell
dotnet build
dotnet run --project src/SentinelStream.App
```

Enter operator name and war room id. The footer shows **log agent connection status** and a short **prototype** note for RTC.

## Roadmap (high level)

1. Keep **config-driven** log paths and URLs; extend agent with more sources (e.g. Windows Event Log) behind env flags.  
2. Integrate **Agora RTC** + token flow; then align encryption UI with real behavior.  
3. Optional **ASP.NET** backend for sessions and tokens.  
4. **Forensics:** call `ForensicHasher` when exporting session artifacts.  
5. **Shared terminal** only with a clear security and audit model.

## Security disclaimer

For **authorized** incident response, education, and testing only. Misuse against systems you do not own or lack permission to monitor may be illegal.

---

*Branding note: README historically referenced “SentinelStream” and Agora; repository layout uses `SentinelStream.*` projects.*
