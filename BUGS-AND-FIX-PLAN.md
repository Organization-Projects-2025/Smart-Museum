# Smart Museum — Verified bugs & fix plan

This document validates an external AI bug report against the current repo (May 2026).

## Fix checklist (implemented)

- [x] `run_all.bat`, `start_server.bat`, `python/start_python_server.bat` → `start.bat` / `main.py`
- [x] Stale `python_server.py` strings in C# and `BUILD_INSTRUCTIONS.md`
- [x] `auth_service.py` — all responses newline-terminated (`_send_line`)
- [x] `AuthIntegration` — `NEW:` parse, `leaveOpen`, connect/read timeouts, line I/O
- [x] `InputPrioritizer` — lock around shared state
- [x] `HandTrackClient` — connect timeout + `ConnectAsync` (non-blocking UI)
- [x] `GestureClient` — bad JSON no longer disconnects
- [x] `SlideShowManager` — single advance path in timer
- [x] `FavoritesUI` — font dispose
- [x] `TuioDemo` — image cache, `GetWorkspaceRoot`, YOLO context 5003 disabled, auth flow
- [x] `gesture_service.py` — `_SERVER_DIR` fix
- [x] `face_store.py` — thread lock
- [x] `GazeEmotionClient` — UTF-8 decoder for fragmented reads
- [x] `yolo_server.py` — prefer `MUSEUM_CAMERA` over `YOLO_CAMERA`
- [x] `.env.example` — `skip_user=user0` clarity
- [x] Legacy `hand_tracker.py` port comment

**How to run the stack (correct):** `start.bat` → `python/server/main.py`; C# from Visual Studio (`C#/TUIO_CSHARP.sln`).

---

## Summary

| Category | AI report | After verification |
|----------|-----------|------------------|
| Critical | 10 | **6 confirmed**, 2 downgraded, 2 false/overstated |
| High | 13 | **7 confirmed**, 4 low/legacy, 2 false |
| Medium | 13 | **6 worth fixing**, rest optional/docs |

The clock/watch path (**port 5005**, `watchGestureClient`) is the live YOLO integration. Several issues are **legacy scripts** or **optional features** (port 5003 phone/book banner), not blockers for core menu/gesture flow.

---

## Confirmed — fix first

### 1. Dead YOLO context client (port 5003) — **Real (optional feature broken)**

| | |
|---|---|
| **Symptom** | Phone/book “ambient” banner never gets YOLO tracks after login. |
| **Code** | `C#/YoloContextClient.cs` (default port 5003); `C#/TuioDemo.cs` ~1992–2046 (`InitializeYoloContext`, `OnYoloFrame`). |
| **Cause** | No `yolo_context_service.py` in repo; production YOLO is `python/server/yolo_server.py` on **5005** (clock/menu only). |
| **Fix plan** | **A)** Remove `YoloContextClient` + `OnYoloFrame` if phone banner is unused. **B)** Or extend `yolo_server` STATUS/JSON with classes `phone`/`book`/`person` and subscribe from C# on the existing watch client. Do **not** add a second port without need. |

### 2. Broken startup batch files — **Real**

| File | Problem |
|------|---------|
| `run_all.bat:19` | `python python_server.py` — file does not exist. |
| `start_server.bat:3` | Hardcoded `C:\Users\user\...\python.exe` + `python_server.py`. |
| `python/start_python_server.bat:3` | Same hardcoded path + `server/python_server.py` (also missing). |

**Fix plan:** Point all scripts at `start.bat` logic (venv from `.env` `venv_name`, run `%PYTHON% python\server\main.py`). Delete or add a one-line redirect comment in obsolete bats. Update stale strings in `C#/AuthIntegration.cs:596`, `C#/TuioDemo.cs:562`, `C#/BUILD_INSTRUCTIONS.md` that say `python_server.py`.

### 3. Auth `NEW:` parsing in `AuthIntegration` — **Real (mitigated in UI only)**

| | |
|---|---|
| **Python** | `python/server/auth_service.py:120–128` — `NEW:user3:25:male:white` |
| **C# bug** | `AuthIntegration.cs:688–690`, `764–766` — `userId = response.Substring(4)` keeps colons. |
| **Mitigation** | `TuioDemo.cs:607` re-parses via `NewFaceDemographics.TryParseNewResponse("NEW:" + uid, …)` (comment on line 605 acknowledges this). |
| **Risk** | Any caller that uses `userId` without `TryParseNewResponse`; wrong status strings; `RegisterFaceScan` is **unused** but still wrong. |

**Fix plan:** In `AuthIntegration`, parse `NEW:` with `TryParseNewResponse` (or `Split(':')` → `parts[1]`) for both `RegisterFaceScan` and `AuthLobbyScan`. Remove duplicate hack in `TuioDemo` once API returns clean `userId`.

### 4. Auth TCP responses missing `\n` — **Real (protocol inconsistency)**

| | |
|---|---|
| **Code** | `auth_service.py:259–265` — `face_register_scan`, `face_id_scan`, `bluetooth_*` send without `+ b"\n"`; `face_auth_lobby` correctly adds `\n` (273). |
| **Impact** | Lobby uses `StreamReader.ReadLine` → needs `\n`. Register path uses `recieveMessage()` single `Read(1024)` → often works for one packet, fragile if split/coalesced. |

**Fix plan:** Append `+ b"\n"` to **every** `conn.send` in `auth_service.py`. Optionally switch C# auth client to line-based reads everywhere.

### 5. `InputPrioritizer` thread safety — **Real**

| | |
|---|---|
| **Writes** | `TuioDemo.cs` TUIO callbacks ~3258–3276 (`addTuioObject` / `removeTuioObject`) — TUIO thread. |
| **Reads** | Gesture poll ~2570 — UI timer thread. |
| **Code** | `C#/InputPrioritizer.cs:11–36, 60–78` — no lock/volatile. |

**Fix plan:** `lock` object around `tuioPresent` / `tuioClearedTime`, or make fields `volatile` + `Interlocked` for bool if you keep logic simple.

### 6. `AuthIntegration` `StreamReader` closes socket — **Real**

| | |
|---|---|
| **Code** | `AuthIntegration.cs:180` — `using (var reader = new StreamReader(stream, …))` disposes underlying `NetworkStream`. |
| **Impact** | After `sendCommandAndStream`, same `SocketClient` may be unusable if reused (lobby closes connection today — lower risk). |

**Fix plan:** `new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true)` (.NET 4.5+).

### 7. `HandTrackClient.Connect()` blocks form load — **Real**

| | |
|---|---|
| **Code** | `C#/TuioDemo.cs:395` calls `InitializeHandTracker()` in ctor; `HandTrackClient.cs:78` synchronous `Connect()`. |
| **Impact** | If hand service down, default TCP connect can stall UI ~20s (ReceiveTimeout does not cap connect). |

**Fix plan:** Connect on background thread with short timeout (match `YoloContextClient` 3s pattern), or defer until 3D feature first used.

### 8. `recieveMessage()` fragile auth I/O — **Real**

| | |
|---|---|
| **Code** | `AuthIntegration.cs:130–138` — one `Read`, no timeout, no line framing. |

**Fix plan:** Shared line reader with timeout; align with Python `\n` protocol.

### 9. `GestureClient` permanent disconnect on bad JSON — **Real**

| | |
|---|---|
| **Code** | `GestureClient.cs:259–266` — parse failure sets `isConnected = false`. |

**Fix plan:** Log and skip bad line; only disconnect on null stream / IO errors.

### 10. `SlideShowManager` duplicate advance paths — **Real (edge case)**

| | |
|---|---|
| **Code** | `UpdateGazeAttention` ~139–142 (advance at 3s away + 50% baseline); `OnTimerTick` ~194–196 (advance at 1s away + full baseline). Both run on 50ms cadence from UI. |

**Fix plan:** Single advance decision (only in timer **or** only in gaze update); debounce `AdvanceSlide()` with a “advanced this slide” flag.

### 11. `FavoritesUI` GDI font leak — **Real**

| | |
|---|---|
| **Code** | `FavoritesUI.cs:89` — `new Font(...)` every `UpdateAppearance()` without disposing prior. |

**Fix plan:** One font field created in ctor; dispose in `Dispose`.

### 12. `TryLoadImage` caches null forever — **Real (minor)**

| | |
|---|---|
| **Code** | `TuioDemo.cs:5242–5252` — `imgCache[relativePath] = img` even when `img` is null. |

**Fix plan:** Do not cache misses, or use a negative-cache with retry.

---

## Confirmed — lower priority / legacy

| Issue | Location | Notes | Fix plan |
|-------|----------|-------|----------|
| Legacy hand tracker port 5555 | `python/hand_tracking/hand_tracker.py:19` vs `hand_service.py:5004` | C# uses **5004** via `main.py` — correct. 5555 is orphan/legacy (`python_server_legacy.py`). | Mark legacy or delete `hand_tracker.py` if unused. |
| `gesture_service.py` wrong `_SERVER_DIR` | Lines 139–141 → `python/python/server` | Import usually works because file is already under `python/server`. | Delete bad block or set `_SERVER_DIR = SCRIPT_DIR`. |
| YOLO local camera ≠ hub camera | `yolo_server.py:185` | Only if hub missing and `YOLO_CAMERA` ≠ `MUSEUM_CAMERA`. | When hub exists, never open local cap; document env vars. |
| Stale docs / error messages | `BUILD_INSTRUCTIONS.md`, auth UI strings | Reference `python_server.py`. | Search-replace to `python/server/main.py`. |
| `run_all.bat` hardcoded `C#\bin\Debug` | Line 26 | Breaks x86/x64 output folders. | Prefer `start.bat` + VS; or probe for `TUIO_DEMO.exe`. |
| `GetWorkspaceRoot()` 3× `Parent` | `TuioDemo.cs:1753–1759` | Works for `bin\Debug` → repo root; fragile for publish layouts. | Walk up until `.env` or `C#/content` exists. |
| `face_store` concurrent `reload`/`match` | `face_service.py` + threaded `auth_service.start` (293) | Theoretical with multiple clients. | `threading.Lock` in `face_store.py` if you support parallel auth connections. |
| Demographics race mapping | `demographics_service.py:110` | `middle eastern` → `black` is a **product** choice, not a crash. | Document or add CSV race value. |
| `.env` vs `.env.example` defaults | `.env.example` only in repo | Example: `skip_auth=0`, `skip_user=1`. Local `.env` is gitignored — verify manually. | Align examples with README intent. |

---

## Downgraded or false (AI overstated)

| # | AI claim | Verdict |
|---|----------|---------|
| 6 | `users.csv` paths break from `bin\Debug` | **Overstated.** Paths are repo-relative (`python/data/faces/...`). C# resolves via `GetWorkspaceRoot()` (`TuioDemo.cs:1753+`, `ResolveContentFilePath`); Python via `face_store._WORKSPACE`. Breaks only if workspace root detection fails (see #14). |
| 9 | `GazeEmotionClient` UTF-8 split | **Low risk.** `_emotion_history` is only touched from one background thread (`gaze_emotion_service.py` `_body`). UTF-8 split can still garble rare multi-byte line boundaries — use `StreamReader` if you see mojibake. |
| 11 | `_emotion_history` race | **False** for current design (single inference thread). |
| 12 | `face_store` race | **Low** — possible with multiple simultaneous auth TCP clients; normal install uses one C# app. |
| 13 | `hand_service` imports `gesture_service.mp` | **Coupling**, not a runtime bug if gesture stack starts. Extract shared `mediapipe_compat` module when refactoring. |
| 17 | SlideShow double-fire | **Partially real** — see #10; not always observable. |
| 19 | Public `SocketClient` fields | **Style** — not a functional bug. |
| 20 | `.env` conflict | **Documentation** — can't verify committed `.env`. |
| 21–23, 27–28, 30–31 | Various medium items | Warmup size, bare `except`, `CircularMenuController` “Home”, `Path.GetDirectoryName`, lambda leak — **optional** cleanups. |
| — | `RegisterFaceScan` corruption | Method **never called**; live path is `AuthLobbyScan` + `TryParseNewResponse` workaround. Still fix `AuthIntegration` for API correctness. |
| — | YoloContext 20s UI freeze | **False** for context client — `YoloContextClient.ConnectAsync` has **3s** timeout (`YoloContextClient.cs:41–46`). Hand tracker is the real stall. |

---

## Suggested fix order (phases)

### Phase 1 — Unblock developers (≈1 hour)

1. Fix or retire `run_all.bat`, `start_server.bat`, `python/start_python_server.bat`.
2. Update error strings/docs that mention `python_server.py`.
3. Add `\n` to all `auth_service.py` responses.

### Phase 2 — Auth correctness (≈2 hours)

4. Parse `NEW:` in `AuthIntegration` via `TryParseNewResponse`; simplify `TuioDemo` caller.
5. `StreamReader(..., leaveOpen: true)` + line-based auth receive with timeout.

### Phase 3 — Runtime stability (≈2–4 hours)

6. `InputPrioritizer` locking.
7. `HandTrackClient` async/deferred connect.
8. `GestureClient` don’t disconnect on one bad JSON line.
9. `SlideShowManager` single advance path; `FavoritesUI` font dispose; image cache null handling.

### Phase 4 — Product / architecture (when needed)

10. Decide fate of **port 5003** YOLO context (remove vs merge into `yolo_server`).
11. Legacy cleanup: `hand_tracker.py` / `python_server_legacy.py`.
12. `GetWorkspaceRoot` marker-file probe; optional `face_store` lock.

---

## Quick reference — ports (live stack)

| Port | Service | C# client |
|------|---------|-----------|
| 5000 | `auth_service.py` | `AuthIntegration` / `FaceRecognitionService` |
| 5001 | `gesture_service.py` | `GestureClient` (hand gestures) |
| 5002 | `gaze_emotion_service.py` | `GazeEmotionClient` |
| 5004 | `hand_service.py` | `HandTrackClient` |
| 5005 | `yolo_server.py` | Watch `GestureClient` (clock/menu) |
| ~~5003~~ | *(missing)* | `YoloContextClient` — **dead** |

---

*Generated by code review against the Smart Museum repo; re-run this triage after large merges.*
