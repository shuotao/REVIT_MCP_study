# Ribbon Exclusive-Lock Impl Log

## Round 1 — Sonnet

### Files Changed

- `MCP/Core/SocketService.cs`
- `MCP/Configuration/ServiceSettings.cs`
- `MCP/Commands/MCPCommands.cs`
- `MCP/Application.cs`

### Summary

Implemented Design B (exclusive lock + manual switch) across the four permitted files:

**`MCP/Core/SocketService.cs`** (main correctness surface)
- Replaced bare `_webSocket` with lock-guarded state: `_connectionLock`, `_activeSocket`, `_activeRemoteEndpoint`, `_connectedAtUtc`, `_lastRejectLogUtc`.
- `IsConnected` now reads under lock; added `IsLocked_NoLock()` helper.
- Rewrote `AcceptConnectionsAsync`: checks lock state before `AcceptWebSocketAsync`; rejects with HTTP 409 + `RateLimitedRejectLog` (30s throttle) when locked; otherwise accepts, sets the three lock-guarded fields inside the lock, logs `"[Socket] MCP Server 已連線 (locked) - "+endpoint`, and starts `ReceiveMessagesAsync` via `Task.Run`.
- Added `SwitchConnection()` — clears state under lock, then calls `Abort()` (not `CloseAsync`, to avoid the "already one outstanding receive" race), returns `(released, previousRemote)`.
- `ReceiveMessagesAsync` now has a `finally` that compare-and-clears (`ReferenceEquals`) the active socket and is the **single disposer** (`socket.Dispose()`); added a `WebSocketException` catch logged at Debug (not Error) for user-triggered aborts.
- `Stop()` now acquires the socket under lock, calls `Abort()` only (no `CloseAsync`/`Dispose`), leaving disposal to the receive task's `finally`; the synchronous `_httpListener.Stop()/Close()` block is unchanged.
- Added `GetStatusSnapshot()` returning `(locked, remote, sinceUtc)`.
- `SendResponseAsync` renamed to use `_activeSocket`, unchanged logic/guard.

**`MCP/Configuration/ServiceSettings.cs`** — added `public bool ExclusiveLock { get; set; } = true;`. `ConfigVersion`/`ValidateAndFix()` untouched.

**`MCP/Commands/MCPCommands.cs`** — added `SwitchConnectionCommand : IExternalCommand` (same `[Transaction(TransactionMode.Manual)]` pattern, calls `SwitchConnection()`, shows TaskDialog feedback). `SettingsCommand.Execute` now appends a connection-status block (locked/idle, remote client, connect time) via `GetStatusSnapshot()` when the service is running.

**`MCP/Application.cs`** — added the 4th ribbon button `"MCPSwitch"` ("切換/\n釋放連線") wired to `RevitMCP.Commands.SwitchConnectionCommand`, placed right after the settings button.

Confirmed no remaining references to the old `_webSocket` field (only one comment mentions it descriptively), and that `MCP-Server/`, the wire protocol, and port 8964 were not touched. No `git` commands were run.

### Round 1 — OPS verdict

pass=true, R24=true, R26=true, issues=[]

## Doc-align round 1 — Sonnet

### Files Changed

- `CLAUDE.md`
- `README.md`
- `README.zh-TW.md`

### Summary

Rewrote exclusive-lock documentation across three core files to reflect Design B (HTTP 409 rejection + manual switch button). Counts unchanged (167/75/50), Traditional Chinese text renders correctly with no mojibake. All three files updated, no code touched, no git commands run.

**`CLAUDE.md`** — rewrote the `## Single-Connection Limitation` section (lines ~69-76): replaced "holds one MCP connection... newly connected replaces the previous" with the exclusive-lock/HTTP-409-reject model, added bullets for the "切換/釋放連線" ribbon button (release + let next client take the lock, anonymous-transport caveat), the "MCP 設定" dialog showing the connected client name (via `?client=` from MCP `clientInfo.name`), and `ServiceSettings.ExclusiveLock` (default `true`) as the escape hatch. Reset guidance now mentions the switch button alongside ribbon restart.

**`README.md`** — rewrote the `### Switching Between AI Clients` section (lines ~219-225): describes the exclusive lock / clean HTTP 409 rejection (vs. the old "replaces" language), and rewrote the 3-step procedure to use the "切換/釋放連線" (Switch/Release Connection) ribbon button plus the "MCP 設定" dialog showing the connected client name.

**`README.zh-TW.md`** — parallel edit to `### AI Client 切換與並用限制` (lines ~219-226) in Traditional Chinese, mirroring the same content: 獨占鎖 / HTTP 409 拒絕、切換/釋放連線 按鈕、MCP 設定 對話框顯示連線中的 Client。Verified UTF-8 renders cleanly, no mojibake.

Count numbers (167 tools / 75 domain files / 50 skills) were left untouched in all three files — verified via grep. No code files or git commands were touched.
