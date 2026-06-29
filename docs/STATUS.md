# Project status & handoff

_Last updated: 2026-06-29_

This document captures the current state of GoodGlam so work can resume on another machine.
For end-user build/run instructions see the [README](../README.md); this file focuses on the
**development state, key decisions, and what's next**.

## TL;DR

- **What it is:** a Dalamud (C#) plugin that watches the Need/Greed roll window and, when a
  rollable item is used in a *popular* glamour on Eorzea Collection (EC), logs it to a persistent,
  browsable history window and raises a clickable bell (the old transient toast is gone).
- **State:** MVP **feature-complete and compiles clean** (0 warnings / 0 errors) against
  **Dalamud API 15 / `net10.0-windows`**. EC integration logic is **runtime-verified** against the
  live site. **Not yet tested inside the running game.**
- **Biggest gotcha:** on **native Windows** EC blocks .NET's HTTP stacks via Cloudflare TLS
  fingerprinting, so the plugin shells out to the system **`curl.exe`**. Under **Wine (Linux)** the
  opposite holds — in-process `HttpClient` works but `curl.exe` doesn't — so the transport is chosen
  at runtime (see [Transport](#transport-important)).

## Quick start on a fresh machine

Requires the **.NET 10 SDK**. The plugin builds and runs on **Windows and Linux**:

- **Windows:** also relies on `C:\Windows\System32\curl.exe` (present on Windows 10 1803+).
- **Linux:** runs under XIVLauncher.Core (Wine); no `curl.exe` needed — it uses in-process HTTP.

```powershell
# 1. Restore the Dalamud dev libraries into .dalamud/ (gitignored, ~56 MB)  [Windows]
New-Item -ItemType Directory -Force -Path .dalamud | Out-Null
curl.exe -L -o .dalamud/latest.zip https://goatcorp.github.io/dalamud-distrib/latest.zip
Expand-Archive -Path .dalamud/latest.zip -DestinationPath .dalamud -Force

# 2. Build
dotnet build src/GoodGlam/GoodGlam.csproj -c Release
# -> src/GoodGlam/bin/Release/GoodGlam.dll  (+ GoodGlam.json)
```

> The csproj defaults to the repo-local `.dalamud/` folder. To use your own XIVLauncher dev hooks
> instead, set the `DALAMUD_HOME` environment variable to that folder. On Linux this is the easiest
> route: `export DALAMUD_HOME="$HOME/.xlcore/dalamud/Hooks/dev"` then `dotnet build` — no `.dalamud/`
> download required.

### Load in-game (dev)

1. `/xlsettings` → **Experimental** → **Dev Plugin Locations** → add the path to the built
   `GoodGlam.dll`.
2. Open the plugin installer, enable **GoodGlam**.
3. `/goodglam` opens the history window (`/goodglam config` for settings). Enter a duty,
   win/contest a roll, and confirm popular drops appear in the history with a bell.

## What works / what's verified

| Area | Status |
|---|---|
| Project builds (API 15, net10.0-windows) | ✅ 0 err (Windows **and** Linux) |
| EC item search `POST /gear/<slot>/search` (game ID → EC ID) | ✅ runtime-verified (25430 → 14930) |
| EC popularity scrape `GET /glamours?...orderBy=loves` (parse loves + name) | ✅ runtime-verified |
| `curl.exe` transport from .NET subprocess (native Windows) | ✅ verified |
| In-process `HttpClient` transport under Wine (Linux) | ✅ runtime-verified (POST + GET, HTTP 200) |
| Managed-first transport with curl fallback (auto-select) | ✅ verified under XIVLauncher Wine |
| Persistent history store + scrollable window (clickable links) | ✅ unit-tested; ⛔ not tested in-game |
| `NeedGreed` addon hook + `Loot` struct read | ⛔ not tested in-game |
| Bell notification on qualifying drop | ⛔ not tested in-game |
| Config window / `/goodglam` command | ⛔ not tested in-game |

**Next concrete step:** load the DLL in a live client and confirm the `NeedGreed` hook fires and a
drop lands in the history window. Everything up to the EC calls is verified; the in-game UI/hook
path is the untested remainder.

## Transport (important)

EC has **no public API**, and on **native Windows** its Cloudflare WAF **hard-blocks .NET's managed
HTTP** — both `SocketsHttpHandler` and `WinHttpHandler` return `403` even with full browser headers,
because Cloudflare fingerprints the TLS ClientHello (JA3/JA4). The system **`curl.exe`**
(libcurl/Schannel) produces a handshake Cloudflare accepts, so on Windows the plugin shells out to it
via `System.Diagnostics.Process`.

**Cross-platform selection.** The plugin runs on Linux too (XIVLauncher.Core under Wine), where the
opposite holds: there is no `curl.exe` in the prefix (a native ELF launched via `Process.Start` fails
with `E_HANDLE`) but in-process `HttpClient` reaches EC fine — Wine's TLS goes through GnuTLS/OpenSSL,
a fingerprint Cloudflare **accepts** (`HTTP 200`, verified against the live site).

Rather than sniff the OS, `Glam/EcTransport.cs` uses a **try-managed-first, fall-back-to-curl**
composite (`FallbackEcTransport`):

- Try `ManagedHttpTransport` (in-process `HttpClient`) first. Works under Wine and most platforms.
- If it comes back blocked (Cloudflare `403` → `null`), fall back to `CurlTransport` (`curl.exe`).
- Whichever last succeeded becomes primary, so steady-state traffic uses one working path.

This is self-correcting: curl is spawned only on native Windows where managed HTTP is blocked, and is
dropped automatically if Cloudflare ever stops fingerprinting .NET's stack — no Wine detection needed.

- Dalamud does **not** sandbox plugins, so `Process.Start` is allowed (Windows path).
- **Known trade-offs (Windows path):** a child process spawned from the game can look unusual to
  AV/EDR; this design would be rejected by the official Dalamud plugin repo; there's per-lookup spawn
  overhead.
- **Verified:** in-process .NET `HttpClient` *can* reach GitHub static hosting
  (`raw.githubusercontent.com`, `*.github.io`) on Windows too — only EC is blocked there. This is what
  makes the planned replacement viable.

### Planned replacement (deferred, preferred)

A **GitHub Actions** cron job runs the `curl` crawler (curl works fine on the runners), builds a
compact static JSON index (`xivApiId → { loves, glamId }`), and publishes it to GitHub Pages / a
release asset. The plugin then just **downloads that static file in-process** and does in-memory
lookups — no subprocess, no in-game scraping, serverless. Not yet implemented.

## Key domain knowledge: the three item-ID spaces

The same item ("Ronkan Bandana of Scouting") has three unrelated IDs:

| ID | Example | Source | Used for |
|---|---|---|---|
| Game item ID (`XIVApiId`) | `25430` | SE `Item` sheet | what the **loot drop** reports; universal join key |
| EC ID | `14930` | EC's own DB | EC's `filter[<slot>Piece]=` query param |
| Lodestone hash | `1821cb137de` | SE Lodestone | not needed by us |

There is **no math relation** between them — only lookup tables. Conveniently, EC's
`/gear/<slot>/search` returns **all three together**, so it is its own game-ID ↔ EC-ID bridge.

## Architecture / file layout

```
src/GoodGlam/
  Plugin.cs                      entry point, wiring, /goodglam command
  Services.cs                    [PluginService] locator
  Configuration.cs               persisted settings (Enabled, LovesThreshold=100, CacheTtlHours=12, Filters); v3
  Glam/GlamSlot.cs               EquipSlotCategory -> EC slot; FilterParam = "<key>Piece"
  Glam/ItemResolver.cs           game item ID -> name + slot (Lumina Item sheet); HQ normalize; skips non-gear
  Glam/EcFilterOptions.cs        EC filter value/label tables (shared by query builder + config UI)
  Glam/PopularityFilters.cs      global EC-parity filters; builds active filter[..] params + cache signature
  Glam/EcTransport.cs            IEcTransport; managed-first HttpClient w/ curl.exe fallback (FallbackEcTransport)
  Glam/EorzeaCollectionClient.cs IGlamSource; builds EC requests + parses loves+name (delegates HTTP to IEcTransport)
  Glam/GlamPopularityService.cs  orchestration + per-item/per-filter TTL cache + notify
  History/NotificationHistory.cs PopularDropRecord + JSON-backed, capped (500) history store
  History/HistoryNotifier.cs     INotifier: append to history + raise persistent clickable bell
  Loot/LootWatcher.cs            NeedGreed AddonLifecycle hook; reads CSLoot.Instance()->Items
  Windows/ConfigWindow.cs        ImGui settings UI
  Windows/HistoryWindow.cs       scrollable popular-drop history (clickable EC links, clear)
GoodGlam.json                    plugin manifest (DalamudApiLevel 15)
```

Key seam: **`IGlamSource`** abstracts the data source. The curl-backed `EorzeaCollectionClient` can
be swapped for a static-index client (the deferred GitHub Actions approach) without touching the
loot/resolver/notify code.

## MVP behaviour & decisions

- **"Popular"** = at least one glamour using the item has **≥ `LovesThreshold`** loves (default 100).
- **Detection point:** the `NeedGreed` roll window (`AddonLifecycle` PostSetup / PostRefresh), so you
  can be notified *before* you roll. Items are de-duplicated per window.
- **Notification:** popular drops are logged to a **persistent, capped history store**
  (`history.json` in the plugin config dir, newest first, max 500), shown in a scrollable
  **history window** with clickable EC glamour links, plus a persistent clickable **bell** that
  opens the window. The old transient toast is removed.
- **Caching:** per game-item-ID with a configurable TTL (default 12h) to stay polite to EC. The
  active filter signature is folded into the cache key, so changing filters never serves a stale,
  differently-filtered result.
- **Filters (MVP+1):** global EC-parity filters in config (`Configuration.Filters`); each active
  one is appended as a `filter[..]` param onto the loves-ordered listing, so the threshold is
  evaluated against the filtered set. Config version bumped 1 → 2, then 2 → 3 for the history rework
  (additive — old configs load unchanged; history lives in a separate file).

## Roadmap / next steps

1. **In-game smoke test** of the NeedGreed hook + history/bell (the only unverified path).
2. **Replace the curl subprocess** with the GitHub Actions crawler + static JSON index.
3. ✅ **MVP+1:** configurable filters mirroring EC (gender, race, date submitted, tags =
   classification/style/theme/color, intended-for/job, level to equip, exclude Mog Station /
   seasonal). Stored globally in config (`Filters`), appended to every popularity lookup; the
   loves threshold is judged against the filtered result set. Server filtering is out of scope.
4. ✅ **History window:** scrollable, persistent popularity history with clickable glamour links,
   replacing the toast. Follow-up: on-item overlay showing the top hearts count.
5. **MVP+2:** EC login → restrict notifications to glamours the user has saved.

## Notes for resuming

- `.dalamud/`, `bin/`, `obj/` are gitignored; restore `.dalamud/` per [Quick start](#quick-start-on-a-fresh-machine).
- Dalamud's distrib currently targets **net10.0** (revision 7312, assembly v15 → API level 15). If a
  future distrib bumps the major version, update `DalamudApiLevel` in `GoodGlam.json`.
- ImGui bindings are `Dalamud.Bindings.ImGui` (the newer binding package), not `ImGuiNET`.
